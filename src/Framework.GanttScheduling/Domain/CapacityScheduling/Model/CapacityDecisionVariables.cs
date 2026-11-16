#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;

/// <summary>
/// 产能调度的决策变量矩阵 / The capacity scheduling decision-variable matrix.
/// </summary>
/// <typeparam name="E">执行器类型 / The executor type.</typeparam>
/// <typeparam name="A">动作类型 / The action type.</typeparam>
/// <remarks>
/// **这是产能调度一直缺的那一层。** `CapacityCostMinimization` / `ExecutorCapacityConstraint` / `OrderConstraint` 三个
/// `Invoke` 都是 `=> Ok`，`BunchCapacitySchedulingResourceUsage.Register` 也被改成了显式失败，根因都一样：
/// 没有决策变量，就没有任何"每动作每时隙加工多少"的量可以写进约束和目标。
///
/// 对应物是 `compilation.operationTime[actionIndex, slotIndex]`——一个动作 × 时隙的矩阵。
/// C# 侧用 <see cref="UIntVariable2"/>：它就是这个形状，且范围与整数性由它自己管，不需要在这里再写一遍。
///
/// **This layer is what capacity scheduling has been missing.** The three `Invoke` bodies are `=> Ok` and `BunchCapacitySchedulingResourceUsage.Register` was changed to fail
/// explicitly, all for the same root cause: without decision variables there is no "how much is processed per action per slot" quantity to put into a row or an objective. The code's
/// counterpart is `compilation.operationTime[actionIndex, slotIndex]` — an action-by-slot matrix. On the C# side <see cref="UIntVariable2"/> is exactly that shape, and it owns its range
/// and integrality, so nothing needs rewriting here.
/// </remarks>
public sealed class CapacityDecisionVariables<E, A> : Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model.ICapacityAmountSource
    where E : Executor
    where A : IProductionAction {
    private readonly IReadOnlyList<A> _actions;

    private readonly IReadOnlyList<TimeRange> _slots;

    private readonly string _name;

    private UIntVariable2? _variables;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="actions">动作列表 / The actions.</param>
    /// <param name="slots">时隙列表 / The slots.</param>
    /// <param name="name">名称 / The name.</param>
    public CapacityDecisionVariables(
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        string name = "capacity_operation_time") {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(slots);
        _actions = actions;
        _slots = slots;
        _name = name;
    }

    /// <summary>动作列表 / The actions.</summary>
    public IReadOnlyList<A> Actions => _actions;

    /// <summary>时隙列表 / The slots.</summary>
    public IReadOnlyList<TimeRange> Slots => _slots;

    /// <summary>名称 / The name.</summary>
    public string Name => _name;

    /// <inheritdoc/>
    System.Collections.Generic.IReadOnlyList<Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeRange>
        Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model.ICapacityAmountSource.ActionSlots => _slots;

    /// <inheritdoc/>
    System.Collections.Generic.IReadOnlyList<IProductionAction>
        Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model.ICapacityAmountSource.Actions =>
        (System.Collections.Generic.IReadOnlyList<IProductionAction>)(object)_actions;

    /// <inheritdoc/>
    bool Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model.ICapacityAmountSource.IsRegistered =>
        _variables is not null;

    /// <inheritdoc/>
    Fuookami.Ospf.Math.Symbol.ISymbol Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model.ICapacityAmountSource.AmountOf(
        IProductionAction action, int slotIndex) {
        foreach (A candidate in _actions) {
            if (Equals(candidate, action)) {
                return SymbolOf(candidate, slotIndex);
            }
        }
        throw new System.ArgumentOutOfRangeException(nameof(action), "动作不在本矩阵里 / the action is not part of this matrix");
    }

    /// <summary>
    /// 变量矩阵；**注册之前是 `null`** / The variable matrix; **`null` before registration**.
    /// </summary>
    /// <remarks>
    /// **未注册时是 `null` 而不是一个全零的矩阵**：全零矩阵读起来像"每个动作在每个时隙都加工 0 件"，
    /// 那是**一个合法的解**，会静默通过所有检查。`null` 至少在取用时立刻炸。
    ///
    /// **`null` rather than an all-zero matrix before registration**: an all-zero matrix reads as "every action processes nothing in every slot", which is **a feasible solution** and would
    /// silently pass every check. `null` at least blows up at the point of use.
    /// </remarks>
    public UIntVariable2? Variables => _variables;

    /// <summary>
    /// 取一个变量的符号 / Get the symbol of one variable.
    /// </summary>
    /// <param name="action">动作 / The action.</param>
    /// <param name="slotIndex">时隙下标 / The slot index.</param>
    /// <returns>符号 / The symbol.</returns>
    /// <remarks>
    /// 返回 `ISymbol` 而不是 `ILinearIntermediateSymbol&lt;Flt64&gt;`：这是变量项（`CombinationVariableItem`），
    /// 它自己不是线性中间符号，但**可以直接进单项式**——建行时用 `new LinearMonomial&lt;Flt64&gt;(Flt64.One, SymbolOf(...))`。
    ///
    /// Returns `ISymbol` rather than `ILinearIntermediateSymbol&lt;Flt64&gt;`: this is a variable item (`CombinationVariableItem`), which is not itself a linear intermediate symbol but
    /// **goes straight into a monomial** — build a row with `new LinearMonomial&lt;Flt64&gt;(Flt64.One, SymbolOf(...))`.
    /// </remarks>
    public Fuookami.Ospf.Math.Symbol.ISymbol SymbolOf(A action, int slotIndex) {
        ArgumentNullException.ThrowIfNull(action);
        if (_variables is null) {
            throw new InvalidOperationException(
                "产能决策变量尚未注册 / the capacity decision variables have not been registered yet");
        }
        int actionIndex = IndexOf(action);
        if (actionIndex < 0) {
            throw new ArgumentOutOfRangeException(nameof(action), "动作不在本矩阵里 / the action is not part of this matrix");
        }
        if (slotIndex < 0 || slotIndex >= _slots.Count) {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), "时隙下标越界 / the slot index is out of range");
        }
        return _variables[actionIndex, slotIndex];
    }

    /// <summary>
    /// 注册到模型 / Register into a model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    /// <remarks>
    /// 空动作或空时隙直接成功且**不建矩阵**：没有决策就没有变量，造一个 0×0 的矩阵只会让下游以为有东西可用。
    ///
    /// With no actions or no slots it succeeds and **builds no matrix**: no decision means no variable, and a 0x0 matrix would only make downstream code believe something is usable.
    /// </remarks>
    public Try Register(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_actions.Count == 0 || _slots.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        // **逐格注册变量项，而不是把矩阵当一个对象加进去**：`UIntVariable2` 本身不是 `IVariableItem`，
        // 模型只认格子。与 `PreciseAssignment` 的注册方式一致。
        // **Register the cells, not the matrix as one object**: `UIntVariable2` is not an `IVariableItem`; the model only accepts cells. This mirrors `PreciseAssignment`.
        _variables ??= new UIntVariable2(_name, _actions.Count, _slots.Count);
        for (int action = 0; action < _actions.Count; ++action) {
            for (int slot = 0; slot < _slots.Count; ++slot) {
                Try added = model.Add(_variables[action, slot]);
                if (!added.IsOk) {
                    return added;
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private int IndexOf(A action) {
        for (int index = 0; index < _actions.Count; ++index) {
            if (Equals(_actions[index], action)) {
                return index;
            }
        }
        return -1;
    }
}
