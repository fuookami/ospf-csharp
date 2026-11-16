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
) : Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model.IAbstractResourceCapacity<Flt64> {
    /// <summary>
    /// 数量下界 / The quantity lower bound.
    /// </summary>
    /// <remarks>
    /// `FlowControl` 是 `FltX` 上的抽象资源容量，`quantityRangeValue` 从 0 到额度。
    /// C# 侧它此前只是个规则记录，**当不了容量**——所以 `Flow` 也就当不了 `ConnectionResource`。
    ///
    /// In the code `FlowControl` is an `AbstractResourceCapacity` over `FltX`, with a quantity range from 0 to the amount. On the C# side it was only a rule record and could not
    /// serve as a capacity — which is why `Flow` could not be a `ConnectionResource` either.
    /// </remarks>
    public Flt64 LowerBound => Flt64.Zero;

    /// <summary>数量上界 / The quantity upper bound.</summary>
    public Flt64 UpperBound => new(Capacity.Amount);

    /// <inheritdoc/>
    TimeSpan Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model.IAbstractResourceCapacity<Flt64>.Interval => Capacity.Interval;

    /// <summary>是否关闭 / Whether closed.</summary>
    public bool Closed => Capacity.Closed;

    /// <inheritdoc/>
    public override string ToString() => $"{Airport.Icao}_{Scene}_{Capacity}_{Time.Start.ToShortString()}_{Time.End.ToShortString()}";
}

/// <summary>
/// 表示机场特定场景的一组流量控制规则的流量资源。
/// A flow resource representing a set of flow control rules at an airport for a specific scene.
/// </summary>
/// <remarks>
/// **它此前只是个普通类**：有 `Id`/`Airport`/`Scene`/`Capacities`，却不继承 `ConnectionResource`，因此**当不了资源**——
/// 它是连接资源（容量类型是 `FlowControl`），会被装进 `BunchSchedulingConnectionResourceUsage` 变成连接资源用量。
/// 一个"长得像资源、但不是资源"的类，比完全没有这个类更难发现。
///
/// **It used to be a plain class**: it had `Id`/`Airport`/`Scene`/`Capacities` yet did not derive from `ConnectionResource`, so **it could not serve as a resource** — in the code it
/// is a connection resource whose capacity type is `FlowControl`, wired into `BunchSchedulingConnectionResourceUsage` as connection usage. A class that looks like a resource without being one is
/// harder to spot than a missing class.
/// </remarks>
public sealed class Flow : Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model.ConnectionResource<FlowControl, Flt64> {
    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="id">标识 / The id.</param>
    /// <param name="airport">机场 / The airport.</param>
    /// <param name="scene">场景 / The scene.</param>
    /// <param name="capacities">容量列表 / The capacities.</param>
    public Flow(string id, Airport airport, FlowControlScene scene, IReadOnlyList<FlowControl> capacities) {
        Id = id;
        Airport = airport;
        Scene = scene;
        Capacities = capacities;
    }

    /// <summary>ID / ID.</summary>
    public override string Id { get; }

    /// <summary>名称 / The name.</summary>
    public override string Name => $"{Airport.Icao}_flow";

    /// <summary>机场 / Airport.</summary>
    public Airport Airport { get; }

    /// <summary>场景 / Scene.</summary>
    public FlowControlScene Scene { get; }

    /// <summary>容量列表 / Capacities.</summary>
    public override IReadOnlyList<FlowControl> Capacities { get; }

    /// <summary>初始数量 / The initial quantity.</summary>
    public override Flt64 InitialQuantityValue => Flt64.Zero;

    /// <inheritdoc/>
    public override Flt64 UsedQuantity<T, E, A>(
        Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.AbstractTaskBunch<T, E, A> bunch,
        TimeRange time) => UsedBy<T, E, A>(default!, default!, time);

    /// <inheritdoc/>
    /// <remarks>
    /// **按命中的条件数计**：`capacities.count { scene(...) }`，不是把各条容量加起来。也就是说"这个时间范围内有
    /// 多少条规则同时生效"，而不是"这些规则一共给了多少额度"。
    ///
    /// **Counts the conditions that fire**: the code is `capacities.count { scene(...) }`, not a sum of the amounts. It answers "how many rules are active in this range", not
    /// "how much allowance do they add up to".
    /// </remarks>
#pragma warning disable CS8765
    public override Flt64 UsedBy<T, E, A>(T prevTask, T task, TimeRange time) {
        int fired = 0;
        foreach (FlowControl capacity in Capacities) {
            if (capacity.Scene.Matches(prevTask as FlightTask, task as FlightTask, Airport, time, capacity.Condition)) {
                ++fired;
            }
        }
        return new Flt64(fired);
    }
#pragma warning restore CS8765
}
