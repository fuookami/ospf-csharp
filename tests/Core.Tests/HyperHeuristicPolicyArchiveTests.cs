// 策略档案导出与恢复测试 / Tests for policy archive export and restore.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 策略档案的导出与恢复 / Policy archive export and restore.
/// </summary>
public sealed class HyperHeuristicPolicyArchiveTests {
    /// <summary>**导出再恢复，选择策略的状态回来** / Export then restore brings back the selection policy's state.</summary>
    [Fact]
    public void ExportAndRestore_ShouldRoundTripTheRootPolicy() {
        HyperHeuristicRun<Flt64, Flt64> source = Run();
        source.Run();
        Result<PolicyArchive, ErrorCode, Error<ErrorCode>> exported =
            HyperHeuristicPolicyArchive<Flt64, Flt64>.Export(source, "def");
        exported.Should().BeOfType<Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>>();

        HyperHeuristicRun<Flt64, Flt64> target = Run();
        HyperHeuristicPolicyArchive<Flt64, Flt64>.Restore(
                target, ((Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>)exported).Value, "def")
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        target.PolicySnapshot().Revision.Should().Be(source.PolicySnapshot().Revision);
        target.PolicySnapshot().Cursor.Should().Be(source.PolicySnapshot().Cursor);
    }

    /// <summary>**特征版本不同必须被拒绝** / A differing feature version must be rejected.</summary>
    /// <remarks>
    /// 只比 `definition` 会让"特征从 3 维扩到 5 维"被当成兼容：表宽对不上，而恢复**不会报错**，
    /// 它只把前 3 维填进去、后 2 维留零，**于是策略从此按"永远看到零"的状态做决策**。
    ///
    /// Comparing `definition` alone treats "features grew from 3 to 5 dimensions" as compatible: the table width does not match, restore **does not fail**,
    /// it fills the first three and leaves two at zero, **so the policy decides forever on "always zero"**.
    /// </remarks>
    [Fact]
    public void Restore_ShouldRejectFeatureVersionMismatch() {
        HyperHeuristicRun<Flt64, Flt64> source = Run();
        PolicyArchive exported = ((Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>)
            HyperHeuristicPolicyArchive<Flt64, Flt64>.Export(source, "def")).Value;

        HyperHeuristicPolicyArchive<Flt64, Flt64>.Restore(
                Run(), exported with { FeatureVersion = "progress:2" }, "def")
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**定义摘要不同必须被拒绝** / A differing definition digest must be rejected.</summary>
    [Fact]
    public void Restore_ShouldRejectDefinitionMismatch() {
        HyperHeuristicRun<Flt64, Flt64> source = Run();
        PolicyArchive exported = ((Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>)
            HyperHeuristicPolicyArchive<Flt64, Flt64>.Export(source, "def")).Value;

        HyperHeuristicPolicyArchive<Flt64, Flt64>.Restore(
                Run(), exported with { Definition = "other" }, "def")
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**算子集合不一致必须被拒绝** / A differing operator set must be rejected.</summary>
    [Fact]
    public void Restore_ShouldRejectOperatorSetMismatch() {
        HyperHeuristicRun<Flt64, Flt64> source = Run();
        PolicyArchive exported = ((Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>)
            HyperHeuristicPolicyArchive<Flt64, Flt64>.Export(source, "def")).Value;

        HyperHeuristicPolicyArchive<Flt64, Flt64>.Restore(
                Run(),
                exported with {
                    Operators = new Dictionary<string, IReadOnlyDictionary<string, PolicySnapshot>>(
                        StringComparer.Ordinal),
                },
                "def")
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**档案里多出一个算子也必须被拒绝** / A policy archive carrying an extra operator must also be rejected.</summary>
    /// <remarks>
    /// **这条测试是补的**：原来的"算子集合不一致"用例是"档案里一个都没有"，那被逐 id 的存在性检查拦下。
    /// 把"数量也要相等"那一行删掉，测试照样通过——**变异存活暴露了另一个方向没被覆盖**。
    ///
    /// **This test was added afterwards**: the original "operator set mismatch" case was "the archive has none", which the per-id existence check
    /// catches. Deleting the "counts must also match" line kept the test green — **the surviving mutation exposed the other direction.**
    /// </remarks>
    [Fact]
    public void Restore_ShouldRejectExtraOperators() {
        HyperHeuristicRun<Flt64, Flt64> source = Run();
        PolicyArchive exported = ((Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>)
            HyperHeuristicPolicyArchive<Flt64, Flt64>.Export(source, "def")).Value;
        var operators = new Dictionary<string, IReadOnlyDictionary<string, PolicySnapshot>>(
            exported.Operators, StringComparer.Ordinal);
        operators["b"] = new Dictionary<string, PolicySnapshot>(StringComparer.Ordinal);

        HyperHeuristicPolicyArchive<Flt64, Flt64>.Restore(
                Run(), exported with { Operators = operators }, "def")
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**不支持的版本必须被拒绝** / An unsupported version must be rejected.</summary>
    [Fact]
    public void Restore_ShouldRejectUnsupportedVersion() {
        HyperHeuristicRun<Flt64, Flt64> source = Run();
        PolicyArchive exported = ((Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>)
            HyperHeuristicPolicyArchive<Flt64, Flt64>.Export(source, "def")).Value;

        HyperHeuristicPolicyArchive<Flt64, Flt64>.Restore(Run(), exported with { Version = 2 }, "def")
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造一次运行 / Build a run.</summary>
    /// <returns>运行 / The run.</returns>
    private static HyperHeuristicRun<Flt64, Flt64> Run() {
        SearchProblem<Flt64, Flt64> problem = Problem();
        var workers = new List<HyperHeuristicWorker<Flt64, Flt64>> {
            new("a", problem, new SearchRandom(1L), new StubSession(), InitializationMode.Model),
        };
        var config = new HyperHeuristicConfig(
            new ComponentSpec("quadratic", new System.Text.Json.Nodes.JsonObject()),
            new[] { new AlgorithmConfig("a", new ComponentSpec("pso", new System.Text.Json.Nodes.JsonObject()), 20) },
            new BudgetConfig(Evaluations: 10L, SliceEvaluations: 5L, SliceSteps: 1L));
        return new HyperHeuristicRun<Flt64, Flt64>(
            config, problem, workers,
            new TabularSelectionPolicy("round-robin", new[] { "a" }),
            new ProgressFeatures(),
            new NotWorseAcceptancePolicy<Flt64, Flt64>(
                new AnyArityMinimizationDominance(), value => new[] { value }),
            FixedBudgetAllocator.Instance,
            new SearchMemory<Flt64, Flt64>(
                100, 20,
                new NotWorseAcceptancePolicy<Flt64, Flt64>(
                    new AnyArityMinimizationDominance(), value => new[] { value }),
                false,
                new AnyArityMinimizationDominance(),
                value => new[] { value },
                _ => true),
            new SearchBudget(10L),
            new SearchRandom(1L),
            new ImprovementRewardPolicy());
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <returns>问题 / The problem.</returns>
    private static SearchProblem<Flt64, Flt64> Problem() =>
        new(
            "p", "p",
            new BoundedStubModel<object?>(1, -10.0, 10.0),
            SearchCodecs.Flt64,
            SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            validSolution: _ => true,
            objectiveCoordinates: value => new[] { value },
            admissible: _ => true);

    /// <summary>会真正消费额度的子会话 / A child session that actually consumes allowance.</summary>
    private sealed class StubSession : ISearchSession<Flt64, Flt64> {
        private long _steps;

        /// <inheritdoc/>
        public bool SupportsImport => true;

        /// <inheritdoc/>
        public Task<Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>> Step(
            SearchSlice budget) {
            _steps++;
            long consumed = 0L;
            while (budget.Acquire()) {
                consumed++;
            }
            return Task.FromResult(Results.Ok(new SearchStepResult<Flt64, Flt64>(
                new[] { Individual(_steps + 5.0) }, consumed, 1L, SearchTermination.SliceComplete)));
        }

        /// <inheritdoc/>
        public Task<Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>> ImportSolutions(
            IReadOnlyList<Solution<Flt64>> solutions, SearchSlice budget) =>
            Task.FromResult(Results.Ok(new SearchStepResult<Flt64, Flt64>(
                Array.Empty<IIndividual<Flt64, Flt64>>(), 0L, 0L, SearchTermination.SliceComplete)));

        /// <inheritdoc/>
        public Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> Snapshot() =>
            Results.Ok(new SearchSessionSnapshot(
                true, _steps, 0L,
                JsonDocument.Parse("{}").RootElement.Clone(),
                JsonDocument.Parse("[]").RootElement.Clone()));

        /// <inheritdoc/>
        public Result<Success, ErrorCode, Error<ErrorCode>> Restore(SearchSessionSnapshot snapshot) {
            _steps = snapshot.Iteration;
            return Results.Ok(new Success());
        }

        /// <inheritdoc/>
        public Result<Success, ErrorCode, Error<ErrorCode>> Close() => Results.Ok(new Success());

        /// <summary>构造一个个体 / Build one individual.</summary>
        /// <param name="objective">目标值 / The objective value.</param>
        /// <returns>个体 / The individual.</returns>
        private static IIndividual<Flt64, Flt64> Individual(double objective) =>
            new SolutionWithFitness<Flt64, Flt64>(
                new[] { new Flt64(objective) }, new Flt64(objective), objective);
    }
}
