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
#if COPT_AVAILABLE
using CoptSdk = Copt;
#endif

namespace Fuookami.Ospf.Core.Plugin.Copt;

internal static class CoptReportSupport {
    internal static readonly SolverCapabilities LinearCapabilities = new(
        ModelTypes: new HashSet<SolverModelType> {
            SolverModelType.Lp,
            SolverModelType.Mip,
        },
        Dual: true,
        Farkas: true,
        NativeIis: true,
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
        int nativeStatus,
        SolveSolution<Flt64>? solution,
        SolveStatistics statistics,
        bool cancelledDuringSolve,
        bool interruptedDuringSolve,
        SolverConfig config,
        bool quadratic) {
        if (solution is not null
            && (solution.Objective is not { } objective
                || !double.IsFinite(objective.ToDouble())
                || solution.Values.Values.Any(value => !double.IsFinite(value.ToDouble())))) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "COPT 解或目标值不是有限数 / COPT solution or objective is not finite"));
        }
        if (nativeStatus == 1 && solution is null
            && statistics.BestBound is null
            && statistics.SolveTime is null) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "COPT 最优状态缺少可读结果 / COPT optimal status has no readable result"));
        }

        ProblemStatus problem = nativeStatus switch {
            1 => ProblemStatus.Feasible,
            2 => ProblemStatus.Infeasible,
            3 => ProblemStatus.Unbounded,
            4 => ProblemStatus.InfeasibleOrUnbounded,
            _ when solution is not null => ProblemStatus.Feasible,
            _ => ProblemStatus.Unknown,
        };
        TerminationReason termination = nativeStatus switch {
            1 or 2 or 3 or 4 => TerminationReason.Completed,
            6 => TerminationReason.NodeLimit,
            8 => TerminationReason.TimeLimit,
            11 => TerminationReason.IterationLimit,
            10 => cancelledDuringSolve
                ? TerminationReason.Cancelled
                : TerminationReason.Interrupted,
            5 or 7 => TerminationReason.NumericalFailure,
            9 => interruptedDuringSolve
                ? TerminationReason.Interrupted
                : TerminationReason.BackendFailure,
            _ => TerminationReason.BackendFailure,
        };
        bool completed = termination == TerminationReason.Completed;
        SolveDiagnostics<Flt64> diagnostics = termination == TerminationReason.BackendFailure
            ? new SolveDiagnostics<Flt64>(Errors: [new SolveIssue(
                Code: "copt-native-status",
                Category: SolveIssueCategory.Backend,
                Message: $"未识别的 COPT 状态：{nativeStatus} / Unrecognized COPT status: {nativeStatus}")])
            : new SolveDiagnostics<Flt64>();
        SolverDescriptor descriptor = new(
            SolverId: "copt",
            BackendName: "COPT",
            Capabilities: quadratic ? QuadraticCapabilities : LinearCapabilities,
            BackendVersion: NativeVersion);
        return Results.Ok(new SolveReport<Flt64> {
            ProblemStatus = problem,
            TerminationReason = termination,
            SolutionPresence = solution is null
                ? SolutionPresence.None
                : nativeStatus == 1
                    ? SolutionPresence.Optimal
                    : SolutionPresence.Incumbent,
            Solution = solution,
            Proof = new SolveProof(
                Status: completed ? ProofStatus.Claimed : ProofStatus.None,
                Kind: completed ? "backend-status" : null),
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
            $"旧接口无法返回无解报告：{report.TerminationReason} / Legacy interface cannot return a report without a solution: {report.TerminationReason}"));
    }

#if COPT_AVAILABLE
    // 托管程序集与原生 DLL 均由同一 COPT_HOME 锁定，版本探针读取托管程序集； /
    // The managed assembly and native DLL are pinned by the same COPT_HOME, so the probe reads the managed version.
    internal static string NativeVersion =>
        typeof(CoptSdk.Envr).Assembly.GetName().Version?.ToString(3) ?? "8.0.3";
#else
    internal static string NativeVersion => "unavailable";
#endif
}
