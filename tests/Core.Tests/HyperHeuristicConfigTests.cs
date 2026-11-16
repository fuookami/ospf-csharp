// 超启发式配置测试 / Tests for the hyper-heuristic configuration.

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 超启发式配置 / The hyper-heuristic configuration.
/// </summary>
public sealed class HyperHeuristicConfigTests {
    /// <summary>**合法配置通过校验** / A legal configuration validates.</summary>
    [Fact]
    public void Validate_ShouldAcceptLegalConfiguration() =>
        Build().Validate().Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**算法标识必须整体匹配，不能只匹配前缀片段** / Algorithm identities must match whole, not as a fragment.</summary>
    /// <remarks>
    /// 正则里没有 `^`/`$` 时 `IsMatch` 只要求"含有一个合法片段"，于是 `"a b"` 会被判定为合法。这是**只有整体匹配检查才能
    /// 拦下**的一类输入。
    /// Without `^`/`$` in the regex, `IsMatch` only requires "contains one legal fragment", so `"a b"` would pass. This is exactly the
    /// class of input **only a whole-match check stops**.
    /// </remarks>
    [Fact]
    public void Validate_ShouldRejectIdentityWithInnerBlank() {
        HyperHeuristicConfig config = Build(algorithmId: "a b");

        config.Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**标识里的前导空白被拒绝** / A leading blank in the identity is rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectIdentityWithLeadingBlank() {
        HyperHeuristicConfig config = Build(algorithmId: " a");

        config.Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**重复标识被拒绝** / Duplicate identities are rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectDuplicateIdentities() {
        HyperHeuristicConfig config = BuildWithAlgorithms(
            Algorithm("same"), Algorithm("same"));

        config.Validate().Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**种群规模必须为正** / The population size must be positive.</summary>
    [Fact]
    public void Validate_ShouldRejectNonPositiveSize() {
        Build(algorithmSize: 0).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        Build(algorithmSize: -1).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**至少给出一个全局停止条件** / At least one global stopping condition is required.</summary>
    /// <remarks>
    /// 全都留空会让搜索没有任何终止依据，而配置本身"看起来完整"——运行会一直跑到调用方放弃，**而不是报配置错误**。
    /// Leaving them all empty gives the search no termination basis while the configuration "looks complete" — the run simply keeps going
    /// until the caller gives up, **instead of reporting a configuration error**.
    /// </remarks>
    [Fact]
    public void Validate_ShouldRequireStoppingLimit() {
        var budget = new BudgetConfig();

        BuildWithBudget(budget).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**四种停止条件各单独成立即可** / Any single stopping condition suffices.</summary>
    [Fact]
    public void Validate_ShouldAcceptEachSingleLimit() {
        BuildWithBudget(new BudgetConfig(Evaluations: 100L)).Validate()
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        BuildWithBudget(new BudgetConfig(Decisions: 10L)).Validate()
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        BuildWithBudget(new BudgetConfig(TimeMillis: 5L)).Validate()
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        BuildWithBudget(new BudgetConfig(Steps: 5L)).Validate()
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**负的上限被拒绝** / A negative limit is rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectNegativeLimit() {
        BuildWithBudget(new BudgetConfig(Evaluations: -1L)).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**片段额度必须为正** / Slice allowances must be positive.</summary>
    [Fact]
    public void Validate_ShouldRejectNonPositiveSliceAllowance() {
        BuildWithBudget(new BudgetConfig(Evaluations: 10L, SliceEvaluations: 0L)).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        BuildWithBudget(new BudgetConfig(Evaluations: 10L, SliceSteps: 0L)).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**自动检查点要求持久化已启用且给出路径** / Automatic checkpoints require persistence and a destination.</summary>
    /// <remarks>
    /// 只检查间隔会让一次自动检查点在运行时才失败，**而失败点距离配置错误已经很远**。
    /// Checking only the interval defers the failure to run time, **far from the configuration mistake**.
    /// </remarks>
    [Fact]
    public void Validate_ShouldRequirePersistenceForCheckpoints() {
        BuildWithPersistence(new PersistenceConfig(false, "/tmp/x", 5L)).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        BuildWithPersistence(new PersistenceConfig(true, null, 5L)).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        BuildWithPersistence(new PersistenceConfig(true, "/tmp/x", 5L)).Validate()
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**负的检查点间隔被拒绝** / A negative checkpoint interval is rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectNegativeCheckpointInterval() {
        BuildWithPersistence(new PersistenceConfig(true, "/tmp/x", -1L)).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**非正的并行度与容量被拒绝** / Non-positive parallelism and capacities are rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectNonPositiveCapacities() {
        BuildWithExecution(new ExecutionConfig(Parallelism: 0)).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        BuildWithMemory(new MemoryConfig(ArchiveCapacity: 0)).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        BuildWithMemory(new MemoryConfig(WorkingCapacity: 0)).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**不支持的版本被拒绝** / An unsupported version is rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectUnsupportedVersion() {
        (Build() with { Version = 2 }).Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**空算法列表被拒绝** / An empty algorithm list is rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectEmptyAlgorithms() {
        BuildWithAlgorithms().Validate()
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**构建器产出可校验的配置** / The builder produces a validatable configuration.</summary>
    [Fact]
    public void Builder_ShouldProduceValidConfiguration() {
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> created = HyperHeuristics.Create(
            builder => builder
                .Problem("model")
                .Algorithm(Algorithm("genetic"))
                .WithBudget(new BudgetConfig(Evaluations: 100L)));

        created.Should().BeOfType<Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
        ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)created).Value.Validate()
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**构建时缺少 problem 直接失败** / A missing problem fails at build time.</summary>
    /// <remarks>
    /// 塞一个默认 problem 会让一份"忘了声明问题"的配置被当成"用模型问题"的配置，**而运行时会拿一个空模型去求解**。
    /// Substituting a default problem turns "forgot to declare the problem" into "uses the model problem", **and the run then solves
    /// against an empty model**.
    /// </remarks>
    [Fact]
    public void Builder_ShouldRequireProblem() {
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> created = HyperHeuristics.Create(
            builder => builder
                .Algorithm("genetic", "genetic")
                .WithBudget(new BudgetConfig(Evaluations: 100L)));

        created.Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**构建时即校验配置** / The configuration is validated at build time.</summary>
    /// <remarks>
    /// 把校验推迟到调用点会让一份非法配置在构造阶段"成功"、在运行时才失败。
    /// Deferring validation to the call site lets an illegal configuration "succeed" at construction and only fail at run time.
    /// </remarks>
    [Fact]
    public void Builder_ShouldValidateAtBuildTime() {
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> created = HyperHeuristics.Create(
            builder => builder
                .Problem("model")
                .Algorithm("genetic", "genetic")
                .WithBudget(new BudgetConfig()));

        created.Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**算法构建器产出完整配置** / The algorithm builder produces a complete configuration.</summary>
    [Fact]
    public void AlgorithmBuilder_ShouldPopulateFields() {
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> created = HyperHeuristics.Create(
            builder => builder
                .Problem("model")
                .Algorithm("genetic", "genetic", algorithm => {
                    algorithm.Size = 30;
                    algorithm.Selection = new ComponentSpec("frr-mab");
                    algorithm.Updates = new[] { new ComponentSpec("de") };
                    algorithm.Mutations = new[] { new ComponentSpec("levy") };
                    algorithm.Scalarization = "weighted-sum";
                })
                .WithBudget(new BudgetConfig(Evaluations: 100L)));

        AlgorithmConfig algorithm =
            ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)created).Value.Algorithms[0];
        algorithm.Id.Should().Be("genetic");
        algorithm.Size.Should().Be(30);
        algorithm.Selection!.Type.Should().Be("frr-mab");
        algorithm.Updates![0].Type.Should().Be("de");
        algorithm.Mutations![0].Type.Should().Be("levy");
        algorithm.Scalarization.Should().Be("weighted-sum");
    }

    /// <summary>**算法构建器可以嵌套子算法** / The algorithm builder nests child algorithms.</summary>
    [Fact]
    public void AlgorithmBuilder_ShouldNestChildren() {
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> created = HyperHeuristics.Create(
            builder => builder
                .Problem("model")
                .Algorithm("parent", "genetic", algorithm =>
                    algorithm.Algorithm("child", "genetic"))
                .WithBudget(new BudgetConfig(Evaluations: 100L)));

        AlgorithmConfig parent =
            ((Ok<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>)created).Value.Algorithms[0];
        parent.Children!.Count.Should().Be(1);
        parent.Children![0].Id.Should().Be("child");
    }

    /// <summary>**构建器回调抛异常时返回失败** / A throwing builder callback returns a failure.</summary>
    [Fact]
    public void Builder_ShouldConvertThrownException() {
        Result<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>> created = HyperHeuristics.Create(
            _ => throw new InvalidOperationException("boom"));

        created.Should().BeOfType<Failed<HyperHeuristicConfig, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**子算法不递归校验** / Child algorithms are not validated recursively.</summary>
    /// <remarks>
    /// `validate` 只检查顶层；递归校验会让一份"顶层合法、子层有问题"的配置在父层报错，
    /// **而错误信息指向的是父层标识**。这是的**已知行为**。
    /// The code's `validate` checks only the top level; recursive validation would report a "top-level legal, child broken"
    /// configuration against the parent, **with the error message pointing at the parent identity**. This is **known behaviour** matching
    /// the code.
    /// </remarks>
    [Fact]
    public void Validate_ShouldNotValidateChildren() {
        var child = new AlgorithmConfig("bad id", new ComponentSpec("genetic"), Size: 0);
        var parent = new AlgorithmConfig("parent", new ComponentSpec("genetic"), Children: new[] { child });

        BuildWithAlgorithms(parent).Validate()
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造一个算法配置 / Build an algorithm configuration.</summary>
    /// <param name="id">标识 / The identity.</param>
    /// <param name="size">规模 / The size.</param>
    /// <returns>算法配置 / The algorithm configuration.</returns>
    private static AlgorithmConfig Algorithm(string id, int size = 20) =>
        new(id, new ComponentSpec("genetic"), size);

    /// <summary>构造配置 / Build a configuration.</summary>
    /// <param name="algorithmId">算法标识 / The algorithm identity.</param>
    /// <param name="algorithmSize">算法规模 / The algorithm size.</param>
    /// <returns>配置 / The configuration.</returns>
    private static HyperHeuristicConfig Build(string algorithmId = "genetic", int algorithmSize = 20) =>
        BuildWithAlgorithms(Algorithm(algorithmId, algorithmSize));

    /// <summary>用给定算法构造配置 / Build a configuration with the given algorithms.</summary>
    /// <param name="algorithms">算法 / The algorithms.</param>
    /// <returns>配置 / The configuration.</returns>
    private static HyperHeuristicConfig BuildWithAlgorithms(params AlgorithmConfig[] algorithms) =>
        new(new ComponentSpec("model"), algorithms, new BudgetConfig(Evaluations: 100L));

    /// <summary>用给定预算构造配置 / Build a configuration with the given budget.</summary>
    /// <param name="budget">预算 / The budget.</param>
    /// <returns>配置 / The configuration.</returns>
    private static HyperHeuristicConfig BuildWithBudget(BudgetConfig budget) =>
        new(new ComponentSpec("model"), new[] { Algorithm("genetic") }, budget);

    /// <summary>用给定持久化配置构造配置 / Build a configuration with the given persistence.</summary>
    /// <param name="persistence">持久化配置 / The persistence configuration.</param>
    /// <returns>配置 / The configuration.</returns>
    private static HyperHeuristicConfig BuildWithPersistence(PersistenceConfig persistence) =>
        new(new ComponentSpec("model"), new[] { Algorithm("genetic") },
            new BudgetConfig(Evaluations: 100L), Persistence: persistence);

    /// <summary>用给定执行配置构造配置 / Build a configuration with the given execution.</summary>
    /// <param name="execution">执行配置 / The execution configuration.</param>
    /// <returns>配置 / The configuration.</returns>
    private static HyperHeuristicConfig BuildWithExecution(ExecutionConfig execution) =>
        new(new ComponentSpec("model"), new[] { Algorithm("genetic") },
            new BudgetConfig(Evaluations: 100L), Execution: execution);

    /// <summary>用给定记忆配置构造配置 / Build a configuration with the given memory.</summary>
    /// <param name="memory">记忆配置 / The memory configuration.</param>
    /// <returns>配置 / The configuration.</returns>
    private static HyperHeuristicConfig BuildWithMemory(MemoryConfig memory) =>
        new(new ComponentSpec("model"), new[] { Algorithm("genetic") },
            new BudgetConfig(Evaluations: 100L), Memory: memory);
}
