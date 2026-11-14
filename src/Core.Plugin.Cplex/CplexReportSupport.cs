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
#if CPLEX_AVAILABLE
using CplexSdk = ILOG.CPLEX.Cplex;
#endif

namespace Fuookami.Ospf.Core.Plugin.Cplex;

internal static class CplexReportSupport {
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

    internal static SolveReport<Flt64> Cancelled() => new() {
        ProblemStatus = ProblemStatus.Unknown,
        TerminationReason = TerminationReason.Cancelled,
        SolutionPresence = SolutionPresence.None,
    };

    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Create(
        string status,
        string detailedStatus,
        SolveSolution<Flt64>? solution,
        SolveStatistics statistics,
        bool cancelledDuringSolve,
        bool interruptedDuringSolve,
        SolverConfig config,
        bool quadratic,
        string nativeVersion) {
        if (solution is not null
            && (solution.Objective is not { } objective
                || !double.IsFinite(objective.ToDouble())
                || solution.Values.Values.Any(value => !double.IsFinite(value.ToDouble())))) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "CPLEX 解或目标值不是有限数 / CPLEX solution or objective is not finite"));
        }

        ProblemStatus problem = Problem(status, detailedStatus, solution);
        TerminationReason termination = Termination(
            detailedStatus,
            cancelledDuringSolve,
            interruptedDuringSolve);
        bool completed = termination == TerminationReason.Completed;
        bool optimal = completed && IsOptimal(detailedStatus);
        SolveDiagnostics<Flt64> diagnostics = termination == TerminationReason.BackendFailure
            ? new SolveDiagnostics<Flt64>(Errors: [new SolveIssue(
                Code: "cplex-native-status",
                Category: SolveIssueCategory.Backend,
                Message: $"未识别的 CPLEX 状态：{status}/{detailedStatus} / " +
                    $"Unrecognized CPLEX status: {status}/{detailedStatus}")])
            : new SolveDiagnostics<Flt64>();
        SolverDescriptor descriptor = new(
            SolverId: "cplex",
            BackendName: "CPLEX",
            Capabilities: quadratic ? QuadraticCapabilities : LinearCapabilities,
            BackendVersion: nativeVersion);
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

    internal static bool HasSolution(string status, string detailedStatus) =>
        status is "Optimal" or "Feasible" or "Bounded"
        || detailedStatus is "Feasible" or "NumBest" or "NodeLimFeas"
            or "Optimal" or "OptimalTol" or "OptimalPopulated"
            or "OptimalPopulatedTol" or "FirstOrder";

    internal static bool IsOptimal(string detailedStatus) =>
        detailedStatus is "Optimal" or "OptimalTol" or "OptimalPopulated"
            or "OptimalPopulatedTol" or "FirstOrder" or "MultiObjOptimal";

    private static ProblemStatus Problem(
        string status,
        string detailedStatus,
        SolveSolution<Flt64>? solution) =>
        status switch {
            "Infeasible" => ProblemStatus.Infeasible,
            "Unbounded" => ProblemStatus.Unbounded,
            "InfeasibleOrUnbounded" => ProblemStatus.InfeasibleOrUnbounded,
            "Optimal" or "Feasible" or "Bounded" => ProblemStatus.Feasible,
            _ => detailedStatus switch {
                "Infeasible" or "OptimalInfeas" or "ConflictMinimal" => ProblemStatus.Infeasible,
                "Unbounded" or "RelaxationUnbounded" or "OptimalFaceUnbounded"
                    or "MultiObjUnbounded" => ProblemStatus.Unbounded,
                "InfOrUnbd" or "MultiObjInfOrUnbd" => ProblemStatus.InfeasibleOrUnbounded,
                "Feasible" or "NumBest" or "NodeLimFeas" or "FirstOrder"
                    or "Optimal" or "OptimalTol" or "OptimalPopulated"
                    or "OptimalPopulatedTol" or "MultiObjOptimal"
                    or "MultiObjNonOptimal" => ProblemStatus.Feasible,
                _ when solution is not null => ProblemStatus.Feasible,
                _ => ProblemStatus.Unknown,
            },
        };

    private static TerminationReason Termination(
        string detailedStatus,
        bool cancelled,
        bool interrupted) =>
        detailedStatus switch {
            "AbortTimeLim" or "AbortDetTimeLim" or "ConflictAbortTimeLim"
                or "ConflictAbortDetTimeLim" => TerminationReason.TimeLimit,
            "AbortItLim" or "ConflictAbortItLim" => TerminationReason.IterationLimit,
            "NodeLimFeas" or "NodeLimInfeas" or "ConflictAbortNodeLim" => TerminationReason.NodeLimit,
            "SolLim" or "PopulateSolLim" => TerminationReason.SolutionLimit,
            "AbortObjLim" or "AbortPrimObjLim" or "AbortDualObjLim"
                or "ConflictAbortObjLim" => TerminationReason.ObjectiveLimit,
            "AbortUser" or "ConflictAbortUser" => cancelled
                ? TerminationReason.Cancelled
                : interrupted
                    ? TerminationReason.Interrupted
                    : TerminationReason.BackendFailure,
            "MemLimFeas" or "MemLimInfeas" or "ConflictAbortMemLim"
                or "FailFeas" or "FailInfeas" or "FailFeasNoTree"
                or "FailInfeasNoTree" => TerminationReason.NumericalFailure,
            "Unknown" => TerminationReason.BackendFailure,
            _ => TerminationReason.Completed,
        };

#if CPLEX_AVAILABLE
    internal static string NativeVersion =>
        typeof(CplexSdk).Assembly.GetName().Version?.ToString(3) ?? "12.10.0";
#else
    internal static string NativeVersion => "unavailable";
#endif
}
