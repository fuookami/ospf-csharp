#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Service;

/// <summary>
/// 使用图、可反转对和初始批次初始化批次生成聚合。
/// Initializes the bunch generation aggregation with graphs, reverse pairs, and initial bunches.
/// </summary>
public sealed class AggregationInitializer {
    private static readonly FlightTaskFeasibilityJudger.Config Config = new(
        CheckEnabledTime: false,
        TimeExtractor: t => t.Time
    );

    /// <summary>
    /// 初始化批次生成的聚合。
    /// Initializes the aggregation for bunch generation.
    /// </summary>
    public Result<Aggregation, ErrorCode, Error<ErrorCode>> Invoke(
        IReadOnlyList<Aircraft> aircrafts,
        IReadOnlyDictionary<Aircraft, AircraftUsability> aircraftUsability,
        IReadOnlyList<FlightTask> flightTasks,
        IReadOnlyList<FlightTaskBunch> originBunches,
        Lock lockObj,
        FlightTaskFeasibilityJudger feasibilityJudger,
        InitialFlightTaskBunchGenerator initialGenerator,
        bool withOrderChange = false) {
        var reverse = InitReverseEnabledFlight(flightTasks, originBunches, lockObj, withOrderChange);
        var flightTaskGroups = GroupFlightTasks(flightTasks);
        var graphGenerator = new RouteGraphGenerator(reverse, new RouteGraphConfiguration(withOrderChange),
            (aircraft, prev, succ) => feasibilityJudger.Invoke(aircraft, prev, succ, Config));
        var graphs = GenerateGraphs(aircrafts, aircraftUsability, flightTaskGroups, graphGenerator);
        var initialBunches = GenerateInitialBunches(aircrafts, aircraftUsability, flightTasks, originBunches, initialGenerator);
        return Results.Ok(new Aggregation(graphs, reverse, initialBunches));
    }

    private static IReadOnlyDictionary<Airport, IReadOnlyList<FlightTask>> GroupFlightTasks(IReadOnlyList<FlightTask> flightTasks) {
        var groups = new Dictionary<Airport, List<FlightTask>>();
        foreach (var task in flightTasks) {
            if (!groups.TryGetValue(task.Dep, out var list)) { list = new(); groups[task.Dep] = list; }
            list.Add(task);
            foreach (var dep in task.DepBackup) {
                if (!groups.TryGetValue(dep, out var backupList)) { backupList = new(); groups[dep] = backupList; }
                backupList.Add(task);
            }
        }
        foreach (var list in groups.Values) list.Sort((a, b) => (a.Time?.Start ?? a.TimeWindow?.Start ?? DateTimeOffset.MaxValue).CompareTo(b.Time?.Start ?? b.TimeWindow?.Start ?? DateTimeOffset.MaxValue));
        return groups.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<FlightTask>)kv.Value);
    }

    private static FlightTaskReverse InitReverseEnabledFlight(
        IReadOnlyList<FlightTask> flightTasks, IReadOnlyList<FlightTaskBunch> originBunches, Lock lockObj, bool withOrderChange) {
        var timeDiffLimit = FlightTaskReverse.DefaultTimeDifferenceLimit;
        var pairs = new List<(FlightTask, FlightTask)>();
        if (withOrderChange && flightTasks.Count >= 2) {
            while (true) {
                pairs.Clear();
                for (int i = 0; i < flightTasks.Count; i++) {
                    for (int j = i + 1; j < flightTasks.Count; j++) {
                        if (FlightTaskReverse.Symmetrical(flightTasks[i], flightTasks[j], lockObj, timeDiffLimit + TimeSpan.FromHours(2.5))) {
                            pairs.Add((flightTasks[i], flightTasks[j]));
                        } else {
                            if (FlightTaskReverse.ReverseEnabled(flightTasks[i], flightTasks[j], lockObj, timeDiffLimit)) pairs.Add((flightTasks[i], flightTasks[j]));
                            if (FlightTaskReverse.ReverseEnabled(flightTasks[j], flightTasks[i], lockObj, timeDiffLimit)) pairs.Add((flightTasks[j], flightTasks[i]));
                        }
                    }
                }
                if (timeDiffLimit == TimeSpan.Zero) break;
                if (pairs.Count <= (int)FlightTaskReverse.CriticalSize && timeDiffLimit <= TimeSpan.FromHours(3)) break;
                timeDiffLimit -= TimeSpan.FromHours(1);
            }
        }
        return FlightTaskReverse.Create(pairs, originBunches, lockObj, timeDiffLimit);
    }

    private static IReadOnlyDictionary<Aircraft, Graph> GenerateGraphs(
        IReadOnlyList<Aircraft> aircrafts, IReadOnlyDictionary<Aircraft, AircraftUsability> aircraftUsability,
        IReadOnlyDictionary<Airport, IReadOnlyList<FlightTask>> flightTaskGroups, RouteGraphGenerator generator) {
        var graphs = new Dictionary<Aircraft, Graph>();
        foreach (var aircraft in aircrafts) {
            graphs[aircraft] = generator.Invoke(aircraft, aircraftUsability[aircraft], flightTaskGroups);
        }
        return graphs;
    }

    private static IReadOnlyList<FlightTaskBunch> GenerateInitialBunches(
        IReadOnlyList<Aircraft> aircrafts, IReadOnlyDictionary<Aircraft, AircraftUsability> aircraftUsability,
        IReadOnlyList<FlightTask> flightTasks, IReadOnlyList<FlightTaskBunch> originBunches, InitialFlightTaskBunchGenerator generator) {
        var generated = new HashSet<Aircraft>();
        var bunches = new List<FlightTaskBunch>();
        foreach (var bunch in originBunches) {
            if (!aircrafts.Contains(bunch.Executor)) continue;
            var locked = flightTasks.Where(t => IsLocked(t, bunch.Executor)).OrderBy(t => t.Time?.Start).ToList();
            var newBunch = generator.Invoke(bunch.Executor, aircraftUsability[bunch.Executor], locked, bunch);
            if (newBunch is not null) { generated.Add(bunch.Executor); bunches.Add(newBunch); }
        }
        foreach (var aircraft in aircrafts) {
            if (generated.Contains(aircraft)) continue;
            var locked = flightTasks.Where(t => IsLocked(t, aircraft)).OrderBy(t => t.Time?.Start).ToList();
            var newBunch = generator.EmptyBunch(aircraft, aircraftUsability[aircraft], locked);
            if (newBunch is not null) bunches.Add(newBunch);
        }
        return bunches;
    }

    private static bool IsLocked(FlightTask task, Aircraft aircraft)
        => !task.CancelEnabled && !task.AircraftChangeEnabled && task.Aircraft == aircraft;
}
