#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;

/// <summary>
/// 乘客变更变量 / The passenger change variables.
/// </summary>
/// <remarks>
/// 建两层变量：
///
/// * **舱位变更**：每位乘客、**除自己舱位以外**的每个舱位一个变量——自己那一档不需要"变更"变量；
/// * **航班变更**（可选）：每位乘客、同一航路上**其它**航班、每个舱位一个变量。
///
/// 两层都以上界 = 人数收紧。
///
/// **一处必要差异**：`PassengerClass.entries` 枚举取全部舱位，而 C# 侧只有包字符串的 `PassengerClassId`（没有枚举），
/// 所以舱位清单由调用方传入。**缺了它就只能自己猜舱位**，而猜出来的舱位表与容量表对不上时，变更变量永远取零。
///
/// It builds two variable layers: **cabin change** — one variable per passenger and every cabin **except the passenger's own** (no "change" is needed to
/// stay in the same cabin); and **flight change** (optional) — one per passenger, per other flight on the same route, per cabin. Both layers are bounded above by the head count.
///
/// **One necessary difference**: this implementation reads every cabin from the `PassengerClass.entries` enum, while C# only has the string-wrapping `PassengerClassId` (no enum), so the cabin
/// list comes from the caller. **Without it the cabins can only be guessed**, and a guessed cabin table that disagrees with the capacity table keeps every change variable at zero.
/// </remarks>
public sealed class PassengerChange {
    private readonly IReadOnlyList<FlightTask> _flights;

    private readonly IReadOnlyList<FlightPassenger> _passengers;

    private readonly IReadOnlyList<PassengerClassId> _classes;

    private readonly bool _withFlightChange;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="flights">全部航班任务 / All flight tasks.</param>
    /// <param name="passengers">航班乘客 / The flight passengers.</param>
    /// <param name="classes">全部舱位 / All cabins.</param>
    /// <param name="withFlightChange">是否建航班变更变量 / Whether to build flight-change variables.</param>
    public PassengerChange(
        IReadOnlyList<FlightTask> flights,
        IReadOnlyList<FlightPassenger> passengers,
        IReadOnlyList<PassengerClassId> classes,
        bool withFlightChange = false) {
        ArgumentNullException.ThrowIfNull(flights);
        ArgumentNullException.ThrowIfNull(passengers);
        ArgumentNullException.ThrowIfNull(classes);
        _flights = flights;
        _passengers = passengers;
        _classes = classes;
        _withFlightChange = withFlightChange;
    }

    /// <summary>是否建航班变更变量 / Whether flight-change variables are built.</summary>
    public bool WithFlightChange => _withFlightChange;

    /// <summary>
    /// 每位乘客可以改签到哪些航班 / Which flights each passenger may be moved to.
    /// </summary>
    /// <remarks>
    /// **同航路、且不是原来那一班**：改签到完全不同的航路上，行程本身就断了，不是"变更"而是"重排"。
    ///
    /// **Same route, and not the original flight**: moving to a different route breaks the itinerary itself — that is a re-plan, not a change.
    /// </remarks>
    public IReadOnlyDictionary<FlightTask, IReadOnlyList<FlightTask>> ToFlights {
        get {
            var byRoute = new Dictionary<(Airport Dep, Airport Arr), List<FlightTask>>();
            foreach (FlightTask flight in _flights) {
                if (!byRoute.TryGetValue((flight.Dep, flight.Arr), out List<FlightTask>? sameRoute)) {
                    sameRoute = new List<FlightTask>();
                    byRoute[(flight.Dep, flight.Arr)] = sameRoute;
                }
                sameRoute.Add(flight);
            }

            var table = new Dictionary<FlightTask, IReadOnlyList<FlightTask>>();
            foreach (FlightTask flight in _passengers.Select(passenger => passenger.Flight).Distinct()) {
                table[flight] = byRoute.TryGetValue((flight.Dep, flight.Arr), out List<FlightTask>? sameRoute)
                    ? sameRoute.Where(candidate => !ReferenceEquals(candidate, flight)).ToList()
                    : new List<FlightTask>();
            }
            return table;
        }
    }

    /// <summary>舱位变更变量 / The cabin change variables.</summary>
    public IReadOnlyDictionary<FlightPassenger, IReadOnlyDictionary<PassengerClassId, UIntVar>>? PassengerClassChange { get; private set; }

    /// <summary>航班变更变量 / The flight change variables.</summary>
    public IReadOnlyDictionary<FlightPassenger, IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, UIntVar>>>? PassengerFlightChange { get; private set; }

    /// <summary>
    /// 注册到模型 / Register into the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Try Register(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        if (PassengerClassChange is null) {
            var table = new Dictionary<FlightPassenger, IReadOnlyDictionary<PassengerClassId, UIntVar>>();
            var items = new List<IVariableItem>();
            foreach (FlightPassenger passenger in _passengers) {
                var row = new Dictionary<PassengerClassId, UIntVar>();
                foreach (PassengerClassId cls in _classes) {
                    if (cls == passenger.Cls) {
                        // **自己那一档不需要变更变量**：建了它只会让目标里多一个恒零的项。
                        // **No change variable for the passenger's own cabin**: it would only add an always-zero term to the objective.
                        continue;
                    }
                    var variable = new UIntVar("passenger_class_change_" + passenger.Passenger.Id + "_" + cls);
                    variable.Range.Leq(new Fuookami.Ospf.Math.Algebra.Number.UInt64(passenger.Amount));
                    row[cls] = variable;
                    items.Add(variable);
                }
                table[passenger] = row;
            }
            PassengerClassChange = table;
            Try added = model.Add(items);
            if (!added.IsOk) {
                return added;
            }
        }

        if (!_withFlightChange) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        if (PassengerFlightChange is null) {
            IReadOnlyDictionary<FlightTask, IReadOnlyList<FlightTask>> alternatives = ToFlights;
            var table = new Dictionary<FlightPassenger, IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, UIntVar>>>();
            var items = new List<IVariableItem>();
            foreach (FlightPassenger passenger in _passengers) {
                var byFlight = new Dictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, UIntVar>>();
                IReadOnlyList<FlightTask> targets = alternatives.TryGetValue(passenger.Flight, out IReadOnlyList<FlightTask>? found)
                    ? found
                    : Array.Empty<FlightTask>();
                foreach (FlightTask target in targets) {
                    var byClass = new Dictionary<PassengerClassId, UIntVar>();
                    foreach (PassengerClassId cls in _classes) {
                        var variable = new UIntVar("passenger_flight_change_" + passenger.Passenger.Id + "_" + target.Id + "_" + cls);
                        variable.Range.Leq(new Fuookami.Ospf.Math.Algebra.Number.UInt64(passenger.Amount));
                        byClass[cls] = variable;
                        items.Add(variable);
                    }
                    byFlight[target] = byClass;
                }
                table[passenger] = byFlight;
            }
            PassengerFlightChange = table;
            return model.Add(items);
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
