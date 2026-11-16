// 搜索特征与奖励策略 / Search features and reward policies.
// 与固定 Kotlin （5029 B）与（4868 B）对应。
// And (4868 B).

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 搜索进度 / Search progress.
/// </summary>
/// <param name="Decision">决策序号 / The decision index.</param>
/// <param name="Evaluations">已用评估 / The consumed evaluations.</param>
/// <param name="EvaluationLimit">评估上限 / The evaluation limit.</param>
/// <param name="Stagnation">停滞计数 / The stagnation count.</param>
/// <param name="Diversity">多样性 / The diversity.</param>
/// <param name="SuccessRate">成功率 / The success rate.</param>
/// <param name="Cost">代价 / The cost.</param>
public sealed record SearchProgress(
    [property: JsonPropertyName("decision")] long Decision,
    [property: JsonPropertyName("evaluations")] long Evaluations,
    [property: JsonPropertyName("evaluationLimit")] long EvaluationLimit,
    [property: JsonPropertyName("stagnation")] long Stagnation,
    [property: JsonPropertyName("diversity")] double Diversity,
    [property: JsonPropertyName("successRate")] Flt64 SuccessRate = default,
    [property: JsonPropertyName("cost")] Flt64 Cost = default);

/// <summary>
/// 特征提取器 / A feature extractor.
/// </summary>
public interface IFeatureExtractor {
    /// <summary>特征版本 / The feature version.</summary>
    string Version { get; }

    /// <summary>状态数量 / The state count.</summary>
    int? StateCount => null;

    /// <summary>
    /// 提取选择上下文 / Extract the selection context.
    /// </summary>
    /// <param name="progress">搜索进度 / The search progress.</param>
    /// <returns>选择上下文 / The selection context.</returns>
    SelectionContext Extract(SearchProgress progress);
}

/// <summary>
/// 进度特征 / Progress features.
/// </summary>
/// <remarks>
/// **状态是 `进度桶 × 8 + 停滞桶`**：进度分 8 桶（`0..7`），停滞夹到 `0..7`（每桶 1 步）。把停滞当连续量直接加会让状态数
/// 随停滞计数线性膨胀，而策略的状态上限（64）被静默越界。
/// **The state is `progress bucket × 8 + stagnation bucket`**: progress has 8 buckets (`0..7`) and stagnation is clamped to `0..7`
/// (one bucket per step). Treating stagnation as a continuous quantity added directly makes the state count grow linearly with the
/// stagnation count, silently exceeding the policy's 64-state limit.
/// **无评估上限时进度恒为 0**。用 `evaluations / Long.MAX_VALUE` 虽然也近似为 0，但当上限是 `long.MaxValue` 且评估数也很大时，
/// 整数除法会给出 0 而浮点除法给出一个极小的非零值——**两者最终都落进第 0 桶，但特征向量里的连续分量不同**。
/// **Without an evaluation limit the progress is exactly 0.** Using `evaluations / long.MaxValue` looks equivalent, but when the limit is
/// `long.MaxValue` and the evaluation count is also large, integer division yields 0 while floating-point division yields a tiny non-zero
/// value — **both land in bucket 0, yet the continuous feature components differ**.
/// **特征向量是 `[进度, 停滞/1000, 多样性, 成功率, 代价]`**，前三个是 `double`、后两个是 `Flt64`。省略后两个会让奖励策略读到
/// 恒为零的成功率，**而上下文看起来完全正常**。
/// **The feature vector is `[progress, stagnation/1000, diversity, success rate, cost]`**, the first three `double` and the last two
/// `Flt64`. Omitting the last two makes reward policies read a permanently zero success rate **while the context looks perfectly normal**.
/// </remarks>
public sealed class ProgressFeatures : IFeatureExtractor {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static ProgressFeatures Instance { get; } = new();

    /// <inheritdoc/>
    public string Version => "progress:1";

    /// <inheritdoc/>
    public int? StateCount => 64;

