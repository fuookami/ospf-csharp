#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>
/// 逻辑 Benders 迭代引擎：线性主问题、CP 子问题、领域割和统一证明门控。
/// Logic-based Benders engine over a linear master, a CP subproblem, domain cuts, and proof gates.
/// </summary>
public sealed class LogicBasedBendersEngine {
    private readonly BendersMasterProblemSolver _masterSolver;
    private readonly IConstraintProgrammingSolver _subproblemSolver;
    private readonly IBendersVariableBinding _binding;
    private readonly IBendersCutOracle _cutOracle;
    private readonly LogicBasedBendersOptions _options;

    /// <summary>创建逻辑 Benders 引擎 / Create a logic-based Benders engine.</summary>
    /// <param name="masterSolver">主问题求解适配器 / Master-solve adapter.</param>
    /// <param name="subproblemSolver">CP 子问题求解器 / CP subproblem solver.</param>
    /// <param name="binding">主问题到 CP 的绑定 / Master-to-CP binding.</param>
    /// <param name="cutOracle">领域割生成器 / Domain cut oracle.</param>
    /// <param name="options">引擎选项 / Engine options.</param>
    public LogicBasedBendersEngine(
        BendersMasterProblemSolver masterSolver,
        IConstraintProgrammingSolver subproblemSolver,
        IBendersVariableBinding binding,
        IBendersCutOracle? cutOracle = null,
        LogicBasedBendersOptions? options = null) {
        ArgumentNullException.ThrowIfNull(masterSolver);
        ArgumentNullException.ThrowIfNull(subproblemSolver);
        ArgumentNullException.ThrowIfNull(binding);
        _masterSolver = masterSolver;
        _subproblemSolver = subproblemSolver;
        _binding = binding;
        _cutOracle = cutOracle ?? new BinaryNoGoodCutOracle();
        _options = options ?? new LogicBasedBendersOptions();
    }

    /// <summary>求解逻辑 Benders 模型 / Solve a logic-based Benders model.</summary>
    /// <param name="master">可变线性主问题 / Mutable linear master.</param>
    /// <param name="subproblem">静态 CP 子问题 / Static CP subproblem.</param>
    /// <returns>最终报告或结构化求解错误 / Final report or a structured solve error.</returns>
    public async Task<Result<
        LogicBasedBendersReport,
        ErrorCode,
        Error<ErrorCode>>> SolveAsync(
        LinearMetaModel<Flt64> master,
        ConstraintProgrammingModel subproblem) {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(subproblem);
        string? optionError = ValidateOptions(_options);
        if (optionError is not null) {
            return Failure<LogicBasedBendersReport>(ErrorCode.IllegalArgument, optionError);
        }
        if (master.IsClosed) {
            return Failure<LogicBasedBendersReport>(
                ErrorCode.ApplicationStopped,
                "主问题模型已关闭 / Master model is closed");
        }
        if (subproblem.IsClosed) {
            return Failure<LogicBasedBendersReport>(
                ErrorCode.ApplicationStopped,
                "CP 子问题模型已关闭 / CP subproblem model is closed");
        }

        Result<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>> snapshotResult =
            subproblem.Snapshot();
        if (snapshotResult is not Ok<
                ConstraintProgrammingModelSnapshot,
                ErrorCode,
                Error<ErrorCode>> snapshotOk) {
            return Propagate<LogicBasedBendersReport, ConstraintProgrammingModelSnapshot>(
                snapshotResult);
        }

        Result<
            IConstraintProgrammingSession,
            ErrorCode,
            Error<ErrorCode>> sessionResult = _subproblemSolver.CreateSession(
                model: subproblem,
                options: _options.ConstraintProgrammingOptions);
        if (sessionResult is not Ok<
                IConstraintProgrammingSession,
                ErrorCode,
                Error<ErrorCode>> sessionOk) {
            return Propagate<LogicBasedBendersReport, IConstraintProgrammingSession>(
                sessionResult);
        }

        using IConstraintProgrammingSession session = sessionOk.Value;
        return await IterateAsync(
            master: master,
            subproblem: subproblem,
            snapshot: snapshotOk.Value,
            session: session).ConfigureAwait(false);
    }

