#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service.Limits;

/// <summary>
/// 推荐重量均衡约束 / The recommended-weight-equalisation constraint.
/// </summary>
/// <remarks>
/// 对**每一对需要推荐重量的舱位**加**两条**互相制约的约束。
///
/// 五处必须沿用：
///
/// 1. **两条约束，方向相反**：`z[j1] 不超过 z[j2] + mlw1 × actualLoaded[j2]` 与 `z[j2] 不超过 z[j1] + mlw2 × actualLoaded[j1]`。
///    只加一条会让偏差单向无界。
/// 2. **松弛项的系数是"另一个舱位的 `mlw`"**，不是自己的。写反会让两个舱位的容量张冠李戴。
/// 3. **`j2` 严格大于 `j1`**，避免同一对加两次。写法是 `j2` 不大于 `j1` 时跳过。
/// 4. **两个舱位都必须 `RecommendedWeightNeeded`**。只判一个会让另一个舱位被迫承担均衡。
/// 5. **约束名带两个舱位**，且第二条把顺序反过来（`_${position2}_${position1}`），否则两条同名。
///
/// **two constraints in opposite directions** for every pair of positions needing recommended weight, `z[j1] 不超过 z[j2] + mlw1 × actualLoaded[j2]` and
/// `z[j2] 不超过 z[j1] + mlw2 × actualLoaded[j1]` — one alone leaves the deviation unbounded in one direction; **the slack coefficient is the *other* position's `mlw`**, not its own, and
/// swapping them mixes up the two capacities; **`j2` strictly above `j1`** so each pair is added once; **both positions must have `RecommendedWeightNeeded`**, since testing only one forces the
/// other to carry the balance; and **the names carry both positions**, with the second reversing the order so the two do not collide.
/// </remarks>
internal sealed class RecommendedWeightEqualizationLimit
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;

    /// <summary>
    /// 构造均衡约束 / Construct the equalisation constraint.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">载荷 / The load.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public RecommendedWeightEqualizationLimit(
        IReadOnlyList<StowagePosition> positions,
        Load load,
        string name = "recommended_weight_equalization_limit")
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(load);
        _positions = positions;
        _load = load;
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束 / Apply the constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_load.Z is not { } z)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "推荐装载数量变量未注册 / the recommended-load-count variables are not registered"));
        }

        for (int j1 = 0; j1 < _positions.Count; ++j1)
        {
            StowagePosition position1 = _positions[j1];
            if (!position1.Status.RecommendedWeightNeeded)
            {
                continue;
            }
            for (int j2 = j1 + 1; j2 < _positions.Count; ++j2)
            {
                StowagePosition position2 = _positions[j2];
                if (!position2.Status.RecommendedWeightNeeded)
                {
                    continue;
                }
                if (_load.ActualLoadedSymbol(j2) is not { } actual2)
                {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "满载符号缺失 / the loaded symbol is missing at position " + j2));
                }
                if (_load.ActualLoadedSymbol(j1) is not { } actual1)
                {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "满载符号缺失 / the loaded symbol is missing at position " + j1));
                }

                // `z[j1] 不超过 z[j2] + mlw1 × actual2`
                // **系数是另一个舱位的 `mlw`**。
                // **The coefficient is the other position's `mlw`**.
                string firstName = Name + "_" + position1.Name + "_" + position2.Name;
                var firstRow = new LinearInequality<Flt64>(
                    Difference(z[j1], z[j2], position1.Mlw.Mlw, actual2),
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison.LE,
                    firstName);
                Try first = model.AddConstraint(firstRow, group: null, name: firstName);
                if (!first.IsOk)
                {
                    return first;
                }
                // `z[j2] 不超过 z[j1] + mlw2 × actual1`
                string secondName = Name + "_" + position2.Name + "_" + position1.Name;
                var secondRow = new LinearInequality<Flt64>(
                    Difference(z[j2], z[j1], position2.Mlw.Mlw, actual1),
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison.LE,
                    secondName);
                Try second = model.AddConstraint(secondRow, group: null, name: secondName);
                if (!second.IsOk)
                {
                    return second;
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 造 `z[a] − z[b] − mlw × actual` 的多项式 / Build the polynomial `z[a] − z[b] − mlw × actual`.
    /// </summary>
    /// <param name="left">左端变量 / The left variable.</param>
    /// <param name="right">右端变量 / The right variable.</param>
    /// <param name="mlw">该舱位最大载重 / The position's maximum load weight.</param>
    /// <param name="actual">另一舱位的满载符号 / The other position's loaded symbol.</param>
    /// <returns>多项式 / The polynomial.</returns>
    private static LinearPolynomial<Flt64> Difference(
        Fuookami.Ospf.Math.Symbol.ISymbol left,
        Fuookami.Ospf.Math.Symbol.ISymbol right,
        double mlw,
        Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64> actual)
    {
        var monomials = new List<LinearMonomial<Flt64>> {
            new(new Flt64(1.0), left),
            new(new Flt64(-1.0), right),
            new(new Flt64(-mlw), actual),
        };
        return new LinearPolynomial<Flt64>(monomials, Flt64.Zero);
    }
}
