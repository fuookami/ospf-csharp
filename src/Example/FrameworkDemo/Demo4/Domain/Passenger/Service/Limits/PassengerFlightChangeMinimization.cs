#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service.Limits;

/// <summary>
/// 航班变更的最小化目标 / The flight-change objective.
/// </summary>
/// <remarks>
/// 对每位乘客、**同航路的每个其它航班**、每个舱位，累加
/// `系数 × passengerFlightChange[乘客, 目标航班, 舱位]`。系数按"从哪班、到哪班、哪一档"三点给——
/// **同一个舱位在不同航段上的代价可以不同**。
///
/// 这一层比舱位变更那层多一维，也多了两个"空表即空目标"的入口：**没开航班变更**、**没有可替换的航班**。
/// 两种情况下 `monomials` 为空，就不加目标——**空目标不是"目标为零"，是不存在**。
///
/// Accumulates `coefficient × passengerFlightChange[passenger, target flight, cabin]` over every passenger, **every other flight on the
/// same route**, and every cabin. The coefficient is keyed by "from which flight, to which flight, which cabin" — **the same cabin can cost differently on different legs**.
///
/// This layer has one dimension more than the cabin-change one, and two more "empty table means empty objective" entries: **flight change is off**, and **there is no alternative
/// flight**. In both cases `monomials` is empty and no objective is added — **an empty objective is not "a zero objective", it does not exist**.
/// </remarks>
public sealed class PassengerFlightChangeMinimization
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> {
    private readonly IReadOnlyList<FlightPassenger> _passengers;

    private readonly PassengerChange _change;

    private readonly Func<FlightTask, FlightTask, PassengerClassId, Flt64> _coefficient;

    private readonly string _name;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="passengers">航班乘客 / The flight passengers.</param>
    /// <param name="change">变更变量 / The change variables.</param>
    /// <param name="coefficient">按出发航班、目标航班与舱位给的系数，缺省 1 / The coefficient per source flight, target flight and cabin, 1 when omitted.</param>
    /// <param name="name">管道名称 / The pipeline name.</param>
    public PassengerFlightChangeMinimization(
        IReadOnlyList<FlightPassenger> passengers,
        PassengerChange change,
        Func<FlightTask, FlightTask, PassengerClassId, Flt64>? coefficient = null,
        string name = "passenger_flight_change_minimization") {
        ArgumentNullException.ThrowIfNull(passengers);
        ArgumentNullException.ThrowIfNull(change);
        _passengers = passengers;
        _change = change;
        _coefficient = coefficient ?? ((_, _, _) => Flt64.One);
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
                "航班变更最小化只能施加到线性元模型 / flight-change minimisation can only be applied to a linear meta model"));
        }

        if (!_change.WithFlightChange || _change.PassengerFlightChange is not { } table) {
            // **没开航班变更就不是错误**：这种情况下 `monomials` 为空，直接不加目标。
            // **Flight change being off is not an error**: the code finds `monomials` empty here and simply adds no objective.
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        var monomials = new List<LinearMonomial<Flt64>>();
        foreach (FlightPassenger passenger in _passengers) {
            if (!table.TryGetValue(passenger, out IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, UIntVar>>? byFlight)) {
                continue;
            }
            foreach ((FlightTask toFlight, IReadOnlyDictionary<PassengerClassId, UIntVar> byCabin) in byFlight) {
                foreach ((PassengerClassId cls, UIntVar variable) in byCabin) {
                    Flt64 coefficient = _coefficient(passenger.Flight, toFlight, cls);
                    if (coefficient.ToDouble() == 0.0) {
                        continue;
                    }
                    monomials.Add(new LinearMonomial<Flt64>(coefficient, variable));
                }
            }
        }

        if (monomials.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(monomials, Flt64.Zero),
            "passenger flight change",
            displayName: null);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **返回零而不是抛异常**：影子价格为零只是让列生成少一个加速项，结果仍然正确。`ponytail:` 天花板——等它的对偶信息真被用上再补。
    ///
    /// **Returns zero rather than throwing**: a zero shadow price only costs column generation one acceleration term while the answer stays correct. A **`ponytail:` ceiling**, to be
    /// filled in once its dual information is actually used.
    /// </remarks>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>? Extractor()
        => (map, args) => Flt64.Zero;

    /// <inheritdoc/>
    /// <remarks>
    /// 目标项不属于约束，对偶解里没有它的行——**这里只返回成功，不假装认回了什么**。
    ///
    /// Objective terms are not constraints and have no rows in the dual solution — **this only returns success without pretending to have matched anything**.
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