    /// <inheritdoc/>
    public SelectionContext Extract(SearchProgress progress) {
        ArgumentNullException.ThrowIfNull(progress);
        double ratio = progress.EvaluationLimit == long.MaxValue
            ? 0.0
            : progress.Evaluations / (double)global::System.Math.Max(progress.EvaluationLimit, 1L);
        double bounded = global::System.Math.Clamp(ratio, 0.0, 1.0);
        // **停滞桶必须夹到 `0..7`**：不夹会让状态越过 64 个上限，而策略不会报错、只是把越界状态当作新状态。
        // **The stagnation bucket must be clamped to `0..7`**: without it the state exceeds the 64-state limit, and policies do not
        // report an error — they just treat out-of-range states as new ones.
        long stagnationBucket = global::System.Math.Clamp(progress.Stagnation, 0L, 7L);
        int state = (int)(bounded * 7.0) * 8 + (int)stagnationBucket;
        var features = new List<Flt64> {
            new(bounded),
            new(global::System.Math.Min(progress.Stagnation, 1000L) / 1000.0),
            new(progress.Diversity),
            progress.SuccessRate,
            progress.Cost,
        };
        return new SelectionContext(state, features, progress.Decision, progress.Stagnation);
    }
}

/// <summary>
/// 奖励上下文 / A reward context.
/// </summary>
/// <param name="Improved">是否改进 / Whether it improved.</param>
/// <param name="Contribution">贡献度 / The contribution.</param>
/// <param name="Evaluations">评估次数 / The evaluation count.</param>
/// <param name="ElapsedNanos">耗时（纳秒）/ The elapsed nanoseconds.</param>
/// <param name="Action">动作名 / The action name.</param>
/// <param name="Reference">参考目标坐标 / The reference objective coordinates.</param>
/// <param name="Candidates">候选目标坐标 / The candidate objective coordinates.</param>
public sealed record RewardContext(
    [property: JsonPropertyName("improved")] bool Improved,
    [property: JsonPropertyName("contribution")] Flt64 Contribution,
    [property: JsonPropertyName("evaluations")] long Evaluations,
    [property: JsonPropertyName("elapsedNanos")] long ElapsedNanos = 0,
    [property: JsonPropertyName("action")] string Action = "algorithm",
    [property: JsonPropertyName("reference")] IReadOnlyList<IReadOnlyList<Flt64>>? Reference = null,
    [property: JsonPropertyName("candidates")] IReadOnlyList<IReadOnlyList<Flt64>>? Candidates = null);

/// <summary>
/// 奖励策略 / A reward policy.
/// </summary>
/// <remarks>
/// **`Evaluate` 把非有限奖励变成失败结果**。让 `NaN` 流进策略更新会让整张表变成 `NaN`，而那之后**每一次选择都返回某个合法动作**，
/// 只是排序已经失效。
/// **`Evaluate` turns a non-finite reward into a failed result.** Letting `NaN` flow into the policy update turns the whole table into
/// `NaN`, after which **every selection still returns some legal action** — only the ordering is broken.
/// </remarks>
public interface IRewardPolicy {
    /// <summary>是否需要目标坐标 / Whether objective coordinates are required.</summary>
    bool RequiresCoordinates => false;

    /// <summary>
    /// 计算奖励 / Compute the reward.
    /// </summary>
    /// <param name="context">奖励上下文 / The reward context.</param>
    /// <returns>奖励 / The reward.</returns>
    Flt64 Reward(RewardContext context);

    /// <summary>
    /// 计算奖励并校验有限性 / Compute the reward and check finiteness.
    /// </summary>
    /// <param name="context">奖励上下文 / The reward context.</param>
    /// <returns>奖励 / The reward.</returns>
    Result<Flt64, ErrorCode, Error<ErrorCode>> Evaluate(RewardContext context) {
        Result<Flt64, ErrorCode, Error<ErrorCode>> computed =
            SearchBoundary.Execute(() => Reward(context));
        if (computed is Failed<Flt64, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Flt64>(failed.Error);
        }
        Flt64 value = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)computed).Value;
        return double.IsFinite(value.ToDouble())
            ? Results.Ok(value)
            : SearchCodecSupport.SearchInvalid<Flt64>("奖励不是有限数 / Reward is not finite");
    }
}

