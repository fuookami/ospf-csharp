// 策略训练的数据契约与轨迹持久化 / The policy trainer's data contract and trajectory persistence.
// `TrainingCase` / `TrainingSplit` / `TrainingObservation` / `TrainingReport` / `PolicyEvaluationReport`、
// `PolicyTrainer.replay` 与 `TrajectoryFiles`。
// The half of the: that does **not** depend on the solver:
// `TrainingCase` / `TrainingSplit` / `TrainingObservation` / `TrainingReport` / `PolicyEvaluationReport`,
// `PolicyTrainer.replay` and `TrajectoryFiles`.
//
// **未实现的部分**：`train` / `PolicyEvaluator.evaluate` / `compare` —— 三者都要 `HyperHeuristicSolver.open(...)`，
// 而该文件在有 47241 B，尚未实现。`PolicyTrainer` 里**能独立成立的那一半**先落地并压满测试。
// **Not implemented**: `train` / `PolicyEvaluator.evaluate` / `compare` — all three need `HyperHeuristicSolver.open(...)`, and that file is 47241 B in
// the implementation and not yet implemented. The **half of `PolicyTrainer` that stands on its own** lands first, with tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Learning;

/// <summary>
/// 一个训练实例 / One training instance.
/// </summary>
/// <param name="Id">标识 / The identifier.</param>
/// <param name="Problem">问题的组件规格 / The problem's component spec.</param>
public sealed record TrainingCase(string Id, ComponentSpec Problem);

