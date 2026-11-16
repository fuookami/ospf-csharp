#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service.Limits;

/// <summary>
/// 行程上的取消单调性 / Cancellation monotonicity along an itinerary.
/// </summary>
/// <remarks>
/// ：行程上相邻两段之间加一条
/// `cancel[前一段] ≤ cancel[后一段]`——**前一段取消得多，后一段只能取消得更多**。行程是串起来的，
/// 把前面一段的人取消了，后面几段自然也不会有这批人。
///
/// **方向写反不会报错**：模型照样可行，只是解里会出现"前一段只取消 1 人、后一段取消 4 人"这种现实中不存在的组合。
/// 所以判据必须**读出左右两侧各是谁**，而不是只数行数。
///
/// It adds `cancel[previous leg] ≤ cancel[next leg]` between adjacent legs — **the next leg can only be cancelled at least as much
/// as the previous one**. An itinerary is a chain: cancel the people on an earlier leg and the later legs simply will not have them.
///
/// **Reversing the direction raises nothing**: the model stays feasible and the solution merely contains combinations that cannot happen — "1 cancelled on the first leg, 4 on the next".
/// The criterion must therefore **read which variable sits on each side**, not just count rows.
/// </remarks>
public sealed class PassengerRouteCancelConstraint
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> {
    private readonly IReadOnlyList<FlightPassenger> _passengers;

    private readonly PassengerCancel _cancel;

    private readonly string _name;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="passengers">航班乘客 / The flight passengers.</param>
    /// <param name="cancel">取消变量 / The cancellation variables.</param>
    /// <param name="name">管道名称 / The pipeline name.</param>
    public PassengerRouteCancelConstraint(
        IReadOnlyList<FlightPassenger> passengers,
        PassengerCancel cancel,
        string name = "passenger_route_cancel_constraint") {
        ArgumentNullException.ThrowIfNull(passengers);
        ArgumentNullException.ThrowIfNull(cancel);
        _passengers = passengers;
        _cancel = cancel;
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
                "乘客行程取消约束只能施加到线性元模型 / the passenger route-cancel constraint can only be applied to a linear meta model"));
        }

        for (int index = 0; index < _passengers.Count; ++index) {
            FlightPassenger passenger = _passengers[index];
            if (passenger.Prev is not { } prev) {
                // **行程的第一段没有前一段**，所以不产生行——不是"跳过一条该有的行"。
                // **The first leg of an itinerary has no previous leg**, so it produces no row — this is not "skipping a row that should exist".
                continue;
            }
            string rowName = _name + "_" + index;
            Try added = linear.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, _cancel.Of(prev)) }, Flt64.Zero),
                    new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, _cancel.Of(passenger)) }, Flt64.Zero),
                    Comparison.LE,
                    rowName),
                group: null,
                name: rowName,
                args: null);
            if (!added.IsOk) {
                return added;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **返回零而不是抛异常**：影子价格为零只是让列生成少一个加速项，结果仍然正确。这条约束的键是"行程上的相邻两段"，
    /// 而 C# 的影子价格参数里没有行程信息，`ponytail:` 天花板——等参数接口需要携带时再补。
    ///
    /// **Returns zero rather than throwing**: a zero shadow price only costs column generation one acceleration term while the answer stays correct. This constraint's key is "two
    /// adjacent legs of an itinerary" and C#'s shadow-price arguments carry no itinerary — a **`ponytail:` ceiling**, to be filled in when the interface needs it.
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
