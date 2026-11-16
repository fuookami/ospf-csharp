// 最小低甲板载荷：按零油重点插值 / Minimum low-deck payload, interpolated from zero-fuel weight points.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;

/// <summary>
/// 最小低甲板载荷点 / A minimum low-deck payload point.
/// </summary>
/// <param name="MinLowPayloadValue">此点的最小低载荷 / The minimum low payload at this point.</param>
/// <param name="Zfw">此点的零燃油重量 / The zero-fuel weight at this point.</param>
public sealed record MinLowPayloadPoint(double MinLowPayloadValue, double Zfw);

/// <summary>
/// 从零燃油重量点插值的最小低甲板载荷约束 / The minimum low-deck payload constraint, interpolated from zero-fuel weight points.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `demo2/domain/airworthiness_security/model/MinLowPayload.kt`。它是一条**下界**：下舱装得太少会让重心太靠前，
/// 所以"最小低载荷"是安全要求，不是容量要求。
///
/// 两支取值，取决于零油重是否已知：
///
/// | 已知零油重 | 产出的符号 |
/// | --- | --- |
/// | 有 | **常数**——把那个数代进插值表，得到一个数 |
/// | 无 | **关于零油重估计的线性式**——斜率取首尾两点，截距反推 |
///
/// 第二支是这个类唯一有意思的地方：零油重本身是决策变量的表达式（业载 + 干操作重量 + 救生筏），所以"最小低载荷"也是一个表达式，
/// 不是数。写成常数要先把零油重估成某个数，那等于把一条约束降级成一个猜测。
///
/// Mirrors the baseline. This is a **lower bound**: too little in the lower hold puts the centre of gravity too far forward, so "minimum low payload" is a safety requirement, not a
/// capacity one. Two branches — a known zero-fuel weight gives a **constant** (substitute that number into the table), an unknown one gives a **linear expression in the zero-fuel
/// weight estimate** (slope from the first and last points, intercept back-solved). The second branch is the only interesting part: the zero-fuel weight is itself an expression
/// over decisions (payload + dry operating weight + liferaft), so the minimum low payload is an expression too. Forcing it to a constant would mean guessing a number first,
/// downgrading a constraint into a guess.
/// </remarks>
public sealed class MinLowPayload
{
    private readonly TotalWeight _totalWeight;

