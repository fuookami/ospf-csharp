#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service.Limits;

/// <summary>
/// 改签只能在时间上接得住的航班上进行 / A re-booking may only target a flight the connection can still catch.
/// </summary>
/// <remarks>
/// ：对行程上每一段，用 `IfFunction` 造一个 0/1 指示变量，
/// 表示"这个接口太紧、赶不上"，再把改签变量压到 `人数 × 指示变量` 以下。
///
/// 两个方向的判断各一条：
///
/// * **赶下一段**：`最早可开始时刻 = 本段到达 − 下一段出发 + 机场中转时间`，为正表示"赶不上"；
/// * **从上一段赶过来**：`最晚可结束时刻 = 上一段到达 − 本段出发 + 机场中转时间`，为正表示"来不及"。
///
/// **指示变量写反不会报错**：改签会被整片放开（或者整片封死），求解照样成功——**只有"紧接口时改签必须为零"与
/// "松接口时改签可以不为零"两条判据能分辨**。
///
/// **一处必要差异**：`IfFunction` 的 `strictBoundary` 取默认的 `NONZERO_TOLERANCE = 1e-10`，
/// C# 的 `Create` 没有缺省值，所以显式传 `1e-10`；传 0.5 之类的"看起来像阈值"的值会让 `SafeLattice` 直接拒绝构造。
///
/// It builds a 0/1 indicator per itinerary leg with `IfFunction` — "this connection is too tight to catch" — and bounds the re-booking
/// variables by `head count × indicator`.
///
/// Two directions, one row each: **catching the next leg** — `earliest start = this leg's arrival − the next leg's departure + airport transfer time`, positive means "cannot catch it";
/// and **coming from the previous leg** — `latest end = the previous leg's arrival − this leg's departure + airport transfer time`, positive means "too late".
///
/// **Reversing the indicator raises nothing**: re-booking is either fully opened or fully blocked, and the solve still succeeds — **only the two criteria "a tight connection forces zero
/// re-booking" and "a loose connection allows non-zero re-booking" can tell**. 
///
/// **One necessary difference**: its `IfFunction` defaults `strictBoundary` to `NONZERO_TOLERANCE = 1e-10`; C#'s `Create` has no default, so 1e-10 is passed explicitly — a
/// "threshold-looking" value such as 0.5 makes `SafeLattice` refuse to construct.
/// </remarks>
public sealed class PassengerFlightChangeConstraint
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> {
    private readonly TimeWindow<Flt64> _timeWindow;

    private readonly IReadOnlyList<FlightPassenger> _passengers;

    private readonly BunchSchedulingTaskTime<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> _time;

    private readonly PassengerChange _change;

    private readonly string _name;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="timeWindow">时间窗 / The time window.</param>
    /// <param name="passengers">航班乘客 / The flight passengers.</param>
    /// <param name="time">任务时间层 / The task time layer.</param>
    /// <param name="change">变更变量 / The change variables.</param>
    /// <param name="name">管道名称 / The pipeline name.</param>
    public PassengerFlightChangeConstraint(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<FlightPassenger> passengers,
        BunchSchedulingTaskTime<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> time,
        PassengerChange change,
        string name = "passenger_flight_change_constraint") {
        ArgumentNullException.ThrowIfNull(timeWindow);
        ArgumentNullException.ThrowIfNull(passengers);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(change);
        _timeWindow = timeWindow;
        _passengers = passengers;
        _time = time;
        _change = change;
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "航班变更约束只能施加到线性元模型 / the flight-change constraint can only be applied to a linear meta model"));
        }

        if (!_change.WithFlightChange || _change.PassengerFlightChange is not { } changes) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        if (_time.EstimateStartTime is not { } startTimes || _time.EstimateEndTime is not { } endTimes) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "时间层还没有估计时刻 / the time layer has no estimated times yet"));
        }

        // **后继取第一段**：一位乘客的同一腿只可能有一个后继（行程是一条链）。
        // **The first successor wins**: a leg can only have one successor, because an itinerary is a chain.
        var successors = new Dictionary<FlightPassenger, FlightPassenger>();
        foreach (FlightPassenger passenger in _passengers) {
            if (passenger.Prev is { } prev && !successors.ContainsKey(prev)) {
                successors[prev] = passenger;
            }
        }

        for (int index = 0; index < _passengers.Count; ++index) {
            FlightPassenger passenger = _passengers[index];
            if (!changes.TryGetValue(passenger, out IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, UIntVar>>? byFlight)) {
                continue;
            }
            if (!_change.ToFlights.TryGetValue(passenger.Flight, out IReadOnlyList<FlightTask>? alternatives)) {
                continue;
            }

            if (passenger.Prev is { } previous) {
                if (!startTimes.TryGetValue(passenger.Flight, out object? thisStart)
                    || !endTimes.TryGetValue(previous.Flight, out object? previousEnd)) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "接口两段没有估计时刻 / the two legs of the connection have no estimated times"));
                }
                Try inbound = AddIndicator(linear, changes, byFlight, alternatives, passenger,
                    "earliest", new[]
                    {
                        new LinearMonomial<Flt64>(Flt64.One, (ISymbol)previousEnd),
                        new LinearMonomial<Flt64>(Flt64.One.Negate(), (ISymbol)thisStart),
                    },
                    _timeWindow.ValueOf(previous.Flight.Arr.PassengerTransferTime),
                    index);
                if (!inbound.IsOk) {
                    return inbound;
                }
            }

            if (successors.TryGetValue(passenger, out FlightPassenger? next)) {
                if (!endTimes.TryGetValue(passenger.Flight, out object? thisEnd)
                    || !startTimes.TryGetValue(next.Flight, out object? nextStart)) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "接口两段没有估计时刻 / the two legs of the connection have no estimated times"));
                }
                Try outbound = AddIndicator(linear, changes, byFlight, alternatives, passenger,
                    "latest", new[]
                    {
                        new LinearMonomial<Flt64>(Flt64.One, (ISymbol)thisEnd),
                        new LinearMonomial<Flt64>(Flt64.One.Negate(), (ISymbol)nextStart),
                    },
                    _timeWindow.ValueOf(next.Flight.Dep.PassengerTransferTime),
                    index);
                if (!outbound.IsOk) {
                    return outbound;
                }
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <remarks>
    /// **一个指示变量管住这一段的全部舱位**：接口赶不赶得上与乘客坐哪个舱位无关，按舱位各造一个指示变量只是把二进制变量数翻倍。
    ///
    /// **One indicator covers every cabin of that leg**: whether a connection can be caught does not depend on the cabin, and one indicator per cabin would only double the binaries.
    /// </remarks>
    private Try AddIndicator(
        IAbstractLinearMetaModel<Flt64> linear,
        IReadOnlyDictionary<FlightPassenger, IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, UIntVar>>> changes,
        IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, UIntVar>> byFlight,
        IReadOnlyList<FlightTask> alternatives,
        FlightPassenger passenger,
        string direction,
        IReadOnlyList<LinearMonomial<Flt64>> slackMonomials,
        Flt64 transferTime,
        int index) {
        // **取负号自己动手**：条件多项式要的是 `−(最早可开始时刻)`，而这个多项式里混着表达式符号，直接取负会踩到不可展开的中间符号。
        // **Negate it by hand**: the condition wants `−(earliest start)`, and this polynomial mixes expression symbols, so negating the built polynomial trips over intermediate symbols.
        var condition = new LinearPolynomial<Flt64>(
            slackMonomials.Select(monomial => new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol!)).ToList(),
            transferTime.Negate());
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(1e-10),
            name: _name + "_" + direction + "_" + index);
        if (created is not Ok<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "指示变量构造失败 / the indicator could not be constructed"));
        }
        var indicator = new LinearFunctionSymbolAdapter<Flt64>(ok.Value!);
        Try added = linear.Add(indicator);
        if (!added.IsOk) {
            return added;
        }

        foreach (FlightTask toFlight in alternatives) {
            if (!byFlight.TryGetValue(toFlight, out IReadOnlyDictionary<PassengerClassId, UIntVar>? byCabin)) {
                continue;
            }
            foreach ((PassengerClassId cls, UIntVar variable) in byCabin) {
                var rhs = new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(new Flt64(passenger.Amount), ok.Value!.ResultVar) },
                    Flt64.Zero);
                var lhs = new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
                    Flt64.Zero);
                string rowName = _name + "_" + index + "_" + toFlight.Key.Id + "_" + cls.Cls;
                Try row = linear.AddConstraint(
                    new LinearInequality<Flt64>(lhs, rhs, Comparison.LE, rowName),
                    group: null,
                    name: rowName,
                    args: null);
                if (!row.IsOk) {
                    return row;
                }
            }
        }

        _ = changes;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **返回零而不是抛异常**：影子价格为零只是让列生成少一个加速项，结果仍然正确。`ponytail:` 天花板——等这条约束的对偶信息被列生成用上再补。
    ///
    /// **Returns zero rather than throwing**: a zero shadow price only costs column generation one acceleration term while the answer stays correct. A **`ponytail:` ceiling**, to be
    /// filled in once column generation uses this constraint's dual information.
    /// </remarks>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>? Extractor()
        => (map, args) => Flt64.Zero;

    /// <inheritdoc/>
    /// <remarks>
    /// 目前没有键可认，所以只是返回成功——**不假装认回了什么**。
    ///
    /// There is no key to recognise yet, so this only returns success — **without pretending to have matched anything**.
    /// </remarks>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        ArgumentNullException.ThrowIfNull(shadowPriceMap);
        ArgumentNullException.ThrowIfNull(shadowPrices);
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