/// <summary>
/// 训练集划分 / A training split.
/// </summary>
/// <param name="Training">训练集 / The training set.</param>
/// <param name="Validation">验证集 / The validation set.</param>
/// <param name="Test">测试集 / The test set.</param>
public sealed record TrainingSplit(
    IReadOnlyList<TrainingCase> Training,
    IReadOnlyList<TrainingCase> Validation,
    IReadOnlyList<TrainingCase> Test) {
    /// <summary>
    /// **校验划分：训练集非空、标识非空且全局唯一、问题规格全局不重复** / Validate the split: a non-empty training set, non-blank globally unique identifiers, and globally distinct problem specs.
    /// </summary>
    /// <remarks>
    /// **"问题规格也要唯一"不是多余的**：同一个问题既在训练集又在测试集里，会让"泛化"这个词失去意义——
    /// **而报告上的训练/测试指标都会很漂亮**。这正是本校验要拦下的情况。
    ///
    /// **"The problem specs must also be distinct" is not redundant**: one problem sitting in both the training and the test set makes the word
    /// "generalization" meaningless — **while both the training and the test numbers in the report look excellent**. That is exactly the case this check
    /// stops.
    /// </remarks>
    /// <returns>校验结果 / The validation result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        var all = new List<TrainingCase>(Training.Count + Validation.Count + Test.Count);
        all.AddRange(Training);
        all.AddRange(Validation);
        all.AddRange(Test);
        if (Training.Count == 0) {
            return Invalid("training, validation and test instances must be nonempty/unique and disjoint");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var problems = new HashSet<(string, int)>();
        foreach (TrainingCase instance in all) {
            if (string.IsNullOrWhiteSpace(instance.Id)) {
                return Invalid("training, validation and test instances must be nonempty/unique and disjoint");
            }
            if (!ids.Add(instance.Id)) {
                return Invalid("training, validation and test instances must be nonempty/unique and disjoint");
            }
            if (!problems.Add(SpecKey(instance.Problem))) {
                return Invalid("training, validation and test instances must be nonempty/unique and disjoint");
            }
        }
        return Results.Ok(new Success());
    }

    /// <summary>规格的可比较键 / A comparable key for a component spec.</summary>
    /// <param name="spec">组件规格 / The component spec.</param>
    /// <returns>键 / The key.</returns>
    private static (string, int) SpecKey(ComponentSpec spec) =>
        (spec.Type + "|" + (spec.Parameters?.ToJsonString() ?? "{}"), spec.Version);

    /// <summary>失败 / A failure.</summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Invalid(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}

/// <summary>
/// 一次训练观测 / One training observation.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Instance">实例标识 / The instance identifier.</param>
/// <param name="Seed">随机种子 / The random seed.</param>
/// <param name="Result">搜索结果 / The search result.</param>
public sealed record TrainingObservation<O, V>(string Instance, long Seed, SearchResult<O, V> Result)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 一次训练的结果 / The outcome of one training run.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Policy">训练出的策略档案 / The trained policy archive.</param>
/// <param name="Training">训练观测 / The training observations.</param>
/// <param name="Validation">验证观测 / The validation observations.</param>
/// <param name="Test">测试观测 / The test observations.</param>
public sealed record TrainingReport<O, V>(
    PolicyArchive Policy,
    IReadOnlyList<TrainingObservation<O, V>> Training,
    IReadOnlyList<TrainingObservation<O, V>> Validation,
    IReadOnlyList<TrainingObservation<O, V>> Test)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 一次策略评估的结果 / The outcome of one policy evaluation.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Observations">观测 / The observations.</param>
/// <param name="Quality">质量指标 / The quality figures.</param>
/// <param name="FailureRate">失败率 / The failure rate.</param>
/// <param name="Evaluations">评估次数 / The evaluation count.</param>
/// <param name="ActiveNanos">有效纳秒 / The active nanoseconds.</param>
public sealed record PolicyEvaluationReport<O, V>(
    IReadOnlyList<TrainingObservation<O, V>> Observations,
    IReadOnlyList<Flt64> Quality,
    Flt64 FailureRate,
    long Evaluations,
    long ActiveNanos)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 策略训练器：当前只有"回放"与数据契约 / The policy trainer, currently carrying only the replay path and the data contract.
/// </summary>
/// <remarks>
/// **`Replay` 的校验顺序不是随意的**：先筛作用域，再校验特征版本与去重。反过来会让"别的作用域的轨迹"被算进重复检测，
/// **于是同一段轨迹因为被另一个作用域也记录了一份而报"重复"**。
///
/// **`Replay`'s validation order is not arbitrary**: filter by scope first, then check the feature version and duplicates. The other order counts
/// **another scope's** transitions as duplicates, **so one trajectory is reported as "duplicated" merely because another scope also recorded it**.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class PolicyTrainer<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>搜索注册表 / The search registry.</summary>
    private readonly SearchRegistry<O, V> _registry;

    /// <summary>
    /// 构造训练器 / Construct the trainer.
    /// </summary>
    /// <param name="registry">搜索注册表 / The search registry.</param>
    public PolicyTrainer(SearchRegistry<O, V> registry) {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>搜索注册表 / The search registry.</summary>
    public SearchRegistry<O, V> Registry => _registry;

    /// <summary>
    /// 把轨迹回放进一个选择策略 / Replay trajectories into a selection policy.
    /// </summary>
    /// <param name="policy">选择策略 / The selection policy.</param>
    /// <param name="transitions">轨迹 / The transitions.</param>
    /// <param name="featureVersion">特征版本 / The feature version.</param>
    /// <param name="scope">作用域 / The scope.</param>
    /// <returns>回放结果 / The replay result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Replay(
        ISelectionPolicy policy,
        IReadOnlyList<SelectionTransition> transitions,
        string featureVersion,
        string scope = "algorithm") {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(featureVersion);
        ArgumentNullException.ThrowIfNull(scope);
        var selected = new List<SelectionTransition>(transitions.Count);
        foreach (SelectionTransition transition in transitions) {
            if (string.Equals(transition.Scope, scope, StringComparison.Ordinal)) {
                selected.Add(transition);
            }
        }
        var seen = new HashSet<(string, long)>();
        foreach (SelectionTransition transition in selected) {
            if (!string.Equals(transition.FeatureVersion, featureVersion, StringComparison.Ordinal)
                || !seen.Add((transition.Episode, transition.Decision))) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "incompatible features or duplicate transitions"));
            }
        }
        foreach (SelectionTransition transition in selected) {
            Result<Success, ErrorCode, Error<ErrorCode>> observed = policy.Observe(transition);
            if (observed is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<Success>(failure.Error);
            }
        }
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 训练一个策略 / Train a policy.
    /// </summary>
    /// <remarks>
    /// **训练是"逐回合接续"的**：每个（实例, 种子）组合用**上一回合导出的策略**开局，跑完再把导出的策略接给下一回合。
    /// 若每个回合都从空策略开始，最终导出的只是**最后一个回合**学到的内容——而报告里写着"训练了 N 个实例 × M 个种子"。
    ///
    /// **Training is cumulative across episodes**: each (instance, seed) pair starts from the policy exported by the previous episode and hands its own
    /// export to the next. Starting every episode from a blank policy would leave the export holding **only what the last episode learned**, while the
    /// report still says "trained N instances × M seeds".
    ///
    /// **训练模式与评估模式必须分开设置**：训练回合用 `LearningMode.Train`，验证/测试用 `Frozen`。
    /// 用 `Train` 跑验证会让验证集**顺便改写策略**，于是"验证"与"测试"两个数字都不再反映训练出的那份策略。
    ///
    /// **The training and evaluation modes must be set apart**: training episodes use `LearningMode.Train`, validation and test use `Frozen`.
    /// Running validation under `Train` lets the validation set **rewrite the policy**, so neither the validation nor the test figure describes the
    /// policy that was trained.
    /// </remarks>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="split">数据划分 / The split.</param>
    /// <param name="seeds">随机种子 / The seeds.</param>
    /// <param name="initial">初始策略档案；可为 <c>null</c> / The initial policy archive, possibly <c>null</c>.</param>
    /// <returns>训练报告或失败 / The training report or a failure.</returns>
    public Result<TrainingReport<O, V>, ErrorCode, Error<ErrorCode>> Train(
        HyperHeuristicConfig config,
        TrainingSplit split,
        IReadOnlyList<long> seeds,
        PolicyArchive? initial = null) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(seeds);
        Result<Success, ErrorCode, Error<ErrorCode>> valid = split.Validate();
        if (valid is Failed<Success, ErrorCode, Error<ErrorCode>> invalidSplit) {
            return Results.Failed<TrainingReport<O, V>>(invalidSplit.Error);
        }
        Result<Success, ErrorCode, Error<ErrorCode>> checkedSeeds = ValidateSeeds(seeds);
        if (checkedSeeds is Failed<Success, ErrorCode, Error<ErrorCode>> invalidSeeds) {
            return Results.Failed<TrainingReport<O, V>>(invalidSeeds.Error);
        }

        PolicyArchive? policy = initial;
        var training = new List<TrainingObservation<O, V>>();
        foreach (TrainingCase instance in split.Training) {
            foreach (long seed in seeds) {
                HyperHeuristicConfig current = WithEpisode(config, instance.Problem, seed, LearningMode.Train);
                Result<HyperHeuristicSession<O, V>, ErrorCode, Error<ErrorCode>> opened =
                    HyperHeuristicSolver<O, V>.Open(
                        current, _registry, policy, "policy:training", "configuration:training");
                if (opened is Failed<HyperHeuristicSession<O, V>, ErrorCode, Error<ErrorCode>>
                    openFailure) {
                    return Results.Failed<TrainingReport<O, V>>(openFailure.Error);
                }
                HyperHeuristicSession<O, V> session =
                    ((Ok<HyperHeuristicSession<O, V>, ErrorCode, Error<ErrorCode>>)opened).Value;
                try {
                    SearchResult<O, V> result = session.Run.Run();
                    if (session.Run.Ledger.Diagnostics.Count > 0) {
                        return Results.Failed<TrainingReport<O, V>>(new Err<ErrorCode>(
                            ErrorCode.ApplicationFailed,
                            string.Join("; ", session.Run.Ledger.Diagnostics)));
                    }
                    Result<PolicyArchive, ErrorCode, Error<ErrorCode>> exported =
                        HyperHeuristicPolicyArchive<O, V>.Export(session.Run, session.Definition);
                    if (exported is Failed<PolicyArchive, ErrorCode, Error<ErrorCode>> exportFailure) {
                        return Results.Failed<TrainingReport<O, V>>(exportFailure.Error);
                    }
                    policy = ((Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>)exported).Value;
                    training.Add(new TrainingObservation<O, V>(instance.Id, seed, result));
                } finally {
                    session.Run.Close();
                }
            }
        }
        if (policy is null) {
            return Results.Failed<TrainingReport<O, V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "no training episode completed"));
        }

        var evaluator = new PolicyEvaluator<O, V>(_registry);
        Result<IReadOnlyList<TrainingObservation<O, V>>, ErrorCode, Error<ErrorCode>> validation =
            evaluator.Evaluate(config, split.Validation, seeds, policy);
        if (validation is Failed<IReadOnlyList<TrainingObservation<O, V>>, ErrorCode, Error<ErrorCode>>
            validationFailure) {
            return Results.Failed<TrainingReport<O, V>>(validationFailure.Error);
        }
        Result<IReadOnlyList<TrainingObservation<O, V>>, ErrorCode, Error<ErrorCode>> test =
            evaluator.Evaluate(config, split.Test, seeds, policy);
        if (test is Failed<IReadOnlyList<TrainingObservation<O, V>>, ErrorCode, Error<ErrorCode>>
            testFailure) {
            return Results.Failed<TrainingReport<O, V>>(testFailure.Error);
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (policy.Metadata is not null) {
            foreach ((string key, string value) in policy.Metadata) {
                metadata[key] = value;
            }
        }
        var ids = new List<string>(split.Training.Count);
        foreach (TrainingCase instance in split.Training) {
            ids.Add(instance.Id);
        }
        var seedText = new List<string>(seeds.Count);
        foreach (long seed in seeds) {
            seedText.Add(seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        metadata["trainingInstances"] = string.Join(",", ids);
        metadata["seeds"] = string.Join(",", seedText);
        return Results.Ok(new TrainingReport<O, V>(
            policy with { Metadata = metadata },
            training,
            ((Ok<IReadOnlyList<TrainingObservation<O, V>>, ErrorCode, Error<ErrorCode>>)validation).Value,
            ((Ok<IReadOnlyList<TrainingObservation<O, V>>, ErrorCode, Error<ErrorCode>>)test).Value));
    }

    /// <summary>校验种子 / Validate the seeds.</summary>
    /// <param name="seeds">种子 / The seeds.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> ValidateSeeds(
        IReadOnlyList<long> seeds) {
        if (seeds.Count == 0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "seeds must be nonempty and unique"));
        }
        var seen = new HashSet<long>();
        foreach (long seed in seeds) {
            if (!seen.Add(seed)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "seeds must be nonempty and unique"));
            }
        }
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 构造某一回合的配置 / Build the configuration for one episode.
    /// </summary>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="problem">问题规格 / The problem spec.</param>
    /// <param name="seed">种子 / The seed.</param>
    /// <param name="mode">学习模式 / The learning mode.</param>
    /// <returns>配置 / The configuration.</returns>
    internal static HyperHeuristicConfig WithEpisode(
        HyperHeuristicConfig config, ComponentSpec problem, long seed, LearningMode mode) =>
        config with {
            Problem = problem,
            Execution = (config.Execution ?? new ExecutionConfig()) with { Seed = seed },
            Learning = (config.Learning ?? new LearningConfig()) with { Mode = mode },
        };
}

