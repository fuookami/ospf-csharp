// 派发执行器测试 / Tests for the dispatch executor.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 派发的执行 / Executing a dispatch.
/// </summary>
public sealed class HyperHeuristicExecutorTests {
    /// <summary>**已初始化时先导入再步进** / An initialised worker imports before stepping.</summary>
    [Fact]
    public void Execute_ShouldImportBeforeStepWhenInitialized() {
        var session = new RecordingSession();
        HyperHeuristicExecution<Flt64, Flt64> execution = HyperHeuristicExecutor<Flt64, Flt64>.Execute(
            session, new SearchBudget(100L), Dispatch(seeds: 2), initialized: true, evaluationLimit: 100L);

        session.Events.Should().Equal("import", "step");
        execution.Initialized.Should().BeTrue();
    }

    /// <summary>**未初始化时先步进再导入** / An uninitialised worker steps before importing.</summary>
    /// <remarks>
    /// 未初始化的会话在第一次步进时**建立种群**，此前导入的种子会被这次建立覆盖掉——
    /// 于是"导入了"与"没导入"没有任何区别，**而调用方以为共享已经发生**。
    ///
    /// An uninitialised session **builds its population** on the first step, overwriting anything imported before it — so "imported" and
    /// "not imported" are indistinguishable **while the caller believes sharing took place**.
    /// </remarks>
    [Fact]
    public void Execute_ShouldImportAfterFirstProductiveStepWhenNotInitialized() {
        var session = new RecordingSession();
        HyperHeuristicExecution<Flt64, Flt64> execution = HyperHeuristicExecutor<Flt64, Flt64>.Execute(
            session, new SearchBudget(100L), Dispatch(seeds: 2), initialized: false, evaluationLimit: 100L);

        session.Events.Should().Equal("step", "import");
        execution.Initialized.Should().BeTrue();
    }

    /// <summary>**没有产出步数时不导入，也不把工作单元标记为已初始化** / No productive step means no import and no initialisation.</summary>
    [Fact]
    public void Execute_ShouldNotImportWithoutProductiveStep() {
        var session = new RecordingSession { StepsPerCall = 0L };
        HyperHeuristicExecution<Flt64, Flt64> execution = HyperHeuristicExecutor<Flt64, Flt64>.Execute(
            session, new SearchBudget(100L), Dispatch(seeds: 2), initialized: false, evaluationLimit: 100L);

        session.Events.Should().Equal("step");
        execution.Initialized.Should().BeFalse();
    }

    /// <summary>**原本已初始化的工作单元不会因为一次零步而退回未初始化** / A worker already initialised does not regress on a zero-step call.</summary>
    [Fact]
    public void Execute_ShouldKeepInitialisedStateOnZeroStep() {
        var session = new RecordingSession { StepsPerCall = 0L };
        HyperHeuristicExecution<Flt64, Flt64> execution = HyperHeuristicExecutor<Flt64, Flt64>.Execute(
            session, new SearchBudget(100L), Dispatch(seeds: 0), initialized: true, evaluationLimit: 100L);

        execution.Initialized.Should().BeTrue(
            "零步不代表退回未初始化 / a zero step does not mean falling back to uninitialised");
    }

    /// <summary>**导入与步进的候选按导入在前、步进在中、后置导入在后的顺序汇总** / Candidates aggregate in import-step-import order.</summary>
    [Fact]
    public void Execute_ShouldAggregateCandidatesInOrder() {
        var session = new RecordingSession();
        HyperHeuristicExecution<Flt64, Flt64> execution = HyperHeuristicExecutor<Flt64, Flt64>.Execute(
            session, new SearchBudget(100L), Dispatch(seeds: 1), initialized: true, evaluationLimit: 100L);

        SearchStepResult<Flt64, Flt64> report =
            ((Ok<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)execution.Report).Value;
        report.Candidates.Should().HaveCount(2);
        report.Candidates[0].Objective.ToDouble().Should().Be(100.0,
            "导入的候选排在前 / imported candidates come first");
        report.Candidates[1].Objective.ToDouble().Should().Be(200.0);
    }

