#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
#if MINDOPT_AVAILABLE
using MDOEnv = Mindopt.MDOEnv;
#endif

namespace Fuookami.Ospf.Core.Plugin.Mindopt;

internal static class MindoptReportSupport {
    internal static readonly SolverCapabilities LinearCapabilities = new(
        ModelTypes: new HashSet<SolverModelType> {
            SolverModelType.Lp,
            SolverModelType.Mip,
        },
        Dual: true,
        Farkas: true,
        WarmStart: true,
        SolutionPool: true,
        Interrupt: true);

    internal static readonly SolverCapabilities QuadraticCapabilities = new(
        ModelTypes: new HashSet<SolverModelType> {
            SolverModelType.Qp,
            SolverModelType.Qcp,
            SolverModelType.Mip,
        },
        WarmStart: true,
        SolutionPool: true,
        Interrupt: true);

    internal static SolveReport<Flt64> Cancelled(SolverConfig config, bool quadratic) {
        SolverDescriptor descriptor = Descriptor(quadratic);
        return new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Unknown,
            TerminationReason = TerminationReason.Cancelled,
            SolutionPresence = SolutionPresence.None,
            Provenance = config.Provenance(descriptor),
            Fingerprints = Fingerprints(config, descriptor),
        };
    }

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Create(
        int status,
        SolveSolution<Flt64>? solution,
        SolveStatistics statistics,
        bool cancelledDuringSolve,
        bool interruptedDuringSolve,
        SolverConfig config,
        bool quadratic) {
        if (solution is not null
            && (solution.Objective is not { } objective
                || !double.IsFinite(objective.ToDouble())
                || solution.Values.Values.Any(value => !double.IsFinite(value.ToDouble()))
                || solution.Pool.Any(pool => pool.Values.Any(value => !double.IsFinite(value.ToDouble()))))) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "MindOPT 解或目标值不是有限数 / MindOPT solution or objective is not finite"));
        }

        ProblemStatus problem = Problem(status, solution);
        TerminationReason termination = Termination(
            status: status,
            cancelled: cancelledDuringSolve,
            interrupted: interruptedDuringSolve);
        bool completed = termination == TerminationReason.Completed;
        bool optimal = completed && status == MindoptStatus.Optimal;
        SolveDiagnostics<Flt64> diagnostics = termination == TerminationReason.BackendFailure
            ? new SolveDiagnostics<Flt64>(Errors: [new SolveIssue(
                Code: "mindopt-native-status",
                Category: SolveIssueCategory.Backend,
                Message: $"未识别的 MindOPT 状态：{status} / Unrecognized MindOPT status: {status}")])
            : new SolveDiagnostics<Flt64>();
        SolverDescriptor descriptor = Descriptor(quadratic);
        return Results.Ok(new SolveReport<Flt64> {
            ProblemStatus = problem,
            TerminationReason = termination,
            SolutionPresence = solution is null
                ? SolutionPresence.None
                : optimal
                    ? SolutionPresence.Optimal
                    : SolutionPresence.Incumbent,
            Solution = solution,
            Proof = new SolveProof(
                Status: completed && (optimal || problem != ProblemStatus.Unknown)
                    ? ProofStatus.Claimed
                    : ProofStatus.None,
                Kind: completed && (optimal || problem != ProblemStatus.Unknown)
                    ? "backend-status"
                    : null),
            Statistics = statistics,
            Diagnostics = diagnostics,
            Provenance = config.Provenance(descriptor),
            Fingerprints = Fingerprints(config, descriptor),
        });
    }

    internal static Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ToLegacy(
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result) {
        if (result is Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(failed.Error);
        }
        if (result is Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        if (result is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "未知求解结果 / Unknown solve result"));
        }
        SolveReport<Flt64> report = ok.Value;
        if (report.ProblemStatus == ProblemStatus.Feasible
            && report.Solution is { Objective: { } objective } solution
            && report.Statistics.SolveTime is { } time) {
            Flt64 bound = report.Statistics.BestBound ?? objective;
            Flt64 gap = report.Statistics.Gap ?? Flt64.Zero;
            return Results.Ok(new FeasibleSolverOutput<Flt64>(
                Obj: objective,
                Solution: solution.Values,
                Time: time,
                PossibleBestObj: bound,
                Gap: gap,
                Iterations: report.Statistics.Iterations,
                NodeCount: report.Statistics.Nodes,
                BestBound: bound,
                MipGap: gap,
                SolveTime: time,
                ObjValueOrNull: objective,
                PossibleBestObjValueOrNull: bound,
                BestBoundValueOrNull: bound));
        }
        ErrorCode code = report.ProblemStatus switch {
            ProblemStatus.Infeasible => ErrorCode.ORModelInfeasible,
            ProblemStatus.Unbounded => ErrorCode.ORModelUnbounded,
            ProblemStatus.InfeasibleOrUnbounded => ErrorCode.ORModelInfeasibleOrUnbounded,
            _ when report.TerminationReason == TerminationReason.Cancelled => ErrorCode.ApplicationStopped,
            _ => ErrorCode.OREngineSolvingException,
        };
        return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
            code,
            $"旧接口无法返回无解报告：{report.TerminationReason} / " +
            $"Legacy interface cannot return a report without a solution: {report.TerminationReason}"));
    }

    internal static SolveFingerprints Fingerprints(
        SolverConfig config,
        SolverDescriptor descriptor) =>
        new(
            Configuration: config.ConfigurationSnapshot().Fingerprint(),
            Solver: SolveFingerprinting.Solver(descriptor));

    internal static SolverDescriptor Descriptor(bool quadratic) =>
        new(
            SolverId: "mindopt",
            BackendName: "MindOPT",
            Capabilities: quadratic ? QuadraticCapabilities : LinearCapabilities,
            BackendVersion: NativeVersion);

    internal static string NativeVersion {
        get {
#if MINDOPT_AVAILABLE
            string? home = Environment.GetEnvironmentVariable("MINDOPT_HOME");
            if (!string.IsNullOrWhiteSpace(home)) {
                string native = Path.Combine(home, "win64-x86", "bin", "mindopt.dll");
                try {
                    if (File.Exists(native)) {
                        var version = FileVersionInfo.GetVersionInfo(native);
                        if (!string.IsNullOrWhiteSpace(version.FileVersion)) {
                            return version.FileVersion;
                        }
                        if (!string.IsNullOrWhiteSpace(version.ProductVersion)) {
                            return version.ProductVersion;
                        }
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                }
            }
            return typeof(MDOEnv).Assembly.GetName().Version?.ToString(3) ?? "unknown";
#else
            return "unavailable";
#endif
        }
    }

    private static ProblemStatus Problem(int status, SolveSolution<Flt64>? solution) =>
        status switch {
            MindoptStatus.Optimal or MindoptStatus.SubOptimal => ProblemStatus.Feasible,
            MindoptStatus.Infeasible => ProblemStatus.Infeasible,
            MindoptStatus.Unbounded => ProblemStatus.Unbounded,
            MindoptStatus.InfeasibleOrUnbounded => ProblemStatus.InfeasibleOrUnbounded,
            _ when solution is not null => ProblemStatus.Feasible,
            _ => ProblemStatus.Unknown,
        };

    private static TerminationReason Termination(int status, bool cancelled, bool interrupted) {
        if (cancelled) {
            return TerminationReason.Cancelled;
        }
        return status switch {
            MindoptStatus.Unknown => TerminationReason.BackendFailure,
            MindoptStatus.IterationLimit => TerminationReason.IterationLimit,
            MindoptStatus.TimeLimit => TerminationReason.TimeLimit,
            MindoptStatus.NodeLimit or MindoptStatus.StallingNodeLimit => TerminationReason.NodeLimit,
            MindoptStatus.SolutionLimit => TerminationReason.SolutionLimit,
            MindoptStatus.Interrupted => interrupted
                ? TerminationReason.Interrupted
                : TerminationReason.BackendFailure,
            _ => TerminationReason.Completed,
        };
    }
}

internal static class MindoptStatus {
    internal const int Unknown = 0;
    internal const int Optimal = 1;
    internal const int Infeasible = 2;
    internal const int Unbounded = 3;
    internal const int InfeasibleOrUnbounded = 4;
    internal const int SubOptimal = 5;
    internal const int IterationLimit = 6;
    internal const int TimeLimit = 7;
    internal const int NodeLimit = 8;
    internal const int SolutionLimit = 9;
    internal const int StallingNodeLimit = 10;
    internal const int Interrupted = 11;
}
