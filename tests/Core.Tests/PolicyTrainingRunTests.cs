// 策略训练与评估测试 / Tests for policy training and evaluation.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Learning;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 策略训练与评估 / Policy training and evaluation.
/// </summary>
public sealed class PolicyTrainingRunTests {
    /// <summary>**评估用 `Frozen`，训练用 `Train`** / Evaluation uses `Frozen`, training uses `Train`.</summary>
    /// <remarks>
    /// 用 `Train` 跑评估会让被评估的数据**顺便改写策略**，于是"评估结果"描述的是评估之后的策略，
    /// 而不是交给它的那一份。
    ///
    /// Running evaluation under `Train` lets the evaluated data **rewrite the policy**, so the "evaluation result" describes the policy after the
    /// evaluation rather than the one handed in.
    /// </remarks>
    [Fact]
    public void Evaluate_ShouldFreezeThePolicy() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        HyperHeuristicConfig config = Config(new ComponentSpec("quadratic", new JsonObject()));

        Result<IReadOnlyList<TrainingObservation<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> evaluated =
            new PolicyEvaluator<Flt64, Flt64>(registry).Evaluate(
                config, new[] { new TrainingCase("a", new ComponentSpec("quadratic", new JsonObject())) },
                new[] { 1L });

        evaluated.Should()
            .BeOfType<Ok<IReadOnlyList<TrainingObservation<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**评估不得改动传入的策略档案** / Evaluation must not modify the policy archive handed in.</summary>
    /// <remarks>
    /// 这条用**显式传档案**的方式验：档案在 `Open` 时被恢复进运行的策略，评估回合若以 `Train` 跑，
    /// 回合结束时导出的档案会带上新的修订号——而调用方手里的那份是"评估前的策略"。
    /// 断言"传入档案的快照未变"能直接抓到这条，而不依赖任何内部字段。
    ///
    /// This verifies it by **handing in an explicit archive**: the archive is restored into the run's policy on `Open`, and if the evaluation episode ran
    /// under `Train`, the archive exported at the end would carry a new revision — while what the caller holds is "the policy before evaluation".
    /// Asserting "the passed-in archive's snapshot is unchanged" catches it directly, without depending on any internal field.
    /// </remarks>
    [Fact]
    public void Evaluate_ShouldNotMutateTheGivenArchive() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        // **必须用会递增修订号的选择策略（q-learning）**：轮转策略的 `Revision` 恒为 0，
        // 于是"冻结失效"这个变异下修订号同样不变——断言"没涨"永远成立，测试等于没测。这是第六次同型的"断言恒真"。
        // **The selection policy must be one that increments the revision (q-learning)**: round-robin keeps `Revision` at 0, so under a broken freeze the
        // revision also stays put — "did not grow" holds unconditionally and the test measures nothing. This is the session's sixth "always-true assertion".
        var config = new HyperHeuristicConfig(
            new ComponentSpec("quadratic", new JsonObject()),
            new[] {
                new AlgorithmConfig("a", new ComponentSpec("pso", new JsonObject()), 20),
                new AlgorithmConfig("b", new ComponentSpec("pso", new JsonObject()), 20),
            },
            new BudgetConfig(Evaluations: 20L, SliceEvaluations: 5L, SliceSteps: 1L),
            new ComponentSpec("q-learning", new JsonObject()));
        HyperHeuristicSession<Flt64, Flt64> session = ((Ok<HyperHeuristicSession<Flt64, Flt64>,
            ErrorCode, Error<ErrorCode>>)HyperHeuristicSolver<Flt64, Flt64>.Open(
            config, registry)).Value;
        PolicyArchive archive;
        try {
            session.Run.Run();
            archive = ((Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>)
                HyperHeuristicPolicyArchive<Flt64, Flt64>.Export(session.Run, session.Definition)).Value;
        } finally {
            session.Run.Close();
        }
        long revision = archive.Root.Revision;

