#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;

/// <summary>
/// 评估航班任务是否匹配流量控制标准的条件接口。
/// Condition interface for evaluating whether a flight task matches flow control criteria.
/// </summary>
public interface IFlowControlCondition {
    /// <summary>
    /// 评估给定任务是否匹配此条件。
    /// Evaluates whether the given task matches this condition.
    /// </summary>
    bool Invoke(FlightTask task);
}

/// <summary>
/// 按航班类型和飞机子机型过滤的具体流量控制条件。
/// Concrete flow control condition filtering by flight types and aircraft minor types.
/// </summary>
/// <param name="FlightTypes">航班类型集合 / Flight types.</param>
/// <param name="AircraftMinorTypes">飞机子类型集合 / Aircraft minor types.</param>
public sealed record FlowControlCondition(
    ISet<FlightType>? FlightTypes = null,
    ISet<AircraftMinorType>? AircraftMinorTypes = null
) : IFlowControlCondition {
    /// <inheritdoc/>
    public bool Invoke(FlightTask task) {
        if (!task.IsFlight) return false;
        if (FlightTypes is not null && FlightTypes.Count > 0) {
            var type = task is FlightLeg leg ? ((FlightLegPlan)leg.Plan).FlightType : FlightTypeExtensions.FromAirportTypes(task.Dep.Type, task.Arr.Type);
            if (!FlightTypes.Contains(type)) return false;
        }
        if (task.Aircraft is not null && AircraftMinorTypes is not null && AircraftMinorTypes.Count > 0) {
            if (!AircraftMinorTypes.Contains(task.Aircraft.MinorType)) return false;
        }
        return true;
    }
}

/// <summary>
/// 描述任务何时与机场交互的流量控制场景枚举。
/// Enumerates the flow control scenes describing when tasks interact with an airport.
/// </summary>
public enum FlowControlScene {
    /// <summary>出发 / Departure.</summary>
    Departure,
    /// <summary>到达 / Arrival.</summary>
    Arrival,
    /// <summary>出发和到达 / Departure and arrival.</summary>
    DepartureArrival,
    /// <summary>停留 / Stay.</summary>
    Stay
}

/// <summary>
/// 流量控制场景扩展方法。
/// Extension methods for flow control scene.
/// </summary>
public static class FlowControlSceneExtensions {
    /// <summary>
    /// 评估给定任务对在指定机场和时间是否匹配此场景。
    /// Evaluates whether the given task pair matches this scene at the specified airport and time.
    /// </summary>
    public static bool Matches(this FlowControlScene scene, FlightTask? prevTask, FlightTask? task, Airport airport, TimeRange time, IFlowControlCondition? condition = null) {
        if (task is null || (condition is not null && !condition.Invoke(task))) return false;
        return scene switch {
            FlowControlScene.Departure => task.DepartedWhen(airport, time),
            FlowControlScene.Arrival => task.ArrivedWhen(airport, time),
            FlowControlScene.DepartureArrival => task.DepartedWhen(airport, time) || task.ArrivedWhen(airport, time),
            FlowControlScene.Stay => task.LocatedWhen(prevTask!, airport, time),
            _ => false
        };
    }
}

/// <summary>
/// 流量控制的容量规格。
/// Capacity specification for a flow control.
/// </summary>
/// <param name="Amount">数量 / Amount.</param>
/// <param name="Interval">时间间隔 / Time interval.</param>
public sealed record FlowControlCapacity(ulong Amount, TimeSpan Interval) {
    /// <summary>是否关闭 / Whether closed.</summary>
    public bool Closed => Amount == 0;

    /// <summary>创建关闭的流量控制 / Create closed flow control.</summary>
    public static FlowControlCapacity Close(TimeRange time) => new(0, time.Duration);

    /// <inheritdoc/>
    public override string ToString() => Closed ? "closed" : $"{Amount}_{(int)Interval.TotalMinutes}m";
}

/// <summary>
/// 指定机场在给定场景和时间范围内容量限制的流量控制规则。
/// A flow control rule specifying capacity limits at an airport for a given scene and time range.
/// </summary>
public sealed record FlowControl(
    string Id,
    Airport Airport,
    TimeRange Time,
    FlowControlScene Scene,
    FlowControlCapacity Capacity,
    IFlowControlCondition? Condition = null
) {
    /// <summary>是否关闭 / Whether closed.</summary>
    public bool Closed => Capacity.Closed;

    /// <inheritdoc/>
    public override string ToString() => $"{Airport.Icao}_{Scene}_{Capacity}_{Time.Start.ToShortString()}_{Time.End.ToShortString()}";
}

/// <summary>
/// 表示机场特定场景的一组流量控制规则的流量资源。
/// A flow resource representing a set of flow control rules at an airport for a specific scene.
/// </summary>
public sealed class Flow {
    /// <summary>构造函数 / Constructor.</summary>
    public Flow(string id, Airport airport, FlowControlScene scene, IReadOnlyList<FlowControl> capacities) {
        Id = id;
        Airport = airport;
        Scene = scene;
        Capacities = capacities;
    }

    /// <summary>ID / ID.</summary>
    public string Id { get; }

    /// <summary>机场 / Airport.</summary>
    public Airport Airport { get; }

    /// <summary>场景 / Scene.</summary>
    public FlowControlScene Scene { get; }

    /// <summary>容量列表 / Capacities.</summary>
    public IReadOnlyList<FlowControl> Capacities { get; }
}