/// <summary>
/// 改进奖励 / Improvement reward.
/// </summary>
public sealed class ImprovementRewardPolicy : IRewardPolicy {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static ImprovementRewardPolicy Instance { get; } = new();

    /// <inheritdoc/>
    public Flt64 Reward(RewardContext context) {
        ArgumentNullException.ThrowIfNull(context);
        return context.Improved ? Flt64.One : Flt64.Zero;
    }
}

/// <summary>
/// 代价感知奖励 / Cost-aware reward.
/// </summary>
public sealed class CostAwareRewardPolicy : IRewardPolicy {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static CostAwareRewardPolicy Instance { get; } = new();

    /// <inheritdoc/>
    public Flt64 Reward(RewardContext context) {
        ArgumentNullException.ThrowIfNull(context);
        return new Flt64(context.Contribution.ToDouble()
            / global::System.Math.Max(context.Evaluations, 1L));
    }
}

/// <summary>
/// 奖励上下文构造 / Reward-context construction.
/// </summary>
public static class RewardContexts {
    /// <summary>
    /// 从个体集合构造奖励上下文 / Build a reward context from individual sets.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="reference">参考个体 / The reference individuals.</param>
    /// <param name="candidates">候选个体 / The candidates.</param>
    /// <param name="contribution">贡献度 / The contribution.</param>
    /// <param name="evaluations">评估次数 / The evaluation count.</param>
    /// <param name="elapsedNanos">耗时（纳秒）/ The elapsed nanoseconds.</param>
    /// <param name="action">动作名 / The action name.</param>
    /// <param name="projection">目标坐标投影 / The objective-coordinate projection.</param>
    /// <param name="admissible">个体是否合法可行 / Whether an individual is valid and feasible.</param>
    /// <returns>奖励上下文 / The reward context.</returns>
    public static RewardContext Build<ObjValue, V>(
        IReadOnlyList<IIndividual<ObjValue, V>> reference,
        IReadOnlyList<IIndividual<ObjValue, V>> candidates,
        (bool Improved, double Ratio) contribution,
        long evaluations,
        long elapsedNanos,
        string action,
        Func<ObjValue, IReadOnlyList<Flt64>>? projection,
        Func<IIndividual<ObjValue, V>, bool> admissible)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(admissible);
        return new RewardContext(
            contribution.Improved, new Flt64(contribution.Ratio), evaluations,
            elapsedNanos, action, Coordinates(reference), Coordinates(candidates));

        IReadOnlyList<IReadOnlyList<Flt64>> Coordinates(
            IReadOnlyList<IIndividual<ObjValue, V>> entries) {
            if (projection is null) {
                return Array.Empty<IReadOnlyList<Flt64>>();
            }
            var result = new List<IReadOnlyList<Flt64>>(entries.Count);
            foreach (IIndividual<ObjValue, V> entry in entries) {
                if (admissible(entry)) {
                    result.Add(projection(entry.Objective));
                }
            }
            return result;
        }
    }
}

/// <summary>
/// 时间归一化奖励 / Time-normalized reward.
/// </summary>
/// <remarks>
/// **额外耗时为负时返回 `NaN`，由 `Evaluate` 转成失败**。夹到 0 会让一次"时钟倒退"变成一次极高的奖励，
/// 而策略照样更新、只是朝错误方向。
/// **A negative elapsed time returns `NaN`, which `Evaluate` turns into a failure.** Clamping to 0 turns a clock regression into an
/// extremely high reward, and the policy still updates — just in the wrong direction.
/// </remarks>
public sealed class TimeNormalizedReward : IRewardPolicy {
    /// <summary>
    /// 构造奖励 / Construct the reward.
    /// </summary>
    /// <param name="epsilonSeconds">秒级下界 / The seconds floor.</param>
    public TimeNormalizedReward(double epsilonSeconds = 1e-9) => EpsilonSeconds = epsilonSeconds;

