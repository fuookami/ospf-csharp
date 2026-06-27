#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crews;

/// <summary>
/// 机组域对象聚合。
/// Aggregation of crew domain objects.
/// </summary>
/// <param name="Crews">机组列表 / Crews.</param>
/// <param name="CrewSchedules">排班列表 / Crew schedules.</param>
/// <param name="TransitTimes">中转时间映射 / Transit times.</param>
public sealed class Aggregation(
    IReadOnlyList<global::Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model.Crew> Crews,
    IReadOnlyList<CrewSchedule> CrewSchedules,
    IReadOnlyDictionary<TransitTimeScene, TransitTime> TransitTimes
);

/// <summary>
/// 机组域操作的上下文。
/// Context for crew domain operations.
/// </summary>
public sealed class CrewContext {
    /// <summary>聚合 / Aggregation.</summary>
    public required Aggregation Aggregation { get; set; }
}
