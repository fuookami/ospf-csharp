#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task;

/// <summary>
/// 组合机场、飞机、航段和原始批次的任务域聚合。
/// Aggregation for the task domain combining airports, aircraft, flight legs, and origin bunches.
/// </summary>
/// <param name="TimeWindow">时间窗口 / Time window.</param>
/// <param name="Airports">机场列表 / Airports.</param>
/// <param name="Aircrafts">飞机列表 / Aircrafts.</param>
/// <param name="AircraftUsability">飞机可用性 / Aircraft usability.</param>
/// <param name="Legs">航段列表 / Flight legs.</param>
/// <param name="Maintenances">维护列表 / Maintenances.</param>
/// <param name="Aogs">AOG 列表 / AOGs.</param>
/// <param name="TransferFlights">中转航班列表 / Transfer flights.</param>
/// <param name="OriginBunches">原始批次 / Origin bunches.</param>
public sealed class Aggregation(
    TimeRange TimeWindow,
    IReadOnlyList<Airport> Airports,
    IReadOnlyList<Aircraft> Aircrafts,
    IReadOnlyDictionary<Aircraft, AircraftUsability> AircraftUsability,
    IReadOnlyList<FlightLeg> Legs,
    IReadOnlyList<Maintenance> Maintenances,
    IReadOnlyList<Aog> Aogs,
    IReadOnlyList<Transfer> TransferFlights,
    IReadOnlyList<FlightTaskBunch> OriginBunches
) {
    /// <summary>所有航班任务 / All flight tasks.</summary>
    public IReadOnlyList<FlightTask> FlightTasks { get; } = BuildFlightTasks(Legs, Maintenances, Aogs, TransferFlights);

    /// <summary>
    /// 检查飞机是否对给定的恢复策略和任务启用。
    /// Checks whether the aircraft is enabled for the given recovery policy and task.
    /// </summary>
    public bool Enabled(Aircraft aircraft, FlightTaskAssignment recoveryPolicy, FlightTask? task = null) {
        if (!AircraftUsability.TryGetValue(aircraft, out var usability)) return false;
        var time = recoveryPolicy.Time?.End ?? task?.Time?.End;
        return time is not null && usability.EnabledTime <= time.Value;
    }

    private static IReadOnlyList<FlightTask> BuildFlightTasks(
        IReadOnlyList<FlightLeg> legs,
        IReadOnlyList<Maintenance> maintenances,
        IReadOnlyList<Aog> aogs,
        IReadOnlyList<Transfer> transfers) {
        var tasks = new List<FlightTask>(legs.Count + maintenances.Count + aogs.Count + transfers.Count);
        tasks.AddRange(legs);
        tasks.AddRange(maintenances);
        tasks.AddRange(aogs);
        tasks.AddRange(transfers);
        return tasks;
    }
}
