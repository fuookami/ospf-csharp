#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Model;

/// <summary>
/// 绝对优先级系数 / The absolute priority coefficients.
/// </summary>
/// <remarks>
/// 与基线 `express_effectiveness/model/AbsoluteOrder.kt` 对齐：**每个优先级配一个按舱位取值的函数**，用于预配载模式下的最大化目标。
///
/// 三处必须照抄：
///
/// 1. **系数公式是 `(优先级 + 1) × (最大装载顺序 − 该舱位顺序 + 1)`**。两项都是**加一**，不是不加。
///    少一个加一会让优先级 0 或装载顺序最大的舱位系数变成 0，整个偏好消失。
/// 2. **`maxOfOrNull` 为空时回退 0**。没有舱位时最大装载顺序取 0，而不是抛异常。
/// 3. **未登记的优先级回退 1**（基线 `?: Flt64.one`），不是 0。返回 0 会让那种货物在目标里没有权重。
///
/// Aligned with the baseline: **one function per priority**, mapping a position to its coefficient, used by the predistribution maximisation objective. Three things must be copied:
/// **the formula is `(priority + 1) × (maxLoadingOrder − thisOrder + 1)`** — both terms have a plus one, and dropping either zeroes the coefficient for priority 0 or for the
/// highest-order position, erasing the preference; **an empty `maxOfOrNull` falls back to 0** rather than throwing; and **an unregistered priority falls back to 1** (the baseline's
/// `?: Flt64.one`), not 0, which would leave that cargo weightless in the objective.
/// </remarks>
public sealed class AbsoluteOrder
{
    private readonly IReadOnlyDictionary<CargoPriority, Func<StowagePosition, Flt64>> _coefficient;

    /// <summary>
    /// 构造绝对优先级系数 / Construct the absolute priority coefficients.
    /// </summary>
    /// <param name="coefficient">优先级到函数的映射 / The priority-to-function mapping.</param>
    public AbsoluteOrder(IReadOnlyDictionary<CargoPriority, Func<StowagePosition, Flt64>> coefficient)
    {
        ArgumentNullException.ThrowIfNull(coefficient);
        _coefficient = coefficient;
    }

    /// <summary>优先级到函数的映射 / The priority-to-function mapping</summary>
    public IReadOnlyDictionary<CargoPriority, Func<StowagePosition, Flt64>> Coefficient => _coefficient;

    /// <summary>
    /// 从货物与舱位派生 / Derive from the items and positions.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <returns>绝对优先级系数 / The absolute priority coefficients.</returns>
    public static AbsoluteOrder From(IReadOnlyList<Item> items, IReadOnlyList<StowagePosition> positions)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);

        byte latestLoadingOrder = 0;
        foreach (StowagePosition position in positions)
        {
            byte order = OrderOf(position);
            if (order > latestLoadingOrder)
            {
                latestLoadingOrder = order;
            }
        }

        var coefficient = new Dictionary<CargoPriority, Func<StowagePosition, Flt64>>();
        foreach (Item item in items)
        {
            CargoPriority priority = item.Priority;
            if (coefficient.ContainsKey(priority))
            {
                continue;
            }
            coefficient[priority] = position =>
            {
                // `(max − order + 1) × (priority + 1)`，两处加一都不能省。
                // `(max − order + 1) × (priority + 1)`; neither plus one may be dropped.
                var positionPreference = new Flt64(latestLoadingOrder - OrderOf(position) + 1.0);
                // `UInt64.Value` 是 protected，用 `ToFlt64()`。
                // `UInt64.Value` is protected, so `ToFlt64()` is used.
                return (priority.Priority.ToFlt64() + Flt64.One) * positionPreference;
            };
        }
        return new AbsoluteOrder(coefficient);
    }

    /// <summary>
    /// 取系数 / Fetch the coefficient.
    /// </summary>
    /// <param name="priority">优先级 / The priority.</param>
    /// <param name="position">舱位 / The position.</param>
    /// <returns>系数 / The coefficient.</returns>
    public Flt64 Invoke(CargoPriority priority, StowagePosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        // **未登记的优先级回退 1**，不是 0。
        // **An unregistered priority falls back to 1**, not 0.
        return _coefficient.TryGetValue(priority, out Func<StowagePosition, Flt64>? compute)
            ? compute(position)
            : Flt64.One;
    }

    /// <summary>
    /// 取舱位的装载顺序 / The position's loading order.
    /// </summary>
    /// <param name="position">舱位 / The position.</param>
    /// <returns>装载顺序 / The loading order.</returns>
    private static byte OrderOf(StowagePosition position) =>
        position.AircraftPosition.LoadingOrder?.Order ?? position.AircraftPosition.LinearLoadingOrder;
}
