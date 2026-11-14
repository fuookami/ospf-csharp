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
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// 保留旧弹性与删除过滤算法并明确标记为启发式证据的线性 IIS 分析器。
/// Linear IIS analyzer that keeps the legacy elastic and deletion filtering and grades its evidence as heuristic.
/// </summary>
/// <remarks>
/// 它是编排链的最后一条策略：既不精确也不保证最小，因此 <c>Exactness</c> 与 <c>Validity</c> 都是启发式，
/// <c>Minimality</c> 未检查；弹性过滤直接命中时证据视为完整，需要守卫约束的删除过滤只算部分完整。
/// 约束与变量身份直接取用 IIS 模型自带的原行、原列映射，因此不依赖展示名或结构相等做行匹配。
/// It is the last strategy in the chain: neither exact nor minimal, so <c>Exactness</c> and <c>Validity</c> stay heuristic
/// and <c>Minimality</c> is unchecked; evidence is complete when the elastic filter hits directly and partial once guard
/// rows are required by deletion filtering.
/// Constraint and variable identities come from the IIS model's own origin-row and origin-column maps, so no row matching
/// by display names or structural equality is needed.
/// </remarks>
public sealed class LegacyElasticInfeasibilityAnalyzer :
    ICapabilityAwareInfeasibilityAnalyzer<ILinearTriadModelView>,
    IMaterializingInfeasibilityAnalyzer<ILinearTriadModelView, LinearIisModel> {
    private static readonly Flt64 _negativeInfinity = NumericConstantsRegistry.For<Flt64>().NegativeInfinity!.Value;
    private static readonly Flt64 _positiveInfinity = NumericConstantsRegistry.For<Flt64>().PositiveInfinity!.Value;

    private readonly IAbstractLinearSolver _solver;
    private readonly IisConfig _config;

    /// <inheritdoc/>
    public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.ElasticFilter;

    /// <inheritdoc/>
    public InfeasibilityAnalyzerCapabilities Capabilities { get; } = new(
        Source: InfeasibilityEvidenceSource.ElasticFilter,
        ModelTypes: new HashSet<SolverModelType> {
            SolverModelType.Lp,
            SolverModelType.Mip,
        },
        Exact: false);

    /// <summary>创建 legacy 弹性 IIS 分析器 / Create the legacy elastic IIS analyzer.</summary>
    /// <param name="solver">用于反复求解弹性模型的线性求解器 / Linear solver that re-solves the elastic model.</param>
    /// <param name="config">IIS 配置；默认使用标准诊断配置 / IIS configuration; the default diagnostic configuration when null.</param>
    public LegacyElasticInfeasibilityAnalyzer(
        IAbstractLinearSolver solver,
        IisConfig? config = null) {
        _solver = solver;
        _config = config ?? new IisConfig();
    }

    /// <inheritdoc/>
    public async Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        ILinearTriadModelView model,
        CancellationToken cancellationToken = default) {
        Result<MaterializedInfeasibilityEvidence<LinearIisModel>, ErrorCode, Error<ErrorCode>> result =
            await AnalyzeMaterializedAsync(
                model: model,
                cancellationToken: cancellationToken);
        return result is Ok<MaterializedInfeasibilityEvidence<LinearIisModel>, ErrorCode, Error<ErrorCode>> ok
            ? Results.Ok(ok.Value.Evidence)
            : LinearIisCompute.Propagate<InfeasibilityEvidence, MaterializedInfeasibilityEvidence<LinearIisModel>>(
                result);
    }

    /// <inheritdoc/>
    public async Task<Result<MaterializedInfeasibilityEvidence<LinearIisModel>, ErrorCode, Error<ErrorCode>>>
        AnalyzeMaterializedAsync(
            ILinearTriadModelView model,
            CancellationToken cancellationToken = default) {
        var started = Stopwatch.StartNew();
        Result<LinearIisModel, ErrorCode, Error<ErrorCode>> result = await _solver.ComputeLegacyIISAsync(
            model: model,
            config: _config,
            cancellationToken: cancellationToken);
        if (result is not Ok<LinearIisModel, ErrorCode, Error<ErrorCode>> ok) {
            return LinearIisCompute
                .Propagate<MaterializedInfeasibilityEvidence<LinearIisModel>, LinearIisModel>(result);
        }
        return Results.Ok(Build(
            model: model,
            iis: ok.Value,
            elapsed: started.Elapsed));
    }

    private static MaterializedInfeasibilityEvidence<LinearIisModel> Build(
        ILinearTriadModelView model,
        LinearIisModel iis,
        TimeSpan elapsed) {
        List<ConstraintId> constraintIds = [.. iis.OriginRows.Select(model.DiagnosticConstraintId)];
        VariableId[] variableIds = [
            .. iis.OriginColumns.Select(index => model.Variables[index].DiagnosticVariableId()),
        ];
        var bounds = new List<VariableBoundRef>();
        var domains = new List<VariableDomainRef>();
        for (int column = 0; column < iis.Variables.Count; column++) {
            ModelViewVariable variable = iis.Variables[column];
            if (variable.LowerBound != _negativeInfinity) {
                bounds.Add(new VariableBoundRef(variableIds[column], BoundSide.Lower));
            }
            if (variable.UpperBound != _positiveInfinity) {
                bounds.Add(new VariableBoundRef(variableIds[column], BoundSide.Upper));
            }
            if (variable.Type.IsIntegerType) {
                domains.Add(new VariableDomainRef(variableIds[column]));
            }
        }
        var members = new List<InfeasibilityMember>();
        members.AddRange(constraintIds.Distinct().Select(id =>
            (InfeasibilityMember)new InfeasibilityMember.ConstraintMember(id)));
        members.AddRange(bounds.Select(bound =>
            (InfeasibilityMember)new InfeasibilityMember.VariableBoundMember(bound)));
        members.AddRange(domains.Select(domain =>
            (InfeasibilityMember)new InfeasibilityMember.VariableDomainMember(domain)));
        bool relaxedFeasible = iis.RelaxedFeasible;
        return new MaterializedInfeasibilityEvidence<LinearIisModel>(
            Evidence: new InfeasibilityEvidence {
                Source = relaxedFeasible
                    ? InfeasibilityEvidenceSource.ElasticFilter
                    : InfeasibilityEvidenceSource.DeletionFilter,
                Exactness = EvidenceExactness.Heuristic,
                Completeness = relaxedFeasible
                    ? EvidenceCompleteness.Complete
                    : EvidenceCompleteness.Partial,
                ConstraintIds = new HashSet<ConstraintId>(constraintIds),
                VariableBoundIds = new HashSet<VariableId>(bounds.Select(bound => bound.VariableId)),
                Elapsed = elapsed,
                Validity = EvidenceValidity.Heuristic,
                Minimality = EvidenceMinimality.NotChecked,
                VariableBoundRefs = new HashSet<VariableBoundRef>(bounds),
                VariableDomainRefs = new HashSet<VariableDomainRef>(domains),
                Members = new HashSet<InfeasibilityMember>(members),
                UnavailableReason = new SolveIssue(
                    Code: "legacy-iis-heuristic",
                    Category: SolveIssueCategory.Unsupported,
                    Message: "旧 IIS 过滤结果未经过独立 IIS 复验 / " +
                        "Legacy IIS filtering is not an independently verified IIS"),
            },
            Artifact: iis);
    }
}