    /// <summary>
    /// 构造最小低甲板载荷 / Construct the minimum low-deck payload.
    /// </summary>
    /// <param name="points">插值点 / The interpolation points.</param>
    /// <param name="totalWeight">总重模型 / The total weight model.</param>
    public MinLowPayload(
        IReadOnlyList<MinLowPayloadPoint> points,
        TotalWeight totalWeight) {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) {
            throw new ArgumentOutOfRangeException(
                nameof(points),
                "插值表至少要有一点 / the interpolation table needs at least one point");
        }
        _totalWeight = totalWeight;
        Points = points.OrderBy(point => point.Zfw).ToList();
    }

    /// <summary>插值点（按零燃油重量升序）/ The interpolation points, ordered by zero-fuel weight</summary>
    public IReadOnlyList<MinLowPayloadPoint> Points { get; }

    /// <summary>最小低甲板载荷符号 / The minimum low-deck payload symbol</summary>
    public LinearExpressionSymbol? MinLowPayloadSymbol { get; private set; }

    /// <summary>
    /// 插值给定零燃油重量下的最小低载荷 / Interpolate the minimum low payload at a zero-fuel weight.
    /// </summary>
    /// <param name="zeroFuelWeight">零燃油重量 / The zero-fuel weight.</param>
    /// <returns>最小低载荷 / The minimum low payload.</returns>
    /// <remarks>
    /// 四条分支照抄基线：只有一点 → 那点；不高于首点 → 首点；不低于末点 → 末点；否则在相邻两点间**线性插值**。
    ///
    /// 注意这与 的 `MaxLoadWeightLimit` 是**相反的**：那里是阶跃（段内恒定），这里是插值（段内线性）。两者都是基线的做法，差异来自数据本身——
    /// 最大装载重量是按图表给出的阶梯规格，最小低载荷是一条连续的边界曲线。**不要因为"看着不一致"就把其中一个改成另一个的样子。**
    ///
    /// Four branches as in the baseline: one point → that point; not above the first → the first; not below the last → the last; otherwise **linear interpolation** between
    /// neighbours. Note this is the **opposite** of the earlier `MaxLoadWeightLimit`: that one steps (constant within a segment), this one interpolates (linear within a span). Both are
    /// the baseline's behaviour and the difference comes from the data — the maximum load weight is a stepped chart specification, the minimum low payload a continuous boundary
    /// curve. **Do not "fix" one to look like the other just because they differ.**
    /// </remarks>
    public double Interpolate(double zeroFuelWeight) {
        if (Points.Count == 1) {
            return Points[0].MinLowPayloadValue;
        }
        if (zeroFuelWeight <= Points[0].Zfw) {
            return Points[0].MinLowPayloadValue;
        }
        if (zeroFuelWeight >= Points[Points.Count - 1].Zfw) {
            return Points[Points.Count - 1].MinLowPayloadValue;
        }
        for (int i = 1; i < Points.Count; ++i) {
            MinLowPayloadPoint previous = Points[i - 1];
            MinLowPayloadPoint current = Points[i];
            if (zeroFuelWeight <= current.Zfw) {
                double span = current.Zfw - previous.Zfw;
                if (span == 0.0) {
                    return previous.MinLowPayloadValue;
                }
                double ratio = (zeroFuelWeight - previous.Zfw) / span;
                return previous.MinLowPayloadValue + (current.MinLowPayloadValue - previous.MinLowPayloadValue) * ratio;
            }
        }
        return Points[Points.Count - 1].MinLowPayloadValue;
    }

    /// <summary>
    /// 注册最小低甲板载荷符号 / Register the minimum low-deck payload symbol.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        LinearExpressionSymbol symbol;
        if (_totalWeight.ComputedTotalWeight.TryGetValue(FlightPhase.ZeroFuel, out double computedZeroFuel)) {
            symbol = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: new Flt64(Interpolate(computedZeroFuel))),
                name: "min_low_payload");
        } else if (_totalWeight.EstimateTotalWeight(FlightPhase.ZeroFuel) is { } zeroFuelEstimate) {
            // 首尾两点定斜率与截距：基线用 `sorted.first` / `sorted.last`，不是最小二乘拟合。
            // The first and last points fix the slope and intercept: the baseline uses `sorted.first` / `sorted.last`, not a least-squares fit.
            MinLowPayloadPoint first = Points[0];
            MinLowPayloadPoint last = Points[Points.Count - 1];
            double span = last.Zfw - first.Zfw;
            if (span == 0.0) {
                // 所有点的零油重相同，斜率无定义。退回常数，而不是让除零产生的 Infinity 混进模型。
                // Every point shares one zero-fuel weight, so the slope is undefined. Fall back to a constant rather than letting a division by zero leak Infinity into the model.
                symbol = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: new Flt64(first.MinLowPayloadValue)),
                    name: "min_low_payload");
            } else {
                // 斜率 = (末点载荷 − 首点载荷) / (末点零油重 − 首点零油重)；截距反推使直线过首点。
                // Slope = (last payload − first payload) / (last zero-fuel weight − first weight); the intercept is back-solved so the line passes through the first point.
                var slope = new Flt64((last.MinLowPayloadValue - first.MinLowPayloadValue) / span);
                Flt64 intercept = new Flt64(first.MinLowPayloadValue)
                    .Plus(new Flt64(first.Zfw).Times(slope).Negate());
                var polynomial = new MutableLinearPolynomial<Flt64>();
                foreach (LinearMonomial<Flt64> monomial in zeroFuelEstimate.Polynomial.Monomials) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(
                        monomial.Coefficient.Times(slope),
                        monomial.Symbol));
                }
                // 零油重估计本身带常数项（干操作重量 + 救生筏 + 业载常数），乘上斜率后并进截距。
                // The zero-fuel estimate carries its own constant (dry operating weight + liferaft + payload constant); scaled by the slope it joins the intercept.
                polynomial.AddConstant(intercept.Plus(
                    new Flt64(zeroFuelEstimate.Polynomial.Constant.ToDouble()).Times(slope)));
                symbol = new LinearExpressionSymbol(polynomial, name: "min_low_payload");
            }
        } else {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "零油重总重符号未注册 / the zero-fuel total weight symbol is not registered"));
        }

        Try added = model.Add(symbol);
        if (!added.IsOk) {
            return added;
        }
        MinLowPayloadSymbol = symbol;
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
