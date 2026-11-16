#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
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
/// 舱位变更的最小化目标 / The cabin-change objective.
/// </summary>
/// <remarks>
/// ：对每位乘客、**除自己舱位以外**的每个舱位，累加 `系数 × passengerClassChange[乘客, 舱位]`。
///
/// 求和口径是这一层唯一容易错的地方：**不是"每位乘客一项"，而是"每位乘客 × 除自己以外的舱位"**。
/// 少算会让"改舱"变成免费的（目标里没有它的价格），多算（把自己那一档也算进去）会因为那个变量根本不存在而取不到值。
///
/// Its accumulates `coefficient × passengerClassChange[passenger, cabin]` for every passenger and **every cabin except the passenger's
/// own**.
///
/// That summation is the only easy mistake here: **it is not "one term per passenger" but "one per passenger per other cabin"**. Under-counting makes a cabin change free (its price is
/// absent from the objective); over-counting (including the own cabin) fails to fetch a value because that variable does not exist at all.
/// </remarks>
public sealed class PassengerClassChangeMinimization
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> {
    private readonly IReadOnlyList<FlightPassenger> _passengers;

    private readonly PassengerChange _change;

    private readonly Func<FlightPassenger, PassengerClassId, Flt64> _coefficient;

    private readonly string _name;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="passengers">航班乘客 / The flight passengers.</param>
    /// <param name="change">变更变量 / The change variables.</param>
    /// <param name="coefficient">按乘客与目标舱位给的系数，缺省 1 / The coefficient per passenger and target cabin, 1 when omitted.</param>
    /// <param name="name">管道名称 / The pipeline name.</param>
    public PassengerClassChangeMinimization(
        IReadOnlyList<FlightPassenger> passengers,
        PassengerChange change,
        Func<FlightPassenger, PassengerClassId, Flt64>? coefficient = null,
        string name = "passenger_class_change_minimization") {
        ArgumentNullException.ThrowIfNull(passengers);
        ArgumentNullException.ThrowIfNull(change);
        _passengers = passengers;
        _change = change;
        _coefficient = coefficient ?? ((_, _) => Flt64.One);
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
                "舱位变更最小化只能施加到线性元模型 / cabin-change minimisation can only be applied to a linear meta model"));
        }

        if (_change.PassengerClassChange is not { } table) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "变更变量还没注册 / the change variables have not been registered yet"));
        }

        var monomials = new List<LinearMonomial<Flt64>>();
        foreach (FlightPassenger passenger in _passengers) {
            if (!table.TryGetValue(passenger, out IReadOnlyDictionary<PassengerClassId, Fuookami.Ospf.Core.Variable.UIntVar>? row)) {
                continue;
            }
            foreach ((PassengerClassId cls, Fuookami.Ospf.Core.Variable.UIntVar variable) in row) {
                Flt64 coefficient = _coefficient(passenger, cls);
                if (coefficient.ToDouble() == 0.0) {
                    continue;
                }
                monomials.Add(new LinearMonomial<Flt64>(coefficient, variable));
            }
        }

        if (monomials.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(monomials, Flt64.Zero),
            "passenger class change",
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