/// <summary>
/// 策略评估器 / A policy evaluator.
/// </summary>
/// <remarks>
/// **评估一律用 `LearningMode.Frozen`**。用 `Train` 跑评估会让被评估的数据**顺便改写策略**，
/// 于是"评估结果"描述的是评估过程之后的策略，而不是交给它的那一份。
///
/// **Evaluation always uses `LearningMode.Frozen`.** Running it under `Train` lets the evaluated data **rewrite the policy**, so the "evaluation result"
/// describes the policy after the evaluation rather than the one handed in.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class PolicyEvaluator<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly SearchRegistry<O, V> _registry;

    /// <summary>
    /// 构造评估器 / Construct the evaluator.
    /// </summary>
    /// <param name="registry">搜索注册表 / The search registry.</param>
    public PolicyEvaluator(SearchRegistry<O, V> registry) {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// 评估一份策略 / Evaluate a policy.
    /// </summary>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="cases">实例 / The instances.</param>
    /// <param name="seeds">种子 / The seeds.</param>
    /// <param name="policy">策略档案；可为 <c>null</c> / The policy archive, possibly <c>null</c>.</param>
    /// <returns>观测或失败 / The observations or a failure.</returns>
    public Result<IReadOnlyList<TrainingObservation<O, V>>, ErrorCode, Error<ErrorCode>> Evaluate(
        HyperHeuristicConfig config,
        IReadOnlyList<TrainingCase> cases,
        IReadOnlyList<long> seeds,
        PolicyArchive? policy = null) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(seeds);
        Result<Success, ErrorCode, Error<ErrorCode>> valid = config.Validate();
        if (valid is Failed<Success, ErrorCode, Error<ErrorCode>> invalidConfig) {
            return Results.Failed<IReadOnlyList<TrainingObservation<O, V>>>(invalidConfig.Error);
        }
        // **评估必须有显式的评估额度**：没有额度时运行只受"步数/决策/时间"约束，
        // 而这三者默认都为 `null`——于是**评估会跑到 `NoCandidate` 或无限循环**，得到的数字不可比。
        // **Evaluation needs an explicit evaluation limit**: without it the run is bounded only by steps/decisions/time, all of which default to `null`
        // — so **evaluation runs until `NoCandidate` or forever**, and the numbers are not comparable.
        if (config.Budget.Evaluations is null) {
            return Results.Failed<IReadOnlyList<TrainingObservation<O, V>>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "evaluation requires an evaluation limit and seeds"));
        }
        if (seeds.Count == 0) {
            return Results.Failed<IReadOnlyList<TrainingObservation<O, V>>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "evaluation requires an evaluation limit and seeds"));
        }
        var reports = new List<TrainingObservation<O, V>>(cases.Count * seeds.Count);
        foreach (TrainingCase instance in cases) {
            foreach (long seed in seeds) {
                // **与训练共用同一个 `WithEpisode`**。在这里另写一份 `with` 表达式，会让"评估要冻结策略"这条规则
                // 存在两份实现，而**两处迟早给出不同答案**——其中一处被改成 `Train` 之后，评估会悄悄开始学习。
                // **It shares `WithEpisode` with training.** Writing a second `with` expression here would leave two implementations of "evaluation
                // freezes the policy", and **the two would eventually disagree** — once one is changed to `Train`, evaluation silently starts learning.
                HyperHeuristicConfig current = PolicyTrainer<O, V>.WithEpisode(
                    config, instance.Problem, seed, LearningMode.Frozen);
                Result<HyperHeuristicSession<O, V>, ErrorCode, Error<ErrorCode>> opened =
                    HyperHeuristicSolver<O, V>.Open(
                        current, _registry, policy, "policy:evaluation", "configuration:evaluation");
                SearchResult<O, V> result;
                if (opened is Failed<HyperHeuristicSession<O, V>, ErrorCode, Error<ErrorCode>>
                    openFailure) {
                    // **打开失败也记一条观测，而不是整体失败**。跳过它会让失败率的分母少掉一次，
                    // **于是"评估了 10 个实例、失败 0 次"的报告其实只评估了 7 个**。
                    // **An open failure still records an observation instead of failing the whole evaluation.** Skipping it shrinks the failure-rate
                    // denominator, **so a report of "10 instances, 0 failures" actually evaluated only 7**.
                    result = FailedResult(openFailure.Error.Message);
                } else {
                    HyperHeuristicSession<O, V> session =
                        ((Ok<HyperHeuristicSession<O, V>, ErrorCode, Error<ErrorCode>>)opened).Value;
                    try {
                        result = session.Run.Run();
                        if (session.Run.Ledger.Diagnostics.Count > 0) {
                            result = result with {
                                Termination = SearchTermination.Failed,
                                Diagnostics = new List<string>(session.Run.Ledger.Diagnostics),
                            };
                        }
                    } finally {
                        session.Run.Close();
                    }
                }
                reports.Add(new TrainingObservation<O, V>(instance.Id, seed, result));
            }
        }
        return Results.Ok<IReadOnlyList<TrainingObservation<O, V>>>(reports);
    }

    /// <summary>
    /// 比较多份配置 / Compare several configurations.
    /// </summary>
    /// <param name="configurations">命名配置 / The named configurations.</param>
    /// <param name="cases">实例 / The instances.</param>
    /// <param name="seeds">种子 / The seeds.</param>
    /// <param name="policies">命名策略档案 / The named policy archives.</param>
    /// <param name="quality">质量指标；可为 <c>null</c> / The quality measure, possibly <c>null</c>.</param>
    /// <returns>命名报告或失败 / The named reports or a failure.</returns>
    public Result<IReadOnlyDictionary<string, PolicyEvaluationReport<O, V>>, ErrorCode, Error<ErrorCode>>
        Compare(
            IReadOnlyDictionary<string, HyperHeuristicConfig> configurations,
            IReadOnlyList<TrainingCase> cases,
            IReadOnlyList<long> seeds,
            IReadOnlyDictionary<string, PolicyArchive>? policies = null,
            Func<SearchResult<O, V>, Flt64?>? quality = null) {
        ArgumentNullException.ThrowIfNull(configurations);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(seeds);
        // **比较要求额度一致且显式**。额度不同时"谁更好"取决于额度而不是策略，
        // 而报告里只列出质量与失败率——**读者会以为差异来自策略**。
        // **A comparison requires equal, explicit budgets.** With different budgets "which is better" depends on the budget rather than the policy, and
        // the report only shows quality and failure rate — **readers would take the difference as coming from the policy**.
        long? budget = null;
        foreach ((string name, HyperHeuristicConfig config) in configurations) {
            if (config.Budget.Evaluations is null) {
                return Results.Failed<IReadOnlyDictionary<string, PolicyEvaluationReport<O, V>>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument,
                        "comparisons require equal explicit evaluation budgets and known policy IDs"));
            }
            if (budget is null) {
                budget = config.Budget.Evaluations;
            } else if (budget != config.Budget.Evaluations) {
                return Results.Failed<IReadOnlyDictionary<string, PolicyEvaluationReport<O, V>>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument,
                        "comparisons require equal explicit evaluation budgets and known policy IDs"));
            }
        }
        if (configurations.Count == 0) {
            return Results.Failed<IReadOnlyDictionary<string, PolicyEvaluationReport<O, V>>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "comparisons require equal explicit evaluation budgets and known policy IDs"));
        }
        if (policies is not null) {
            foreach (string name in policies.Keys) {
                if (!configurations.ContainsKey(name)) {
                    return Results.Failed<IReadOnlyDictionary<string, PolicyEvaluationReport<O, V>>>(
                        new Err<ErrorCode>(ErrorCode.IllegalArgument,
                            "comparisons require equal explicit evaluation budgets and known policy IDs"));
                }
            }
        }

        var reports = new Dictionary<string, PolicyEvaluationReport<O, V>>(StringComparer.Ordinal);
        foreach ((string name, HyperHeuristicConfig config) in configurations) {
            PolicyArchive? policy = policies is not null && policies.TryGetValue(name, out PolicyArchive? found)
                ? found
                : null;
            Result<IReadOnlyList<TrainingObservation<O, V>>, ErrorCode, Error<ErrorCode>> evaluated =
                Evaluate(config, cases, seeds, policy);
            if (evaluated is Failed<IReadOnlyList<TrainingObservation<O, V>>, ErrorCode, Error<ErrorCode>>
                evaluateFailure) {
                return Results.Failed<IReadOnlyDictionary<string, PolicyEvaluationReport<O, V>>>(
                    evaluateFailure.Error);
            }
            IReadOnlyList<TrainingObservation<O, V>> observations =
                ((Ok<IReadOnlyList<TrainingObservation<O, V>>, ErrorCode, Error<ErrorCode>>)evaluated).Value;
            var values = new List<Flt64>();
            if (quality is not null) {
                foreach (TrainingObservation<O, V> observation in observations) {
                    Flt64? figure = quality(observation.Result);
                    if (figure is not null) {
                        values.Add(figure.Value);
                    }
                }
            }
            foreach (Flt64 figure in values) {
                if (!double.IsFinite(figure.ToDouble())) {
                    return Results.Failed<IReadOnlyDictionary<string, PolicyEvaluationReport<O, V>>>(
                        new Err<ErrorCode>(ErrorCode.IllegalArgument,
                            "non-finite evaluation quality"));
                }
            }
            long failures = 0L;
            long evaluations = 0L;
            long activeNanos = 0L;
            foreach (TrainingObservation<O, V> observation in observations) {
                if (observation.Result.Termination == SearchTermination.Failed) {
                    failures++;
                }
                evaluations += observation.Result.Evaluations;
                activeNanos += observation.Result.ActiveNanos;
            }
            reports[name] = new PolicyEvaluationReport<O, V>(
                observations, values,
                new Flt64(failures / (double)(observations.Count > 0 ? observations.Count : 1)),
                evaluations, activeNanos);
        }
        return Results.Ok<IReadOnlyDictionary<string, PolicyEvaluationReport<O, V>>>(reports);
    }

    /// <summary>构造一个失败结果 / Build a failed result.</summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>结果 / The result.</returns>
    private static SearchResult<O, V> FailedResult(string message) => new(
        Array.Empty<IIndividual<O, V>>(), false, SearchTermination.Failed, 0L, 0L, 0L, 0L,
        new[] { message });
}

