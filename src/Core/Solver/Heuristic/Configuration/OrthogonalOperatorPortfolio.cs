// 正交算子组合 / The orthogonal operator portfolio.
// 与固定 Kotlin 基线 `configuration/OrthogonalOperatorPortfolio.kt`（2996 B）对应。
// Mirrors the pinned baseline's `OrthogonalOperatorPortfolio.kt` (2996 B).

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Configuration;

/// <summary>
/// 正交算子组合 / The orthogonal operator portfolio.
/// </summary>
/// <remarks>
/// **连续组合的规模下限是 4，而它实际返回 5 个算法**。下限 4 是"正交性需要至少四类行为"的判据；若改成"返回个数"的下限，
/// 调用方传 4 会得到一个少一个算法的组合，**而校验通过、组合看起来仍然正交**。
/// **The continuous portfolio's size floor is 4, yet it returns 5 algorithms.** The floor of 4 is the "orthogonality needs at least four
/// behaviour classes" predicate; turning it into "the number returned" lets a caller passing 4 get a portfolio one algorithm short,
/// **while validation passes and the portfolio still looks orthogonal**.
/// **`frr-mab` 选择器只被三个算法共用，前两个算法用默认的 `round-robin`**。给所有算法都配 `frr-mab` 会让每个算法都
/// 独立学习自己的滑动窗口，**而"正交"退化成"五个独立的单臂老虎机"**。
/// **The `frr-mab` selector is shared by three algorithms only; the first two use the default `round-robin`.** Giving every algorithm
/// `frr-mab` makes each learn its own sliding window, **and "orthogonal" degenerates into five independent bandits**.
/// **`intensification` 的规模固定为 1**。让它跟随调用方的规模会让"强化"退化成又一次全局搜索，
/// **而组合仍然声称自己覆盖了强化这一类行为**。
/// **`intensification` has a fixed size of 1.** Letting it follow the caller's size degrades "intensification" into another global search,
/// **while the portfolio still claims to cover the intensification behaviour class**.
/// </remarks>
public static class OrthogonalOperatorPortfolio {
    /// <summary>
    /// 连续问题的正交组合 / The orthogonal portfolio for continuous problems.
    /// </summary>
    /// <param name="size">种群规模 / The population size.</param>
    /// <returns>算法配置列表 / The algorithm configuration list.</returns>
    public static Result<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>> Continuous(
        int size = 20) {
        if (size < 4) {
            return SearchCodecSupport.SearchInvalid<IReadOnlyList<AlgorithmConfig>>(
                "orthogonal continuous portfolio requires at least four individuals");
        }
        var selector = new ComponentSpec("frr-mab");
        var restart = new JsonObject { ["restartOnImport"] = true };
        return Results.Ok<IReadOnlyList<AlgorithmConfig>>(new List<AlgorithmConfig> {
            new("distribution", new ComponentSpec("cma-es", restart), size),
            new("adaptive-difference", new ComponentSpec("l-shade"), size),
            new("encircling", new ComponentSpec("gwo"), size,
                Updates: new[] { new ComponentSpec("hho"), new ComponentSpec("gwo") },
                Mutations: new[] { new ComponentSpec("none") },
                Selection: selector),
            new("escape", new ComponentSpec("genetic"), size,
                Updates: new[] { new ComponentSpec("opposition"), new ComponentSpec("de") },
                Mutations: new[] { new ComponentSpec("levy") },
                Selection: selector),
            new("intensification", new ComponentSpec("genetic"), 1,
                Updates: new[] { new ComponentSpec("pattern-search") },
                Mutations: new[] { new ComponentSpec("none") }),
        });
    }

    /// <summary>
    /// 排列问题的正交组合 / The orthogonal portfolio for permutation problems.
    /// </summary>
    /// <param name="size">种群规模 / The population size.</param>
    /// <returns>算法配置列表 / The algorithm configuration list.</returns>
    public static Result<IReadOnlyList<AlgorithmConfig>, ErrorCode, Error<ErrorCode>> Permutation(
        int size = 20) {
        if (size <= 0) {
            return SearchCodecSupport.SearchInvalid<IReadOnlyList<AlgorithmConfig>>(
                "invalid permutation portfolio size");
        }
        return Results.Ok<IReadOnlyList<AlgorithmConfig>>(new List<AlgorithmConfig> {
            new("permutation", new ComponentSpec("genetic"), size,
                Updates: new[] { new ComponentSpec("order") },
                Mutations: new[] { new ComponentSpec("inversion"), new ComponentSpec("swap") },
                Selection: new ComponentSpec("frr-mab")),
        });
    }
}
