// 超启发式运行测试 / Tests for the hyper-heuristic run.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Scheduling;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 超启发式运行 / The hyper-heuristic run.
/// </summary>
public sealed class HyperHeuristicRunTests {
    /// <summary>**评估额度用尽时停下，且报 `EvaluationLimit`** / The run stops at the evaluation allowance and reports `EvaluationLimit`.</summary>
    [Fact]
    public void Run_ShouldStopAtEvaluationLimit() {
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)new StubSession()) },
            evaluations: 10L, slice: 5L);

        SearchResult<Flt64, Flt64> result = run.Run();

        result.Termination.Should().Be(SearchTermination.EvaluationLimit);
        run.Ledger.Accounted.Should().Be(10L);
        run.Ledger.Completed.Should().Be(2L, "两次各 5 点额度 / two slices of five");
    }

    /// <summary>**选择策略决定谁被派发** / The selection policy decides who is dispatched.</summary>
    [Fact]
    public void Run_ShouldRouteThroughTheSelector() {
        var first = new StubSession();
        var second = new StubSession();
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)first), ("b", second) },
            evaluations: 20L, slice: 5L);

        run.Run();

        (first.Steps + second.Steps).Should().Be(4);
        first.Steps.Should().Be(2, "轮转选择应当平均分配 / round-robin should split evenly");
        second.Steps.Should().Be(2);
    }

    /// <summary>**子会话报失败时诊断进账本，结果报 `Failed`** / A failing child's diagnostics enter the ledger and the result reports `Failed`.</summary>
    [Fact]
    public void Run_ShouldRecordDiagnosticsAndFail() {
        var session = new StubSession { Fail = true };
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)session) },
            evaluations: 20L, slice: 5L);

        SearchResult<Flt64, Flt64> result = run.Run();

        result.Termination.Should().Be(SearchTermination.Failed);
        run.Ledger.Diagnostics.Should().NotBeEmpty();
    }

    /// <summary>**全部工作单元禁用后报 `NoCandidate`** / Once every worker is disabled the run reports `NoCandidate`.</summary>
    [Fact]
    public void Run_ShouldReportNoCandidateWhenEverythingIsDisabled() {
        var session = new StubSession { NoCandidate = true };
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)session) },
            evaluations: 1000L, slice: 5L);

        SearchResult<Flt64, Flt64> result = run.Run();

        result.Termination.Should().Be(SearchTermination.NoCandidate);
        run.Ledger.Completed.Should().BeGreaterThan(0L);
    }

    /// <summary>**外部停止优先于预算** / An external stop outranks the budget.</summary>
    [Fact]
    public void Run_ShouldHonourRequestStop() {
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)new StubSession()) },
            evaluations: 1000L, slice: 5L);
        run.RequestStop();

        run.Run().Termination.Should().Be(SearchTermination.Stopped);
        run.Ledger.Completed.Should().Be(0L, "停止在第一次派发之前生效 / the stop takes effect before the first dispatch");
    }

    /// <summary>**每次决策都留下一条转移，且带奖励与评估数** / Every decision leaves a transition carrying its reward and evaluations.</summary>
    [Fact]
    public void Run_ShouldRecordOneTransitionPerDecision() {
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)new StubSession()) },
            evaluations: 10L, slice: 5L);

        run.Run();

        run.Transitions.Should().HaveCount(2);
        run.Transitions[0].Decision.Should().Be(0L);
        run.Transitions[1].Decision.Should().Be(1L);
        run.Transitions[0].Action.Should().Be("a");
        run.Transitions[0].Evaluations.Should().Be(5L);
        run.Transitions[0].Episode.Should().StartWith("p:p:",
            "回合标识 = 问题 id:指纹:种子 / the episode is problem id:fingerprint:seed");
    }

    /// <summary>**奖励反映改进：第一次必改进，之后目标值变差就不改进** / The reward reflects improvement: the first decision improves, later worse ones do not.</summary>
    [Fact]
    public void Run_ShouldCarryImprovementIntoTheReward() {
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)new StubSession()) },
            evaluations: 10L, slice: 5L);

        run.Run();

        run.Transitions[0].Reward.ToDouble().Should().Be(1.0,
            "首个个体进入空记忆，算作改进 / the first individual enters empty memory, which counts as improvement");
        run.Transitions[1].Reward.ToDouble().Should().Be(0.0,
            "第二个更差，不算改进 / the second is worse and does not improve");
    }

    /// <summary>**账本的成功数与停滞数跟着改进走** / The ledger's success and stagnation counters follow the improvement.</summary>
    /// <remarks>
    /// **这条测试是补的**：`Transitions[].Reward` 由 `HyperHeuristicCommit` 算，与账本里的 `Successes`/`Stagnation` 是**两条独立的路径**。
    /// 原来只断言转移里的奖励，于是把账本那两行改成常量，测试照样通过——**变异存活暴露了"断言覆盖了另一条路径"**。
    /// 账本这两个数还是特征输入，写错会让选择策略看到假的成功率。
    ///
    /// **This test was added afterwards**: `Transitions[].Reward` is computed by `HyperHeuristicCommit`, which is a **separate path** from the ledger's
    /// `Successes`/`Stagnation`. The original test only asserted the reward on the transition, so replacing those two ledger lines with constants kept
    /// it green — **the surviving mutation exposed "the assertion covered the other path"**. Those two ledger numbers also feed the features, so getting
    /// them wrong shows the selection policy a fake success rate.
    /// </remarks>
    [Fact]
    public void Run_ShouldTrackSuccessesAndStagnation() {
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)new StubSession()) },
            evaluations: 10L, slice: 5L);

        run.Run();

        run.Ledger.Successes.Should().Be(1L, "只有第一次改进 / only the first decision improves");
        run.Ledger.Stagnation.Should().Be(1L, "第二次没改进 / the second does not improve");
    }

    /// <summary>**录制出的调度日志必须通过校验并带 `end` 事件** / The recorded schedule log validates and carries an `end` event.</summary>
    /// <remarks>
    /// `ValidatePrefix` 是逐事件的自动机：`begin → dispatch → complete → yield` 的顺序、`decision` 递增、
    /// 动作必须出现在 `available`、`complete` 的额度不得超过 `dispatch`、`end` 的累计值必须与事件序列一致。
    /// **这些约束彼此耦合，任何一条单独检查都通不过一个真正非法的日志**——所以这里断言的是"校验通过"这一整体结论。
    ///
    /// `ValidatePrefix` is a per-event state machine: the `begin → dispatch → complete → yield` order, the incrementing `decision`, the action
    /// appearing in `available`, `complete` never exceeding `dispatch`, and `end`'s totals matching the sequence. **These are mutually coupled, and
    /// no single check rejects a genuinely illegal log** — so the assertion here is the whole verdict, "validation passes".
    /// </remarks>
    [Fact]
    public void Run_ShouldExportAValidScheduleLog() {
        var schedule = new ScheduleControl();
        schedule.Bind("cfg", "p:p", null).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)new StubSession()) },
            evaluations: 10L, slice: 5L, schedule: schedule);

        run.Run();
        Result<ScheduleLog, ErrorCode, Error<ErrorCode>> exported = run.ExportSchedule();

        exported.Should().BeOfType<Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>>();
        ScheduleLog log = ((Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>)exported).Value;
        log.Events[^1].Kind.Should().Be("end");
        log.Events[^1].Evaluations.Should().Be(10L);
        log.Events[^1].Steps.Should().Be(2L);
        log.Validate().Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        log.Events[0].Kind.Should().Be("begin");
        log.Events[1].Kind.Should().Be("dispatch");
        log.Events[2].Kind.Should().Be("complete");
        log.Events[3].Kind.Should().Be("yield");
    }

    /// <summary>**派发事件带着随机流状态** / The dispatch event carries the random stream state.</summary>
    [Fact]
    public void Run_ShouldRecordTheRandomStateOnDispatch() {
        var schedule = new ScheduleControl();
        schedule.Bind("cfg", "p:p", null);
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)new StubSession()) },
            evaluations: 5L, slice: 5L, schedule: schedule);

        run.Run();
        ScheduleLog log = ((Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>)run.ExportSchedule()).Value;

        log.Events.Should().Contain(evt => evt.Kind == "dispatch" && evt.Random != 0L,
            "重放需要同一随机状态 / replay needs the same random state");
        log.Events.First(evt => evt.Kind == "dispatch").Available.Should().Contain("a");
    }

    /// <summary>**未启用录制时导出必须失败** / Exporting without recording must fail.</summary>
    [Fact]
    public void Run_ShouldRefuseToExportWithoutRecording() =>
        Run(new[] { ("a", (ISearchSession<Flt64, Flt64>)new StubSession()) }, evaluations: 5L, slice: 5L)
            .ExportSchedule().Should()
            .BeOfType<Failed<ScheduleLog, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**并行度 > 1 时一轮派发多个工作单元** / With parallelism > 1 a round dispatches several workers.</summary>
    /// <remarks>
    /// **不能只断言"每个工作单元各跑了几步"**：轮转选择下，一轮一个和一轮两个跑同样的轮数后各步数完全相同，
    /// 于是"忽略并行度"这个变异照样通过。这里断言**轮数**（`yield` 事件数）：20 点额度、5 点切片、并行度 2 → 两轮；
    /// 并行度 1 → 四轮。
    ///
    /// **"How many steps each worker took" is not enough**: under round-robin selection, one-per-round and two-per-round reach the same per-worker
    /// counts, so an "ignore the parallelism" mutation still passes. This asserts **the number of rounds** (the `yield` event count): 20 evaluations,
    /// 5-point slices, parallelism 2 → two rounds; parallelism 1 → four.
    /// </remarks>
    [Fact]
    public void Run_ShouldDispatchSeveralWorkersPerRound() {
        var first = new StubSession();
        var second = new StubSession();
        var schedule = new ScheduleControl();
        schedule.Bind("cfg", "p:p", null);
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)first), ("b", second) },
            evaluations: 20L, slice: 5L, parallelism: 2, schedule: schedule);

        run.Run();
        ScheduleLog log = ((Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>)run.ExportSchedule()).Value;

        first.Steps.Should().Be(2);
        second.Steps.Should().Be(2);
        run.Ledger.Accounted.Should().Be(20L);
        CountOf(log, "yield").Should().Be(2, "两轮，每轮两个 / two rounds of two");
    }

    /// <summary>**一轮里的额度必须预留，否则会超支** / Allowances must be reserved within a round, or the budget overspends.</summary>
    /// <remarks>
    /// 按"剩余额度"逐个分配而不预留，一轮里每个工作单元都看到同一份剩余额——**一轮就能把全局预算超支数倍**，
    /// 而结果里的 `evaluations` 会大于配置的额度。
    ///
    /// Allocating each slot from the same "remaining" figure without reserving lets every worker in a round see the same remainder — **one round can
    /// overspend the global budget several times**, and the reported `evaluations` exceeds the configured allowance.
    /// </remarks>
    [Fact]
    public void Run_ShouldNeverOverspendTheBudget() {
        string[] sessions = new[] { "a", "b", "c" };
        var stubs = new StubSession[] { new StubSession(), new StubSession(), new StubSession() };
        var schedule = new ScheduleControl();
        schedule.Bind("cfg", "p:p", null);
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] {
                (sessions[0], (ISearchSession<Flt64, Flt64>)stubs[0]),
                (sessions[1], (ISearchSession<Flt64, Flt64>)stubs[1]),
                (sessions[2], (ISearchSession<Flt64, Flt64>)stubs[2]),
            },
            evaluations: 12L, slice: 5L, parallelism: 3, schedule: schedule);

        run.Run();
        ScheduleLog log = ((Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>)run.ExportSchedule()).Value;

        run.Ledger.Accounted.Should().BeLessThanOrEqualTo(12L,
            "一轮三个各 5 点的额度必须被预留 / three five-point allowances in one round must be reserved");
        run.Result().Evaluations.Should().Be(run.Ledger.Accounted);
        // **只看"实际消耗没超"抓不住这个变异**：`SearchSlice.Acquire` 本身受全局预算封顶，
        // 所以不预留时"实际消耗"依然是 12——**超支体现在"承诺出去的额度"上**。
        // **"Actual consumption did not exceed" cannot catch this mutation**: `SearchSlice.Acquire` is itself capped by the global budget, so without
        // reservation the actual consumption is still 12 — **the overspend shows up in the allowances promised out**.
        long promised = 0L;
        foreach (ScheduleEvent evt in log.Events) {
            if (evt.Kind == "dispatch") {
                promised += evt.Evaluations;
            }
        }
        promised.Should().BeLessThanOrEqualTo(12L,
            "承诺的额度之和不得超过全局预算 / the promised allowances must fit inside the global budget");
    }

    /// <summary>**派发事件里的可选动作是快照，不随本轮后续选择变化** / The available list in a dispatch event is a snapshot, not a live view.</summary>
    /// <remarks>
    /// 本轮后续的 `picked.Remove(action)` 会改掉同一个列表。存引用的话，**先记下的事件会在事后被悄悄改写成"只剩没被选中的动作"**，
    /// 而日志校验只检查"动作出现在 available 里"——于是被选中的动作从它自己的事件里消失了，校验却照过。
    ///
    /// A later `picked.Remove(action)` in the same round mutates the very same list. Storing a reference means **an already-recorded event is silently
    /// rewritten into "only the actions nobody picked"**, and validation only checks "the action appears in available" — so the chosen action disappears
    /// from its own event while validation still passes.
    /// </remarks>
    [Fact]
    public void Run_ShouldSnapshotTheAvailableListInEvents() {
        var schedule = new ScheduleControl();
        schedule.Bind("cfg", "p:p", null);
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)new StubSession()),
                    ("b", (ISearchSession<Flt64, Flt64>)new StubSession()) },
            evaluations: 10L, slice: 5L, parallelism: 2, schedule: schedule);

        run.Run();
        ScheduleLog log = ((Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>)run.ExportSchedule()).Value;

        foreach (ScheduleEvent evt in log.Events) {
            if (evt.Kind == "dispatch") {
                evt.Available.Should().Contain(evt.Action,
                    "派发事件必须仍然列出被选中的动作 / a dispatch event must still list its own action");
            }
        }
    }

    /// <summary>**选择器给出被屏蔽的动作必须报错，不能悄悄派发同一个工作单元两次** / A masked action must be reported, never silently dispatched twice.</summary>
    /// <remarks>
    /// 轮转选择器永远不会给出被屏蔽的动作，所以这条只能用一个"总是返回 a"的桩策略来验。
    /// 放过它会让同一轮里同一个工作单元被派发两次，两次的额度各自预留、最后只有一次归并——**另一份额度凭空消失**。
    ///
    /// The round-robin selector never returns a masked action, so this needs a stub policy that always answers "a". Letting it through dispatches the
    /// same worker twice in one round, both allowances are reserved, only one is merged — **the other vanishes**.
    /// </remarks>
    [Fact]
    public void Run_ShouldRejectAMaskedAction() {
        HyperHeuristicRun<Flt64, Flt64> run = Run(
            new[] { ("a", (ISearchSession<Flt64, Flt64>)new StubSession()),
                    ("b", (ISearchSession<Flt64, Flt64>)new StubSession()) },
            evaluations: 20L, slice: 5L, parallelism: 2, policy: new StickyPolicy("a"));

        SearchResult<Flt64, Flt64> result = run.Run();

        result.Termination.Should().Be(SearchTermination.Failed);
        run.Ledger.Diagnostics.Should().Contain(
            diagnostic => diagnostic.Contains("masked action", StringComparison.Ordinal),
            "错误必须指名被屏蔽的动作 / the error must name the masked action");
        run.Ledger.Dispatched.Should().Be(1L,
            "第二轮选择就该被拒 / the second pick must be rejected");
    }

    /// <summary>统计某一类事件的条数 / Count the events of one kind.</summary>
    /// <param name="log">日志 / The log.</param>
    /// <param name="kind">事件类型 / The event kind.</param>
    /// <returns>条数 / The count.</returns>
    private static int CountOf(ScheduleLog log, string kind) {
        int count = 0;
        foreach (ScheduleEvent evt in log.Events) {
            if (string.Equals(evt.Kind, kind, StringComparison.Ordinal)) {
                count++;
            }
        }
        return count;
    }

    /// <summary>构造一次运行 / Build a run.</summary>
    /// <param name="workers">工作单元 / The workers.</param>
    /// <param name="evaluations">评估额度 / The evaluation allowance.</param>
    /// <param name="slice">切片额度 / The slice allowance.</param>
    /// <returns>运行 / The run.</returns>
    private static HyperHeuristicRun<Flt64, Flt64> Run(
        (string Id, ISearchSession<Flt64, Flt64> Session)[] workers,
        long evaluations, long slice, int parallelism = 1,
        ScheduleControl? schedule = null, ISelectionPolicy? policy = null) {
        var built = new List<HyperHeuristicWorker<Flt64, Flt64>>(workers.Length);
        var ids = new List<string>(workers.Length);
        SearchProblem<Flt64, Flt64> problem = Problem();
        foreach ((string id, ISearchSession<Flt64, Flt64> session) in workers) {
            built.Add(new HyperHeuristicWorker<Flt64, Flt64>(
                id, problem, new SearchRandom(id.GetHashCode() & 0x7fffffff), session,
                InitializationMode.Model));
            ids.Add(id);
        }
        var config = new HyperHeuristicConfig(
            new ComponentSpec("quadratic", new System.Text.Json.Nodes.JsonObject()),
            new[] { new AlgorithmConfig("a", new ComponentSpec("pso", new System.Text.Json.Nodes.JsonObject()), 20) },
            new BudgetConfig(Evaluations: evaluations, SliceEvaluations: slice, SliceSteps: 1L),
            null,
            new ExecutionConfig(
                parallelism > 1 ? ExecutionMode.Synchronous : ExecutionMode.Serial,
                parallelism, 1L));
        return new HyperHeuristicRun<Flt64, Flt64>(
            config,
            problem,
            built,
            policy ?? new TabularSelectionPolicy("round-robin", ids),
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
            new ImprovementRewardPolicy(),
            schedule);
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
        private int _steps;

        /// <summary>已执行步数 / The executed steps.</summary>
        public int Steps => _steps;

        /// <summary>是否报失败 / Whether it reports a failure.</summary>
        public bool Fail { get; init; }

        /// <summary>是否报"没有候选" / Whether it reports "no candidate".</summary>
        public bool NoCandidate { get; init; }

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
            if (NoCandidate) {
                return Task.FromResult(Results.Ok(new SearchStepResult<Flt64, Flt64>(
                    Array.Empty<IIndividual<Flt64, Flt64>>(), consumed, 0L,
                    SearchTermination.NoCandidate)));
            }
            if (Fail) {
                return Task.FromResult(Results.Ok(new SearchStepResult<Flt64, Flt64>(
                    Array.Empty<IIndividual<Flt64, Flt64>>(), consumed, 1L,
                    SearchTermination.Failed, new[] { "stub failure" })));
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
    /// <summary>总是返回同一个动作的策略桩 / A stub policy that always answers the same action.</summary>
    private sealed class StickyPolicy : ISelectionPolicy {
        private readonly string _action;

        /// <summary>构造桩 / Build the stub.</summary>
        /// <param name="action">固定动作 / The fixed action.</param>
        public StickyPolicy(string action) => _action = action;

        /// <inheritdoc/>
        public long Revision => 0L;

        /// <inheritdoc/>
        public Result<string, ErrorCode, Error<ErrorCode>> Select(
            SelectionContext context, IReadOnlyList<string> available, SearchRandom random) =>
            Results.Ok(_action);

        /// <inheritdoc/>
        public Result<Success, ErrorCode, Error<ErrorCode>> Observe(SelectionTransition transition) =>
            Results.Ok(new Success());

        /// <inheritdoc/>
        public PolicySnapshot Snapshot() => new(1, "sticky", new[] { _action });

        /// <inheritdoc/>
        public Result<Success, ErrorCode, Error<ErrorCode>> Restore(PolicySnapshot snapshot) =>
            Results.Ok(new Success());
    }
}
