// 总重：按飞行阶段的推荐与实际两族 / Total weight: recommended and actual families by flight phase.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 总重模型 / The total weight model.
/// </summary>
/// <remarks>
/// 逐条对齐：每个飞行阶段一份**推荐**与一份**实际**符号；
/// 总重 = 业载 + 干操作重量 + 救生筏重量 + （起飞/着陆阶段的）燃油重量，**零油重阶段不加燃油**；
/// 已经有**已计算总重**的阶段直接取那个常数，不再按公式拼。
///
/// 第三条容易被忽略：`computedTotalWeight` 一旦给出，推荐总重就退化成常数。这不是优化而是语义——某些阶段的总重是
/// 外部给定的（比如已配平算出来的），不该被业载的决策变量牵着走。
///
/// one recommended and one actual symbol per flight phase; total weight = payload + dry operating weight + liferaft + fuel (take-off and landing only,
/// **not** zero-fuel); and a phase that already has a **computed total weight** takes that constant instead of the formula. The third point is easy to miss: once
/// `computedTotalWeight` is given, the recommended total degenerates into a constant. That is semantics, not an optimisation — some phases' totals come from outside (a trimmed
/// figure, say) and should not be dragged around by the payload decisions.
/// </remarks>
public sealed class TotalWeight
{
    private readonly Fuselage _fuselage;
    private readonly IReadOnlyDictionary<FlightPhase, FuelConstant> _fuel;
    private readonly Payload _payload;

    private readonly Dictionary<FlightPhase, LinearExpressionSymbol> _estimate = new();
    private readonly Dictionary<FlightPhase, LinearExpressionSymbol> _actual = new();

    /// <summary>
    /// 构造总重模型 / Construct the total weight model.
    /// </summary>
    /// <param name="fuselage">机身 / The fuselage.</param>
    /// <param name="fuel">按飞行阶段的燃油常量 / The fuel constants by flight phase.</param>
    /// <param name="payload">业载模型 / The payload model.</param>
    /// <param name="maxTotalWeight">按飞行阶段的最大总重 / The maximum total weight by flight phase.</param>
    /// <param name="computedTotalWeight">按飞行阶段的已计算总重 / The computed total weight by flight phase.</param>
    public TotalWeight(
        Fuselage fuselage,
        IReadOnlyDictionary<FlightPhase, FuelConstant> fuel,
        Payload payload,
        IReadOnlyDictionary<FlightPhase, double>? maxTotalWeight = null,
        IReadOnlyDictionary<FlightPhase, double>? computedTotalWeight = null) {
        _fuselage = fuselage;
        _fuel = fuel;
        _payload = payload;
        MaxTotalWeight = maxTotalWeight ?? new Dictionary<FlightPhase, double>();
        ComputedTotalWeight = computedTotalWeight ?? new Dictionary<FlightPhase, double>();
    }

    /// <summary>按飞行阶段的最大总重 / The maximum total weight by flight phase</summary>
    public IReadOnlyDictionary<FlightPhase, double> MaxTotalWeight { get; }

    /// <summary>按飞行阶段的已计算总重 / The computed total weight by flight phase</summary>
    public IReadOnlyDictionary<FlightPhase, double> ComputedTotalWeight { get; }

    /// <summary>取某阶段的推荐总重符号 / The recommended total weight symbol of a phase</summary>
    /// <param name="phase">飞行阶段 / The flight phase.</param>
    /// <returns>推荐总重符号 / The recommended total weight symbol.</returns>
    public LinearExpressionSymbol? EstimateTotalWeight(FlightPhase phase) =>
        _estimate.TryGetValue(phase, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>取某阶段的实际总重符号 / The actual total weight symbol of a phase</summary>
    /// <param name="phase">飞行阶段 / The flight phase.</param>
    /// <returns>实际总重符号 / The actual total weight symbol.</returns>
    public LinearExpressionSymbol? ActualTotalWeight(FlightPhase phase) =>
        _actual.TryGetValue(phase, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>
    /// 拼某阶段的总重多项式 / Build the total weight polynomial of a phase.
    /// </summary>
    /// <param name="phase">飞行阶段 / The flight phase.</param>
    /// <param name="payloadPolynomial">业载多项式 / The payload polynomial.</param>
    /// <returns>总重多项式 / The total weight polynomial.</returns>
    /// <remarks>
    /// "加燃油"只对 `TakeOff`／`Landing` 成立，`ZeroFuel` 什么都不加——这正是"零油重"的定义。写成"总是加燃油"
    /// 会让零油重凭空重出一次起飞油量，而它同时是 `MaxLoadWeight` 分段线性规格的输入。
    ///
 /// This implementation adds fuel only for `TakeOff` / `Landing` and nothing for `ZeroFuel` — that IS the definition of zero fuel. Adding fuel unconditionally would make zero-fuel
    /// weight carry a take-off fuel load out of nowhere, and that figure is also the input to `MaxLoadWeight`'s piecewise specification.
    /// </remarks>
    public LinearPolynomial<Flt64> TotalWeightPolynomial(FlightPhase phase, LinearPolynomial<Flt64> payloadPolynomial) {
        var polynomial = new MutableLinearPolynomial<Flt64>();
        foreach (LinearMonomial<Flt64> monomial in payloadPolynomial.Monomials) {
            polynomial.AddMonomial(monomial);
        }
        polynomial.AddConstant(payloadPolynomial.Constant);
        polynomial.AddConstant(new Flt64(_fuselage.Dow));
        if (_fuselage.Liferaft is { } liferaft) {
            polynomial.AddConstant(new Flt64(liferaft.Weight));
        }
        if (phase is FlightPhase.TakeOff or FlightPhase.Landing && _fuel.TryGetValue(phase, out FuelConstant? fuel)) {
            polynomial.AddConstant(new Flt64(fuel.Weight));
        }
        return new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant);
    }

    /// <summary>
    /// 注册两族总重符号 / Register both total weight families.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_payload.EstimatePayload is not { } estimatePayload || _payload.ActualPayload is not { } actualPayload) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "业载符号未注册 / the payload symbols are not registered"));
        }

        _estimate.Clear();
        _actual.Clear();
        foreach (FlightPhase phase in Enum.GetValues<FlightPhase>()) {
            string name = phase.ToString().ToLowerInvariant();
            LinearExpressionSymbol estimate;
            if (ComputedTotalWeight.TryGetValue(phase, out double computed)) {
                // 已有已计算总重的阶段：推荐总重是那个常数，不跟着业载走。
                // A phase with a computed total: the recommended total is that constant and does not follow the payload.
                estimate = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: new Flt64(computed)),
                    name: "total_weight_" + name);
            } else {
                LinearPolynomial<Flt64> polynomial = TotalWeightPolynomial(phase, estimatePayload.Polynomial);
                estimate = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant),
                    name: "estimate_total_weight_" + name);
            }
            Try addedEstimate = model.Add(estimate);
            if (!addedEstimate.IsOk) {
                return addedEstimate;
            }
            _estimate[phase] = estimate;

            LinearPolynomial<Flt64> actualPolynomial = TotalWeightPolynomial(phase, actualPayload.Polynomial);
            var actual = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(actualPolynomial.Monomials, actualPolynomial.Constant),
                name: "actual_total_weight_" + name);
            Try addedActual = model.Add(actual);
            if (!addedActual.IsOk) {
                return addedActual;
            }
            _actual[phase] = actual;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
