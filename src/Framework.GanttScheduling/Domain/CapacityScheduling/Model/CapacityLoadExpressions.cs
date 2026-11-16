#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;

/// <summary>
/// 产能占用与加工时长的表达式层 / The expression layer for occupancy and processing time.
/// </summary>
/// <remarks>
/// `capacity[e, s]` **不是自由变量**，而是由数量推导出来的表达式：
///
/// * `operationTime[a, s] = 单位加工时长 · Σ_顺序位 x[a, s, o]`；
/// * `capacity[e, s] = Σ_{执行器为 e 的动作} operationTime[a, s]`。
///
/// 这一点差别很关键：早先的 `CapacityUsageVariables` 建的是**自由变量**，它和数量之间没有任何行相连，
/// 所以"占用 ≤ 可用时长"对数量没有任何约束力——正是因此撤掉了那条判据。这里改成表达式，
/// 容量约束就真的压到数量上。
///
/// 单位加工时长按规则取：**离散动作且有 `BatchDuration` 时用它，否则取时间窗刻度（= 1）**。
/// （早先的注释说 C# 接口没有 `BatchDuration`——那是错的，它从 B4xx 起就在，只是没人用。）
///
/// `capacity[e, s]` is **not a free variable** but an expression derived from the amounts: `operationTime[a, s] = unit processing time · sum of x[a, s, o]` and
/// `capacity[e, s] = sum of operationTime[a, s]` over the actions of executor `e`. That difference matters: the earlier `CapacityUsageVariables` builds **free variables** with no row tying
/// them to the amounts, so "occupancy within available duration" constrains nothing — which is exactly why removed that criterion. Expressed this way the capacity constraint really
/// presses on the amounts.
///
/// The unit processing time follows its rule: **a discrete action with `BatchDuration` uses it, otherwise the window scale (1)**. (the earlier comment claimed the C# interface
/// had no `BatchDuration` — wrong, it has existed since B4xx, just unused.)
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class CapacityLoadExpressions<A>
    where A : IProductionAction {
    private readonly IReadOnlyList<A> _actions;

    private readonly IReadOnlyList<TimeRange> _slots;

    private readonly TimeWindow<Flt64> _timeWindow;

    private readonly IReadOnlyList<Executor> _executors;

    private readonly ulong _maxOrderPerSlot;

    private readonly CapacityOrderVariables<A> _variables;

    private Dictionary<(int Action, int Slot), ILinearIntermediateSymbol<Flt64>>? _operationTime;

    private Dictionary<(int Executor, int Slot), ILinearIntermediateSymbol<Flt64>>? _capacity;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="variables">排序变量层 / The order variable layer.</param>
    /// <param name="timeWindow">时间窗口 / The time window.</param>
    public CapacityLoadExpressions(CapacityOrderVariables<A> variables, TimeWindow<Flt64> timeWindow) {
        ArgumentNullException.ThrowIfNull(variables);
        _variables = variables;
        _actions = variables.Actions;
        _slots = variables.Slots;
        _timeWindow = timeWindow;
        _maxOrderPerSlot = variables.MaxOrderPerSlot;
        _executors = _actions.Select(action => action.Executor).Distinct().ToList();
    }

    /// <summary>执行器列表 / The executors.</summary>
    public IReadOnlyList<Executor> Executors => _executors;

    /// <summary>时隙列表 / The slots.</summary>
    public IReadOnlyList<TimeRange> Slots => _slots;

    /// <summary>
    /// 注册表达式层 / Register the expression layer.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    /// <remarks>
    /// 注册顺序：先 `x`（已经在 的变量层里），再 `operationTime`，最后 `capacity`。后两者都是**表达式符号**，
    /// 引用前面已经注册的符号；顺序反了会得到"符号不在模型里"。
    ///
    /// Order: `x` first (the earlier variable layer), then `operationTime`, then `capacity`. The last two are **expression symbols** referencing what came before; reversing the order yields
    /// "symbol not in the model".
    /// </remarks>
    public Try Register(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_variables.Amounts is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "排序变量尚未注册，无法建占用表达式 / the order variables are not registered, so the load expressions cannot be built"));
        }

        int positions = checked((int)_maxOrderPerSlot);
        _operationTime = new Dictionary<(int Action, int Slot), ILinearIntermediateSymbol<Flt64>>();
        for (int action = 0; action < _actions.Count; ++action) {
            for (int slot = 0; slot < _slots.Count; ++slot) {
                // **单位加工时长**：离散动作且有批次时长就用它，否则取时间窗刻度（= 1）。
                // 少了这一层，`x` 的单位就是"件"而不是"时长"，一个时隙只能装 1 件——那不是想要的语义。
                //
                // **The unit processing time**: a discrete action with a batch duration uses it, otherwise the window scale (1). Without this layer `x` is measured in pieces rather
                // than durations, so one slot holds exactly one piece — not the semantics intended.
                Flt64 unit = _actions[action].Discrete && _actions[action].BatchDuration is { } batch
                    ? _timeWindow.ValueOf(batch)
                    : _timeWindow.ValueOf(_timeWindow.Interval);
                var monomials = new List<LinearMonomial<Flt64>>(positions);
                for (int position = 0; position < positions; ++position) {
                    monomials.Add(new LinearMonomial<Flt64>(unit, _variables.AmountOf(action, slot, position)));
                }
                var symbol = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(monomials, constant: Flt64.Zero),
                    name: $"operationTime_{_actions[action].Id}_{slot}");
                Try added = model.Add(symbol);
                if (!added.IsOk) {
                    return added;
                }
                _operationTime[(action, slot)] = symbol;
            }
        }

        _capacity = new Dictionary<(int Executor, int Slot), ILinearIntermediateSymbol<Flt64>>();
        for (int executor = 0; executor < _executors.Count; ++executor) {
            for (int slot = 0; slot < _slots.Count; ++slot) {
                var monomials = new List<LinearMonomial<Flt64>>();
                for (int action = 0; action < _actions.Count; ++action) {
                    if (!Equals(_actions[action].Executor, _executors[executor])) {
                        continue;
                    }
                    monomials.Add(new LinearMonomial<Flt64>(Flt64.One, _operationTime[(action, slot)]));
                }
                var symbol = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(monomials, constant: Flt64.Zero),
                    name: $"capacity_{_executors[executor].Id}_{slot}");
                Try added = model.Add(symbol);
                if (!added.IsOk) {
                    return added;
                }
                _capacity[(executor, slot)] = symbol;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 动作的单位加工时长（窗口单位）/ The unit processing time of an action in window units.
    /// </summary>
    /// <param name="actionIndex">动作下标 / The action index.</param>
    /// <returns>单位加工时长 / The unit processing time.</returns>
    public Flt64 UnitOperationTimeOf(int actionIndex) =>
        _actions[actionIndex].Discrete && _actions[actionIndex].BatchDuration is { } batch
            ? _timeWindow.ValueOf(batch)
            : _timeWindow.ValueOf(_timeWindow.Interval);

    /// <summary>
    /// 从求解结果算出 (执行器, 时隙) 的占用 / Compute the occupancy of an (executor, slot) from a solved model.
    /// </summary>
    /// <param name="model">已求解的模型 / The solved model.</param>
    /// <param name="executorIndex">执行器下标 / The executor index.</param>
    /// <param name="slotIndex">时隙下标 / The slot index.</param>
    /// <returns>窗口单位下的占用 / The occupancy in window units.</returns>
    /// <remarks>
    /// **从数量直接算，不去求值表达式符号**：表达式只是注册进模型的一份等价写法，`capacity[e, s]` 展开之后就是这些数量，
    /// 直接求和既少一层递归，也不会因为"表达式符号有没有被求解器写回"而给出不同答案。
    ///
    /// **Computed from the amounts directly rather than evaluating the expression symbols**: the expression is just an equivalent form registered into the model — `capacity[e, s]`
    /// expands to exactly these amounts — so summing them skips a recursion level and cannot disagree depending on whether the solver wrote the expression symbol back.
    /// </remarks>
    public Flt64 UsageOf(IMetaModel<Flt64> model, int executorIndex, int slotIndex) {
        ArgumentNullException.ThrowIfNull(model);
        if (_variables.Amounts is null) {
            throw new InvalidOperationException(
                "排序变量尚未注册，无法算占用 / the order variables are not registered, so the occupancy cannot be computed");
        }

        int positions = checked((int)_maxOrderPerSlot);
        Flt64 total = Flt64.Zero;
        for (int action = 0; action < _actions.Count; ++action) {
            if (!Equals(_actions[action].Executor, _executors[executorIndex])) {
                continue;
            }
            Flt64 unit = UnitOperationTimeOf(action);
            for (int position = 0; position < positions; ++position) {
                Fuookami.Ospf.Math.Symbol.ISymbol symbol = _variables.AmountOf(action, slotIndex, position);
                Fuookami.Ospf.Core.Token.Token<Flt64>? token = model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)symbol);
                if (token?.Result is null) {
                    throw new InvalidOperationException(
                        $"变量 x[{action},{slotIndex},{position}] 没有取值，占用无从计算 / the variable x[{action},{slotIndex},{position}] holds no value");
                }
                total += new Flt64(unit.ToDouble() * token.Result.Value.ToDouble());
            }
        }
        return total;
    }

    /// <summary>
    /// 把窗口单位下的占用换成时长 / Convert an occupancy in window units into a duration.
    /// </summary>
    /// <param name="usage">窗口单位下的占用 / The occupancy in window units.</param>
    /// <returns>时长 / The duration.</returns>
    public TimeSpan DurationOf(Flt64 usage) => _timeWindow.DurationOf(usage);

    /// <summary>
    /// 取 (执行器, 时隙) 的占用表达式 / Take the occupancy expression of an (executor, slot).
    /// </summary>
    /// <param name="executorIndex">执行器下标 / The executor index.</param>
    /// <param name="slotIndex">时隙下标 / The slot index.</param>
    /// <returns>符号 / The symbol.</returns>
    public ILinearIntermediateSymbol<Flt64> CapacityOf(int executorIndex, int slotIndex) {
        if (_capacity is null) {
            throw new InvalidOperationException(
                "占用表达式尚未注册 / the load expressions have not been registered yet");
        }
        return _capacity[(executorIndex, slotIndex)];
    }
}