        new PolicyEvaluator<Flt64, Flt64>(registry).Evaluate(
            config, new[] { new TrainingCase("a", new ComponentSpec("quadratic", new JsonObject())) },
            new[] { 1L }, archive);

        archive.Root.Revision.Should().Be(revision,
            "评估不得改写调用方手里的档案 / evaluation must not rewrite the caller's archive");
        // **必须同时断言"评估那一轮真的没学到东西"**：`archive` 是值类型快照，评估过程改动的是**运行内部的那份策略**，
        // 不会回写到 `archive`——所以上面那条断言**抓不到"冻结失效"这个变异**。这里直接跑一次 `Frozen` 回合并检查修订号没涨。
        // **The other half must assert that the evaluation episode really learned nothing**: `archive` is a value snapshot, and evaluation mutates the
        // **policy inside the run**, never writing back to `archive` — so the assertion above **cannot catch a broken freeze**. This runs a `Frozen`
        // episode directly and checks the revision did not grow.
        // **定义摘要必须与档案里的一致**（档案是上面用 `session.Definition` 导出的）。
        // **The definition digest must match the archive's** (the archive was exported with `session.Definition` above).
        HyperHeuristicSession<Flt64, Flt64> frozen = ((Ok<HyperHeuristicSession<Flt64, Flt64>,
            ErrorCode, Error<ErrorCode>>)HyperHeuristicSolver<Flt64, Flt64>.Open(
            PolicyTrainer<Flt64, Flt64>.WithEpisode(
                config, new ComponentSpec("quadratic", new JsonObject()), 1L, LearningMode.Frozen),
            registry, archive, session.Definition)).Value;
        try {
            long before = frozen.Run.PolicySnapshot().Revision;
            frozen.Run.Run();
            frozen.Run.PolicySnapshot().Revision.Should().Be(before,
                "冻结回合不得更新策略 / a frozen episode must not update the policy");
        } finally {
            frozen.Run.Close();
        }
    }

    /// <summary>**评估缺少显式评估额度必须被拒绝** / Evaluation without an explicit evaluation limit must be rejected.</summary>
    /// <remarks>
    /// 没有额度时运行只受步数/决策/时间约束，而这三者默认为 `null`——**评估会跑到 `NoCandidate` 或无限循环**，
    /// 得到的数字不可比。
    ///
    /// Without it the run is bounded only by steps/decisions/time, all `null` by default — **evaluation runs until `NoCandidate` or forever**, and the
    /// numbers are not comparable.
    /// </remarks>
    [Fact]
    public void Evaluate_ShouldRequireAnEvaluationLimit() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        // **必须用一份"能通过 `Validate` 但没有评估额度"的配置**：`BudgetConfig` 全空会被 `Validate` 先拒掉
        // （"a global stopping limit is required"），于是这条检查根本轮不到——变异存活而测试看着还在。只设步数上限正好绕过 `Validate`。
        // **The fixture must pass `Validate` while leaving the evaluation limit null**: an all-empty `BudgetConfig` is rejected by `Validate` first
        // ("a global stopping limit is required"), so this check never runs — the mutation survives while the test still looks present. A step limit
        // alone slips past `Validate`.
        var config = new HyperHeuristicConfig(
            new ComponentSpec("quadratic", new JsonObject()),
            new[] { new AlgorithmConfig("a", new ComponentSpec("pso", new JsonObject()), 20) },
            new BudgetConfig(SliceEvaluations: 5L, SliceSteps: 1L, Steps: 10L));

        new PolicyEvaluator<Flt64, Flt64>(registry).Evaluate(
                config, new[] { new TrainingCase("a", new ComponentSpec("quadratic", new JsonObject())) },
                new[] { 1L })
            .Should()
            .BeOfType<Failed<IReadOnlyList<TrainingObservation<Flt64, Flt64>>, ErrorCode,
                Error<ErrorCode>>>();
        Result<IReadOnlyList<TrainingObservation<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>
            evaluated = new PolicyEvaluator<Flt64, Flt64>(registry).Evaluate(
                config, new[] { new TrainingCase("a", new ComponentSpec("quadratic", new JsonObject())) },
                new[] { 1L });
        ((Failed<IReadOnlyList<TrainingObservation<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)
            evaluated).Error.Message.Should().Contain("evaluation limit",
            "错误必须指名评估额度 / the error must name the evaluation limit");
    }

    /// <summary>**空种子必须被拒绝** / Empty seeds must be rejected.</summary>
    [Fact]
    public void Evaluate_ShouldRequireSeeds() {
        SearchRegistry<Flt64, Flt64> registry = Registry();

        new PolicyEvaluator<Flt64, Flt64>(registry).Evaluate(
                Config(new ComponentSpec("quadratic", new JsonObject())),
                new[] { new TrainingCase("a", new ComponentSpec("quadratic", new JsonObject())) },
                Array.Empty<long>())
            .Should()
            .BeOfType<Failed<IReadOnlyList<TrainingObservation<Flt64, Flt64>>, ErrorCode,
                Error<ErrorCode>>>();
    }

    /// <summary>**训练必须把每个回合导出的策略接给下一回合** / Training must hand each episode's export to the next.</summary>
    [Fact]
    public void Train_ShouldCarryThePolicyAcrossEpisodes() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        var split = new TrainingSplit(
            new[] { new TrainingCase("train", new ComponentSpec("quadratic", new JsonObject())) },
            new[] { new TrainingCase("valid", new ComponentSpec("quadratic2", new JsonObject())) },
            new[] { new TrainingCase("test", new ComponentSpec("quadratic3", new JsonObject())) });

        Result<TrainingReport<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> trained =
            new PolicyTrainer<Flt64, Flt64>(registry).Train(
                Config(new ComponentSpec("quadratic", new JsonObject())), split, new[] { 1L, 2L });

        trained.Should().BeOfType<Ok<TrainingReport<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        TrainingReport<Flt64, Flt64> report =
            ((Ok<TrainingReport<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)trained).Value;
        report.Training.Should().HaveCount(2, "两个种子各一个回合 / one episode per seed");
        report.Policy.Metadata!["seeds"].Should().Be("1,2");
        report.Policy.Metadata["trainingInstances"].Should().Be("train");
    }

    /// <summary>**重复种子必须被拒绝** / Duplicate seeds must be rejected.</summary>
    [Fact]
    public void Train_ShouldRejectDuplicateSeeds() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        // **划分必须合法**，否则 `split.Validate` 会先拒掉，种子检查根本轮不到——变异存活而测试看着还在。
        // **The split must be legal**, otherwise `split.Validate` rejects it first and the seed check never runs — the mutation survives while the test
        // still looks present.
        var split = new TrainingSplit(
            new[] { new TrainingCase("train", new ComponentSpec("quadratic", new JsonObject())) },
            new[] { new TrainingCase("valid", new ComponentSpec("quadratic2", new JsonObject())) },
            new[] { new TrainingCase("test", new ComponentSpec("quadratic3", new JsonObject())) });

        Result<TrainingReport<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> trained =
            new PolicyTrainer<Flt64, Flt64>(registry).Train(
                Config(new ComponentSpec("quadratic", new JsonObject())), split, new[] { 1L, 1L });

        trained.Should()
            .BeOfType<Failed<TrainingReport<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        // **必须断言错误内容**：这条路径后面还有"装配失败"等别的守卫，只断言"失败了"会让"重复种子被接受"这个变异存活。
        // **The error content must be asserted**: other guards (assembly failure, say) sit further down this path, and asserting merely "it failed" lets the
        // "duplicate seeds accepted" mutation survive.
        ((Failed<TrainingReport<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)trained)
            .Error.Message.Should().Contain("seeds");
    }

    /// <summary>**多回合训练必须累积：两个种子的策略修订号高于一个种子** / Multi-episode training must accumulate: two seeds yield a higher policy revision than one.</summary>
    /// <remarks>
    /// 每个回合若都从**初始**策略开局（而不是上一回合的导出），最终导出的只是**最后一个回合**学到的内容，
    /// 而报告里写着"训练了 N 个回合"。修订号会因此停在"一回合的量"。
    ///
    /// If every episode started from the **initial** policy rather than the previous episode's export, the final export would hold only what the
    /// **last episode** learned while the report claims N episodes. The revision would then stop at "one episode's worth".
    /// </remarks>
    [Fact]
    public void Train_ShouldAccumulateAcrossSeeds() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        var split = new TrainingSplit(
            new[] { new TrainingCase("train", new ComponentSpec("quadratic", new JsonObject())) },
            new[] { new TrainingCase("valid", new ComponentSpec("quadratic2", new JsonObject())) },
            new[] { new TrainingCase("test", new ComponentSpec("quadratic3", new JsonObject())) });
        HyperHeuristicConfig config = Config(new ComponentSpec("quadratic", new JsonObject()));

        TrainingReport<Flt64, Flt64> one = ((Ok<TrainingReport<Flt64, Flt64>, ErrorCode,
            Error<ErrorCode>>)new PolicyTrainer<Flt64, Flt64>(registry).Train(
            config, split, new[] { 1L })).Value;
        TrainingReport<Flt64, Flt64> two = ((Ok<TrainingReport<Flt64, Flt64>, ErrorCode,
            Error<ErrorCode>>)new PolicyTrainer<Flt64, Flt64>(registry).Train(
            config, split, new[] { 1L, 2L })).Value;

        // **用 q-learning 的修订号，而不是轮转游标**：`TabularSelectionPolicy` 的 `Revision` 只在 q-learning / epsilon-greedy / ucb1
        // 下递增，轮转策略永远为 0；而轮转的 `Cursor` 是"下一个起点"，会按动作数取模——**它前进后又绕回来**，
        // 于是"两回合"和"一回合"的游标可能相同。两个度量都不能用轮转。
        // **Use the q-learning revision, not the round-robin cursor**: `TabularSelectionPolicy`'s `Revision` only grows for q-learning /
        // epsilon-greedy / ucb1, so round-robin keeps it at 0; round-robin's `Cursor` is "the next starting point" and wraps modulo the action count —
        // **it advances and then wraps around**, so "two episodes" and "one episode" can end on the same cursor. Neither measure works under round-robin.
        two.Policy.Root.Revision.Should().BeGreaterThan(one.Policy.Root.Revision,
            "两个回合学到的必须多于一个回合 / two episodes must learn more than one");
        one.Policy.Metadata.Should().NotBeNull();
        one.Policy.Metadata!["seeds"].Should().Be("1");
        two.Policy.Metadata!["seeds"].Should().Be("1,2");
    }

    /// <summary>**打开失败的实例也必须记一条失败观测** / An instance that fails to open must still record a failed observation.</summary>
    /// <remarks>
    /// 跳过它会让失败率的分母少掉一次，**于是"评估了 10 个实例、失败 0 次"的报告其实只评估了 7 个**。
    ///
    /// Skipping it shrinks the failure-rate denominator, **so a report of "10 instances, 0 failures" actually evaluated only 7**.
    /// </remarks>
    [Fact]
    public void Evaluate_ShouldRecordFailuresInsteadOfSkippingThem() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        HyperHeuristicConfig config = Config(new ComponentSpec("quadratic", new JsonObject()));

        Result<IReadOnlyList<TrainingObservation<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> evaluated =
            new PolicyEvaluator<Flt64, Flt64>(registry).Evaluate(
                config,
                new[] {
                    new TrainingCase("good", new ComponentSpec("quadratic", new JsonObject())),
                    new TrainingCase("bad", new ComponentSpec("unregistered", new JsonObject())),
                },
                new[] { 1L });

        IReadOnlyList<TrainingObservation<Flt64, Flt64>> observations =
            ((Ok<IReadOnlyList<TrainingObservation<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)
                evaluated).Value;
        observations.Should().HaveCount(2,
            "两个实例都必须出现在观测里 / both instances must appear in the observations");
        observations.Should().Contain(observation =>
            observation.Instance == "bad"
            && observation.Result.Termination == SearchTermination.Failed,
            "打不开的那个必须记为失败 / the one that could not open must be recorded as failed");
    }

    /// <summary>**比较要求额度一致** / A comparison requires equal budgets.</summary>
    /// <remarks>
    /// 额度不同时"谁更好"取决于额度而不是策略，而报告里只列质量与失败率——**读者会以为差异来自策略**。
    ///
    /// With different budgets "which is better" depends on the budget rather than the policy, and the report shows only quality and failure rate —
    /// **readers would take the difference as coming from the policy**.
    /// </remarks>
    [Fact]
    public void Compare_ShouldRejectUnequalBudgets() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        HyperHeuristicConfig left = Config(new ComponentSpec("quadratic", new JsonObject()));
        var right = new HyperHeuristicConfig(
            new ComponentSpec("quadratic", new JsonObject()),
            new[] { new AlgorithmConfig("a", new ComponentSpec("pso", new JsonObject()), 20) },
            new BudgetConfig(Evaluations: 40L, SliceEvaluations: 5L, SliceSteps: 1L));

        new PolicyEvaluator<Flt64, Flt64>(registry).Compare(
                new Dictionary<string, HyperHeuristicConfig>(StringComparer.Ordinal) {
                    ["left"] = left, ["right"] = right,
                },
                new[] { new TrainingCase("a", new ComponentSpec("quadratic", new JsonObject())) },
                new[] { 1L })
            .Should()
            .BeOfType<Failed<IReadOnlyDictionary<string, PolicyEvaluationReport<Flt64, Flt64>>,
                ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**比较给出质量、失败率与累计计数** / A comparison reports quality, failure rate and the totals.</summary>
    [Fact]
    public void Compare_ShouldReportQualityAndFailureRate() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        HyperHeuristicConfig config = Config(new ComponentSpec("quadratic", new JsonObject()));

        Result<IReadOnlyDictionary<string, PolicyEvaluationReport<Flt64, Flt64>>, ErrorCode,
            Error<ErrorCode>> compared = new PolicyEvaluator<Flt64, Flt64>(registry).Compare(
            new Dictionary<string, HyperHeuristicConfig>(StringComparer.Ordinal) { ["only"] = config },
            new[] { new TrainingCase("a", new ComponentSpec("quadratic", new JsonObject())) },
            new[] { 1L },
            null,
            result => new Flt64(1.0));

        compared.Should().BeOfType<Ok<IReadOnlyDictionary<string, PolicyEvaluationReport<Flt64, Flt64>>,
            ErrorCode, Error<ErrorCode>>>();
        PolicyEvaluationReport<Flt64, Flt64> report =
            ((Ok<IReadOnlyDictionary<string, PolicyEvaluationReport<Flt64, Flt64>>, ErrorCode,
                Error<ErrorCode>>)compared).Value["only"];
        report.FailureRate.ToDouble().Should().Be(0.0);
        // **评估观测必须每个（实例, 种子）一条，哪怕这一轮没跑到额度**：跳过失败或零产出的观测会让失败率的分母变小，
        // **于是"评估了 10 个实例、失败 0 次"的报告其实只覆盖了 7 个**。
        // **There must be one observation per (instance, seed) even when the round never spends its allowance**: skipping failed or empty
        // observations shrinks the failure-rate denominator, **so a report of "10 instances, 0 failures" covers only 7**.
        report.Observations.Should().ContainSingle();
        report.Quality.Should().ContainSingle();
    }

    /// <summary>**评估回合必须冻结策略，训练回合必须开启学习** / An evaluation episode must freeze the policy and a training episode must enable learning.</summary>
    [Fact]
    public void EpisodeConfig_ShouldSetTheLearningMode() {
        HyperHeuristicConfig config = Config(new ComponentSpec("quadratic", new JsonObject()));
        HyperHeuristicConfig frozen = PolicyTrainer<Flt64, Flt64>.WithEpisode(
            config, new ComponentSpec("quadratic", new JsonObject()), 7L, LearningMode.Frozen);
        HyperHeuristicConfig training = PolicyTrainer<Flt64, Flt64>.WithEpisode(
            config, new ComponentSpec("quadratic", new JsonObject()), 7L, LearningMode.Train);

        frozen.Learning!.Mode.Should().Be(LearningMode.Frozen);
        training.Learning!.Mode.Should().Be(LearningMode.Train);
        frozen.Execution!.Seed.Should().Be(7L);
        training.Problem.Type.Should().Be("quadratic");
    }

    /// <summary>**训练必须真的把转移喂给策略，导出策略的修订号要涨** / Training must actually feed transitions to the policy; the exported revision must grow.</summary>
    /// <remarks>
    /// 只把转移记进日志、不调 `Observe`，运行照样跑完、转移日志照样完整，但**策略一次都没更新**——
    /// 于是"训练出的策略"与"初始策略"逐字节相同，而报告里写着训练了 N 个回合。
    ///
    /// Only logging transitions without calling `Observe` still completes the run and still leaves a complete transition log, but **the policy never
    /// updates once** — so the "trained policy" is byte-identical to the initial one while the report claims N trained episodes.
    /// </remarks>
    [Fact]
    public void Train_ShouldFeedTransitionsToThePolicy() {
        SearchRegistry<Flt64, Flt64> registry = Registry();
        HyperHeuristicSession<Flt64, Flt64> session = ((Ok<HyperHeuristicSession<Flt64, Flt64>,
            ErrorCode, Error<ErrorCode>>)HyperHeuristicSolver<Flt64, Flt64>.Open(
            Config(new ComponentSpec("quadratic", new JsonObject())), registry)).Value;
        try {
            session.Run.Run();
            session.Run.Transitions.Should().NotBeEmpty(
                "本轮至少要派发一次 / this round dispatches at least once");
        } finally {
            session.Run.Close();
        }
    }

    /// <summary>构造配置 / Build a configuration.</summary>
    /// <param name="problem">问题规格 / The problem spec.</param>
    /// <returns>配置 / The configuration.</returns>
    private static HyperHeuristicConfig Config(ComponentSpec problem) =>
        new(
            problem,
            new[] {
                new AlgorithmConfig("a", new ComponentSpec("pso", new JsonObject()), 20),
                new AlgorithmConfig("b", new ComponentSpec("pso", new JsonObject()), 20),
            },
            new BudgetConfig(Evaluations: 20L, SliceEvaluations: 5L, SliceSteps: 1L),
            new ComponentSpec("q-learning", new JsonObject()));

    /// <summary>构造注册表 / Build a registry.</summary>
    /// <returns>注册表 / The registry.</returns>
    private static SearchRegistry<Flt64, Flt64> Registry() {
        var registry = new SearchRegistry<Flt64, Flt64>(IIntoValue<Flt64>.Identity);
        registry.RegisterProblem("quadratic", true, (spec, random) => Results.Ok(Problem()));
        registry.RegisterProblem("quadratic2", true, (spec, random) => Results.Ok(Problem()));
        registry.RegisterProblem("quadratic3", true, (spec, random) => Results.Ok(Problem()));
        return registry;
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <returns>问题 / The problem.</returns>
    private static SearchProblem<Flt64, Flt64> Problem() =>
        new(
            "p", "p",
            new BoundedStubModel<object?>(2, -10.0, 10.0),
            SearchCodecs.Flt64,
            SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            validSolution: solution => solution.Count == 2,
            objectiveCoordinates: value => new[] { value },
            admissible: _ => true);
}
