#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Output;

namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>旧求解输出到统一报告的适配 / Legacy solver-output report adapters.</summary>
public static class SolverOutputReportExtensions {
    /// <summary>将旧输出包装为统一报告 / Wrap legacy output in a unified report.</summary>
    /// <remarks>旧输出不记录真实终止原因；适配的 Completed 不能作为真实终止原因或最优证明。 / Legacy outputs do not record the actual termination reason; the adapted Completed is not proof of termination or optimality.</remarks>
    /// <param name="output">旧求解输出 / Legacy solver output.</param>
    /// <param name="runId">运行标识 / Run identifier.</param>
    /// <param name="provenance">求解器来源 / Solver provenance.</param>
    /// <param name="fingerprints">审计指纹 / Audit fingerprints.</param>
    /// <param name="solutionPool">解池 / Solution pool.</param>
    /// <returns>统一求解报告 / Unified solve report.</returns>
    public static SolveReport<Flt64> ToSolveReport(
        this SolverOutput output,
        SolveRunId? runId = null,
        SolverProvenance? provenance = null,
        SolveFingerprints? fingerprints = null,
        IReadOnlyList<Solution<Flt64>>? solutionPool = null) {
        return output switch {
            SolveReport<Flt64> report => report.WithMetadata(
                runId: runId,
                provenance: provenance,
                fingerprints: fingerprints,
                solutionPool: solutionPool),
            FeasibleSolverOutput<Flt64> feasible => new SolveReport<Flt64> {
                RunId = runId,
                ProblemStatus = ProblemStatus.Feasible,
                TerminationReason = TerminationReason.Completed,
                SolutionPresence = SolutionPresence.Incumbent,
                Solution = new SolveSolution<Flt64>(
                    Values: feasible.Solution,
                    Objective: feasible.Obj,
                    Pool: solutionPool),
                Statistics = new SolveStatistics(
                    SolveTime: feasible.SolveTime ?? feasible.Time,
                    Iterations: feasible.Iterations,
                    Nodes: feasible.NodeCount,
                    BestBound: feasible.BestBound,
                    Gap: feasible.MipGap),
                Provenance = provenance,
                Fingerprints = fingerprints ?? new SolveFingerprints(),
            },
            LinearInfeasibleSolverOutput infeasible => NonfeasibleReport(
                output: infeasible,
                status: infeasible.Status,
                runId: runId,
                provenance: provenance,
                fingerprints: fingerprints,
                carriedDiagnostics: infeasible.Diagnostics),
            QuadraticInfeasibleSolverOutput infeasible => NonfeasibleReport(
                output: infeasible,
                status: infeasible.Status,
                runId: runId,
                provenance: provenance,
                fingerprints: fingerprints,
                carriedDiagnostics: infeasible.Diagnostics),
            _ => UnsupportedReport(
                output: output,
                runId: runId,
                provenance: provenance,
                fingerprints: fingerprints),
        };
    }

    private static SolveReport<Flt64> NonfeasibleReport(
        UnifiedSolverOutput output,
        SolverStatus status,
        SolveRunId? runId,
        SolverProvenance? provenance,
        SolveFingerprints? fingerprints,
        SolveDiagnostics<Flt64>? carriedDiagnostics = null) {
        ProblemStatus problem = status switch {
            SolverStatus.Infeasible => ProblemStatus.Infeasible,
            SolverStatus.Unbounded => ProblemStatus.Unbounded,
            SolverStatus.InfeasibleOrUnbounded => ProblemStatus.InfeasibleOrUnbounded,
            _ => ProblemStatus.Unknown,
        };
        SolveDiagnostics<Flt64> diagnostics = problem switch {
            ProblemStatus.Infeasible => new SolveDiagnostics<Flt64>(
                InfeasibilityEvidence: new InfeasibilityEvidence {
                    Source = InfeasibilityEvidenceSource.None,
                    Exactness = EvidenceExactness.Unknown,
                    Completeness = EvidenceCompleteness.Unavailable,
                    Validity = EvidenceValidity.Unknown,
                    Minimality = EvidenceMinimality.NotChecked,
                }),
            ProblemStatus.Unknown => new SolveDiagnostics<Flt64>(
                Errors: new[] {
                    new SolveIssue(
                        Code: "legacy-status-unusable",
                        Category: SolveIssueCategory.Backend,
                        Message: $"旧输出缺少可用结论或解：{status} / Legacy output lacks a usable conclusion or solution: {status}"),
                }),
            _ => new SolveDiagnostics<Flt64>(),
        };
        if (carriedDiagnostics is { } carried) {
            // 旧输出携带的证据优先，映射只补充其缺失部分 / Evidence carried by the legacy output wins; the mapping only fills the gaps.
            diagnostics = diagnostics with {
                InfeasibilityEvidence = carried.InfeasibilityEvidence ?? diagnostics.InfeasibilityEvidence,
                Warnings = [.. diagnostics.Warnings, .. carried.Warnings],
                Errors = [.. diagnostics.Errors, .. carried.Errors],
            };
        }
        return new SolveReport<Flt64> {
            RunId = runId,
            ProblemStatus = problem,
            TerminationReason = problem == ProblemStatus.Unknown ? TerminationReason.BackendFailure : TerminationReason.Completed,
            SolutionPresence = SolutionPresence.None,
            Statistics = new SolveStatistics(
                SolveTime: output.SolveTime,
                Iterations: output.Iterations,
                Nodes: output.NodeCount,
                BestBound: output.BestBound,
                Gap: output.MipGap),
            Diagnostics = diagnostics,
            Provenance = provenance,
            Fingerprints = fingerprints ?? new SolveFingerprints(),
        };
    }

    private static SolveReport<Flt64> UnsupportedReport(
        SolverOutput output,
        SolveRunId? runId,
        SolverProvenance? provenance,
        SolveFingerprints? fingerprints) {
        string typeName = output.GetType().Name;
        return new SolveReport<Flt64> {
            RunId = runId,
            ProblemStatus = ProblemStatus.Unknown,
            TerminationReason = TerminationReason.BackendFailure,
            SolutionPresence = SolutionPresence.None,
            Diagnostics = new SolveDiagnostics<Flt64>(
                Errors: new[] {
                    new SolveIssue(
                        Code: "legacy-output-unsupported",
                        Category: SolveIssueCategory.Unsupported,
                        Message: $"旧输出类型无法转换为线性/二次报告：{typeName} / Legacy output type cannot be converted to a linear/quadratic report: {typeName}"),
                }),
            Provenance = provenance,
            Fingerprints = fingerprints ?? new SolveFingerprints(),
        };
    }
}
