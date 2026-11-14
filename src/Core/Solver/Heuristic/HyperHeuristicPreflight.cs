// 超启发式配置的预检 / Preflight validation for a hyper-heuristic configuration.
// 对应固定 Kotlin 基线 `HyperHeuristicSolver.kt`（47241 B）里 `open()` 前半段的校验阶梯。
// Mirrors the validation ladder in the first half of `open()` in the pinned baseline's `HyperHeuristicSolver.kt` (47241 B).

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 组件规格的默认值 / The default component specs.
/// </summary>
/// <remarks>
/// **这些默认值不是"随便挑的合理值"，而是基线 `HyperHeuristicConfig` 的构造默认值**。写错一个，
/// 预检会认为"配置缺字段"，而用户手里那份配置其实完全合法——**报错指向配置，问题却在默认值表里**。
///
/// **These defaults are not "reasonable picks" but the baseline `HyperHeuristicConfig`'s constructor defaults.** Get one wrong and the preflight
/// reports "the configuration is missing a field" while the user's configuration is in fact entirely legal — **the error points at the configuration
/// while the problem sits in the default table**.
/// </remarks>
public static class HyperHeuristicDefaults {
    /// <summary>默认选择策略 / The default selection policy.</summary>
    public static ComponentSpec Selection { get; } = new("round-robin");

    /// <summary>默认预算分配器 / The default budget allocator.</summary>
    public static ComponentSpec Allocator { get; } = new("fixed");

    /// <summary>默认接受策略 / The default acceptance policy.</summary>
    public static ComponentSpec Acceptance { get; } = new("not-worse");

    /// <summary>默认交换策略 / The default exchange policy.</summary>
    public static ComponentSpec Exchange { get; } = new("none");

    /// <summary>默认特征提取器 / The default feature extractor.</summary>
    public static ComponentSpec Features { get; } = new("progress");

    /// <summary>默认回报策略 / The default reward policy.</summary>
    public static ComponentSpec Reward { get; } = new("improvement");
}

