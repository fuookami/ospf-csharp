// 每个机身区域的最大允许载荷重量 / The maximum allowable load weight per fuselage zone.

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 区域载荷限制部分 / A part contributing to a zone's load weight.
/// </summary>
/// <param name="PositionIndex">位置索引 / The position index.</param>
/// <param name="Weight">重量系数 / The weight coefficient of this part.</param>
/// <remarks>
/// 系数不是 1：一个舱位可以同时落在多个限区里，每个限区按**重叠比例**分它的一部分重量。区域完全覆盖舱位时系数为 1，
/// 只覆盖一半时为 0.5。当成 1 会让每个区域都算上整个舱位的重量——区域越多，约束越假。
///
/// The coefficient is not 1: a position can sit in several zones, and each zone takes a share of its weight in proportion to the **overlap**. Full coverage gives 1, half coverage
/// gives 0.5. Treating it as 1 charges every zone the position's whole weight — the more zones, the more fictitious the constraint.
/// </remarks>
public sealed record ZoneLoadLimitPart(int PositionIndex, double Weight);

/// <summary>
/// 区域载荷限制区域 / A zone with a maximum load weight.
/// </summary>
/// <param name="Name">名称 / The name.</param>
/// <param name="MaxLoadWeight">最大载荷重量（千克）/ The maximum load weight, in kilogram.</param>
/// <param name="Parts">限制部分 / The contributing parts.</param>
/// <param name="Liferaft">此区域内的救生筏，可为空 / The liferaft in this zone, nullable.</param>
public sealed record ZoneLoadLimitZone(
    string Name,
    double MaxLoadWeight,
    IReadOnlyList<ZoneLoadLimitPart> Parts,
    double? LiferaftWeight = null);

/// <summary>
/// 每个机身区域的最大允许载荷重量 / The maximum allowable load weight per fuselage zone.
/// </summary>
/// <remarks>
/// 与对齐。`LimitZone` 持有 `Liferaft?` 对象，
/// 上限只用到它的**重量**，所以这里直接存重量：持有整个对象会让"救生筏在哪"与"区域装了多少"耦合，而后者才是这个类管的事。
///
/// The code's `LimitZone` holds a `Liferaft?` and the limit only uses its **weight**, so the weight is stored directly: holding the object would
/// couple "where the liferaft is" to "how much a zone carries", and only the latter is this class's business.
/// </remarks>
public sealed class MaxZoneLoadWeight
{
    /// <summary>限制区域 / The limit zones</summary>
    public IReadOnlyList<ZoneLoadLimitZone> LimitZones { get; init; } = Array.Empty<ZoneLoadLimitZone>();
}
