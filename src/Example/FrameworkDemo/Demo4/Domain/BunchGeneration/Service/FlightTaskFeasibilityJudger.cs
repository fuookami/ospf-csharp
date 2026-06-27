#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Service;

/// <summary>
/// 检查给定前一任务时航班任务对飞机是否可行。
/// Checks if a flight task is feasible for an aircraft given the previous task.
/// </summary>
public sealed class FlightTaskFeasibilityJudger {
    private readonly IReadOnlyDictionary<Aircraft, AircraftUsability> _aircraftUsability;
    private readonly ConnectionTimeCalculator _connectionTimeCalculator;
    private readonly RuleChecker _ruleChecker;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    public FlightTaskFeasibilityJudger(
        IReadOnlyDictionary<Aircraft, AircraftUsability> aircraftUsability,
        ConnectionTimeCalculator connectionTimeCalculator,
        RuleChecker ruleChecker) {
        _aircraftUsability = aircraftUsability;
        _connectionTimeCalculator = connectionTimeCalculator;
        _ruleChecker = ruleChecker;
    }

    /// <summary>
    /// 可行性判断配置。
    /// Feasibility judgment configuration.
    /// </summary>
    /// <param name="CheckEnabledTime">是否检查启用时间 / Whether to check enabled time.</param>
    /// <param name="TimeExtractor">时间提取器 / Time extractor.</param>
    /// <param name="DepartureTime">出发时间 / Departure time.</param>
    public sealed record Config(
        bool CheckEnabledTime = true,
        Func<FlightTask, TimeRange?>? TimeExtractor = null,
        DateTimeOffset? DepartureTime = null
    );

    /// <summary>
    /// 检查给定航班任务对飞机是否可行。
    /// Checks if the given flight task is feasible for the aircraft.
    /// </summary>
    public bool Invoke(Aircraft aircraft, FlightTask? prevFlightTask, FlightTask flightTask, Config? config = null) {
        config ??= new Config();
        var timeExtractor = config.TimeExtractor ?? (t => t.ScheduledTime);

        if (!CheckAircraft(aircraft, flightTask)) return false;
        if (!CheckAircraftType(aircraft, flightTask)) return false;
        if (!CheckAircraftMinorType(aircraft, flightTask)) return false;
        if (!CheckAircraftCapacity(aircraft, flightTask)) return false;
        if (!CheckAircraftUsability(aircraft, prevFlightTask, flightTask, config)) return false;
        if (!CheckAirportConnection(aircraft, prevFlightTask, flightTask)) return false;
        if (!CheckFlyTime(aircraft, flightTask)) return false;
        if (!CheckTime(aircraft, prevFlightTask, flightTask, config, timeExtractor)) return false;
        if (!CheckTimeWindow(aircraft, prevFlightTask, flightTask, config, timeExtractor)) return false;
        if (!CheckRules(aircraft, prevFlightTask, flightTask)) return false;
        return true;
    }

    private bool CheckAircraft(Aircraft aircraft, FlightTask flightTask)
        => flightTask.AircraftChangeEnabled || flightTask.Aircraft == aircraft;

    private bool CheckAircraftType(Aircraft aircraft, FlightTask flightTask)
        => flightTask.AircraftTypeChangeEnabled || flightTask.Aircraft?.Type == aircraft.Type;

    private bool CheckAircraftMinorType(Aircraft aircraft, FlightTask flightTask)
        => flightTask.AircraftMinorTypeChangeEnabled || flightTask.Aircraft?.MinorType == aircraft.MinorType;

    private bool CheckAircraftCapacity(Aircraft aircraft, FlightTask flightTask)
        => !flightTask.IsFlight || flightTask.Capacity?.Category == aircraft.Capacity.Category;

    private bool CheckAircraftUsability(Aircraft aircraft, FlightTask? prevFlightTask, FlightTask flightTask, Config config) {
        if (prevFlightTask is not null) return true;
        if (!_aircraftUsability.TryGetValue(aircraft, out var usability)) return true;
        if (flightTask.Dep != usability.Location) return false;
        if (config.CheckEnabledTime) {
            var time = flightTask.ScheduledTime;
            if (time is not null && time.Start < usability.EnabledTime) return false;
        }
        return true;
    }

    private bool CheckAirportConnection(Aircraft aircraft, FlightTask? prevFlightTask, FlightTask flightTask) {
        if (prevFlightTask is null) return true;
        if (prevFlightTask.Arr == flightTask.Dep) return true;
        var arr = new List<Airport> { prevFlightTask.Arr };
        arr.AddRange(prevFlightTask.ArrBackup);
        var dep = new List<Airport> { flightTask.Dep };
        dep.AddRange(flightTask.DepBackup);
        foreach (var airport in arr) {
            if (dep.Contains(airport)) return true;
        }
        return false;
    }

    private bool CheckFlyTime(Aircraft aircraft, FlightTask flightTask) {
        if (!flightTask.IsFlight) return true;
        var duration = flightTask.Duration;
        var maxFlyTime = aircraft.MaxFlyTime;
        return duration is null || maxFlyTime is null || duration.Value <= maxFlyTime.Value;
    }

    private bool CheckTime(Aircraft aircraft, FlightTask? prevFlightTask, FlightTask flightTask, Config config, Func<FlightTask, TimeRange?> timeExtractor) {
        if (prevFlightTask is null) return true;
        var prevTime = timeExtractor(prevFlightTask);
        var time = timeExtractor(flightTask);
        if (prevTime is null || time is null) return true;
        var lastBeginTime = flightTask.LatestNormalStartTime(aircraft);
        if (time.Start > lastBeginTime) return false;
        return prevTime.Start < time.Start;
    }

    private bool CheckTimeWindow(Aircraft aircraft, FlightTask? prevFlightTask, FlightTask flightTask, Config config, Func<FlightTask, TimeRange?> timeExtractor) {
        if (prevFlightTask is null) return true;
        var prevTime = timeExtractor(prevFlightTask);
        var time = timeExtractor(flightTask);
        if (prevTime is not null && time is not null) return true;
        var prevTW = prevFlightTask.TimeWindow;
        var tw = flightTask.TimeWindow;
        if (prevTime is not null && tw is not null) {
            var minDep = prevTime.Start + prevFlightTask.DurationFor(aircraft) + _connectionTimeCalculator(aircraft, prevFlightTask, flightTask);
            var maxDep = tw.End - flightTask.DurationFor(aircraft);
            return minDep <= maxDep;
        }
        if (time is not null && prevTW is not null) return prevTW.Start <= time.Start;
        if (prevTW is not null && tw is not null) {
            var minDep = prevTW.Start + prevFlightTask.DurationFor(aircraft) + _connectionTimeCalculator(aircraft, prevFlightTask, flightTask);
            var maxDep = tw.End - flightTask.DurationFor(aircraft);
            return minDep <= maxDep;
        }
        return false;
    }

    private bool CheckRules(Aircraft aircraft, FlightTask? prevFlightTask, FlightTask flightTask) {
        if (prevFlightTask is null || !prevFlightTask.IsFlight || !flightTask.IsFlight) return true;
        return _ruleChecker(aircraft, prevFlightTask, flightTask);
    }
}
