#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
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
/// 乘客取消的最小化目标 / The passenger cancellation objective.
/// </summary>
/// <remarks>
/// ：`Σ 系数 × cancel[乘客]` 放进最小化。系数按乘客给，缺省 1——
/// **系数是"取消这一位有多贵"**：普通旅客取消比要客便宜，缺省值让所有人为一体。
///
/// **目标与上界必须成对**：取消变量本身有"不超过人数"的上界，没有这条目标它会被一直压到上界（**因为取消能给别人腾地方**）；
/// 反过来只有目标没有上界，取消能压到零、上界就没有意义。这里只加目标，上界在 已经钉过。
///
/// Its puts `Σ coefficient × cancel[passenger]` into the minimisation. The coefficient is per passenger and defaults to 1 — **it says how
/// expensive it is to cancel this one**: an ordinary traveller is cheaper to drop than a VIP, and the default treats everyone the same.
///
/// **The objective and the bound must come as a pair**: the cancellation variables already carry a "no more than the head count" bound, and without this objective they are pushed to
/// that bound (**cancelling frees room for others**); conversely an objective without a bound would let cancellation go to zero and make the bound pointless. adds only the
/// objective; the bound was pinned back.
/// </remarks>
public sealed class PassengerCancelMinimization
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> {
    private readonly IReadOnlyList<FlightPassenger> _passengers;

    private readonly PassengerCancel _cancel;

    private readonly Func<FlightPassenger, Flt64> _coefficient;

    private readonly string _name;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="passengers">航班乘客 / The flight passengers.</param>
    /// <param name="cancel">取消变量 / The cancellation variables.</param>
    /// <param name="coefficient">每位乘客的系数，缺省 1 / The per-passenger coefficient, 1 when omitted.</param>
    /// <param name="name">管道名称 / The pipeline name.</param>
    public PassengerCancelMinimization(
        IReadOnlyList<FlightPassenger> passengers,
        PassengerCancel cancel,
        Func<FlightPassenger, Flt64>? coefficient = null,
        string name = "passenger_cancel_minimization") {
        ArgumentNullException.ThrowIfNull(passengers);
        ArgumentNullException.ThrowIfNull(cancel);
        _passengers = passengers;
        _cancel = cancel;
        _coefficient = coefficient ?? (_ => Flt64.One);
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
                "乘客取消最小化只能施加到线性元模型 / passenger cancellation minimisation can only be applied to a linear meta model"));
        }

        if (_passengers.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        var monomials = new List<LinearMonomial<Flt64>>();
        foreach (FlightPassenger passenger in _passengers) {
            Flt64 coefficient = _coefficient(passenger);
            // **系数为零的乘客不进目标**：加一项零系数只是把模型撑大，目标值一分不变。
            // **Passengers with a zero coefficient stay out of the objective**: a zero-coefficient term only bloats the model while the objective value is unchanged.
            if (coefficient.ToDouble() == 0.0) {
                continue;
            }
            monomials.Add(new LinearMonomial<Flt64>(coefficient, _cancel.Of(passenger)));
        }

        if (monomials.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(monomials, Flt64.Zero),
            "passenger cancel",
            displayName: null);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **返回零而不是抛异常**：影子价格为零只是让列生成少一个加速项，结果仍然正确。取消目标没有"键"，
    /// 所以也没有可认回的行。`ponytail:` 天花板——等它的对偶信息真被用上再补。
    ///
    /// **Returns zero rather than throwing**: a zero shadow price only costs column generation one acceleration term while the answer stays correct. This objective has no key, so there
    /// is no row to recognise either. **`ponytail:` ceiling** — to be filled in once its dual information is actually used.
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
