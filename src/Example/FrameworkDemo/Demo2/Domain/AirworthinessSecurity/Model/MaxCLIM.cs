// 最大中心线指数裕度：按起飞重量插值 / The maximum centreline index margin, interpolated by take-off weight.

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
/// CLIM 插值点 / A CLIM interpolation point.
/// </summary>
/// <param name="Tow">此点的起飞重量（千克）/ The take-off weight at this point, in kilogram.</param>
/// <param name="MaxClim">此点的最大 CLIM 值（力矩单位）/ The maximum CLIM value at this point, in the torque unit.</param>
public sealed record MaxClimPoint(double Tow, double MaxClim);

/// <summary>
/// 最大中心线指数裕度 / The maximum centreline index margin.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `demo2/domain/airworthiness_security/model/MaxCLIM.kt`。CLIM 是"重心偏离中心线多少"的裕度，随起飞重量变化：
/// 飞机越重，允许的横向不平衡越小。
///
/// 结构与 <see cref="MinLowPayload"/> 同形：两支（已知总重 → 常数；未知 → 关于总重估计的线性式），**两端都夹住**。
///
/// 夹取这一点值得记下来，因为基线的循环形状容易读错。`for (i in 0 until sorted.size - 1)` 之后的 `return sorted.last` 看起来像"上端外推"，
/// 但 x 超过末点时循环里每个 `if (x leq x2)` 都为假、循环自然结束，于是**恰好**执行到那一行——所以上端是夹住的，不是外推。
/// **这里容易被读成外推，而判据会当场抓住这个误读。**
///
/// Mirrors the baseline. CLIM is the margin by which the centre of gravity departs from the centreline, shrinking as take-off weight grows. The structure matches
/// <see cref="MinLowPayload"/>: two branches (known total → constant, unknown → linear in the estimate) and **both ends clamp**. The clamping is worth recording because the loop
/// shape is easy to misread: the `return sorted.last` after `for (i in 0 until sorted.size - 1)` looks like an upper extrapolation, but above the last point every
/// `if (x leq x2)` is false, the loop falls through, and that line is reached **exactly** — so the top clamps. The first draft read it as extrapolation and wrote both the comment
/// and a criterion to match; the criterion went red immediately.
/// </remarks>
public sealed class MaxCLIM
{
    private readonly TotalWeight _totalWeight;

    /// <summary>
    /// 构造最大 CLIM / Construct the maximum CLIM.
    /// </summary>
    /// <param name="points">插值点 / The interpolation points.</param>
    /// <param name="totalWeight">总重模型 / The total weight model.</param>
    public MaxCLIM(IReadOnlyList<MaxClimPoint> points, TotalWeight totalWeight) {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0) {
            throw new ArgumentOutOfRangeException(
                nameof(points),
                "插值表至少要有一点 / the interpolation table needs at least one point");
        }
        _totalWeight = totalWeight;
        Points = points.OrderBy(point => point.Tow).ToList();
    }

    /// <summary>插值点（按起飞重量升序）/ The interpolation points, ordered by take-off weight</summary>
    public IReadOnlyList<MaxClimPoint> Points { get; }

    /// <summary>最大 CLIM 符号 / The maximum CLIM symbol</summary>
    public LinearExpressionSymbol? MaxClimSymbol { get; private set; }

    /// <summary>
    /// 某起飞重量下的最大 CLIM / The maximum CLIM at a take-off weight.
    /// </summary>
    /// <param name="takeOffWeight">起飞重量 / The take-off weight.</param>
    /// <returns>最大 CLIM / The maximum CLIM.</returns>
    public double At(double takeOffWeight) {
        MaxClimPoint first = Points[0];
        if (takeOffWeight < first.Tow) {
            return first.MaxClim;
        }
        for (int i = 0; i < Points.Count - 1; ++i) {
            MaxClimPoint lower = Points[i];
            MaxClimPoint upper = Points[i + 1];
            if (takeOffWeight <= upper.Tow) {
                double span = upper.Tow - lower.Tow;
                if (span == 0.0) {
                    return lower.MaxClim;
                }
                double slope = (upper.MaxClim - lower.MaxClim) / span;
                return lower.MaxClim + (takeOffWeight - lower.Tow) * slope;
            }
        }
        return Points[Points.Count - 1].MaxClim;
    }

    /// <summary>
    /// 注册最大 CLIM 符号 / Register the maximum CLIM symbol.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        LinearExpressionSymbol symbol;
        if (_totalWeight.ComputedTotalWeight.TryGetValue(FlightPhase.TakeOff, out double computedTow)) {
            symbol = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: new Flt64(At(computedTow))),
                name: "max_clim");
        } else if (_totalWeight.EstimateTotalWeight(FlightPhase.TakeOff) is { } towEstimate) {
            MaxClimPoint first = Points[0];
            MaxClimPoint last = Points[Points.Count - 1];
            double span = last.Tow - first.Tow;
            if (span == 0.0) {
                // 所有点的起飞重量相同，斜率无定义。退回常数而不是让除零产生 Infinity。
                // All points share one take-off weight, so the slope is undefined. Fall back to a constant rather than let a division by zero produce Infinity.
                symbol = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: new Flt64(first.MaxClim)),
                    name: "max_clim");
            } else {
                var slope = new Flt64((last.MaxClim - first.MaxClim) / span);
                Flt64 intercept = new Flt64(first.MaxClim)
                    .Plus(new Flt64(first.Tow).Times(slope).Negate());
                var polynomial = new MutableLinearPolynomial<Flt64>();
                foreach (LinearMonomial<Flt64> monomial in towEstimate.Polynomial.Monomials) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(
                        monomial.Coefficient.Times(slope),
                        monomial.Symbol));
                }
                polynomial.AddConstant(intercept.Plus(
                    new Flt64(towEstimate.Polynomial.Constant.ToDouble()).Times(slope)));
                symbol = new LinearExpressionSymbol(polynomial, name: "max_clim");
            }
        } else {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "起飞重量总重符号未注册 / the take-off total weight symbol is not registered"));
        }

        Try added = model.Add(symbol);
        if (!added.IsOk) {
            return added;
        }
        MaxClimSymbol = symbol;
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
