// 运行器会话测试 / Tests for the runner session and the standalone run.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 运行器会话 / The runner session.
/// </summary>
public sealed class RunnerSearchSessionTests {
    /// <summary>**首步完成初始化** / The first step initializes the state.</summary>
    [Fact]
    public async Task Step_ShouldInitializeOnFirstCall() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session = BuildSession();

        Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await session.Step(new SearchSlice(new SearchBudget(100L), 10L, 1L));

        result.Should().BeOfType<Ok<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        SearchStepResult<Flt64, Flt64> report =
            ((Ok<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Steps.Should().Be(1L);
        report.Termination.Should().Be(SearchTermination.SliceComplete);
        session.Initialized.Should().BeTrue();
        session.Archive.Should().NotBeEmpty();
    }

    /// <summary>**步数上限被遵守** / The step limit is respected.</summary>
    [Fact]
    public async Task Step_ShouldRespectStepLimit() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session = BuildSession();

        Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await session.Step(new SearchSlice(new SearchBudget(100L), 100L, 3L));

        SearchStepResult<Flt64, Flt64> report =
            ((Ok<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Steps.Should().Be(3L);
        session.Iteration.Current.Should().Be(3);
    }

    /// <summary>**额度用尽时以 `EvaluationLimit` 结束** / Running out of allowance ends with `EvaluationLimit`.</summary>
    /// <remarks>
    /// 只有一条额度时，初始化消耗掉它，下一次评估申请失败；报告必须说明是"额度用尽"而不是"完成了"。
    /// With a single unit of allowance, initialization consumes it and the next evaluation fails to acquire; the report must say
    /// "allowance exhausted" rather than "complete".
    /// </remarks>
    [Fact]
    public async Task Step_ShouldTerminateOnEvaluationLimit() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session = BuildSession();

        Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await session.Step(new SearchSlice(new SearchBudget(100L), 1L, 5L));

        SearchStepResult<Flt64, Flt64> report =
            ((Ok<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Termination.Should().Be(SearchTermination.EvaluationLimit);
        report.Steps.Should().Be(1L);
        report.Evaluations.Should().Be(1L);
    }

    /// <summary>**负额度或负步数被拒绝** / A negative allowance or step count is rejected.</summary>
    /// <remarks>
    /// 负数上限在循环条件里表现为"永不推进"，**而一次 `Step` 会返回一个成功的空报告**。
    /// A negative limit manifests in the loop condition as "never advance", **and a `Step` returns a successful empty report**.
    /// </remarks>
    [Fact]
    public async Task Step_ShouldRejectNegativeSlice() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session = BuildSession();

        (await session.Step(new SearchSlice(new SearchBudget(100L), -1L, 1L)))
            .Should().BeOfType<Failed<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        (await session.Step(new SearchSlice(new SearchBudget(100L), 10L, -1L)))
            .Should().BeOfType<Failed<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**模型抛异常时以 `Failed` 结束并带诊断** / A throwing model ends with `Failed` plus diagnostics.</summary>
    [Fact]
    public async Task Step_ShouldReportFailedWhenModelThrows() {
        ThrowingModel model = new();
        ProbeRunner runner = new(
            new BestArchive<Flt64, Flt64>(10), NeverStop.Instance,
            IIntoValue<Flt64>.Identity);
        var session = new RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64>(
            runner,
            new SearchProblem<Flt64, Flt64>(
                "p", "p", model,
                SearchCodecs.Flt64, SearchCodecs.Flt64,
                (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
                validSolution: _ => true,
                admissible: _ => true),
            SearchCodecs.Individual(BuildProblem()));

        Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await session.Step(new SearchSlice(new SearchBudget(100L), 10L, 1L));

        SearchStepResult<Flt64, Flt64> report =
            ((Ok<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Termination.Should().Be(SearchTermination.Failed);
        report.Diagnostics.Should().NotBeNull();
        report.Diagnostics!.Count.Should().BeGreaterThan(0);
        session.Initialized.Should().BeFalse();
    }

    /// <summary>**快照与恢复往返一致** / A snapshot and restore round-trip.</summary>
    [Fact]
    public async Task Snapshot_ShouldRoundTrip() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session = BuildSession();
        await session.Step(new SearchSlice(new SearchBudget(100L), 100L, 2L));
        int iteration = session.Iteration.Current;
        int archiveSize = session.Archive.Count;

        Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> snapshot = session.Snapshot();
        snapshot.Should().BeOfType<Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>>();

        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> restored = BuildSession();
        Result<Success, ErrorCode, Error<ErrorCode>> restore = restored.Restore(
            ((Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>)snapshot).Value);

        restore.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        restored.Iteration.Current.Should().Be(iteration);
        restored.Archive.Count.Should().Be(archiveSize);
        restored.Initialized.Should().BeTrue();
    }

    /// <summary>**未初始化时快照包含真实的 JSON null** / An uninitialized snapshot carries a real JSON null.</summary>
    /// <remarks>
    /// **不能用 `default(JsonElement)`**：它的 `ValueKind` 是 `Undefined`，序列化时会抛 `InvalidOperationException`。
    /// **`default(JsonElement)` must not be used**: its `ValueKind` is `Undefined` and serializing it throws
    /// `InvalidOperationException`.
    /// </remarks>
    [Fact]
    public void Snapshot_ShouldEncodeNullState() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session = BuildSession();

        Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> snapshot = session.Snapshot();

        SearchSessionSnapshot value =
            ((Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>)snapshot).Value;
        value.Initialized.Should().BeFalse();
        value.State.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        value.State.GetRawText().Should().Be("null");
    }

    /// <summary>**缺少状态编解码器时快照失败** / A snapshot fails without a state codec.</summary>
    [Fact]
    public void Snapshot_ShouldRejectMissingCodec() {
        ProbeRunner runner = new(
            new BestArchive<Flt64, Flt64>(10), NeverStop.Instance,
            IIntoValue<Flt64>.Identity);
        var session = new RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64>(
            runner, BuildProblem(), codec: null);

        Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> snapshot = session.Snapshot();
        snapshot.Should().BeOfType<Failed<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>>();
        ((Failed<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>)snapshot).Error.Message
            .Should().Contain("state codec");
    }

    /// <summary>**恢复拒绝负计数** / A restore rejects negative counters.</summary>
    [Fact]
    public void Restore_ShouldRejectNegativeCounters() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session = BuildSession();
        SearchSessionSnapshot snapshot = new(
            false, -1L, 0L,
            System.Text.Json.JsonDocument.Parse("null").RootElement.Clone(),
            System.Text.Json.JsonDocument.Parse("[]").RootElement.Clone());

        session.Restore(snapshot)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**恢复拒绝不支持的快照版本** / A restore rejects an unsupported snapshot version.</summary>
    [Fact]
    public void Restore_ShouldRejectUnsupportedVersion() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session = BuildSession();
        SearchSessionSnapshot snapshot = new(
            false, 0L, 0L,
            System.Text.Json.JsonDocument.Parse("null").RootElement.Clone(),
            System.Text.Json.JsonDocument.Parse("[]").RootElement.Clone(),
            Version: 2);

        session.Restore(snapshot)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**恢复拒绝约束能力不匹配** / A restore rejects a constraint-capability mismatch.</summary>
    [Fact]
    public void Restore_ShouldRejectConstraintMismatch() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session = BuildSession();
        SearchSessionSnapshot snapshot = new(
            false, 0L, 0L,
            System.Text.Json.JsonDocument.Parse("null").RootElement.Clone(),
            System.Text.Json.JsonDocument.Parse("[]").RootElement.Clone(),
            Constraints: System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone());

        session.Restore(snapshot)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**无导入器时不支持导入** / Imports are unsupported without an importer.</summary>
    [Fact]
    public async Task Import_ShouldRejectWhenUnsupported() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session = BuildSession();
        session.SupportsImport.Should().BeFalse();

        (await session.ImportSolutions(
                Array.Empty<Solution<Flt64>>(),
                new SearchSlice(new SearchBudget(100L), 10L))
            ).Should().BeOfType<Failed<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**未初始化时导入被拒绝** / Importing before initialization is rejected.</summary>
    [Fact]
    public async Task Import_ShouldRequireInitialization() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session =
            BuildSession(importer: (state, candidates) => Results.Ok(state));

        (await session.ImportSolutions(
                Array.Empty<Solution<Flt64>>(),
                new SearchSlice(new SearchBudget(100L), 10L))
            ).Should().BeOfType<Failed<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**导入在已初始化的会话上生效** / An import takes effect on an initialized session.</summary>
    [Fact]
    public async Task Import_ShouldReturnCandidates() {
        RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> session =
            BuildSession(importer: (state, candidates) => Results.Ok(state));
        await session.Step(new SearchSlice(new SearchBudget(100L), 100L, 1L));
        int before = session.Iteration.Current;

        Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await session.ImportSolutions(
                new[] { new Solution<Flt64>(new[] { new Flt64(0.25) }) },
                new SearchSlice(new SearchBudget(100L), 10L));

        result.Should().BeOfType<Ok<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
        SearchStepResult<Flt64, Flt64> report =
            ((Ok<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Steps.Should().Be(0L);
        report.Candidates.Should().HaveCount(1);
        report.Evaluations.Should().Be(1L);
        session.Iteration.Current.Should().Be(before,
            "导入不是一次迭代 / an import is not an iteration");
    }

    /// <summary>**独立运行在"立即停止"的判据下仍走完一步** / A standalone run still takes one step under an immediate-stop criterion.</summary>
    /// <remarks>
    /// 写成 `while (!Stopping.ShouldStop(...))` 会让"立即停止"在第一次迭代之前退出，**于是档案永远是空的**。
    /// Writing `while (!Stopping.ShouldStop(...))` makes "stop immediately" exit before the first iteration, **so the archive is always
    /// empty**.
    /// </remarks>
    [Fact]
    public async Task Run_ShouldTakeOneStepBeforeStopping() {
        ProbeRunner runner = new(
            new BestArchive<Flt64, Flt64>(10), AlwaysStop.Instance,
            IIntoValue<Flt64>.Identity);

        Result<IReadOnlyList<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> run =
            await runner.Run(BuildProblem().Model);

        run.Should().BeOfType<Ok<IReadOnlyList<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
        ((Ok<IReadOnlyList<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)run).Value
            .Should().NotBeEmpty("立即停止也必须先走一步 / an immediate stop must still take one step");
    }

    /// <summary>**步进报告失败时独立运行返回失败** / A standalone run returns a failure when a step reports failure.</summary>
    /// <remarks>
    /// **这条测试不能依赖"模型抛异常"**。`Run` 自己构造的独立问题用的是默认维度判定，而测试模型没有注册任何 token，
    /// 于是评估在调用模型之前就被判为"维度不符"，模型**永远不会抛**——配合 `NeverStop` 会变成死循环，
    /// **测试主机会因为内存耗尽而崩溃，而不是报告失败**。改用"一步就直接失败"的运行器来覆盖这条路径。
    /// **This test must not rely on "the model throws".** The standalone problem `Run` builds uses the default dimension predicate, and
    /// the test model registers no tokens, so evaluation is judged a "dimension mismatch" before the model is called and the model
    /// **never throws** — with `NeverStop` that becomes an infinite loop, **and the test host crashes from memory exhaustion instead of
    /// reporting a failure**. A runner that fails on its very first step covers the path instead.
    /// </remarks>
    [Fact]
    public async Task Run_ShouldFailWhenStepFails() {
        BrokenRunner runner = new(
            new BestArchive<Flt64, Flt64>(10), NeverStop.Instance, IIntoValue<Flt64>.Identity);

        Result<IReadOnlyList<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> run =
            await runner.Run(new CountingModel());

        run.Should()
            .BeOfType<Failed<IReadOnlyList<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**独立运行的快照不可用** / A standalone run exposes no checkpoints.</summary>
    /// <remarks>
    /// 不可用编解码器必须一律失败；返回一个"能编码成 null"的编解码器会让快照"成功"，**而恢复时才发现没有任何状态被保存**。
    /// The unavailable codec must always fail; one that "encodes to null" makes the snapshot "succeed", **and only the restore reveals
    /// that no state was saved**.
    /// </remarks>
    [Fact]
    public void UnavailableCodec_ShouldAlwaysFail() {
        UnavailableCodec<Flt64>.Instance.Encode(new Flt64(1.0))
            .Should().BeOfType<Failed<System.Text.Json.JsonElement, ErrorCode, Error<ErrorCode>>>();
        UnavailableCodec<Flt64>.Instance.Decode(
                System.Text.Json.JsonDocument.Parse("null").RootElement.Clone())
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造会话 / Build a session.</summary>
    /// <param name="importer">导入器 / The importer.</param>
    /// <returns>会话 / The session.</returns>
    private static RunnerSearchSession<IIndividual<Flt64, Flt64>, Flt64, Flt64> BuildSession(
        Func<IIndividual<Flt64, Flt64>, IReadOnlyList<IIndividual<Flt64, Flt64>>,
            Result<IIndividual<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>? importer = null) =>
        new(new ProbeRunner(
                new BestArchive<Flt64, Flt64>(10), NeverStop.Instance,
                IIntoValue<Flt64>.Identity),
            BuildProblem(), SearchCodecs.Individual(BuildProblem()), importer);

    /// <summary>构造问题 / Build a problem.</summary>
    /// <returns>问题 / The problem.</returns>
    private static SearchProblem<Flt64, Flt64> BuildProblem() =>
        new("probe", "probe", new CountingModel(),
            SearchCodecs.Flt64, SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            // **必须显式给出维度与可行判定**：默认判定要求解长度等于模型的 token 数，而测试模型没有注册任何 token，
            // 于是一维解会被判为"维度不符"，评估静默返回默认值、候选集合恒为空。
            // **The dimension and feasibility predicates must be explicit**: the defaults require the solution length to equal the
            // model's token count, and the test model registers no tokens, so a one-dimensional solution counts as "dimension mismatch",
            // evaluation silently returns the default, and the candidate set stays empty.
            validSolution: _ => true,
            admissible: _ => true);

    /// <summary>
    /// 单调推进的探针运行器 / A probe runner advancing monotonically.
    /// </summary>
    /// <remarks>
    /// **每一步都真的走一次模型评估**。第一版让 `Initialize`/`Advance` 直接凭空造个体，结果"额度用尽"这条路径永远走不到
    /// （`Step` 报告的评估数恒为 0），而"模型抛异常"那条在 `NeverStop` 下变成死循环——**测试会挂住而不是失败**。
    /// **Every step really goes through one model evaluation.** The first version let `Initialize`/`Advance` conjure individuals from
    /// nothing, so the "allowance exhausted" path was unreachable (the reported evaluation count was always 0) and the "model throws"
    /// case became an infinite loop under `NeverStop` — **the test hung instead of failing**.
    /// </remarks>
    private sealed class ProbeRunner : AbstractRunner<IIndividual<Flt64, Flt64>, Flt64, Flt64> {
        public ProbeRunner(
            IArchive<Flt64, Flt64> archive,
            IStoppingCriterion stopping,
            IIntoValue<Flt64> converter)
            : base(archive, stopping, converter) { }

        protected override Task<IIndividual<Flt64, Flt64>> Initialize(
            IAbstractCallBackModelInterface<object?, Flt64, Flt64> model, Iteration iteration) =>
            Task.FromResult(Evaluate(model, 0.0));

        protected override Task<IIndividual<Flt64, Flt64>> Advance(
            IAbstractCallBackModelInterface<object?, Flt64, Flt64> model,
            Iteration iteration,
            IIndividual<Flt64, Flt64> state) =>
            Task.FromResult(Evaluate(model, state.Objective.ToDouble() + 1.0));

        protected override IReadOnlyList<IIndividual<Flt64, Flt64>> MembersOf(
            IIndividual<Flt64, Flt64> state) =>
            new[] { state };

        private static IIndividual<Flt64, Flt64> Evaluate(
            IAbstractCallBackModelInterface<object?, Flt64, Flt64> model, double value) {
            var solution = new Solution<Flt64>(new[] { new Flt64(value) });
            Flt64? objective = model.Objective(solution.Values);
            return new SolutionWithFitness<Flt64, Flt64>(
                solution.Values, objective ?? new Flt64(double.PositiveInfinity), 0.0);
        }
    }

    /// <summary>
    /// 第一步就失败的运行器 / A runner that fails on its very first step.
    /// </summary>
    private sealed class BrokenRunner : AbstractRunner<IIndividual<Flt64, Flt64>, Flt64, Flt64> {
        public BrokenRunner(
            IArchive<Flt64, Flt64> archive,
            IStoppingCriterion stopping,
            IIntoValue<Flt64> converter)
            : base(archive, stopping, converter) { }

        protected override Task<IIndividual<Flt64, Flt64>> Initialize(
            IAbstractCallBackModelInterface<object?, Flt64, Flt64> model, Iteration iteration) =>
            throw new InvalidOperationException("runner broke");

        protected override Task<IIndividual<Flt64, Flt64>> Advance(
            IAbstractCallBackModelInterface<object?, Flt64, Flt64> model,
            Iteration iteration,
            IIndividual<Flt64, Flt64> state) => Task.FromResult(state);

        protected override IReadOnlyList<IIndividual<Flt64, Flt64>> MembersOf(
            IIndividual<Flt64, Flt64> state) => new[] { state };
    }

    /// <summary>计数模型 / A counting model.</summary>
    private sealed class CountingModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
        public int Calls { get; private set; }

        /// <inheritdoc/>
        public Flt64 DefaultObjective => new(double.PositiveInfinity);

        /// <inheritdoc/>
        public Fuookami.Ospf.Core.Model.Basic.ObjectCategory ObjectCategory =>
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum;

        /// <inheritdoc/>
        public Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<Flt64> Tokens { get; } =
            new ConcurrentAutoTokenTable<Flt64>(
                new Fuookami.Ospf.Math.Symbol.LinearCategory(),
                Array.Empty<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints =>
            new (Func<IReadOnlyList<Flt64>, bool?>, string)[] { (values => true, "c1") };

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, object?> Extractor, string Name)> ObjectiveFunctions =>
            new (Func<IReadOnlyList<Flt64>, object?>, string)[] { (values => null, "f1") };

        /// <inheritdoc/>
        public Flt64 Operation(Flt64 lhs, Flt64 rhs) => lhs;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue() => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue(object? obj) => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 Objective(IReadOnlyList<Flt64> solution) {
            Calls++;
            return solution.Count == 0 ? default : solution[0];
        }

        /// <inheritdoc/>
        public Order CompareObjective(Flt64 lhs, Flt64 rhs) =>
            OrderHelpers.OrderOf(lhs.ToDouble().CompareTo(rhs.ToDouble()));

        /// <inheritdoc/>
        public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => true;

        /// <inheritdoc/>
        public void Flush() { }

        /// <inheritdoc/>
        public void Dispose() { }
    }

    /// <summary>抛异常模型 / A throwing model.</summary>
    private sealed class ThrowingModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
        /// <inheritdoc/>
        public Flt64 DefaultObjective => new(double.PositiveInfinity);

        /// <inheritdoc/>
        public Fuookami.Ospf.Core.Model.Basic.ObjectCategory ObjectCategory =>
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum;

        /// <inheritdoc/>
        public Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<Flt64> Tokens { get; } =
            new ConcurrentAutoTokenTable<Flt64>(
                new Fuookami.Ospf.Math.Symbol.LinearCategory(),
                Array.Empty<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints =>
            new (Func<IReadOnlyList<Flt64>, bool?>, string)[] { (values => true, "c1") };

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, object?> Extractor, string Name)> ObjectiveFunctions =>
            new (Func<IReadOnlyList<Flt64>, object?>, string)[] { (values => null, "f1") };

        /// <inheritdoc/>
        public Flt64 Operation(Flt64 lhs, Flt64 rhs) => lhs;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue() => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue(object? obj) => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 Objective(IReadOnlyList<Flt64> solution) =>
            throw new InvalidOperationException("model broke");

        /// <inheritdoc/>
        public Order CompareObjective(Flt64 lhs, Flt64 rhs) =>
            OrderHelpers.OrderOf(lhs.ToDouble().CompareTo(rhs.ToDouble()));

        /// <inheritdoc/>
        public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => true;

        /// <inheritdoc/>
        public void Flush() { }

        /// <inheritdoc/>
        public void Dispose() { }
    }

    /// <summary>立即停止 / Stop immediately.</summary>
    private sealed class AlwaysStop : IStoppingCriterion {
        public static AlwaysStop Instance { get; } = new();

        /// <inheritdoc/>
        public bool ShouldStop(Iteration iteration) => true;
    }
}
