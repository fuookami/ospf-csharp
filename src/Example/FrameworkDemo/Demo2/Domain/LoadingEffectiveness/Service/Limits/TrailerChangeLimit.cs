#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service.Limits;

/// <summary>
/// 拖车换装目标 / The trailer-change objective.
/// </summary>
/// <remarks>
/// 与对齐：把（相邻拖车对 × 相邻舱位对）的换装指示
/// 加权求和，作为**最小化**目标，名为 `"trailer change"`（带空格）。
///
/// 四点必须沿用：
///
/// 1. **是 `Minimum`**——换装是代价，不是奖励。
/// 2. **目标名带空格**：`"trailer change"`。
/// 3. **空集合时直接返回成功、不加目标**（`if (monomials.isEmpty) return ok`）。
/// 4. **系数是交叉的**：`coefficient(position2, trailer1)`、`coefficient(position1, trailer2)`。
///
/// The (trailer pair × adjacent position pair) indicators weighted and summed into a **minimisation** named `"trailer change"` (with a space). Four things must be
/// copied: **it is `Minimum`**, since changing trailers is a cost rather than a reward; **the name has a space**; **an empty set returns success without an objective** (its early
/// return); and **the coefficient is crossed** — `coefficient(position2, trailer1)` and `coefficient(position1, trailer2)`.
/// </remarks>
internal sealed class TrailerChangeLimit
{
    private readonly IReadOnlyList<(Trailer First, Trailer Second)> _orderedTrailers;
    private readonly IReadOnlyList<(StowagePosition First, StowagePosition Second)> _adjacentPositions;
    private readonly TrailerLoading _loading;
    private readonly Func<(StowagePosition, Trailer), (StowagePosition, Trailer), Flt64> _coefficient;

    /// <summary>
    /// 构造拖车换装目标 / Construct the trailer-change objective.
    /// </summary>
    /// <param name="loading">拖车指示 / The trailer indicators.</param>
    /// <param name="coefficient">按对的系数 / The per-pair coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public TrailerChangeLimit(
        TrailerLoading loading,
        Func<(StowagePosition, Trailer), (StowagePosition, Trailer), Flt64>? coefficient = null,
        string name = "trailer_change_limit")
    {
        ArgumentNullException.ThrowIfNull(loading);
        _loading = loading;
        _orderedTrailers = loading.OrderedTrailers;
        _adjacentPositions = loading.AdjacentPositions;
        _coefficient = coefficient ?? ((_, _) => Flt64.One);
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加目标 / Apply the objective.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        for (int p1 = 0; p1 < _orderedTrailers.Count; ++p1)
        {
            (Trailer trailer1, Trailer trailer2) = _orderedTrailers[p1];
            for (int p2 = 0; p2 < _adjacentPositions.Count; ++p2)
            {
                (StowagePosition position1, StowagePosition position2) = _adjacentPositions[p2];
                if (_loading.TrailerChange(p1, p2) is not { } indicator)
                {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "换装指示未注册 / the trailer-change indicator is not registered at " + p1 + "," + p2));
                }
                SameSourceAdjacentLimit.Accumulate(
                    monomials, ref constant, indicator,
                    _coefficient((position2, trailer1), (position1, trailer2)));
            }
        }
        if (monomials.Count == 0)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, constant),
            "trailer change",
            displayName: null);
    }
}
