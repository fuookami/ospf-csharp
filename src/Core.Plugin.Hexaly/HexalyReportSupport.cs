#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Hexaly.Optimizer;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

internal static class HexalyReportSupport {
    internal static readonly SolverCapabilities LinearCapabilities = new(
        ModelTypes: new HashSet<SolverModelType> {
            SolverModelType.Lp,
            SolverModelType.Mip,
        },
        WarmStart: true,
        Callback: true,
        Interrupt: true);

    internal static readonly SolverCapabilities QuadraticCapabilities = new(
        ModelTypes: new HashSet<SolverModelType> {
            SolverModelType.Qp,
            SolverModelType.Qcp,
            SolverModelType.Mip,
        },
        WarmStart: true,
        Callback: true,
        Interrupt: true);

    internal static SolveReport<Flt64> Cancelled(
        SolverConfig config,
        bool quadratic) {
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
        HxSolutionStatus status,
        SolveSolution<Flt64>? solution,
        SolveStatistics statistics,
        TerminationReason terminationReason,
        SolverConfig config,
        bool quadratic) {
        if (solution is not null
            && (solution.Objective is not { } objective
                || !double.IsFinite(objective.ToDouble())
                || solution.Values.Values.Any(value => !double.IsFinite(value.ToDouble())))) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "Hexaly 解或目标值不是有限数 / Hexaly solution or objective is not finite"));
        }

        ProblemStatus problem = status switch {
            HxSolutionStatus.Optimal or HxSolutionStatus.Feasible => ProblemStatus.Feasible,
            HxSolutionStatus.Inconsistent => ProblemStatus.Infeasible,
            _ => ProblemStatus.Unknown,
        };
        bool proven = terminationReason is TerminationReason.Completed
            && status is HxSolutionStatus.Optimal or HxSolutionStatus.Inconsistent;
        bool optimal = proven && status == HxSolutionStatus.Optimal;
        SolveDiagnostics<Flt64> diagnostics = status == HxSolutionStatus.Infeasible
            ? new SolveDiagnostics<Flt64>(Warnings: [new SolveIssue(
                Code: "hexaly-solution-infeasible",
                Category: SolveIssueCategory.Numerical,
                Message: "Hexaly 当前解违反约束，但未证明模型不可行 / " +
                    "The current Hexaly solution violates constraints, but model infeasibility was not proven")])
            : new SolveDiagnostics<Flt64>();
        SolverDescriptor descriptor = Descriptor(quadratic);
        return Results.Ok(new SolveReport<Flt64> {
            ProblemStatus = problem,
            TerminationReason = terminationReason,
            SolutionPresence = solution is null
                ? SolutionPresence.None
                : optimal
                    ? SolutionPresence.Optimal
                    : SolutionPresence.Incumbent,
            Solution = solution,
            Proof = new SolveProof(
                Status: proven
                    ? ProofStatus.Claimed
                    : ProofStatus.None,
                Kind: proven
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
            _ when report.TerminationReason == TerminationReason.Cancelled =>
                ErrorCode.ApplicationStopped,
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
            SolverId: "hexaly",
            BackendName: "Hexaly",
            Capabilities: quadratic ? QuadraticCapabilities : LinearCapabilities,
            BackendVersion: NativeVersion);

    internal static ErrorCode ClassifyException(
        Exception error,
        ErrorCode fallback) =>
        error is HxException nativeError && nativeError.ErrorCode == HxErrorCode.License
            ? ErrorCode.AuthenticationError
            : fallback;

    internal static string NativeVersion {
        get {
            try {
                return HxVersion.GetVersion();
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                return "unknown";
            }
        }
    }
}