    /// <summary>求解并返回 Core 统一报告 / Solve and return the Core unified report.</summary>
    /// <param name="master">可变线性主问题 / Mutable linear master.</param>
    /// <param name="subproblem">静态 CP 子问题 / Static CP subproblem.</param>
    /// <returns>统一报告或结构化求解错误 / Unified report or a structured solve error.</returns>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SolveReportAsync(
        LinearMetaModel<Flt64> master,
        ConstraintProgrammingModel subproblem) {
        Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> result =
            await SolveAsync(
                master: master,
                subproblem: subproblem).ConfigureAwait(false);
        return result switch {
            Ok<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok(ok.Value.ToSolveReport()),
            Failed<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<SolveReport<Flt64>>(failed.Error),
            Fatal<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<SolveReport<Flt64>>(
                ErrorCode.ApplicationError,
                "逻辑 Benders 报告状态无效 / Invalid logic-based Benders report state"),
        };
    }

    private async Task<Result<
        LogicBasedBendersReport,
        ErrorCode,
        Error<ErrorCode>>> IterateAsync(
        LinearMetaModel<Flt64> master,
        ConstraintProgrammingModel subproblem,
        ConstraintProgrammingModelSnapshot snapshot,
        IConstraintProgrammingSession session) {
        var acceptedCuts = new List<BendersMasterCut>();
        var traces = new List<BendersIterationTrace>();
        var knownCuts = new HashSet<string>(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();
        bool hasSubproblemObjective = snapshot.Objectives.Count > 0;
        int stallIterations = 0;
        SolveReport<Flt64>? lastMaster = null;
        BendersSubproblemAssignment? lastAssignment = null;
        LogicBasedBendersSubproblemResult? lastSubproblem = null;
        int firstIteration = 0;

        if (_options.ResumeState is not null) {
            BendersResumeState resume = _options.ResumeState;
            Result<Success, ErrorCode, Error<ErrorCode>> resumeEvidence =
                BendersCheckpointCodec.ValidateResumeEvidence(
                    state: resume,
                    snapshot: snapshot,
                    proofMode: _options.ProofMode);
            if (resumeEvidence is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<LogicBasedBendersReport, Success>(resumeEvidence);
            }
            if (BendersCheckpointCodec.HasPersistedMasterState(resume)) {
                if (_options.ResumeStateApplier is null) {
                    return Failure<LogicBasedBendersReport>(
                        ErrorCode.IllegalArgument,
                        "Benders checkpoint 包含主问题状态但未提供恢复适配器 / " +
                        "Benders checkpoint contains master state without a resume applier");
                }
                Result<BendersMasterResumeEvidence, ErrorCode, Error<ErrorCode>> applied =
                    _options.ResumeStateApplier(master, resume);
                if (applied is not Ok<
                        BendersMasterResumeEvidence,
                        ErrorCode,
                        Error<ErrorCode>> appliedOk) {
                    return Propagate<LogicBasedBendersReport, BendersMasterResumeEvidence>(
                        applied);
                }
                if (!BendersCheckpointCodec.EvidenceMatches(appliedOk.Value, resume)) {
                    return Failure<LogicBasedBendersReport>(
                        ErrorCode.ORSolutionInvalid,
                        "Benders 主问题恢复证据与 checkpoint 不一致 / " +
                        "Benders master resume evidence disagrees with the checkpoint");
                }
            }

            var resumedTraces = new List<BendersIterationTrace>();
            int previousIteration = -1;
            foreach (string encodedTrace in resume.Trace) {
                Result<BendersIterationTrace, ErrorCode, Error<ErrorCode>> decodedTrace =
                    BendersCheckpointCodec.DecodeTrace(encodedTrace);
                if (decodedTrace is not Ok<
                        BendersIterationTrace,
                        ErrorCode,
                        Error<ErrorCode>> traceOk) {
                    return Propagate<LogicBasedBendersReport, BendersIterationTrace>(
                        decodedTrace);
                }
                if (traceOk.Value.Iteration <= previousIteration ||
                    traceOk.Value.Iteration >= resume.Iteration) {
                    return Failure<LogicBasedBendersReport>(
                        ErrorCode.IllegalArgument,
                        "Benders checkpoint trace 顺序或迭代范围无效 / " +
                        "Benders checkpoint trace order or iteration range is invalid");
                }
                if (traceOk.Value.ProofMode != _options.ProofMode) {
                    return Failure<LogicBasedBendersReport>(
                        ErrorCode.IllegalArgument,
                        "Benders checkpoint trace proof mode 与当前引擎不一致 / " +
                        "Benders checkpoint trace proof mode disagrees with the active engine");
                }
                previousIteration = traceOk.Value.Iteration;
                resumedTraces.Add(traceOk.Value);
            }

            Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> validatedCuts =
                ValidateCuts(resume.Cuts, knownCuts);
            if (validatedCuts is not Ok<
                    List<BendersMasterCut>,
                    ErrorCode,
                    Error<ErrorCode>> validatedCutsOk) {
                return Propagate<LogicBasedBendersReport, List<BendersMasterCut>>(
                    validatedCuts);
            }
            Result<int, ErrorCode, Error<ErrorCode>> seeded = AddCuts(
                master: master,
                cuts: validatedCutsOk.Value,
                allCuts: acceptedCuts,
                knownCuts: knownCuts);
            if (seeded is not Ok<int, ErrorCode, Error<ErrorCode>>) {
                return Propagate<LogicBasedBendersReport, int>(seeded);
            }
            traces.AddRange(resumedTraces);
            firstIteration = resume.Iteration;
            if (firstIteration < 0 || firstIteration >= _options.MaxIterations) {
                return Failure<LogicBasedBendersReport>(
                    ErrorCode.IllegalArgument,
                    "Benders checkpoint iteration 超出当前引擎范围 / " +
                    "Benders checkpoint iteration is outside the active engine range");
            }
        }

        for (int iteration = firstIteration; iteration < _options.MaxIterations; iteration++) {
            if (_options.CancellationToken.IsCancellationRequested) {
                return Results.Ok(Report(
                    problemStatus: ProblemStatus.Unknown,
                    terminationReason: TerminationReason.Cancelled,
                    proofStatus: ProofStatus.None,
                    master: lastMaster,
                    assignment: lastAssignment,
                    subproblem: lastSubproblem,
                    cuts: acceptedCuts,
                    traces: traces,
                    diagnostics: new[] {
                        new SolveIssue(
                            Code: "benders-cancelled",
                            Category: SolveIssueCategory.Backend,
                            Message: "Benders 求解已取消 / Benders solve was cancelled"),
                    }));
            }

            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> masterResult =
                await _masterSolver(master).ConfigureAwait(false);
            if (masterResult is Failed<
                    SolveReport<Flt64>,
                    ErrorCode,
                    Error<ErrorCode>> masterFailure) {
                if (masterFailure.Code == ErrorCode.ORModelInfeasible) {
                    return Results.Ok(Report(
                        problemStatus: ProblemStatus.Infeasible,
                        terminationReason: TerminationReason.Completed,
                        proofStatus: ProofStatus.Claimed,
                        master: lastMaster,
                        assignment: lastAssignment,
                        subproblem: lastSubproblem,
                        cuts: acceptedCuts,
                        traces: traces));
                }
                return Results.Failed<LogicBasedBendersReport>(masterFailure.Error);
            }
            if (masterResult is Fatal<
                    SolveReport<Flt64>,
                    ErrorCode,
                    Error<ErrorCode>> masterFatal) {
                return new Fatal<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>>(
                    masterFatal.Errors);
            }
            if (masterResult is not Ok<
                    SolveReport<Flt64>,
                    ErrorCode,
                    Error<ErrorCode>> masterOk) {
                return Failure<LogicBasedBendersReport>(
                    ErrorCode.ApplicationError,
                    "主问题结果状态无效 / Invalid master result state");
            }

            SolveReport<Flt64> masterOutput = masterOk.Value;
            lastMaster = masterOutput;
            if (masterOutput.ProblemStatus == ProblemStatus.Infeasible) {
                return Results.Ok(Report(
                    problemStatus: ProblemStatus.Infeasible,
                    terminationReason: masterOutput.TerminationReason,
                    proofStatus: masterOutput.Proof.Status,
                    master: masterOutput,
                    assignment: lastAssignment,
                    subproblem: lastSubproblem,
                    cuts: acceptedCuts,
                    traces: traces));
            }
            if (masterOutput.ProblemStatus == ProblemStatus.Unbounded) {
                return Failure<LogicBasedBendersReport>(
                    ErrorCode.ORModelUnbounded,
                    "Benders 主问题无界 / Benders master is unbounded");
            }
            if (masterOutput.ProblemStatus == ProblemStatus.InfeasibleOrUnbounded) {
                return Results.Ok(Report(
                    problemStatus: ProblemStatus.Unknown,
                    terminationReason: masterOutput.TerminationReason,
                    proofStatus: ProofStatus.None,
                    master: masterOutput,
                    assignment: lastAssignment,
                    subproblem: lastSubproblem,
                    cuts: acceptedCuts,
                    traces: traces,
                    diagnostics: new[] {
                        new SolveIssue(
                            Code: "benders-master-inconclusive",
                            Category: SolveIssueCategory.Backend,
                            Message: "主问题返回不可行或无界，无法继续分解 / " +
                                "Master returned infeasible-or-unbounded; decomposition cannot continue"),
                    }));
            }
            if (_options.ProofMode == BendersProofMode.Exact &&
                !IsMasterOptimal(masterOutput)) {
                return Results.Ok(Report(
                    problemStatus: ProblemStatus.Unknown,
                    terminationReason: TerminationReason.BackendFailure,
                    proofStatus: ProofStatus.None,
                    master: masterOutput,
                    assignment: lastAssignment,
                    subproblem: lastSubproblem,
                    cuts: acceptedCuts,
                    traces: traces,
                    diagnostics: new[] {
                        new SolveIssue(
                            Code: "benders-master-not-proven-optimal",
                            Category: SolveIssueCategory.Backend,
                            Message: "Exact 模式要求主问题最优证明 / " +
                                "Exact mode requires a proven master optimum"),
                    }));
            }
            if (masterOutput.Solution is null) {
                return Results.Ok(Report(
                    problemStatus: ProblemStatus.Unknown,
                    terminationReason: masterOutput.TerminationReason,
                    proofStatus: ProofStatus.None,
                    master: masterOutput,
                    assignment: lastAssignment,
                    subproblem: lastSubproblem,
                    cuts: acceptedCuts,
                    traces: traces,
                    diagnostics: new[] {
                        new SolveIssue(
                            Code: "benders-master-without-incumbent",
                            Category: SolveIssueCategory.Backend,
                            Message: "主问题没有 incumbent / Master has no incumbent"),
                    }));
            }

            Result<
                IConstraintProgrammingValueSource,
                ErrorCode,
                Error<ErrorCode>> sourceResult = _options.MasterSolutionSource is null
                ? BendersMasterSolutionValueSource.Create(
                    master: master,
                    report: masterOutput,
                    variables: _options.MasterVariables)
                : _options.MasterSolutionSource(masterOutput);
            if (sourceResult is not Ok<
                    IConstraintProgrammingValueSource,
                    ErrorCode,
                    Error<ErrorCode>> sourceOk) {
                return Propagate<LogicBasedBendersReport, IConstraintProgrammingValueSource>(
                    sourceResult);
            }
            Result<
                BendersSubproblemAssignment,
                ErrorCode,
                Error<ErrorCode>> assignmentResult = _binding.Bind(
                    masterSolution: sourceOk.Value,
                    subproblem: subproblem);
            if (assignmentResult is not Ok<
                    BendersSubproblemAssignment,
                    ErrorCode,
                    Error<ErrorCode>> assignmentOk) {
                return Propagate<LogicBasedBendersReport, BendersSubproblemAssignment>(
                    assignmentResult);
            }
            BendersSubproblemAssignment assignment = assignmentOk.Value;
            lastAssignment = assignment;

            Result<
                ConstraintProgrammingSolverOutput,
                ErrorCode,
                Error<ErrorCode>> subproblemResult = await session.SolveAsync(
                assumptions: assignment.Assumptions,
                fixedValues: assignment.FixedValues,
                hints: null,
                cancellationToken: _options.CancellationToken).ConfigureAwait(false);
            if (subproblemResult is not Ok<
                    ConstraintProgrammingSolverOutput,
                    ErrorCode,
                    Error<ErrorCode>> subproblemOk) {
                return Propagate<LogicBasedBendersReport, ConstraintProgrammingSolverOutput>(
                    subproblemResult);
            }

            LogicBasedBendersSubproblemResult classified =
                ClassifySubproblem(subproblemOk.Value, assignment);
            lastSubproblem = classified;
            switch (classified) {
                case FeasibleSubproblemResult feasible: {
                    Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>>? terminal =
                        HandleFeasible(
                            master: master,
                            subproblem: subproblem,
                            masterOutput: masterOutput,
                            feasible: feasible,
                            iteration: iteration,
                            hasSubproblemObjective: hasSubproblemObjective,
                            acceptedCuts: acceptedCuts,
                            knownCuts: knownCuts,
                            traces: traces,
                            stopwatch: stopwatch);
                    if (terminal is not null) {
                        return terminal;
                    }
                    stallIterations = 0;
                    break;
                }

                case InfeasibleSubproblemResult infeasible: {
                    Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>>? terminal =
                        HandleInfeasible(
                            master: master,
                            subproblem: subproblem,
                            masterOutput: masterOutput,
                            infeasible: infeasible,
                            iteration: iteration,
                            acceptedCuts: acceptedCuts,
                            knownCuts: knownCuts,
                            traces: traces,
                            stopwatch: stopwatch,
                            stallIterations: ref stallIterations);
                    if (terminal is not null) {
                        return terminal;
                    }
                    break;
                }

                case UnknownSubproblemResult unknown: {
                    BendersIterationTrace trace = Trace(
                        iteration: iteration,
                        master: master,
                        masterOutput: masterOutput,
                        subproblemStatus: BendersSubproblemStatus.Unknown,
                        subproblemObjective: null,
                        cutCount: 0,
                        conflictCoreSize: 0,
                        stopwatch: stopwatch);
                    traces.Add(trace);
                    Result<Success, ErrorCode, Error<ErrorCode>>? notifyFailure =
                        Notify(trace);
                    if (notifyFailure is not null) {
                        return Propagate<LogicBasedBendersReport, Success>(notifyFailure);
                    }
                    return Results.Ok(Report(
                        problemStatus: ProblemStatus.Unknown,
                        terminationReason: unknown.TerminationReason,
                        proofStatus: ProofStatus.None,
                        master: masterOutput,
                        assignment: assignment,
                        subproblem: unknown,
                        cuts: acceptedCuts,
                        traces: traces,
                        diagnostics: new[] {
                            new SolveIssue(
                                Code: "benders-subproblem-unknown",
                                Category: SolveIssueCategory.Backend,
                                Message: "CP 子问题未得到证明终态 / " +
                                    "CP subproblem did not reach a proven terminal state"),
                        }));
                }

                default:
                    return Failure<LogicBasedBendersReport>(
                        ErrorCode.ApplicationError,
                        "未知 CP 子问题结果类型 / Unknown CP subproblem result type");
            }
        }

        return Results.Ok(Report(
            problemStatus: ProblemStatus.Unknown,
            terminationReason: TerminationReason.IterationLimit,
            proofStatus: ProofStatus.None,
            master: lastMaster,
            assignment: lastAssignment,
            subproblem: lastSubproblem,
            cuts: acceptedCuts,
            traces: traces,
            diagnostics: new[] {
                new SolveIssue(
                    Code: "benders-iteration-limit",
                    Category: SolveIssueCategory.Backend,
                    Message: "Benders 达到迭代次数上限 / Benders reached the iteration limit"),
            }));
    }

    private Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>>? HandleFeasible(
        LinearMetaModel<Flt64> master,
        ConstraintProgrammingModel subproblem,
        SolveReport<Flt64> masterOutput,
        FeasibleSubproblemResult feasible,
        int iteration,
        bool hasSubproblemObjective,
        List<BendersMasterCut> acceptedCuts,
        HashSet<string> knownCuts,
        List<BendersIterationTrace> traces,
        Stopwatch stopwatch) {
        if (_options.ProofMode == BendersProofMode.Exact &&
            (feasible.Output.ProofStatus != ProofStatus.Verified ||
                feasible.Output.Status != SolverStatus.Optimal)) {
            return Results.Ok(Report(
                problemStatus: ProblemStatus.Unknown,
                terminationReason: TerminationReason.BackendFailure,
                proofStatus: ProofStatus.None,
                master: masterOutput,
                assignment: feasible.Assignment,
                subproblem: feasible,
                cuts: acceptedCuts,
                traces: traces,
                diagnostics: new[] {
                    new SolveIssue(
                        Code: "benders-subproblem-not-proven",
                        Category: SolveIssueCategory.Backend,
                        Message: "Exact 模式要求 CP 子问题已证明最优或不可行 / " +
                            "Exact mode requires the CP subproblem to be proven optimal or infeasible"),
                }));
        }

        Flt64? compatibleObjective = CompatibleObjective(feasible.Output);
        if (_options.ProofMode == BendersProofMode.Exact &&
            hasSubproblemObjective &&
            feasible.Output.ExactObjective is null &&
            feasible.Output.Objective is null &&
            _options.CompleteObjectiveEvaluator is null) {
            return Results.Ok(Report(
                problemStatus: ProblemStatus.Unknown,
                terminationReason: TerminationReason.BackendFailure,
                proofStatus: ProofStatus.None,
                master: masterOutput,
                assignment: feasible.Assignment,
                subproblem: feasible,
                cuts: acceptedCuts,
                traces: traces,
                diagnostics: new[] {
                    new SolveIssue(
                        Code: "benders-subproblem-objective-missing",
                        Category: SolveIssueCategory.Backend,
                        Message: "CP 模型声明了目标但后端未返回可兼容 objective，且未提供完整目标求值契约 / " +
                            "The CP model declares an objective but the backend returned no compatible objective and no complete objective evaluator was provided"),
                }));
        }

        var context = new BendersCutContext(
            Master: master,
            Subproblem: subproblem,
            Assignment: feasible.Assignment,
            Iteration: iteration,
            ProofMode: _options.ProofMode);
        Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> generated =
            _cutOracle.OptimalityCuts(feasible, context);
        if (generated is not Ok<
                List<BendersMasterCut>,
                ErrorCode,
                Error<ErrorCode>> generatedOk) {
            return Propagate<LogicBasedBendersReport, List<BendersMasterCut>>(generated);
        }
        Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> validated =
            ValidateCuts(generatedOk.Value, knownCuts);
        if (validated is not Ok<
                List<BendersMasterCut>,
                ErrorCode,
                Error<ErrorCode>> validatedOk) {
            return Propagate<LogicBasedBendersReport, List<BendersMasterCut>>(validated);
        }

        List<BendersMasterCut> newCuts = validatedOk.Value;
        BendersIterationTrace trace = Trace(
            iteration: iteration,
            master: master,
            masterOutput: masterOutput,
            subproblemStatus: BendersSubproblemStatus.Feasible,
            subproblemObjective: compatibleObjective,
            cutCount: newCuts.Count,
            conflictCoreSize: 0,
            stopwatch: stopwatch);
        traces.Add(trace);
        Result<Success, ErrorCode, Error<ErrorCode>>? notifyFailure = Notify(trace);
        if (notifyFailure is not null) {
            return Propagate<LogicBasedBendersReport, Success>(notifyFailure);
        }

        if (newCuts.Count == 0) {
            if (_options.ProofMode == BendersProofMode.Exact && hasSubproblemObjective) {
                bool hasVerifiedOptimalityCut = acceptedCuts.Any(cut =>
                    cut.Kind == BendersCutKind.Optimality &&
                    cut.Validity == BendersCutValidity.Global &&
                    cut.ProofStatus == ProofStatus.Verified);
                if (!hasVerifiedOptimalityCut) {
                    return Results.Ok(Report(
                        problemStatus: ProblemStatus.Feasible,
                        terminationReason: TerminationReason.IterationLimit,
                        proofStatus: ProofStatus.None,
                        master: masterOutput,
                        assignment: feasible.Assignment,
                        subproblem: feasible,
                        cuts: acceptedCuts,
                        traces: traces,
                        diagnostics: new[] {
                            new SolveIssue(
                                Code: "benders-no-optimality-proof",
                                Category: SolveIssueCategory.Unsupported,
                                Message: "优化型 CP 子问题没有全局有效 optimality proof / " +
                                    "The optimization CP subproblem has no globally valid optimality proof"),
                        }));
                }
                BendersCompleteObjectiveEvaluator? evaluator = _options.CompleteObjectiveEvaluator;
                if (evaluator is null) {
                    return Results.Ok(Report(
                        problemStatus: ProblemStatus.Feasible,
                        terminationReason: TerminationReason.IterationLimit,
                        proofStatus: ProofStatus.None,
                        master: masterOutput,
                        assignment: feasible.Assignment,
                        subproblem: feasible,
                        cuts: acceptedCuts,
                        traces: traces,
                        diagnostics: new[] {
                            new SolveIssue(
                                Code: "benders-objective-contract-missing",
                                Category: SolveIssueCategory.Unsupported,
                                Message: "Exact 模式缺少完整 Benders 目标评估契约 / " +
                                    "Exact mode requires a complete Benders objective evaluator"),
                        }));
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> objectiveResult = evaluator(
                    masterOutput,
                    feasible.Assignment,
                    feasible.Output);
                if (objectiveResult is not Ok<
                        Flt64,
                        ErrorCode,
                        Error<ErrorCode>> objectiveOk) {
                    return Propagate<LogicBasedBendersReport, Flt64>(objectiveResult);
                }
                if (!double.IsFinite(objectiveOk.Value.ToDouble())) {
                    return Failure<LogicBasedBendersReport>(
                        ErrorCode.OREngineSolvingException,
                        "完整 Benders 目标非有限值 / Complete Benders objective is non-finite");
                }
                Flt64? masterObjective = masterOutput.Solution?.Objective;
                if (masterObjective is null) {
                    return Results.Ok(Report(
                        problemStatus: ProblemStatus.Feasible,
                        terminationReason: TerminationReason.IterationLimit,
                        proofStatus: ProofStatus.None,
                        master: masterOutput,
                        assignment: feasible.Assignment,
                        subproblem: feasible,
                        cuts: acceptedCuts,
                        traces: traces,
                        diagnostics: new[] {
                            new SolveIssue(
                                Code: "benders-master-objective-missing",
                                Category: SolveIssueCategory.Unsupported,
                                Message: "Exact 模式缺少主问题目标值 / " +
                                    "Exact mode requires a master objective value"),
                        }));
                }
                Flt64 objectiveGap = (objectiveOk.Value - masterObjective.Value).Abs();
                if (objectiveGap.ToDouble() > _options.OptimalityTolerance.ToDouble()) {
                    return Results.Ok(Report(
                        problemStatus: ProblemStatus.Feasible,
                        terminationReason: TerminationReason.IterationLimit,
                        proofStatus: ProofStatus.None,
                        master: masterOutput,
                        assignment: feasible.Assignment,
                        subproblem: feasible,
                        cuts: acceptedCuts,
                        traces: traces,
                        diagnostics: new[] {
                            new SolveIssue(
                                Code: "benders-objective-inconsistent",
                                Category: SolveIssueCategory.Unsupported,
                                Message: "Exact 模式要求主问题目标与完整 Benders 目标一致 / " +
                                    "Exact mode requires the master objective to match the complete Benders objective"),
                        }));
                }
                Flt64? convergenceGap = MasterConvergenceGap(master, masterOutput);
                if (convergenceGap is null ||
                    convergenceGap.Value.ToDouble() > _options.OptimalityTolerance.ToDouble()) {
                    return Results.Ok(Report(
                        problemStatus: ProblemStatus.Feasible,
                        terminationReason: TerminationReason.IterationLimit,
                        proofStatus: ProofStatus.None,
                        master: masterOutput,
                        assignment: feasible.Assignment,
                        subproblem: feasible,
                        cuts: acceptedCuts,
                        traces: traces,
                        diagnostics: new[] {
                            new SolveIssue(
                                Code: "benders-no-convergence-proof",
                                Category: SolveIssueCategory.Unsupported,
                                Message: "Exact 模式缺少满足容差的主问题上下界收敛证明 / " +
                                    "Exact mode lacks a master incumbent/bound convergence proof within tolerance"),
                        }));
                }
            }
            ProofStatus proofStatus = _options.ProofMode == BendersProofMode.Exact
                ? ProofStatus.Verified
                : ProofStatus.Claimed;
            return Results.Ok(Report(
                problemStatus: ProblemStatus.Feasible,
                terminationReason: TerminationReason.Completed,
                proofStatus: proofStatus,
                master: masterOutput,
                assignment: feasible.Assignment,
                subproblem: feasible,
                cuts: acceptedCuts,
                traces: traces));
        }

        Result<int, ErrorCode, Error<ErrorCode>> addResult = AddCuts(
            master: master,
            cuts: newCuts,
            allCuts: acceptedCuts,
            knownCuts: knownCuts);
        if (addResult is not Ok<int, ErrorCode, Error<ErrorCode>> addOk) {
            return Propagate<LogicBasedBendersReport, int>(addResult);
        }
        return null;
    }

    private Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>>? HandleInfeasible(
        LinearMetaModel<Flt64> master,
        ConstraintProgrammingModel subproblem,
        SolveReport<Flt64> masterOutput,
        InfeasibleSubproblemResult infeasible,
        int iteration,
        List<BendersMasterCut> acceptedCuts,
        HashSet<string> knownCuts,
        List<BendersIterationTrace> traces,
        Stopwatch stopwatch,
        ref int stallIterations) {
        if (_options.ProofMode == BendersProofMode.Exact &&
            infeasible.ProofStatus != ProofStatus.Verified) {
            return Results.Ok(Report(
                problemStatus: ProblemStatus.Unknown,
                terminationReason: TerminationReason.BackendFailure,
                proofStatus: ProofStatus.None,
                master: masterOutput,
                assignment: infeasible.Assignment,
                subproblem: infeasible,
                cuts: acceptedCuts,
                traces: traces,
                diagnostics: new[] {
                    new SolveIssue(
                        Code: "benders-subproblem-not-proven-infeasible",
                        Category: SolveIssueCategory.Backend,
                        Message: "Exact 模式要求 CP 子问题不可行证明 / " +
                            "Exact mode requires a proven CP subproblem infeasibility certificate"),
                }));
        }

        var context = new BendersCutContext(
            Master: master,
            Subproblem: subproblem,
            Assignment: infeasible.Assignment,
            Iteration: iteration,
            ProofMode: _options.ProofMode);
        Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> generated =
            _cutOracle.FeasibilityCuts(infeasible, context);
        if (generated is not Ok<
                List<BendersMasterCut>,
                ErrorCode,
                Error<ErrorCode>> generatedOk) {
            return Propagate<LogicBasedBendersReport, List<BendersMasterCut>>(generated);
        }
        Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> validated =
            ValidateCuts(generatedOk.Value, knownCuts);
        if (validated is not Ok<
                List<BendersMasterCut>,
                ErrorCode,
                Error<ErrorCode>> validatedOk) {
            return Propagate<LogicBasedBendersReport, List<BendersMasterCut>>(validated);
        }
        List<BendersMasterCut> newCuts = validatedOk.Value;
        if (newCuts.Count == 0) {
            return Results.Ok(Report(
                problemStatus: ProblemStatus.Unknown,
                terminationReason: TerminationReason.IterationLimit,
                proofStatus: ProofStatus.None,
                master: masterOutput,
                assignment: infeasible.Assignment,
                subproblem: infeasible,
                cuts: acceptedCuts,
                traces: traces,
                diagnostics: new[] {
                    new SolveIssue(
                        Code: "benders-no-feasibility-cut",
                        Category: SolveIssueCategory.Unsupported,
                        Message: "不可行子问题没有可用的全局有效 cut / " +
                            "No globally valid cut is available for the infeasible subproblem"),
                }));
        }

        int before = acceptedCuts.Count;
        Result<int, ErrorCode, Error<ErrorCode>> addResult = AddCuts(
            master: master,
            cuts: newCuts,
            allCuts: acceptedCuts,
            knownCuts: knownCuts);
        if (addResult is not Ok<int, ErrorCode, Error<ErrorCode>>) {
            return Propagate<LogicBasedBendersReport, int>(addResult);
        }
        int added = acceptedCuts.Count - before;
        BendersIterationTrace trace = Trace(
            iteration: iteration,
            master: master,
            masterOutput: masterOutput,
            subproblemStatus: BendersSubproblemStatus.Infeasible,
            subproblemObjective: null,
            cutCount: added,
            conflictCoreSize: ConflictCoreSize(infeasible.Conflict),
            stopwatch: stopwatch);
        traces.Add(trace);
        Result<Success, ErrorCode, Error<ErrorCode>>? notifyFailure = Notify(trace);
        if (notifyFailure is not null) {
            return Propagate<LogicBasedBendersReport, Success>(notifyFailure);
        }

        if (added == 0) {
            ++stallIterations;
            if (stallIterations >= _options.StallIterationLimit) {
                return Results.Ok(Report(
                    problemStatus: ProblemStatus.Unknown,
                    terminationReason: TerminationReason.Stalled,
                    proofStatus: ProofStatus.None,
                    master: masterOutput,
                    assignment: infeasible.Assignment,
                    subproblem: infeasible,
                    cuts: acceptedCuts,
                    traces: traces,
                    diagnostics: new[] {
                        new SolveIssue(
                            Code: "benders-cut-stalled",
                            Category: SolveIssueCategory.Backend,
                            Message: "Benders 割添加后未产生新约束 / " +
                                "Benders cut generation stalled without a new constraint"),
                    }));
            }
        }
        else {
            stallIterations = 0;
        }
        return null;
    }

    private static LogicBasedBendersSubproblemResult ClassifySubproblem(
        ConstraintProgrammingSolverOutput output,
        BendersSubproblemAssignment assignment) {
        return output switch {
            ConstraintProgrammingFeasibleOutput feasible =>
                new FeasibleSubproblemResult(assignment, feasible),
            ConstraintProgrammingInfeasibleOutput infeasible =>
                new InfeasibleSubproblemResult(
                    Assignment: assignment,
                    Conflict: infeasible.Conflict,
                    ProofStatus: infeasible.ProofStatus),
            ConstraintProgrammingUnknownOutput unknown =>
                new UnknownSubproblemResult(
                    Assignment: assignment,
                    TerminationReason: unknown.TerminationReason),
            _ => new UnknownSubproblemResult(
                Assignment: assignment,
                TerminationReason: TerminationReason.BackendFailure),
        };
    }

    private Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> ValidateCuts(
        IEnumerable<BendersMasterCut> cuts,
        HashSet<string> knownCuts) {
        var accepted = new List<BendersMasterCut>();
        foreach (BendersMasterCut cut in cuts) {
            if (string.IsNullOrWhiteSpace(cut.Source)) {
                return Failure<List<BendersMasterCut>>(
                    ErrorCode.IllegalArgument,
                    "Benders cut source 不能为空 / Benders cut source must not be blank");
            }
            if (!IsSupported(cut.Inequality) ||
                cut.AdditionalInequalities.Any(inequality => !IsSupported(inequality))) {
                return Failure<List<BendersMasterCut>>(
                    ErrorCode.Other,
                    "Benders 主问题首版不支持严格或不等关系 cut / " +
                    "Strict or not-equal Benders cuts are unsupported");
            }
            if (_options.ProofMode == BendersProofMode.Exact &&
                (cut.Validity != BendersCutValidity.Global ||
                    cut.ProofStatus != ProofStatus.Verified)) {
                return Failure<List<BendersMasterCut>>(
                    ErrorCode.IllegalArgument,
                    "Exact 模式只能接受已验证的全局有效 cut / " +
                    "Exact mode only accepts verified globally valid cuts");
            }
            if (!knownCuts.Contains(cut.Key) &&
                accepted.All(existing => existing.Key != cut.Key)) {
                accepted.Add(cut);
            }
        }
        return Results.Ok(accepted);
    }

    private static bool IsSupported(LinearInequality<Flt64> inequality) =>
        inequality.Comparison is Comparison.LE or Comparison.EQ or Comparison.GE;

    internal Result<int, ErrorCode, Error<ErrorCode>> AddCuts(
        LinearMetaModel<Flt64> master,
        IReadOnlyList<BendersMasterCut> cuts,
        List<BendersMasterCut> allCuts,
        HashSet<string> knownCuts,
        Func<
            LinearInequality<Flt64>,
            string,
            Result<Success, ErrorCode, Error<ErrorCode>>>? addConstraint = null) {
        int originalConstraintCount = master.RelationConstraints.Count;
        var masterVariables = master.Tokens.Tokens
            .Select(token => token.Variable)
            .ToHashSet();
        var pendingAuxiliary = new List<BinVar>();
        var pendingKeys = masterVariables
            .Select(variable => variable.Key)
            .ToHashSet();
        foreach (BendersMasterCut cut in cuts) {
            var auxiliary = cut.AuxiliaryVariables.ToHashSet<IVariableItem>();
            IEnumerable<LinearInequality<Flt64>> inequalities =
                new[] { cut.Inequality }.Concat(cut.AdditionalInequalities);
            foreach (LinearInequality<Flt64> inequality in inequalities) {
                foreach (LinearMonomial<Flt64> monomial in
                    inequality.Lhs.Monomials.Concat(inequality.Rhs.Monomials)) {
                    if (monomial.Symbol is not IVariableItem variable ||
                        (!masterVariables.Contains(variable) && !auxiliary.Contains(variable))) {
                        return Failure<int>(
                            ErrorCode.OREngineSolvingException,
                            "Benders cut 包含未知主问题符号 / " +
                            "Benders cut contains an unknown master symbol");
                    }
                }
            }
            foreach (BinVar variable in cut.AuxiliaryVariables) {
                if (masterVariables.Contains(variable)) {
                    continue;
                }
                if (!pendingKeys.Add(variable.Key)) {
                    return Failure<int>(
                        ErrorCode.IllegalArgument,
                        $"Benders cut 辅助变量键重复：{variable.Identifier}:{variable.Index} / " +
                        $"Benders cut auxiliary variable key is duplicated: " +
                        $"{variable.Identifier}:{variable.Index}");
                }
                pendingAuxiliary.Add(variable);
            }
        }

        var addedVariables = new List<BinVar>();
        foreach (BinVar variable in pendingAuxiliary) {
            Result<Success, ErrorCode, Error<ErrorCode>> variableResult = master.Add(variable);
            if (variableResult is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return RollbackCutInstall(
                    master: master,
                    originalConstraintCount: originalConstraintCount,
                    addedVariables: addedVariables,
                    failure: variableResult);
            }
            addedVariables.Add(variable);
            masterVariables.Add(variable);
        }

        int added = 0;
        var installedCuts = new List<BendersMasterCut>();
        foreach (BendersMasterCut cut in cuts) {
            string name = cut.Name ??
                $"benders-{allCuts.Count}-{cut.Kind.ToString().ToLowerInvariant()}";
            IEnumerable<LinearInequality<Flt64>> inequalities =
                new[] { cut.Inequality }.Concat(cut.AdditionalInequalities);
            int constraintIndex = 0;
            foreach (LinearInequality<Flt64> inequality in inequalities) {
                string constraintName = cut.AdditionalInequalities.Count == 0
                    ? name
                    : $"{name}-{constraintIndex}";
                Result<Success, ErrorCode, Error<ErrorCode>> constraintResult =
                    addConstraint is null
                    ? master.AddConstraint(
                        relation: inequality,
                        group: null,
                        name: constraintName,
                        displayName: constraintName)
                    : addConstraint(inequality, constraintName);
                if (constraintResult is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                    return RollbackCutInstall(
                        master: master,
                        originalConstraintCount: originalConstraintCount,
                        addedVariables: addedVariables,
                        failure: constraintResult);
                }
                ++constraintIndex;
            }
            installedCuts.Add(cut);
            ++added;
        }
        foreach (BendersMasterCut cut in installedCuts) {
            knownCuts.Add(cut.Key);
            allCuts.Add(cut);
        }
        return Results.Ok(added);
    }

    private static Result<int, ErrorCode, Error<ErrorCode>> RollbackCutInstall(
        LinearMetaModel<Flt64> master,
        int originalConstraintCount,
        IReadOnlyList<BinVar> addedVariables,
        Result<Success, ErrorCode, Error<ErrorCode>> failure) {
        Try rollback = master.RollbackConstraintsTo(originalConstraintCount);
        RollbackVariables(master, addedVariables);
        if (rollback.IsOk) {
            return Propagate<int, Success>(failure);
        }

        var errors = new List<Error<ErrorCode>>();
        switch (failure) {
            case Failed<Success, ErrorCode, Error<ErrorCode>> failed:
                errors.Add(failed.Error);
                break;
            case Fatal<Success, ErrorCode, Error<ErrorCode>> fatal:
                errors.AddRange(fatal.Errors);
                break;
        }
        switch (rollback) {
            case Failed<Success, ErrorCode, Error<ErrorCode>> failedRollback:
                errors.Add(failedRollback.Error);
                break;
            case Fatal<Success, ErrorCode, Error<ErrorCode>> fatalRollback:
                errors.AddRange(fatalRollback.Errors);
                break;
        }
        return new Fatal<int, ErrorCode, Error<ErrorCode>>(errors);
    }

    private static void RollbackVariables(
        LinearMetaModel<Flt64> master,
        IReadOnlyList<BinVar> variables) {
        for (int index = variables.Count - 1; index >= 0; index--) {
            master.Remove(variables[index]);
        }
    }

    private BendersIterationTrace Trace(
        int iteration,
        LinearMetaModel<Flt64> master,
        SolveReport<Flt64> masterOutput,
        BendersSubproblemStatus subproblemStatus,
        Flt64? subproblemObjective,
        int cutCount,
        int conflictCoreSize,
        Stopwatch stopwatch) {
        return new BendersIterationTrace(
            Iteration: iteration,
            MasterObjective: masterOutput.Solution?.Objective,
            MasterBestBound: masterOutput.Statistics.BestBound,
            MasterStatus: ToSolverStatus(masterOutput),
            SubproblemStatus: subproblemStatus,
            SubproblemObjective: subproblemObjective,
            CutCount: cutCount,
            ConflictCoreSize: conflictCoreSize,
            Elapsed: stopwatch.Elapsed,
            ProofMode: _options.ProofMode,
            ConvergenceGap: MasterConvergenceGap(master, masterOutput));
    }

    private Result<Success, ErrorCode, Error<ErrorCode>>? Notify(
        BendersIterationTrace trace) {
        if (_options.ProgressReporter is null) {
            return null;
        }
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            _options.ProgressReporter(trace);
        return result.IsOk ? null : result;
    }

    private LogicBasedBendersReport Report(
        ProblemStatus problemStatus,
        TerminationReason terminationReason,
        ProofStatus proofStatus,
        SolveReport<Flt64>? master,
        BendersSubproblemAssignment? assignment,
        LogicBasedBendersSubproblemResult? subproblem,
        IEnumerable<BendersMasterCut> cuts,
        IEnumerable<BendersIterationTrace> traces,
        IEnumerable<SolveIssue>? diagnostics = null) {
        return new LogicBasedBendersReport(
            problemStatus: problemStatus,
            terminationReason: terminationReason,
            proof: new SolveProof(
                Status: proofStatus,
                Kind: "logic-based-benders"),
            masterOutput: master,
            assignment: assignment,
            subproblemResult: subproblem,
            cuts: cuts,
            iterations: traces,
            diagnostics: diagnostics);
    }

    private static bool IsMasterOptimal(SolveReport<Flt64> report) =>
        report.ProblemStatus == ProblemStatus.Feasible &&
        report.TerminationReason == TerminationReason.Completed &&
        report.SolutionPresence == SolutionPresence.Optimal &&
        report.Solution is not null;

    private static SolverStatus? ToSolverStatus(SolveReport<Flt64> report) {
        if (report.ProblemStatus == ProblemStatus.Infeasible) {
            return SolverStatus.Infeasible;
        }
        if (report.ProblemStatus == ProblemStatus.Unbounded) {
            return SolverStatus.Unbounded;
        }
        if (report.ProblemStatus == ProblemStatus.InfeasibleOrUnbounded) {
            return SolverStatus.InfeasibleOrUnbounded;
        }
        if (report.Solution is null) {
            return null;
        }
        return report.SolutionPresence == SolutionPresence.Optimal
            ? SolverStatus.Optimal
            : SolverStatus.Feasible;
    }

    private static Flt64? MasterConvergenceGap(
        LinearMetaModel<Flt64> master,
        SolveReport<Flt64> output) {
        if (output.Statistics.BestBound is not { } bestBound ||
            output.Solution?.Objective is not { } objective) {
            return null;
        }
        Flt64 signedGap = master.ObjectCategory == ObjectCategory.Minimum
            ? objective - bestBound
            : bestBound - objective;
        return signedGap.ToDouble() < 0 ? null : signedGap.Abs();
    }

    private static Flt64? CompatibleObjective(
        ConstraintProgrammingFeasibleOutput output) =>
        output.ExactObjective is null ? output.Objective : null;

    private static int ConflictCoreSize(ConstraintProgrammingConflict? conflict) {
        if (conflict is null) {
            return 0;
        }
        return conflict.Members.Count > 0
            ? conflict.Members.Count
            : conflict.ConstraintIds.Count;
    }

    private static string? ValidateOptions(LogicBasedBendersOptions options) {
        if (options.MaxIterations <= 0) {
            return "Benders maxIterations 必须为正 / Benders maxIterations must be positive";
        }
        if (options.StallIterationLimit <= 0) {
            return "Benders stallIterationLimit 必须为正 / Benders stallIterationLimit must be positive";
        }
        if (!double.IsFinite(options.OptimalityTolerance.ToDouble()) ||
            options.OptimalityTolerance.ToDouble() < 0) {
            return "Benders optimalityTolerance 必须为有限非负数 / " +
                "Benders optimalityTolerance must be finite and non-negative";
        }
        return null;
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code: code, message: message));

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        return result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>(
                ErrorCode.ApplicationError,
                "逻辑 Benders 结果状态无效 / Invalid logic-based Benders result state"),
        };
    }
}
