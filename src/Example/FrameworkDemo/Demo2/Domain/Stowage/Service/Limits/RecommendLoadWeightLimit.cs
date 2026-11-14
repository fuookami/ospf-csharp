// 推荐装载重量约束 / The recommended load weight row.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;

/// <summary>
/// 推荐装载重量约束 / The recommended load weight row.
/// </summary>
/// <remarks>
/// 对齐基线 `stowage/service/limits/RecommendLoadWeightLimit.kt`：给每个"需要决策**且**需要推荐重量"的舱位加一行
///
/// <c>z_j + mlw_j · actualLoaded_j ≤ mlw_j</c>
///
/// 这是一条**耦合行**：推荐数量 `z` 与实际装载重量共用同一个上限。单独看 `z ≤ mlw` 不够——真实重量加上推荐量才是这个舱位要装的总量。
/// 少了它，`z` 会一路顶到 `mlw`，推荐方案在真实装载之上超载，而求解器不会抱怨。
///
/// 两个条件都要查：`stowageNeeded || adjustmentNeeded` **且** `recommendedWeightNeeded`。只看后者会给一个不参与决策的舱位加行。
///
/// Aligned with the baseline: for every position that both **takes a decision** and **needs a recommended weight**, add <c>z_j + mlw_j · actualLoaded_j ≤ mlw_j</c>. It is a
/// **coupling row**: the recommended quantity shares one cap with the actual load weight. `z ≤ mlw` alone is not enough — the real weight plus the recommendation is what the
/// position has to carry. Without it `z` drifts to `mlw`, the recommendation overloads on top of the actual load, and the solver never complains. Both conditions matter: only
/// checking the latter adds a row for a position that takes no decision at all.
/// </remarks>
public sealed class RecommendLoadWeightLimit
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;

    /// <summary>
    /// 构造推荐装载重量约束 / Construct the recommended load weight row.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public RecommendLoadWeightLimit(
        IReadOnlyList<StowagePosition> positions,
        Load load,
        string name = "recommend_load_weight_limit") {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(load);
        _positions = positions;
        _load = load;
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束 / Apply the rows.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> linear) {
        ArgumentNullException.ThrowIfNull(linear);
        if (_load.Z is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "推荐装载重量变量未注册 / the recommended load weight variables are not registered"));
        }

        for (int j = 0; j < _positions.Count; ++j) {
            StowagePosition position = _positions[j];
            bool takesDecision = position.Status.StowageNeeded || position.Status.AdjustmentNeeded;
            if (!takesDecision || !position.Status.RecommendedWeightNeeded) {
                continue;
            }
            if (_load.ActualLoadedSymbol(j) is not { } actualLoaded) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "舱位 " + position.Name + " 的实际装载符号未注册 / the actual loaded symbol of position " + position.Name + " is not registered"));
            }

            double maxLoadWeight = position.Mlw.Mlw;
            // 左边 = z_j + mlw · actualLoaded_j，右边 = mlw。
            // Left = z_j + mlw · actualLoaded_j, right = mlw.
            var left = new LinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, _load.Z[j]),
                    new LinearMonomial<Flt64>(new Flt64(maxLoadWeight), actualLoaded),
                },
                Flt64.Zero);
            string rowName = Name + "_" + position.Name;
            var row = new LinearInequality<Flt64>(
                left,
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(maxLoadWeight)),
                Comparison.LE,
                rowName);
            Try added = linear.AddConstraint(row, group: null, name: rowName);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