    /// <summary>**导入失败必须原样上报，不能被吞成"这一步没导入"** / An import failure must surface, not be swallowed as "nothing imported".</summary>
    [Fact]
    public void Execute_ShouldPropagateImportFailure() {
        var session = new RecordingSession { FailImport = true };
        HyperHeuristicExecution<Flt64, Flt64> execution = HyperHeuristicExecutor<Flt64, Flt64>.Execute(
            session, new SearchBudget(100L), Dispatch(seeds: 1), initialized: true, evaluationLimit: 100L);

        execution.Report.Should()
            .BeOfType<Failed<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        session.Events.Should().Equal("import");
    }

    /// <summary>**没有种子就不导入** / No seeds means no import at all.</summary>
    [Fact]
    public void Execute_ShouldNotImportWithoutSeeds() {
        var session = new RecordingSession();
        HyperHeuristicExecutor<Flt64, Flt64>.Execute(
            session, new SearchBudget(100L), Dispatch(seeds: 0), initialized: true, evaluationLimit: 100L);

        session.Events.Should().Equal("step");
    }

    /// <summary>构造一次派发 / Build a dispatch.</summary>
    /// <param name="seeds">种子数 / The seed count.</param>
    /// <returns>派发 / The dispatch.</returns>
    private static HyperHeuristicDispatch<Flt64, Flt64> Dispatch(int seeds) {
        var solutions = new List<Solution<Flt64>>(seeds);
        for (int index = 0; index < seeds; index++) {
            solutions.Add(new Solution<Flt64>(new[] { new Flt64(index) }));
        }
        return new HyperHeuristicDispatch<Flt64, Flt64>(0L, 20L, 1L, solutions);
    }

    /// <summary>记录调用顺序的会话 / A session that records the call order.</summary>
    private sealed class RecordingSession : ISearchSession<Flt64, Flt64> {
        /// <summary>调用顺序 / The call order.</summary>
        public List<string> Events { get; } = new();

        /// <summary>每次步进产出的步数 / The steps produced per call.</summary>
        public long StepsPerCall { get; init; } = 1L;

        /// <summary>导入是否失败 / Whether imports fail.</summary>
        public bool FailImport { get; init; }

        /// <inheritdoc/>
        public bool SupportsImport => true;

        /// <inheritdoc/>
        public Task<Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>> Step(
            SearchSlice budget) {
            Events.Add("step");
            return Task.FromResult(Results.Ok(new SearchStepResult<Flt64, Flt64>(
                new[] { Individual(200.0) }, 1L, StepsPerCall, SearchTermination.SliceComplete)));
        }

        /// <inheritdoc/>
        public Task<Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>> ImportSolutions(
            IReadOnlyList<Solution<Flt64>> solutions, SearchSlice budget) {
            Events.Add("import");
            if (FailImport) {
                return Task.FromResult(Results.Failed<SearchStepResult<Flt64, Flt64>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "import rejected")));
            }
            var imported = new List<IIndividual<Flt64, Flt64>>(solutions.Count);
            foreach (Solution<Flt64> solution in solutions) {
                imported.Add(Individual(100.0));
            }
            return Task.FromResult(Results.Ok(new SearchStepResult<Flt64, Flt64>(
                imported, 1L, 0L, SearchTermination.SliceComplete)));
        }

        /// <inheritdoc/>
        public Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> Snapshot() =>
            Results.Ok(new SearchSessionSnapshot(
                true, 0L, 0L,
                JsonDocument.Parse("{}").RootElement.Clone(),
                JsonDocument.Parse("[]").RootElement.Clone()));

        /// <inheritdoc/>
        public Result<Success, ErrorCode, Error<ErrorCode>> Restore(SearchSessionSnapshot snapshot) =>
            Results.Ok(new Success());

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
