#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;

/// <summary>
/// 产能排序的决策变量层 / The capacity-order decision-variable layer.
/// </summary>
/// <remarks>
/// 基线的 `CapacityOrderCompilation` 在 `register` 里建两个三维量：
///
/// * `x[动作, 时隙, 顺序位]`——加工**数量**（非负整数），每格上界是 `action.upperBound(slot, timeWindow)`；
/// * `b[动作, 时隙, 顺序位]`——**是否选中**（二元）。
///
/// `OrderConstraint` 的三条约束（每位置至多一个动作、`b` 与 `x` 的上下界关联）全部建在这两个量上。C# 侧两个量都是 `null`，
/// 所以那个 `Invoke` 只能写 `=> Ok`。这里把量建起来，后续才能建行。
///
/// The baseline's `CapacityOrderCompilation` builds two three-dimensional quantities in `register`: `x[action, slot, order position]`, the processed **amount** (non-negative integer)
/// with a per-cell upper bound of `action.upperBound(slot, timeWindow)`, and `b[action, slot, order position]`, **whether it is selected** (binary). `OrderConstraint`'s three
/// constraint families (at most one action per position; linking `b` and `x` from below and above) are all built on these two quantities. Both are `null` on the C# side, which is why
/// that `Invoke` can only be `=> Ok`. builds the quantities so the next one can build the rows.
/// </remarks>
/// <typeparam name="A">动作类型 / The action type.</typeparam>
public sealed class CapacityOrderVariables<A>
    where A : IProductionAction {
    private readonly IReadOnlyList<A> _actions;

    private readonly IReadOnlyList<TimeRange> _slots;

    private readonly TimeWindow<Flt64> _timeWindow;

    private readonly ulong _maxOrderPerSlot;

    private readonly string _name;

    private UIntVar3? _amounts;

    private BinVariable3? _selections;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="actions">动作列表 / The actions.</param>
    /// <param name="slots">时隙列表 / The slots.</param>
    /// <param name="timeWindow">时间窗口 / The time window.</param>
    /// <param name="maxOrderPerSlot">每个时隙的最大顺序位数 / The maximum number of order positions per slot.</param>
    /// <param name="name">名称前缀 / The name prefix.</param>
    public CapacityOrderVariables(
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow,
        ulong maxOrderPerSlot,
        string name = "order") {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(slots);
        _actions = actions;
        _slots = slots;
        _timeWindow = timeWindow;
        _maxOrderPerSlot = maxOrderPerSlot;
        _name = name;
    }

    /// <summary>动作列表 / The actions.</summary>
    public IReadOnlyList<A> Actions => _actions;

    /// <summary>时隙列表 / The slots.</summary>
    public IReadOnlyList<TimeRange> Slots => _slots;

    /// <summary>时间窗口 / The time window.</summary>
    public TimeWindow<Flt64> TimeWindow => _timeWindow;

    /// <summary>每时隙最大顺序位数 / The maximum number of order positions per slot.</summary>
    public ulong MaxOrderPerSlot => _maxOrderPerSlot;

    /// <summary>加工数量立方；注册前为 `null` / The amount cube; `null` before registration.</summary>
    public UIntVar3? Amounts => _amounts;

    /// <summary>选中标记立方；注册前为 `null` / The selection cube; `null` before registration.</summary>
    public BinVariable3? Selections => _selections;

    /// <summary>
    /// 数量变量的符号 / The symbol of an amount variable.
    /// </summary>
    /// <param name="actionIndex">动作下标 / The action index.</param>
    /// <param name="slotIndex">时隙下标 / The slot index.</param>
    /// <param name="orderIndex">顺序位下标 / The order-position index.</param>
    /// <returns>符号 / The symbol.</returns>
    public Fuookami.Ospf.Math.Symbol.ISymbol AmountOf(int actionIndex, int slotIndex, int orderIndex) =>
        Cell(_amounts, actionIndex, slotIndex, orderIndex, "数量 / amount");

    /// <summary>
    /// 选中变量的符号 / The symbol of a selection variable.
    /// </summary>
    /// <param name="actionIndex">动作下标 / The action index.</param>
    /// <param name="slotIndex">时隙下标 / The slot index.</param>
    /// <param name="orderIndex">顺序位下标 / The order-position index.</param>
    /// <returns>符号 / The symbol.</returns>
    public Fuookami.Ospf.Math.Symbol.ISymbol SelectionOf(int actionIndex, int slotIndex, int orderIndex) =>
        Cell(_selections, actionIndex, slotIndex, orderIndex, "选中 / selection");

    /// <summary>
    /// 注册到模型 / Register into a model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    /// <remarks>
    /// 每格 `x` 的上界来自 `action.upperBound(slot, timeWindow)`——**这是基线 `register` 里唯一一处业务上界**，
    /// 少了它，`OrderConstraint` 的 `x ≤ M · b` 就只能猜一个 `M`，猜大了解松弛、猜小了切掉可行解。
    /// `b` 不需要额外设置：`BinVariable3` 的值域本来就是 0/1。
    ///
    /// Each `x` cell's upper bound comes from `action.upperBound(slot, timeWindow)` — **the only business upper bound in the baseline's `register`**. Without it,
    /// `OrderConstraint` could only guess the big-M linking factor: too large relaxes the model, too small cuts off feasible solutions. `b` needs nothing extra; `BinVariable3`
    /// already ranges over 0 and 1.
    /// </remarks>
    public Try Register(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        int orderCount = checked((int)_maxOrderPerSlot);
        if (_actions.Count == 0 || _slots.Count == 0 || orderCount == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        _amounts ??= new UIntVar3($"{_name}_x", _actions.Count, _slots.Count, orderCount);
        _selections ??= new BinVariable3($"{_name}_b", _actions.Count, _slots.Count, orderCount);

        for (int action = 0; action < _actions.Count; ++action) {
            for (int slot = 0; slot < _slots.Count; ++slot) {
                ulong upperBound = _actions[action].UpperBound(_slots[slot], _timeWindow);
                for (int order = 0; order < orderCount; ++order) {
                    _amounts[action, slot, order].Range.Leq(new Fuookami.Ospf.Math.Algebra.Number.UInt64(upperBound));
                    Try addedAmount = model.Add(_amounts[action, slot, order]);
                    if (!addedAmount.IsOk) {
                        return addedAmount;
                    }
                    Try addedSelection = model.Add(_selections[action, slot, order]);
                    if (!addedSelection.IsOk) {
                        return addedSelection;
                    }
                }
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static Fuookami.Ospf.Math.Symbol.ISymbol Cell<TVariable>(
        TVariable? variables,
        int actionIndex,
        int slotIndex,
        int orderIndex,
        string what)
        where TVariable : class {
        if (variables is null) {
            throw new InvalidOperationException(
                $"排序变量尚未注册，取不到{what} / the order variables are not registered, so {what} cannot be taken");
        }
        return variables switch {
            UIntVar3 amounts => amounts[actionIndex, slotIndex, orderIndex],
            BinVariable3 selections => selections[actionIndex, slotIndex, orderIndex],
            _ => throw new ArgumentOutOfRangeException(nameof(variables), "未知的立方类型 / unknown cube type"),
        };
    }
}
