#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model;

/// <summary>
/// 机组成员的排班，将航班任务映射到其分配的职级。
/// A crew member's schedule mapping flight tasks to their assigned rank.
/// </summary>
/// <param name="CrewMan">机组成员 / Crew man.</param>
/// <param name="Schedules">排班映射 / Schedule mapping.</param>
public sealed record CrewSchedule(
    IAbstractCrewMan CrewMan,
    IReadOnlyDictionary<FlightTask, object> Schedules
);