    /// <summary>秒级下界 / The seconds floor.</summary>
    public double EpsilonSeconds { get; }

    /// <inheritdoc/>
    public bool RequiresCoordinates => true;

    /// <inheritdoc/>
    public Flt64 Reward(RewardContext context) {
        ArgumentNullException.ThrowIfNull(context);
        if (!double.IsFinite(EpsilonSeconds) || EpsilonSeconds <= 0.0 || context.ElapsedNanos < 0) {
            return new Flt64(double.NaN);
        }
        return new Flt64(RewardSupport.ScalarGain(context) / ((context.ElapsedNanos / 1e9) + EpsilonSeconds));
    }
}

/// <summary>
/// 极值奖励状态 / The extreme-value reward state.
/// </summary>
/// <param name="Size">窗口大小 / The window size.</param>
/// <param name="Observations">观测 / The observations.</param>
public sealed record ExtremeValueState(
    [property: JsonPropertyName("size")] int Size,
    [property: JsonPropertyName("observations")] IReadOnlyList<ExtremeValueObservation> Observations);

/// <summary>
/// 一条极值观测 / One extreme-value observation.
/// </summary>
/// <param name="Action">动作名 / The action name.</param>
/// <param name="Gain">增益 / The gain.</param>
public sealed record ExtremeValueObservation(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("gain")] double Gain);

/// <summary>
/// 极值奖励 / Extreme-value reward.
/// </summary>
/// <remarks>
/// **返回值是"本动作在窗口内的最大增益"，不是全局最大值**。用全局最大值会让所有动作拿到同一个奖励，
/// **策略因此学不到动作之间的差异，而 Q 表仍然在更新**。
/// **The returned value is the maximum gain of this action inside the window, not the global maximum.** Using the global maximum gives
/// every action the same reward, so **the policy learns nothing about differences between actions while the Q table keeps updating**.
/// **观测按动作分别取极值，但窗口是按"全部动作的观测"滚动的**。按动作各自滚动会让低频动作窗口过长，
/// 高频动作的旧增益被过早丢弃。
/// **Observations are aggregated per action, but the window rolls over "observations of all actions".** Rolling per action makes the
/// window of a rarely-used action far too long and discards a frequently-used action's gains too early.
/// </remarks>
public sealed class ExtremeValueReward : IRewardPolicy, IRecoverableComponent {
    private readonly List<ExtremeValueObservation> _observations = new();

    /// <summary>
    /// 构造奖励 / Construct the reward.
    /// </summary>
    /// <param name="windowSize">窗口大小 / The window size.</param>
    public ExtremeValueReward(int windowSize = 50) => WindowSize = windowSize;

    /// <summary>窗口大小 / The window size.</summary>
    public int WindowSize { get; }

    /// <inheritdoc/>
    public bool RequiresCoordinates => true;

    /// <inheritdoc/>
    public Flt64 Reward(RewardContext context) {
        ArgumentNullException.ThrowIfNull(context);
        double gain = RewardSupport.ScalarGain(context);
        if (WindowSize <= 0 || !double.IsFinite(gain)) {
            return new Flt64(double.NaN);
        }
        _observations.Add(new ExtremeValueObservation(context.Action, gain));
        while (_observations.Count > WindowSize) {
            _observations.RemoveAt(0);
        }
        double best = 0.0;
        bool found = false;
        foreach (ExtremeValueObservation observation in _observations) {
            if (string.Equals(observation.Action, context.Action, StringComparison.Ordinal)
                && (!found || observation.Gain > best)) {
                best = observation.Gain;
                found = true;
            }
        }
        return new Flt64(found ? best : 0.0);
    }

