#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
/// <summary>
/// 产能编译决策对象（带顺序）/ Capacity compilation decision object (with order)
/// </summary>
/// <remarks>
/// 三维变量：x[action, slot, order] -> 数量（整型），b[action, slot, order] -> 是否选中（二进制）。
/// Three-dimensional variables: x[action, slot, order] -> amount (integer), b[action, slot, order] -> is_selected (binary).
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class CapacityOrderCompilation<A> : ICapacity<A>
    where A : IProductionAction {
    private readonly IReadOnlyList<A> _actions;
    private readonly IReadOnlyList<TimeRange> _slots;
    private readonly TimeWindow<Flt64> _timeWindow;
    private readonly ulong _maxOrderPerSlot;
    private readonly IReadOnlyList<Executor> _executors;

    /// <summary>
    /// 产能编译构造（带顺序）/ Capacity order compilation constructor
    /// </summary>
    /// <param name="actions">生产动作列表 / List of production actions</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="maxOrderPerSlot">每时隙最大顺序数 / Maximum order per slot</param>
    /// <param name="variables">排序变量层，缺省则解无从提取 / The order variable layer; without it no solution can be extracted.</param>
    /// <param name="load">占用表达式层，缺省按变量层现建 / The load expression layer; built from the variable layer when omitted.</param>
    public CapacityOrderCompilation(
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow,
        ulong maxOrderPerSlot,
        CapacityOrderVariables<A>? variables = null,
        CapacityLoadExpressions<A>? load = null) {
        _actions = actions;
        _slots = slots;
        _timeWindow = timeWindow;
        _maxOrderPerSlot = maxOrderPerSlot;
        Variables = variables;
        Load = load;
        _executors = actions.Select(a => a.Executor).Distinct().ToList();

        foreach (ManualIndexed action in actions.OfType<ManualIndexed>()) {
            if (!action.Indexed) {
                action.SetIndexed();
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Executor> Executors => _executors;

    /// <summary>生产动作列表 / List of production actions</summary>
    public IReadOnlyList<A> Actions => _actions;

    /// <summary>时隙列表 / List of time slots</summary>
    public IReadOnlyList<TimeRange> Slots => _slots;

    /// <summary>时间窗口 / Time window</summary>
    public TimeWindow<Flt64> TimeWindow => _timeWindow;

    /// <summary>每时隙最大顺序数 / Maximum order per slot</summary>
    public ulong MaxOrderPerSlot => _maxOrderPerSlot;

    /// <summary>排序变量层 / The order variable layer.</summary>
    public CapacityOrderVariables<A>? Variables { get; }

    /// <summary>占用表达式层 / The load expression layer.</summary>
    /// <remarks>不传就按变量层现建一个——**单位和容量约束共用同一处规则**，两处各写一份迟早漂移。</remarks>
    public CapacityLoadExpressions<A>? Load { get; }

    /// <inheritdoc/>
    /// <summary>
    /// 提取解 / Extract the solution.
    /// </summary>
    /// <param name="model">模型 / The model.</param>
    /// <returns>解或失败 / The solution or a failure.</returns>
    /// <remarks>
    /// **第一版返回 `Ok` 加一个分配表为空的解**：调用方读到的是「排好了，什么都没排」——**空解和真解无法区分**，
    /// 这是事后对账时最难发现的一类缺陷。现在真的从模型里读回每个 `x[动作, 时隙, 顺序位]` 的取值：
    /// 按 (动作, 时隙) 汇总各顺序位的数量，只保留数量不为零的格子。
    ///
    /// **读不到已求解的值就失败**，不猜：变量没注册、或符号不在模型里、或取值还没写回，都会显式报错。
    /// 把"读不到"当成 0 会安静地报一份空计划。
    ///
    /// **天花板**：`Duration` 现在直接取时隙长度，没有按 `action` 的单位加工时间折算——基线的 `operationTime` 那一层
    /// 还没接。等产能调度有了解路径再补，现在先保证**数量和顺序位是对的**。
    ///
    /// **The first version returned `Ok` with an empty allocation table**: the caller read "solved, nothing scheduled" — **an empty solution and a real one cannot be told apart**,
    /// the hardest defect to find afterwards. It now really reads every `x[action, slot, order position]` back from the model, summing the amounts of all positions per
    /// (action, slot) and keeping only the non-zero cells.
    ///
    /// **A missing solved value fails instead of guessing**: unregistered variables, a symbol that is not in the model, or a value that was not written back all report explicitly.
    /// Treating "cannot read" as 0 would quietly report an empty plan.
    ///
    /// **Ceiling**: `Duration` currently takes the slot length rather than folding in the action's unit processing time — the baseline's `operationTime` layer is not wired. Add it once
    /// capacity scheduling has a solve path; until then **the amounts and order positions are what is guaranteed**.
    /// </remarks>
    public Result<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>> ExtractSolution(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (Variables is null || Variables.Amounts is null) {
            return new Failed<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "排序变量尚未注册，解无从提取 / the order variables are not registered, so there is no solution to extract"));
        }
        CapacityOrderVariables<A> variables = Variables!;
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IMetaModel<Flt64> meta) {
            return new Failed<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "解只能从 Flt64 元模型里读 / a solution can only be read from an Flt64 meta model"));
        }

        var actionAllocations = new List<ActionAllocation<A>>();
        for (int action = 0; action < _actions.Count; ++action) {
            for (int slot = 0; slot < _slots.Count; ++slot) {
                ulong total = 0UL;
                int lastPosition = -1;
                for (int position = 0; position < checked((int)_maxOrderPerSlot); ++position) {
                    Fuookami.Ospf.Math.Symbol.ISymbol symbol = variables.AmountOf(action, slot, position);
                    Fuookami.Ospf.Core.Token.Token<Flt64>? token = meta.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)symbol);
                    if (token is null) {
                        return new Failed<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>>(
                            new Err<ErrorCode>(
                                ErrorCode.ApplicationError,
                                $"变量 x[{action},{slot},{position}] 不在模型里，读不到解 / the variable x[{action},{slot},{position}] is not in the model, so the solution cannot be read"));
                    }
                    if (token.Result is null) {
                        return new Failed<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>>(
                            new Err<ErrorCode>(
                                ErrorCode.ApplicationError,
                                $"变量 x[{action},{slot},{position}] 还没有取值，解无从提取 / the variable x[{action},{slot},{position}] holds no value yet"));
                    }
                    double amount = token.Result.Value.ToDouble();
                    ulong counted = (ulong)double.Round(amount);
                    total += counted;
                    if (counted > 0UL) {
                        lastPosition = position;
                    }
                }
                if (total == 0UL) {
                    continue;
                }
                actionAllocations.Add(new ActionAllocation<A>(
                    _actions[action],
                    _slots[slot],
                    slot,
                    total,
                    _slots[slot].Duration,
                    lastPosition));
            }
        }

        // **占用是从数量算出来的，不是"每个分配占满一个时隙"**。基线：`capacity[e, s]` 求值后经
        // `timeWindow.durationOf(...)` 换回时长。旧写法把每个分配的 `Duration`（= 时隙长度）加起来，
        // 同一时隙里两个动作就会算出"两个时隙的占用"——**数字看着像产能，其实只是动作个数 × 时隙长度**。
        //
        // **Occupancy is computed from the amounts, not "each allocation fills a slot"**. Baseline: evaluate `capacity[e, s]` and convert back through
        // `timeWindow.durationOf(...)`. The old form summed every allocation's `Duration` (the slot length), so two actions in one slot reported "two slots of occupancy" —
        // **a number that looks like capacity but is really the action count times the slot length**.
        CapacityLoadExpressions<A>? load = Load ?? (Variables is null ? null : new CapacityLoadExpressions<A>(Variables, _timeWindow));
        if (load is null) {
            return new Failed<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "没有排序变量层，算不出占用 / without the order variable layer the occupancy cannot be computed"));
        }

        var executorCapacities = new List<ExecutorCapacityResult>();
        for (int index = 0; index < _executors.Count; ++index) {
            Executor executor = _executors[index];
            for (int slot = 0; slot < _slots.Count; ++slot) {
                Flt64 usage = load.UsageOf(meta, index, slot);
                if (usage.ToDouble() <= 0.0) {
                    continue;
                }
                executorCapacities.Add(new ExecutorCapacityResult(
                    executor, _slots[slot], slot, load.DurationOf(usage)));
            }
        }

        // **选中标记也要读出来**：`Σ b ≤ 1` 与 `x ↔ b` 这两族约束只作用在 `b` 上，只读数量的话它们是否成立完全不可见
        // （B733 的两条存活变异就是这么活下来的）。
        // **The selection flags are read out too**: the two families that act on `b` alone are invisible to an amount-only read-back, which is exactly how B733's two mutations survived.
        var selections = new List<OrderSelection<A>>();
        for (int action = 0; action < _actions.Count; ++action) {
            for (int slot = 0; slot < _slots.Count; ++slot) {
                for (int position = 0; position < checked((int)_maxOrderPerSlot); ++position) {
                    Fuookami.Ospf.Math.Symbol.ISymbol picked = variables.SelectionOf(action, slot, position);
                    Fuookami.Ospf.Core.Token.Token<Flt64>? token = meta.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)picked);
                    if (token?.Result is null) {
                        return new Failed<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>>(
                            new Err<ErrorCode>(
                                ErrorCode.ApplicationError,
                                $"选中变量 b[{action},{slot},{position}] 没有取值，解无从提取 / the selection b[{action},{slot},{position}] holds no value"));
                    }
                    selections.Add(new OrderSelection<A>(
                        _actions[action], slot, position, token.Result.Value.ToDouble() > 0.5));
                }
            }
        }

        return Results.Ok<CapacitySchedulingSolution<A>>(new CapacitySchedulingSolution<A>(
            _actions, actionAllocations, executorCapacities, selections));
    }
}
