// 复合搜索会话测试 / Tests for the composite search session.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Scheduling;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 复合搜索会话 / The composite search session.
/// </summary>
public sealed class CompositeSearchSessionTests {
    /// <summary>**顺序模式下子会话要跑满一个相位才换，且最后一格不再前进** / Sequentially, a child keeps the cursor for a full phase and the last slot never advances.</summary>
    /// <remarks>
    /// 前半句少了：每个子算法只跑一步就被换掉；后半句少了：最后一个子算法永远得不到执行。
    /// **两种错误的表面现象都是"复合搜索不如单算法"**，看不出是哪一半写错——所以要分别断言。
    ///
    /// Without the first half every child is replaced after a single step; without the second the last child never runs.
    /// **Both look like "the composite is worse than a single algorithm"**, with no clue which half is wrong — hence separate assertions.
    /// </remarks>
    /// <returns>异步任务 / The asynchronous task.</returns>
    [Fact]
    public async Task Sequential_ShouldHoldAChildForOnePhase() {
        var first = new CountingSession();
        var second = new CountingSession();
        CompositeSearchSession<Flt64, Flt64> session = Session(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)first), ("b", second) },
            sequential: true, phaseSteps: 2);

        await session.Step(new SearchSlice(new SearchBudget(100), 100, 6));

        first.Steps.Should().Be(2, "相位为 2 时第一个子会话跑两步 / with a phase of two the first child runs twice");
        second.Steps.Should().Be(4);
        session.Cursor.Should().Be(1, "最后一格不再前进 / the last slot never advances");
    }

    /// <summary>**轮转模式下每步都换子会话** / Round-robin swaps the child every step.</summary>
    /// <returns>异步任务 / The asynchronous task.</returns>
    [Fact]
    public async Task RoundRobin_ShouldAlternateEveryStep() {
        var first = new CountingSession();
        var second = new CountingSession();
        CompositeSearchSession<Flt64, Flt64> session = Session(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)first), ("b", second) },
            sequential: false, phaseSteps: 2);

        await session.Step(new SearchSlice(new SearchBudget(100), 100, 4));

        first.Steps.Should().Be(2);
        second.Steps.Should().Be(2);
        session.Cursor.Should().Be(0, "四步之后回到第一个子会话 / after four steps it returns to the first child");
    }

    /// <summary>**未初始化的子会话不接受导入** / An uninitialised child accepts no imports.</summary>
    /// <returns>异步任务 / The asynchronous task.</returns>
    [Fact]
    public async Task Import_ShouldBeIgnoredBeforeInitialisation() {
        var child = new CountingSession();
        CompositeSearchSession<Flt64, Flt64> session = Session(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)child) },
            sequential: true, phaseSteps: 1);

        Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> imported =
            await session.ImportSolutions(
                new[] { new Solution<Flt64>(new[] { new Flt64(1.0) }) },
                new SearchSlice(new SearchBudget(10), 10, 1));

        imported.Should().BeOfType<Ok<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Ok<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)imported)
            .Value.Steps.Should().Be(0);
        child.Imports.Should().Be(0, "初始化之前不导入 / no import before initialisation");
    }

    /// <summary>**快照与恢复保持游标与步数** / Snapshot and restore preserve the cursor and the step count.</summary>
    /// <returns>异步任务 / The asynchronous task.</returns>
    [Fact]
    public async Task Snapshot_ShouldRoundTrip() {
        var first = new CountingSession();
        var second = new CountingSession();
        CompositeSearchSession<Flt64, Flt64> session = Session(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)first), ("b", second) },
            sequential: true, phaseSteps: 1);
        await session.Step(new SearchSlice(new SearchBudget(100), 100, 3));

        Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> snapshot = session.Snapshot();
        snapshot.Should().BeOfType<Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>>();

        var restoredFirst = new CountingSession();
        var restoredSecond = new CountingSession();
        CompositeSearchSession<Flt64, Flt64> restored = Session(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)restoredFirst), ("b", restoredSecond) },
            sequential: true, phaseSteps: 1);

        restored.Restore(((Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>)snapshot).Value)
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        restored.Cursor.Should().Be(session.Cursor);
        restored.Completed.Should().Be(session.Completed);
    }

    /// <summary>**恢复拒绝越界游标** / A restore rejects an out-of-range cursor.</summary>
    /// <returns>异步任务 / The asynchronous task.</returns>
    [Fact]
    public async Task Restore_ShouldRejectOutOfRangeCursor() {
        var first = new CountingSession();
        CompositeSearchSession<Flt64, Flt64> session = Session(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)first) },
            sequential: true, phaseSteps: 1);
        await session.Step(new SearchSlice(new SearchBudget(100), 100, 1));
        SearchSessionSnapshot snapshot = ((Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>)
            session.Snapshot()).Value;

        System.Text.Json.Nodes.JsonNode state =
            System.Text.Json.Nodes.JsonNode.Parse(snapshot.State.GetRawText())!;
        state["cursor"] = 5;
        JsonElement tampered = JsonDocument.Parse(state.ToJsonString()).RootElement.Clone();

        session.Restore(new SearchSessionSnapshot(
                snapshot.Initialized, snapshot.Iteration, snapshot.Stagnation,
                tampered, snapshot.Archive))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**缺目标坐标时在构造期就抛出** / Missing objective coordinates throw at construction.</summary>
    /// <remarks>
    /// 退化成"永远只接受更好解"会让复合搜索看起来正常，**而共享记忆里其实只剩当前最优**。
    /// Degrading to "accept only improvements" looks normal while **the shared memory in fact holds nothing but the current best**.
    /// </remarks>
    [Fact]
    public void Constructor_ShouldRequireCoordinates() {
        var children = new Dictionary<string, ISearchSession<Flt64, Flt64>>(StringComparer.Ordinal) {
            ["a"] = new CountingSession(),
        };
        var streams = new Dictionary<string, SearchRandom>(StringComparer.Ordinal) {
            ["a"] = new SearchRandom(1L),
        };

        Action withoutCoordinates = () => _ = new CompositeSearchSession<Flt64, Flt64>(
            children, streams, true, 2, Problem(coordinates: false));
        withoutCoordinates.Should().Throw<ArgumentException>();

        Action withCoordinates = () => _ = new CompositeSearchSession<Flt64, Flt64>(
            children, streams, true, 2, Problem(coordinates: true));
        withCoordinates.Should().NotThrow();
    }

    /// <summary>**任一子会话不支持导入，整体就不支持** / If any child cannot import, the composite cannot either.</summary>
    [Fact]
    public void SupportsImport_ShouldRequireEveryChild() {
        CompositeSearchSession<Flt64, Flt64> all = Session(
            new[] {
                ("a", (ISearchSession<Flt64, Flt64>)new CountingSession()),
                ("b", new CountingSession()),
            },
            sequential: true, phaseSteps: 1);
        CompositeSearchSession<Flt64, Flt64> mixed = Session(
            new[] {
                ("a", (ISearchSession<Flt64, Flt64>)new CountingSession()),
                ("b", new CountingSession { CanImport = false }),
            },
            sequential: true, phaseSteps: 1);

        all.SupportsImport.Should().BeTrue();
        mixed.SupportsImport.Should().BeFalse();
    }

    /// <summary>构造一个复合会话 / Build a composite session.</summary>
    /// <param name="children">子会话 / The children.</param>
    /// <param name="sequential">是否顺序 / Whether sequential.</param>
    /// <param name="phaseSteps">相位步数 / The phase length.</param>
    /// <returns>复合会话 / The composite session.</returns>
    private static CompositeSearchSession<Flt64, Flt64> Session(
        (string Id, ISearchSession<Flt64, Flt64> Child)[] children, bool sequential, int phaseSteps) {
        var map = new Dictionary<string, ISearchSession<Flt64, Flt64>>(StringComparer.Ordinal);
        var streams = new Dictionary<string, SearchRandom>(StringComparer.Ordinal);
        long seed = 1L;
        foreach ((string id, ISearchSession<Flt64, Flt64> child) in children) {
            map[id] = child;
            streams[id] = new SearchRandom(seed++);
        }
        return new CompositeSearchSession<Flt64, Flt64>(
            map, streams, sequential, phaseSteps, Problem(coordinates: true));
    }

    /// <summary>构造一个问题边界 / Build a problem boundary.</summary>
    /// <param name="coordinates">是否提供坐标 / Whether coordinates are supplied.</param>
    /// <returns>问题边界 / The problem boundary.</returns>
    private static SearchProblem<Flt64, Flt64> Problem(bool coordinates) =>
        new(
            "p", "p",
            new BoundedStubModel<object?>(1, -10.0, 10.0),
            SearchCodecs.Flt64,
            SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            validSolution: _ => true,
            objectiveCoordinates: coordinates ? value => new[] { value } : null,
            admissible: _ => true);

    /// <summary>只数步数的子会话 / A child session that only counts steps.</summary>
    private sealed class CountingSession : ISearchSession<Flt64, Flt64> {
        private bool _initialized;

        /// <summary>已执行步数 / The executed steps.</summary>
        public int Steps { get; private set; }

        /// <summary>导入次数 / The import count.</summary>
        public int Imports { get; private set; }

        /// <summary>是否支持导入 / Whether it supports imports.</summary>
        public bool CanImport { get; init; } = true;

        /// <inheritdoc/>
        public bool SupportsImport => CanImport;

        /// <inheritdoc/>
        public Task<Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>> Step(
            SearchSlice budget) {
            Steps++;
            _initialized = true;
            var candidate = new SolutionWithFitness<Flt64, Flt64>(
                new[] { new Flt64(Steps) }, new Flt64(Steps), Steps);
            return Task.FromResult(Results.Ok(new SearchStepResult<Flt64, Flt64>(
                new[] { candidate }, 1L, 1L, SearchTermination.SliceComplete)));
        }

        /// <inheritdoc/>
        public Task<Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>> ImportSolutions(
            IReadOnlyList<Solution<Flt64>> solutions, SearchSlice budget) {
            Imports++;
            return Task.FromResult(Results.Ok(new SearchStepResult<Flt64, Flt64>(
                Array.Empty<IIndividual<Flt64, Flt64>>(), 0L, 0L, SearchTermination.SliceComplete)));
        }

        /// <inheritdoc/>
        public Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> Snapshot() =>
            Results.Ok(new SearchSessionSnapshot(
                _initialized, Steps, 0L,
                JsonDocument.Parse("{\"steps\":" + Steps + "}").RootElement.Clone(),
                JsonDocument.Parse("[]").RootElement.Clone()));

        /// <inheritdoc/>
        public Result<Success, ErrorCode, Error<ErrorCode>> Restore(SearchSessionSnapshot snapshot) {
            ArgumentNullException.ThrowIfNull(snapshot);
            Steps = (int)snapshot.Iteration;
            _initialized = snapshot.Initialized;
            return Results.Ok(new Success());
        }

        /// <inheritdoc/>
        public Result<Success, ErrorCode, Error<ErrorCode>> Close() => Results.Ok(new Success());
    }
}
