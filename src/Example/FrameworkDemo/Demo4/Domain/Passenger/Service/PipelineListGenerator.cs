#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using PassengerAggregation = Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Aggregation;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service;

/// <summary>
/// 乘客域的管线清单 / The passenger-domain pipeline list.
/// </summary>
/// <remarks>
/// ：**三条约束在前、三条目标在后**，顺序固定。
///
/// 顺序在这里有语义：先约束后目标，求解器拿到的才是"在可行域里最小化"；顺序反了不会报错，
/// 只会让列生成的定价失去可用的对偶信息——**而那是"求解成功但改不动"的形态**。
///
/// 与批次汇编那一份生成器一样，系数不从某个全局 `Parameter` 里摸，而是逐个传进来：
/// **一个供 6 条管线共用的全局系数表，改一处影响全部，排查时说不清是谁的代价**。
///
/// The code's puts **three constraints first and three objectives after**, in a fixed order.
///
/// The order carries meaning here: constraints before objectives is what makes the solver "minimise inside the feasible region"; reversing it raises nothing and merely leaves column
/// generation's pricing without usable dual information — **the "it solved but nothing moves" shape**.
///
/// Like the bunch-compilation generator, the coefficients are passed in instead of read from one global `Parameter`: **a single coefficient table shared by six pipelines makes one edit
/// touch everything, and nobody can tell whose cost changed**.
/// </remarks>
public sealed class PipelineListGenerator {
    private readonly PassengerAggregation _aggregation;

    private readonly FlightCapacity _capacity;

    private readonly TimeWindow<Flt64> _timeWindow;

    private readonly BunchSchedulingTaskTime<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> _time;

    private readonly Func<FlightPassenger, Flt64>? _cancelCoefficient;

    private readonly Func<FlightPassenger, PassengerClassId, Flt64>? _classChangeCoefficient;

    private readonly Func<FlightTask, FlightTask, PassengerClassId, Flt64>? _flightChangeCoefficient;

    private readonly string _prefix;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="aggregation">乘客域聚合 / The passenger aggregation.</param>
    /// <param name="capacity">航班容量层 / The flight capacity layer.</param>
    /// <param name="timeWindow">时间窗 / The time window.</param>
    /// <param name="time">任务时间层 / The task time layer.</param>
    /// <param name="cancelCoefficient">取消代价，缺省 1 / The cancellation cost, 1 when omitted.</param>
    /// <param name="classChangeCoefficient">舱位变更代价，缺省 1 / The cabin-change cost, 1 when omitted.</param>
    /// <param name="flightChangeCoefficient">航班变更代价，缺省 1 / The flight-change cost, 1 when omitted.</param>
    /// <param name="prefix">管线名前缀 / The pipeline name prefix.</param>
    public PipelineListGenerator(
        PassengerAggregation aggregation,
        FlightCapacity capacity,
        TimeWindow<Flt64> timeWindow,
        BunchSchedulingTaskTime<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> time,
        Func<FlightPassenger, Flt64>? cancelCoefficient = null,
        Func<FlightPassenger, PassengerClassId, Flt64>? classChangeCoefficient = null,
        Func<FlightTask, FlightTask, PassengerClassId, Flt64>? flightChangeCoefficient = null,
        string prefix = "passenger") {
        ArgumentNullException.ThrowIfNull(aggregation);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(timeWindow);
        ArgumentNullException.ThrowIfNull(time);
        _aggregation = aggregation;
        _capacity = capacity;
        _timeWindow = timeWindow;
        _time = time;
        _cancelCoefficient = cancelCoefficient;
        _classChangeCoefficient = classChangeCoefficient;
        _flightChangeCoefficient = flightChangeCoefficient;
        _prefix = prefix;
    }

    /// <summary>
    /// 生成管线清单 / Build the pipeline list.
    /// </summary>
    /// <returns>管线清单 / The pipeline list.</returns>
    public Result<IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>, ErrorCode, Error<ErrorCode>> Invoke() {
        var pipelines = new List<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>();

        // 一、三条约束 / One: the three constraints.
        pipelines.Add(new PassengerFlightCapacityConstraint(
            flights: _aggregation.Flights,
            amount: _aggregation.Amount,
            capacity: _capacity,
            classes: _aggregation.Classes,
            name: _prefix + "_flight_capacity_constraint"));

        pipelines.Add(new PassengerRouteCancelConstraint(
            passengers: _aggregation.Passengers,
            cancel: _aggregation.Cancel,
            name: _prefix + "_route_cancel_constraint"));

        pipelines.Add(new PassengerFlightChangeConstraint(
            timeWindow: _timeWindow,
            passengers: _aggregation.Passengers,
            time: _time,
            change: _aggregation.Change,
            name: _prefix + "_flight_change_constraint"));

        // 二、三条目标 / Two: the three objectives.
        pipelines.Add(new PassengerCancelMinimization(
            passengers: _aggregation.Passengers,
            cancel: _aggregation.Cancel,
            coefficient: _cancelCoefficient,
            name: _prefix + "_cancel_minimization"));

        pipelines.Add(new PassengerClassChangeMinimization(
            passengers: _aggregation.Passengers,
            change: _aggregation.Change,
            coefficient: _classChangeCoefficient,
            name: _prefix + "_class_change_minimization"));

        pipelines.Add(new PassengerFlightChangeMinimization(
            passengers: _aggregation.Passengers,
            change: _aggregation.Change,
            coefficient: _flightChangeCoefficient,
            name: _prefix + "_flight_change_minimization"));

        return Results.Ok<IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>>(pipelines);
    }
}
