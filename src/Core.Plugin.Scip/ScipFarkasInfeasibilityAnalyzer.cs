#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Plugin.Scip;

/// <summary>
/// 基于 SCIP LP 对偶证书 API 的连续线性 Farkas 分析器。
/// Continuous-LP Farkas analyzer backed by the SCIP LP dual-certificate API.
/// </summary>
/// <remarks>
/// 诊断在自己的 SCIP 实例上重放模型，因此不受主求解运行的预处理或分支定界影响；整数列会被拒绝，因为 LP 证书
/// 不能证明混合整数模型不可行。
/// The diagnosis replays the model on its own SCIP instance, so presolving and branch-and-bound of the primary run cannot
/// affect it; integer columns are rejected because an LP certificate cannot prove a mixed-integer model infeasible.
/// </remarks>
internal sealed class ScipFarkasInfeasibilityAnalyzer : IFarkasInfeasibilityAnalyzer<ILinearTriadModelView> {
    /// <summary>Farkas 乘子非零判定容差 / Tolerance for a nonzero Farkas multiplier.</summary>
    private const double FarkasTolerance = 1.0e-9;

    private readonly ScipLinearSolver _solver;
    private readonly IisConfig _iisConfig;

    /// <inheritdoc/>
    public InfeasibilityAnalyzerCapabilities Capabilities { get; } = new(
        Source: InfeasibilityEvidenceSource.Farkas,
        ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp },
        Exact: true);

    /// <inheritdoc/>
    public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.Farkas;

    /// <summary>创建 SCIP Farkas 分析器 / Create a SCIP Farkas analyzer.</summary>
    /// <param name="solver">发起诊断的 SCIP 线性求解器 / SCIP linear solver that owns the diagnostic run.</param>
    /// <param name="iisConfig">诊断配置 / Diagnostic configuration.</param>
    internal ScipFarkasInfeasibilityAnalyzer(ScipLinearSolver solver, IisConfig iisConfig) {
        _solver = solver;
        _iisConfig = iisConfig;
    }

    /// <inheritdoc/>
    public async Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        ILinearTriadModelView model,
        CancellationToken cancellationToken = default) {
        var stopwatch = Stopwatch.StartNew();
        Result<ScipFarkasOutcome, ErrorCode, Error<ErrorCode>> run;
        try {
            run = await _solver.RunFarkasDiagnosticAsync(model, _iisConfig, cancellationToken);
        } catch (Exception error) when (error is not OutOfMemoryException) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                $"SCIP Farkas 诊断失败：{error.Message} / SCIP Farkas diagnosis failed: {error.Message}"));
        }
        if (run is not Ok<ScipFarkasOutcome, ErrorCode, Error<ErrorCode>> diagnostic) {
            return ScipLpDualSupport.Propagate<InfeasibilityEvidence, ScipFarkasOutcome>(
                run,
                "SCIP Farkas 诊断返回了无效状态 / The SCIP Farkas diagnosis returned an invalid state");
        }
        if (diagnostic.Value.Report.ProblemStatus != ProblemStatus.Infeasible) {
            return Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.ORModelInfeasible,
                "SCIP Farkas 要求模型状态为不可行 / SCIP Farkas requires an infeasible model"));
        }
        return BuildEvidence(model, diagnostic.Value, stopwatch);
    }

    private static Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> BuildEvidence(
        ILinearTriadModelView model,
        ScipFarkasOutcome outcome,
        Stopwatch stopwatch) {
        ScipLinearSnapshot snapshot = outcome.Snapshot;
        if (outcome.Farkas.Count != snapshot.Rows.Length || model.Variables.Count != snapshot.Columns.Length) {
            return Failed("SCIP Farkas 乘子与约束行不同序 / SCIP Farkas multipliers are not aligned with the constraint rows");
        }
        var rowIds = new List<ConstraintId>();
        var members = new List<InfeasibilityMember>();
        double[] multipliers = new double[snapshot.Rows.Length];
        for (int row = 0; row < multipliers.Length; row++) {
            double multiplier = outcome.Farkas[row].ToDouble();
            if (!double.IsFinite(multiplier)) {
                return Failed($"第 {row} 行 Farkas 乘子无效 / The Farkas multiplier for row {row} is invalid");
            }
            multipliers[row] = multiplier;
            if (System.Math.Abs(multiplier) > FarkasTolerance) {
                ConstraintId id = model.DiagnosticConstraintId(row);
                rowIds.Add(id);
                members.Add(new InfeasibilityMember.ConstraintMember(id));
            }
        }
        // 按列聚合证书系数，据此判定不可行性用到了哪一侧的变量界。 / Aggregate the certificate coefficients per column to learn which bound side the proof used.
        double[] aggregated = new double[snapshot.Columns.Length];
        for (int row = 0; row < multipliers.Length; row++) {
            if (System.Math.Abs(multipliers[row]) <= FarkasTolerance) {
                continue;
            }
            ScipRow scipRow = snapshot.Rows[row];
            for (int entry = 0; entry < scipRow.Columns.Length; entry++) {
                aggregated[scipRow.Columns[entry]] += multipliers[row] * scipRow.Coefficients[entry];
            }
        }
        var bounds = new List<VariableBoundRef>();
        for (int column = 0; column < aggregated.Length; column++) {
            if (aggregated[column] > FarkasTolerance) {
                if (double.IsNegativeInfinity(snapshot.Columns[column].Lower)) {
                    return Failed("SCIP Farkas 证书需要有限下界但模型变量无下界 / "
                        + "SCIP Farkas certificate requires a finite lower bound");
                }
                bounds.Add(new VariableBoundRef(
                    model.Variables[column].DiagnosticVariableId(),
                    BoundSide.Lower));
            } else if (aggregated[column] < -FarkasTolerance) {
                if (double.IsPositiveInfinity(snapshot.Columns[column].Upper)) {
                    return Failed("SCIP Farkas 证书需要有限上界但模型变量无上界 / "
                        + "SCIP Farkas certificate requires a finite upper bound");
                }
                bounds.Add(new VariableBoundRef(
                    model.Variables[column].DiagnosticVariableId(),
                    BoundSide.Upper));
            }
        }
        // 证书成员同时列出被用到的变量界，与 侧 members 语义一致。 / Members also list the used variable bounds, matching the Kotlin member semantics.
        foreach (VariableBoundRef bound in bounds) {
            members.Add(new InfeasibilityMember.VariableBoundMember(bound));
        }
        if (members.Count == 0) {
            return Failed("SCIP 未返回非零 Farkas 成分，可能不可行性来自未暴露的变量界 / "
                + "SCIP returned no nonzero Farkas component; infeasibility may come from unsupported variable-bound certificates");
        }
        stopwatch.Stop();
        return Results.Ok(new InfeasibilityEvidence {
            Source = InfeasibilityEvidenceSource.Farkas,
            Exactness = EvidenceExactness.Exact,
            Completeness = EvidenceCompleteness.Complete,
            ConstraintIds = new HashSet<ConstraintId>(rowIds),
            VariableBoundIds = new HashSet<VariableId>(bounds.Select(bound => bound.VariableId)),
            Elapsed = stopwatch.Elapsed,
            Validity = EvidenceValidity.Verified,
            Minimality = EvidenceMinimality.NotChecked,
            VariableBoundRefs = new HashSet<VariableBoundRef>(bounds),
            Members = new HashSet<InfeasibilityMember>(members),
            Reference = "scip.getDualFarkasLinear",
        });
    }

    private static Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> Failed(string message) =>
        Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, message));
}