/// <summary>
/// 超启发式配置的预检 / Preflight validation for a hyper-heuristic configuration.
/// </summary>
/// <remarks>
/// **这一步存在的原因**：`open()` 会真的构造运行器、分配模型、申请令牌表。若配置有误，那些构造在半路失败，
/// 调用方拿到的错误往往指向**第一个碰巧失败的构造函数**，而不是配置里那一行写错的字段。
/// 预检把"能不能跑"与"跑起来"分成两步，**让错误早一步、且指向配置**。
///
/// **Why this step exists**: `open()` really constructs runners, allocates models and claims token tables. On a bad configuration those constructions
/// fail midway, and the error the caller sees usually points at **whichever constructor happened to fail first**, not at the misspelt field in the
/// configuration. The preflight splits "could this run" from "run it", **so the error comes one step earlier and points at the configuration**.
///
/// **校验顺序按依赖性排列**：先解析全局组件，再比较它们的**兼容性**（选择器的状态空间要装得下特征空间），
/// 最后才管持久化。反过来会在"配置根本跑不起来"时先去检查"能不能存盘"，**把真正的错误盖住**。
/// **The checks are ordered by dependency**: resolve the global components, compare their **compatibility** (the selector's state space must hold the
/// feature space), and only then consider persistence. The other order checks "can this be checkpointed" while "this cannot run at all",
/// **burying the real error**.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public static class HyperHeuristicPreflight<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 预检一份配置 / Check a configuration.
    /// </summary>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="registry">搜索注册表 / The search registry.</param>
    /// <param name="random">随机源 / The random source.</param>
    /// <returns>预检结果 / The preflight result.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Check(
        HyperHeuristicConfig config,
        SearchRegistry<O, V> registry,
        SearchRandom random) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(random);
        Result<Success, ErrorCode, Error<ErrorCode>> valid = config.Validate();
        if (valid is Failed<Success, ErrorCode, Error<ErrorCode>> invalid) {
            return Results.Failed<Success>(invalid.Error);
        }

        PersistenceConfig persistence = config.Persistence ?? new PersistenceConfig();
        LearningConfig learning = config.Learning ?? new LearningConfig();
        MemoryConfig memory = config.Memory ?? new MemoryConfig();
        BudgetConfig budget = config.Budget;

        Result<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>> problemResult =
            registry.Problem(config.Problem, random);
        if (problemResult is Failed<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>> problemFailure) {
            return Results.Failed<Success>(problemFailure.Error);
        }
        SearchProblem<O, V> problem =
            ((Ok<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>>)problemResult).Value;

        if (persistence.Enabled && !registry.RecoverableProblem(config.Problem, problem)) {
            return Fail("problem callbacks must be declared stateless or expose a problem state");
        }

        ComponentSpec selectionSpec = config.Selection ?? HyperHeuristicDefaults.Selection;
        ComponentSpec featuresSpec = learning.Features ?? HyperHeuristicDefaults.Features;
        ComponentSpec rewardSpec = learning.Reward ?? HyperHeuristicDefaults.Reward;
        ComponentSpec acceptanceSpec = memory.Acceptance ?? HyperHeuristicDefaults.Acceptance;
        ComponentSpec exchangeSpec = memory.Exchange ?? HyperHeuristicDefaults.Exchange;
        ComponentSpec allocatorSpec = budget.Allocator ?? HyperHeuristicDefaults.Allocator;

        var algorithmIds = new List<string>(config.Algorithms.Count);
        foreach (AlgorithmConfig algorithm in config.Algorithms) {
            algorithmIds.Add(algorithm.Id);
        }

        Result<ISelectionPolicy, ErrorCode, Error<ErrorCode>> selector = registry.Selection(
            selectionSpec, algorithmIds, learning.Mode != LearningMode.Frozen);
        if (selector is Failed<ISelectionPolicy, ErrorCode, Error<ErrorCode>> selectorFailure) {
            return Fail("invalid selector: " + selectorFailure.Error.Message);
        }
        Result<IFeatureExtractor, ErrorCode, Error<ErrorCode>> features =
            registry.Features(featuresSpec);
        if (features is Failed<IFeatureExtractor, ErrorCode, Error<ErrorCode>> featuresFailure) {
            return Fail("invalid features: " + featuresFailure.Error.Message);
        }
        Result<IRewardPolicy, ErrorCode, Error<ErrorCode>> reward = registry.Reward(rewardSpec);
        if (reward is Failed<IRewardPolicy, ErrorCode, Error<ErrorCode>> rewardFailure) {
            return Fail("invalid reward: " + rewardFailure.Error.Message);
        }
        long horizon = budget.Decisions ?? budget.Steps ?? budget.Evaluations ?? long.MaxValue;
        Result<IMemoryAcceptance<O, V>, ErrorCode, Error<ErrorCode>> acceptance =
            registry.Acceptance(acceptanceSpec, problem, horizon);
        if (acceptance is Failed<IMemoryAcceptance<O, V>, ErrorCode, Error<ErrorCode>> acceptanceFailure) {
            return Fail("invalid acceptance: " + acceptanceFailure.Error.Message);
        }
        Result<IExchangePolicy<O, V>, ErrorCode, Error<ErrorCode>> exchange =
            registry.Exchange(exchangeSpec);
        if (exchange is Failed<IExchangePolicy<O, V>, ErrorCode, Error<ErrorCode>> exchangeFailure) {
            return Fail("invalid exchange: " + exchangeFailure.Error.Message);
        }
        Result<IBudgetAllocator, ErrorCode, Error<ErrorCode>> allocator =
            registry.Allocator(allocatorSpec);
        if (allocator is Failed<IBudgetAllocator, ErrorCode, Error<ErrorCode>> allocatorFailure) {
            return Fail("invalid allocator: " + allocatorFailure.Error.Message);
        }

        ISelectionPolicy policy =
            ((Ok<ISelectionPolicy, ErrorCode, Error<ErrorCode>>)selector).Value;
        IFeatureExtractor extractor =
            ((Ok<IFeatureExtractor, ErrorCode, Error<ErrorCode>>)features).Value;

        // **状态空间必须装得下特征空间**。装不下时选择器会把不同的特征状态压成同一个，**策略学到的是"被合并过的状态"**，
        // 而训练曲线依旧平滑。
        // **The state space must hold the feature space.** When it cannot, the selector collapses distinct feature states into one, **so the policy
        // learns over merged states** while the training curve stays smooth.
        if (extractor.StateCount is int needed
            && policy.StateCount is int available
            && available < needed) {
            return Fail("selector state space is smaller than feature space");
        }

        IRewardPolicy rewardPolicy = ((Ok<IRewardPolicy, ErrorCode, Error<ErrorCode>>)reward).Value;
        if (rewardPolicy.RequiresCoordinates && problem.ObjectiveCoordinates is null) {
            return Fail("reward requires explicit normalized minimization coordinates");
        }
        if (rewardSpec.Type is "time-normalized" or "extreme-value" && problem.MultiObjective) {
            return Fail("scalar gain reward does not accept vector objectives");
        }

        if (!persistence.Enabled) {
            return Results.Ok(new Success());
        }
        if (!registry.ComponentRecoverable("features", featuresSpec, extractor)
            || !registry.ComponentRecoverable("reward", rewardSpec, rewardPolicy)
            || !registry.ComponentRecoverable("acceptance", acceptanceSpec, acceptance)
            || !registry.ComponentRecoverable("exchange", exchangeSpec, exchange)
            || !registry.ComponentRecoverable("allocator", allocatorSpec, allocator)) {
            return Fail("global components must be explicitly stateless or implement recovery");
        }
        return Results.Ok(new Success());
    }

    /// <summary>失败 / A failure.</summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
