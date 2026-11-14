#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;

/// <summary>
/// 产能决策变量的只读视图 / A read-only view of the capacity decision variables.
/// </summary>
/// <remarks>
/// 资源用量表是 `R, C, V` 泛型的，而决策变量是 `E, A` 泛型的——直接用会把 `E` / `A` 传染给整条资源链。
/// 中间加一个非泛型视图，两边都不必知道对方的泛型参数。
///
/// **这是这张表唯一的合法来源**：没有它就填不出用量（`Register` 会显式失败），而不是填一张空表假装成功。
///
/// The resource usage table is generic over `R, C, V` while the decision variables are generic over `E, A`; using them directly would infect the whole resource chain with `E` and `A`.
/// A non-generic view in between keeps each side ignorant of the other's type parameters. **This is the table's only legitimate source**: without it the usage cannot be filled
/// (`Register` fails explicitly) rather than filling an empty table while pretending success.
/// </remarks>
public interface ICapacityAmountSource {
    /// <summary>动作时隙网格 / The action-slot grid.</summary>
    IReadOnlyList<TimeRange> ActionSlots { get; }

    /// <summary>参与决策的动作 / The actions that take part in the decision.</summary>
    IReadOnlyList<IProductionAction> Actions { get; }

    /// <summary>
    /// 注册之前不给出任何符号 / Gives no symbol before registration.
    /// </summary>
    bool IsRegistered { get; }

    /// <summary>
    /// 取一个动作在一个动作时隙上的决策变量 / Take an action's decision variable on one action slot.
    /// </summary>
    /// <param name="action">动作 / The action.</param>
    /// <param name="slotIndex">动作时隙下标 / The action-slot index.</param>
    /// <returns>符号 / The symbol.</returns>
    Fuookami.Ospf.Math.Symbol.ISymbol AmountOf(IProductionAction action, int slotIndex);
}
