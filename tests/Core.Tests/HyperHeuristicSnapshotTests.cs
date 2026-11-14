// 运行检查点测试 / Tests for run checkpoints.

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
/// 运行检查点 / Run checkpoints.
/// </summary>
public sealed class HyperHeuristicSnapshotTests {
    /// <summary>**捕获再恢复，计数与转移都回来** / Capture then restore brings back the counters and the transitions.</summary>
    [Fact]
    public void CaptureAndRestore_ShouldRoundTripCounters() {
        HyperHeuristicRun<Flt64, Flt64> run = Run(evaluations: 20L, slice: 5L);
        run.Run();
        int before = run.Transitions.Count;
        run.Ledger.Accounted.Should().Be(20L);

        Result<RunCheckpoint, ErrorCode, Error<ErrorCode>> captured = Capture(run);
        captured.Should().BeOfType<Ok<RunCheckpoint, ErrorCode, Error<ErrorCode>>>();

        HyperHeuristicRun<Flt64, Flt64> fresh = Run(evaluations: 20L, slice: 5L);
        HyperHeuristicSnapshots<Flt64, Flt64>.Restore(
                fresh, ((Ok<RunCheckpoint, ErrorCode, Error<ErrorCode>>)captured).Value, "cfg", fresh.Problem)
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        fresh.Ledger.Accounted.Should().Be(20L);
        fresh.Transitions.Should().HaveCount(before);
    }

    /// <summary>**恢复不会把诊断带进终止判定** / Restore does not carry diagnostics into the termination decision.</summary>
    /// <remarks>
    /// 诊断是追加的。不先清空，恢复后的账本会同时含有两段历史，而**终止判定只看"诊断是否非空"**——
    /// 于是一份从干净状态恢复的运行会立刻报 `Failed`。
    ///
    /// Diagnostics append. Without clearing first, the restored ledger holds both histories, and **the termination decision only looks at "are
    /// diagnostics non-empty"** — so a run restored from a clean state would immediately report `Failed`.
    /// </remarks>
    [Fact]
    public void Restore_ShouldClearStaleDiagnostics() {
        HyperHeuristicRun<Flt64, Flt64> source = Run(evaluations: 10L, slice: 5L);
        source.Run();
        Result<RunCheckpoint, ErrorCode, Error<ErrorCode>> captured = Capture(source);

        HyperHeuristicRun<Flt64, Flt64> fresh = Run(evaluations: 10L, slice: 5L);
        fresh.Ledger.Diagnostics.Add("stale");
        HyperHeuristicSnapshots<Flt64, Flt64>.Restore(
                fresh, ((Ok<RunCheckpoint, ErrorCode, Error<ErrorCode>>)captured).Value, "cfg", fresh.Problem)
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        fresh.Ledger.Diagnostics.Should().BeEmpty(
            "恢复后的账本不该带恢复前的诊断 / a restored ledger must not carry pre-restore diagnostics");
        fresh.Result().Termination.Should().NotBe(SearchTermination.Failed);
    }