/// <summary>
/// 轨迹文件：一行一条 JSON / Trajectory files: one JSON record per line.
/// </summary>
/// <remarks>
/// **写入走"临时文件 → 原子移动 → 失败则退回普通移动 → 无论如何删除临时文件"**。直接写目标文件会让一次中途失败
/// 留下**一个前缀合法、后半截缺失的文件**——读回来时前半段轨迹是好的，于是看起来"只是少了点数据"。
///
/// **Writing goes "temp file → atomic move → fall back to a plain move → delete the temp file either way".** Writing the target directly makes a
/// mid-way failure leave **a file whose prefix is valid and whose tail is missing** — on read-back the leading trajectories are fine, so it looks like
/// "just a bit of data missing".
/// </remarks>
public static class TrajectoryFiles {
    /// <summary>
    /// 写出轨迹 / Write trajectories.
    /// </summary>
    /// <param name="path">目标路径 / The target path.</param>
    /// <param name="transitions">轨迹 / The transitions.</param>
    /// <returns>写入结果 / The write result.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Write(
        string path, IReadOnlyList<SelectionTransition> transitions) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(transitions);
        try {
            string target = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }
            string temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write,
                           FileShare.None)) {
                    using (var writer = new StreamWriter(stream)) {
                        foreach (SelectionTransition transition in transitions) {
                            writer.Write(JsonSerializer.Serialize(transition));
                            writer.Write('\n');
                        }
                    }
                }
                try {
                    File.Move(temp, target, overwrite: true);
                } catch (IOException) {
                    File.Move(temp, target, overwrite: true);
                }
            } finally {
                if (File.Exists(temp)) {
                    File.Delete(temp);
                }
            }
            return Results.Ok(new Success());
        } catch (Exception exception) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, exception.Message));
        }
    }

    /// <summary>
    /// 读入轨迹 / Read trajectories.
    /// </summary>
    /// <param name="path">路径 / The path.</param>
    /// <returns>轨迹 / The transitions.</returns>
    public static Result<List<SelectionTransition>, ErrorCode, Error<ErrorCode>> Read(string path) {
        ArgumentNullException.ThrowIfNull(path);
        try {
            var transitions = new List<SelectionTransition>();
            foreach (string line in File.ReadLines(path)) {
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }
                SelectionTransition? transition = JsonSerializer.Deserialize<SelectionTransition>(line);
                if (transition is null) {
                    return Results.Failed<List<SelectionTransition>>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "invalid trajectory record"));
                }
                transitions.Add(transition);
            }
            return Results.Ok(transitions);
        } catch (Exception exception) when (exception is IOException or JsonException
            or UnauthorizedAccessException or ArgumentException) {
            return Results.Failed<List<SelectionTransition>>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, exception.Message));
        }
    }
}