    /// <inheritdoc/>
    public Result<JsonElement, ErrorCode, Error<ErrorCode>> Save() =>
        Results.Ok(JsonSerializer.SerializeToElement(
            new ExtremeValueState(WindowSize, new List<ExtremeValueObservation>(_observations))));

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Load(JsonElement state) {
        Result<ExtremeValueState, ErrorCode, Error<ErrorCode>> decoded = SearchBoundary.Execute(
            () => JsonSerializer.Deserialize<ExtremeValueState>(state.GetRawText())!);
        if (decoded is Failed<ExtremeValueState, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        ExtremeValueState saved = ((Ok<ExtremeValueState, ErrorCode, Error<ErrorCode>>)decoded).Value;
        if (saved.Size != WindowSize || saved.Observations.Count > WindowSize) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid extreme-value reward state");
        }
        foreach (ExtremeValueObservation observation in saved.Observations) {
            if (string.IsNullOrWhiteSpace(observation.Action)
                || !double.IsFinite(observation.Gain) || observation.Gain < 0.0) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid extreme-value reward state");
            }
        }
        _observations.Clear();
        _observations.AddRange(saved.Observations);
        return Results.Ok(new Success());
    }
}

/// <summary>
/// 支配奖励 / Dominance reward.
/// </summary>
public sealed class DominanceReward : IRewardPolicy {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static DominanceReward Instance { get; } = new();

    /// <inheritdoc/>
    public Flt64 Reward(RewardContext context) {
        ArgumentNullException.ThrowIfNull(context);
        return context.Contribution;
    }
}

/// <summary>
/// 奖励计算的共用逻辑 / Shared reward computation.
/// </summary>
internal static class RewardSupport {
    /// <summary>
    /// 单目标标量增益 / The single-objective scalar gain.
    /// </summary>
    /// <remarks>
    /// **任一点非单维或非有限时返回 `NaN`**，而不是跳过该点。跳过会让一个多维坐标被静默忽略，
    /// 剩下的单维点算出一个"看起来合理"的增益。
    /// **Returns `NaN` when any point is non-scalar or non-finite** instead of skipping it. Skipping silently ignores a multi-dimensional
    /// coordinate and the remaining scalar points produce a plausible-looking gain.
    /// </remarks>
    /// <param name="context">奖励上下文 / The reward context.</param>
    /// <returns>增益 / The gain.</returns>
    internal static double ScalarGain(RewardContext context) {
        IReadOnlyList<IReadOnlyList<Flt64>> reference =
            context.Reference ?? Array.Empty<IReadOnlyList<Flt64>>();
        IReadOnlyList<IReadOnlyList<Flt64>> candidates =
            context.Candidates ?? Array.Empty<IReadOnlyList<Flt64>>();
        if (reference.Count == 0 && candidates.Count == 0) {
            return context.Contribution.ToDouble();
        }
        foreach (IReadOnlyList<IReadOnlyList<Flt64>> group in new[] { reference, candidates }) {
            foreach (IReadOnlyList<Flt64> point in group) {
                if (point.Count != 1 || !double.IsFinite(point[0].ToDouble())) {
                    return double.NaN;
                }
            }
        }
        if (reference.Count == 0 || candidates.Count == 0) {
            return 0.0;
        }
        double referenceMin = reference[0][0].ToDouble();
        foreach (IReadOnlyList<Flt64> point in reference) {
            double value = point[0].ToDouble();
            if (value < referenceMin) {
                referenceMin = value;
            }
        }
        double candidateMin = candidates[0][0].ToDouble();
        foreach (IReadOnlyList<Flt64> point in candidates) {
            double value = point[0].ToDouble();
            if (value < candidateMin) {
                candidateMin = value;
            }
        }
        return global::System.Math.Max(referenceMin - candidateMin, 0.0);
    }
}
