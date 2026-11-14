#nullable enable

using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 分析目标是否可达的结论 / A conclusion about whether an analysis target is reachable.
/// </summary>
public enum AnalysisStatus {
    /// <summary>目标可达或已证明可行 / Target reached or feasibility proved.</summary>
    Reachable,
    /// <summary>目标不可达或已证明不可行 / Target unreachable or infeasibility proved.</summary>
    Unreachable,
    /// <summary>求解器停止但没有形成证明 / Solver stopped without a proof.</summary>
    Unknown,
    /// <summary>当前分析能力不受支持 / Requested analysis is unsupported.</summary>
    Unsupported,
}

/// <summary>
/// 分析状态扩展 / Analysis-status extensions.
/// </summary>
public static class AnalysisStatusExtensions {
    /// <summary>
    /// 是否为求解器证明结论 / Whether this is a solver-proven conclusion.
    /// </summary>
    /// <param name="status">分析状态 / Analysis status.</param>
    /// <returns>是否已证明 / Whether proven.</returns>
    public static bool IsProven(this AnalysisStatus status) =>
        status is AnalysisStatus.Reachable or AnalysisStatus.Unreachable;

    /// <summary>
    /// 将统一求解结论映射到满足性问题分析状态 / Map a solve conclusion to satisfaction-analysis status.
    /// </summary>
    /// <param name="problemStatus">统一求解结论 / Unified solve conclusion.</param>
    /// <returns>不经最优性证明门控的分析状态 / Analysis status without an optimality-proof gate.</returns>
    public static AnalysisStatus ToAnalysisStatus(this ProblemStatus problemStatus) {
        return problemStatus switch {
            ProblemStatus.Feasible => AnalysisStatus.Reachable,
            ProblemStatus.Infeasible => AnalysisStatus.Unreachable,
            ProblemStatus.Unbounded or ProblemStatus.InfeasibleOrUnbounded or ProblemStatus.Unknown =>
                AnalysisStatus.Unknown,
            _ => AnalysisStatus.Unknown,
        };
    }

    /// <summary>
    /// 带证明门控的求解结论映射 / Proof-gated solve-conclusion mapping.
    /// </summary>
    /// <param name="problemStatus">统一求解结论 / Unified solve conclusion.</param>
    /// <param name="proven">求解器是否已形成证明 / Whether the solver proved the conclusion.</param>
    /// <returns>经证明门控的分析状态 / Proof-gated analysis status.</returns>
    public static AnalysisStatus ToAnalysisStatus(this ProblemStatus problemStatus, bool proven) {
        return problemStatus switch {
            ProblemStatus.Feasible when proven => AnalysisStatus.Reachable,
            ProblemStatus.Infeasible when proven => AnalysisStatus.Unreachable,
            ProblemStatus.Feasible or ProblemStatus.Infeasible => AnalysisStatus.Unknown,
            ProblemStatus.Unbounded or ProblemStatus.InfeasibleOrUnbounded or ProblemStatus.Unknown =>
                AnalysisStatus.Unknown,
            _ => AnalysisStatus.Unknown,
        };
    }
}