    /// <summary>**配置摘要不一致的检查点必须被拒绝** / A mismatched configuration digest must be rejected.</summary> / A checkpoint with a mismatched configuration digest must be rejected.
    /// <remarks>
    /// 把一份别的配置的检查点恢复进本次运行，会让计数与记忆都对不上，而**搜索会若无其事地继续**——
    /// 从外部看不出它是从别人的状态里接着跑的。
    ///
    /// Restoring another configuration's checkpoint leaves the counters and memory inconsistent while **the search carries on regardless** —
    /// nothing outside shows it resumed from someone else's state.
    /// </remarks>
    [Fact]
    public void Restore_ShouldRejectMismatchedIdentity() {
        HyperHeuristicRun<Flt64, Flt64> source = Run(evaluations: 10L, slice: 5L);
        source.Run();
        RunCheckpoint captured = ((Ok<RunCheckpoint, ErrorCode, Error<ErrorCode>>)Capture(source)).Value;

        HyperHeuristicRun<Flt64, Flt64> fresh = Run(evaluations: 10L, slice: 5L);
        HyperHeuristicSnapshots<Flt64, Flt64>.Restore(fresh, captured, "other", fresh.Problem)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**工作单元集合不一致的检查点必须被拒绝** / A checkpoint whose worker set differs must be rejected.</summary>
    [Fact]
    public void Restore_ShouldRejectMismatchedWorkerSet() {
        HyperHeuristicRun<Flt64, Flt64> source = Run(evaluations: 10L, slice: 5L);
        source.Run();
        RunCheckpoint captured = ((Ok<RunCheckpoint, ErrorCode, Error<ErrorCode>>)Capture(source)).Value;

        HyperHeuristicRun<Flt64, Flt64> two = Run(
            evaluations: 10L, slice: 5L,
            ids: new[] { "a", "b" });
        HyperHeuristicSnapshots<Flt64, Flt64>.Restore(two, captured, "cfg", two.Problem)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**检查点里多出一个工作单元也必须被拒绝** / A checkpoint carrying an extra worker must also be rejected.</summary>
    /// <remarks>
    /// **这条测试是补的**：原来的"工作单元集合不一致"用例是"检查点少一个"，那被逐 id 的存在性检查拦下。
    /// 于是把"数量也要相等"那一行删掉，测试照样通过——**变异存活暴露了另一个方向没被覆盖**。
    /// 多一个工作单元意味着检查点来自一个更宽的运行，恢复进窄运行会让**多出来的那份额度无处归属**。
    ///
    /// **This test was added afterwards**: the original "worker set mismatch" case was "the checkpoint has one fewer", which the per-id existence
    /// check catches. Deleting the "counts must also match" line therefore left the test green — **the surviving mutation exposed the other direction.**
    /// An extra worker means the checkpoint came from a wider run, and restoring it into a narrower one leaves **that extra allowance with no owner**.
    /// </remarks>
    [Fact]
    public void Restore_ShouldRejectExtraWorkers() {
        HyperHeuristicRun<Flt64, Flt64> source = Run(evaluations: 10L, slice: 5L, ids: new[] { "a", "b" });
        source.Run();
        RunCheckpoint captured = ((Ok<RunCheckpoint, ErrorCode, Error<ErrorCode>>)Capture(source)).Value;

        HyperHeuristicRun<Flt64, Flt64> narrow = Run(evaluations: 10L, slice: 5L);
        HyperHeuristicSnapshots<Flt64, Flt64>.Restore(narrow, captured, "cfg", narrow.Problem)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**未初始化状态与禁用状态都随检查点回来** / The initialised and disabled flags travel with the checkpoint.</summary>
    [Fact]
    public void CaptureAndRestore_ShouldRoundTripFlags() {
        HyperHeuristicRun<Flt64, Flt64> source = Run(evaluations: 10L, slice: 5L);
        source.Run();
        RunCheckpoint captured = ((Ok<RunCheckpoint, ErrorCode, Error<ErrorCode>>)Capture(source)).Value;

        HyperHeuristicRun<Flt64, Flt64> fresh = Run(evaluations: 10L, slice: 5L);
        HyperHeuristicSnapshots<Flt64, Flt64>.Restore(fresh, captured, "cfg", fresh.Problem);

        fresh.IsInitialized("a").Should().BeTrue();
        fresh.IsDisabled("a").Should().BeFalse();
    }

    /// <summary>构造一次运行 / Build a run.</summary>
    /// <param name="evaluations">评估额度 / The evaluation allowance.</param>
    /// <param name="slice">切片额度 / The slice allowance.</param>
    /// <param name="ids">算法标识 / The algorithm identifiers.</param>
    /// <returns>运行 / The run.</returns>
    private static HyperHeuristicRun<Flt64, Flt64> Run(
        long evaluations, long slice, string[]? ids = null) {
        ids ??= new[] { "a" };
        var workers = new List<HyperHeuristicWorker<Flt64, Flt64>>(ids.Length);
        SearchProblem<Flt64, Flt64> problem = Problem();
        foreach (string id in ids) {
            workers.Add(new HyperHeuristicWorker<Flt64, Flt64>(
                id, problem, new SearchRandom(1L), new StubSession(), InitializationMode.Model));
        }
        var config = new HyperHeuristicConfig(
            new ComponentSpec("quadratic", new System.Text.Json.Nodes.JsonObject()),
            new[] { new AlgorithmConfig("a", new ComponentSpec("pso", new System.Text.Json.Nodes.JsonObject()), 20) },
            new BudgetConfig(Evaluations: evaluations, SliceEvaluations: slice, SliceSteps: 1L));
        return new HyperHeuristicRun<Flt64, Flt64>(
            config, problem, workers,
            new TabularSelectionPolicy("round-robin", ids),
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
            new SearchBudget(evaluations),
            new SearchRandom(1L),
            new ImprovementRewardPolicy());
    }

    /// <summary>捕获检查点 / Capture a checkpoint.</summary>
    /// <param name="run">运行 / The run.</param>
    /// <returns>检查点或失败 / The checkpoint or a failure.</returns>
    private static Result<RunCheckpoint, ErrorCode, Error<ErrorCode>> Capture(
        HyperHeuristicRun<Flt64, Flt64> run) =>
        HyperHeuristicSnapshots<Flt64, Flt64>.Capture(run, run.Configuration, run.Problem, "cfg");

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
