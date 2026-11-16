// 压舱物：压舱重量符号与自适应最低压舱重量 / Ballast: the ballast weight symbol and the adaptive minimum.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 压舱物 / Ballast for weight balance.
/// </summary>
/// <remarks>
/// 两个符号：
///
/// | 符号 | 构成 | 何时注册 |
/// | --- | --- | --- |
/// | `ballast_weight` | Σ 各压舱舱位的估计装载重量 | 总是 |
/// | `min_ballast_weight` | 最低压舱重量，或**零** | 仅 `withSoftSecurity` 模式 |
///
/// 四处容易做错：
///
/// 1. **压舱舱位是"可用的那些"**，不是全部舱位。构造时按 `Status.Available` 过滤；用全部舱位会让不可用的舱位也算进压舱重量。
/// 2. **`min_ballast_weight` 只在软安保模式下注册**。硬安保模式下注册它会让约束生成器以为有一条最低压舱要求可加。
/// 3. **没有最低值时显式注册零**，不是留空。留空会让下游无法区分"没有要求"与"要求为零"，而两者的约束行为不同。
/// 4. **`ballast_weight` 的单项式是估计装载重量符号本身**，不是新变量。压舱物就是货物，重量已经由装载决策决定了。
///
/// Two symbols as tabulated. Four easy mistakes: (1) **ballast positions are the available ones**, filtered at construction — using all positions
/// counts unavailable ones into the ballast weight; (2) **`min_ballast_weight` registers only under soft security** — under hard security it would tell the constraint generator
/// there is a minimum ballast requirement to add; (3) **an explicit zero when no minimum is supplied**, not nothing, or downstream cannot tell "no requirement" from "a requirement
/// of zero", and the two behave differently; (4) **`ballast_weight`'s monomials are the estimated load weight symbols themselves**, not fresh variables — ballast is cargo, and its
/// weight is already decided by the loading decisions.
/// </remarks>
public sealed class Ballast
{
    /// <summary>飞机型号 / Aircraft model</summary>
    public AircraftModel AircraftModel { get; }

    /// <summary>全部舱位 / All positions</summary>
    public IReadOnlyList<StowagePosition> Positions { get; }

    /// <summary>可用于压舱的舱位（已按可用性过滤）/ The positions eligible for ballast, filtered by availability</summary>
    public IReadOnlyList<StowagePosition> BallastPositions { get; }

    /// <summary>最小所需压舱重量，未指定时为 `null` / The minimum required ballast weight, or `null`</summary>
    public double? MinBallastWeight { get; }

    /// <summary>
    /// 建议压舱重量，未指定时为 `null` / The advised ballast weight, or `null`.
    /// </summary>
    /// <remarks>
    /// 与 `MinBallastWeight` 是**两件不同的事**：最小值是硬下界，建议值是**软目标**——`AdviceBallastWeightLimit` 只在它存在时
    /// 建一个单边松弛并最小化，因此"没有建议值"必须是一个可表达的状态，不能折成 0（0 会变成"建议不压舱"）。
    ///
    /// Distinct from `MinBallastWeight`: the minimum is a hard lower bound while the advised value is a **soft target** — `AdviceBallastWeightLimit` builds a one-sided slack
    /// and minimises it only when the advice exists, so "no advice" must be representable and must not be folded to 0 (which would mean "advised to carry no ballast").
    /// </remarks>
    public Quantity<Flt64>? AdviceBallastWeight { get; }

    /// <summary>装载模型 / The load model</summary>
    public Load Load { get; }

    /// <summary>压舱重量符号 / The ballast weight symbol</summary>
    public LinearExpressionSymbol? BallastWeightSymbol { get; private set; }

    /// <summary>自适应最低压舱重量符号（仅软安保）/ The adaptive minimum ballast weight symbol, soft security only</summary>
    public LinearExpressionSymbol? AdaptiveMinBallastWeightSymbol { get; private set; }

    /// <summary>
    /// 构造压舱物 / Construct the ballast.
    /// </summary>
    /// <param name="aircraftModel">飞机型号 / The aircraft model.</param>
    /// <param name="positions">全部舱位 / All positions.</param>
    /// <param name="minBallastWeight">最小压舱重量 / The minimum ballast weight.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="adviceBallastWeight">建议压舱重量 / The advised ballast weight.</param>
    public Ballast(
        AircraftModel aircraftModel,
        IReadOnlyList<StowagePosition> positions,
        double? minBallastWeight,
        Load load,
        Quantity<Flt64>? adviceBallastWeight = null) {
        AircraftModel = aircraftModel;
        Positions = positions;
        MinBallastWeight = minBallastWeight;
        AdviceBallastWeight = adviceBallastWeight;
        Load = load;

        var available = new List<StowagePosition>();
        foreach (StowagePosition position in positions) {
            if (position.Status.Available) {
                available.Add(position);
            }
        }
        BallastPositions = available;
    }

    /// <summary>
    /// 注册压舱符号 / Register the ballast symbols.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        var polynomial = new MutableLinearPolynomial<Flt64>();
        for (int j = 0; j < Positions.Count; ++j) {
            if (!Positions[j].Status.Available) {
                continue;
            }
            if (Load.EstimateLoadWeightSymbol(j) is not { } estimate) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "压舱舱位缺少估计装载重量符号 / a ballast position has no estimated load weight symbol: "
                    + Positions[j].Name));
            }
            foreach (LinearMonomial<Flt64> monomial in estimate.Polynomial.Monomials) {
                polynomial.AddMonomial(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
            }
            polynomial.AddConstant(estimate.Polynomial.Constant);
        }
        var ballastWeight = new LinearExpressionSymbol(polynomial, name: "ballast_weight");
        Try addedWeight = model.Add(ballastWeight);
        if (!addedWeight.IsOk) {
            return addedWeight;
        }
        BallastWeightSymbol = ballastWeight;

        if (!stowageMode.WithSoftSecurity()) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        var minimum = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(constant: new Flt64(MinBallastWeight ?? 0.0)),
            name: "min_ballast_weight");
        Try addedMinimum = model.Add(minimum);
        if (!addedMinimum.IsOk) {
            return addedMinimum;
        }
        AdaptiveMinBallastWeightSymbol = minimum;
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
