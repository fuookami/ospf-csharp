#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
#if MOSEK_AVAILABLE
using mosek;
#endif

namespace Fuookami.Ospf.Core.Plugin.Mosek;

internal static class MosekReportSupport {
    internal static readonly SolverCapabilities LinearCapabilities = new(
        ModelTypes: new HashSet<SolverModelType> {
            SolverModelType.Lp,
            SolverModelType.Mip,
        },
        WarmStart: true);

    internal static SolveReport<Flt64> Cancelled(SolverConfig config) {
        SolverDescriptor descriptor = Descriptor();
        return new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Unknown,
            TerminationReason = TerminationReason.Cancelled,
            SolutionPresence = SolutionPresence.None,
            Provenance = config.Provenance(descriptor),
            Fingerprints = Fingerprints(config, descriptor),
        };
    }

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Create(
        SolverStatus status,
        SolveSolution<Flt64>? solution,
        SolveStatistics statistics,
        SolverConfig config,
        TerminationReason? terminationReason = null) {
        if (solution is not null
            && (solution.Objective is not { } objective
                || !double.IsFinite(objective.ToDouble())
                || solution.Values.Values.Any(value => !double.IsFinite(value.ToDouble())))) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "MOSEK 解或目标值不是有限数 / MOSEK solution or objective is not finite"));
        }

        ProblemStatus problem = status switch {
            SolverStatus.Optimal or SolverStatus.Feasible => ProblemStatus.Feasible,
            SolverStatus.Infeasible => ProblemStatus.Infeasible,
            SolverStatus.Unbounded => ProblemStatus.Unbounded,
            SolverStatus.InfeasibleOrUnbounded => ProblemStatus.InfeasibleOrUnbounded,
            _ => ProblemStatus.Unknown,
        };
        TerminationReason termination = terminationReason
            ?? (status == SolverStatus.SolvingException
                ? TerminationReason.BackendFailure
                : TerminationReason.Completed);
        bool proven = termination is not TerminationReason.BackendFailure
            and not TerminationReason.NumericalFailure;
        bool optimal = proven && status == SolverStatus.Optimal;
        bool claimed = proven && (optimal || problem != ProblemStatus.Unknown);
        SolveDiagnostics<Flt64> diagnostics = termination == TerminationReason.BackendFailure
            ? new SolveDiagnostics<Flt64>(Errors: [new SolveIssue(
                Code: "mosek-native-status",
                Category: SolveIssueCategory.Backend,
                Message: "MOSEK 返回未识别的求解状态 / MOSEK returned an unrecognized solution status")])
            : new SolveDiagnostics<Flt64>();
        SolverDescriptor descriptor = Descriptor();
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
                Status: claimed ? ProofStatus.Claimed : ProofStatus.None,
                Kind: claimed ? "backend-status" : null),
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

    internal static SolverDescriptor Descriptor() =>
        new(
            SolverId: "mosek",
            BackendName: "MOSEK",
            Capabilities: LinearCapabilities,
            BackendVersion: NativeVersion);

    internal static ErrorCode ClassifyErrorCode(System.Exception error, ErrorCode fallback) =>
#if MOSEK_AVAILABLE
        error is mosek.Error nativeError && IsLicense(nativeError.Code)
            ? ErrorCode.AuthenticationError
            : fallback;
#else
        fallback;
#endif

#if MOSEK_AVAILABLE
    private static bool IsLicense(rescode code) =>
        code is rescode.err_license
            or rescode.err_license_expired
            or rescode.err_license_version
            or rescode.err_license_old_server_version
            or rescode.err_size_license
            or rescode.err_prob_license
            or rescode.err_file_license
            or rescode.err_missing_license_file
            or rescode.err_size_license_con
            or rescode.err_size_license_var
            or rescode.err_size_license_intvar
            or rescode.err_optimizer_license
            or rescode.err_flexlm
            or rescode.err_license_server
            or rescode.err_license_max
            or rescode.err_license_moseklm_daemon
            or rescode.err_license_feature
            or rescode.err_platform_not_licensed
            or rescode.err_license_cannot_allocate
            or rescode.err_license_cannot_connect
            or rescode.err_license_invalid_hostid
            or rescode.err_license_server_version
            or rescode.err_license_no_server_support
            or rescode.err_license_no_server_line
            or rescode.err_size_license_numcores;
#endif

    internal static string NativeVersion =>
#if MOSEK_AVAILABLE
        typeof(Env).Assembly.GetName().Version?.ToString(3) ?? "10.2";
#else
        "unavailable";
#endif
}
