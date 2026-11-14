#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// **二次模型的旧式弹性过滤 IIS 分析器** / The legacy elastic-filter IIS analyzer for quadratic models.
/// </summary>
/// <remarks>
/// **B1260 补的是"机器都在、只差一个适配器"的那种缺口。**
///
/// **逐项核对的结果：** **`QuadraticLegacyIisCompute`（含 `ComputeLegacyIISAsync`）、`QuadraticIisModel`、
/// `QuadraticElasticFilterResult`、`QuadraticIisEvidenceSource`、以及 `DiagnosticConstraintId` 的二次重载
/// <b>全都在</b>；线性版的 `LegacyElasticInfeasibilityAnalyzer` 也在。**
///
/// **缺的只是这个类：** **没有它，那整套二次 IIS 机器<b>无法经分析器框架被调用</b>——
/// 与 B1250 的"投影有了、没人调用"是同一类缺口，只是这次缺的是一个适配器而不是一次调用。**
///
/// ### 与线性版的一处实质差别，必须写清
///
/// **线性版用 `iis.RelaxedFeasible` 决定报 `Complete` 还是 `Partial`。**
/// **而 `QuadraticIisModel` <b>只有三个字段</b>（`Subsystem`／`OriginRows`／`OriginColumns`），
/// 没有这个标志。**
///
/// **所以二次版<b>一律报 `Partial`</b>——** **在一个拿不到依据的地方，
/// 报弱的那一边；报强的那一边会把"没检查"说成"检查过"。**
///
/// **这不是缺陷，是<b>信息在传递中丢失</b>：** **`ComputeLegacyIISAsync` 内部其实知道松弛是否可行，
/// 但它没有把这件事带出来。** **要修就得改那个返回类型，而那会牵动所有调用方，
/// 所以本批选择"报弱"，并把原因写在这里。**
///
/// **B1260 fills the "everything is here except an adapter" kind of gap.**
///
/// **The survey found `QuadraticLegacyIisCompute`, `QuadraticIisModel`, `QuadraticElasticFilterResult`,
/// `QuadraticIisEvidenceSource` and the quadratic `DiagnosticConstraintId` overload all present, along with the linear
/// analyzer.** **Only this class was missing — and without it the whole quadratic IIS machinery <b>cannot be reached
/// through the analyzer framework</b>, the same shape as B1250's "the projection exists and nobody calls it", except the
/// missing piece is an adapter rather than a call site.**
///
/// **One substantive difference from the linear version, stated plainly: the linear analyzer uses
/// `iis.RelaxedFeasible` to choose between `Complete` and `Partial`, while `QuadraticIisModel` has <b>only three
/// fields</b> and no such flag.** **So the quadratic analyzer always reports `Partial`: where the ground is missing,
/// report the weaker side — claiming the stronger turns "not checked" into "checked".**
///
/// **That is not a defect but <b>information lost in transit</b>: `ComputeLegacyIISAsync` does know whether the relaxation
/// was feasible, it merely does not carry it out.** **Fixing that means changing the return type and touching every caller,
/// so this batch reports weak and records why.**
/// </remarks>
public sealed class LegacyElasticQuadraticInfeasibilityAnalyzer :
    ICapabilityAwareInfeasibilityAnalyzer<IQuadraticTetradModelView>,
    IMaterializingInfeasibilityAnalyzer<IQuadraticTetradModelView, QuadraticIisModel> {
    private static readonly Flt64 _negativeInfinity = NumericConstantsRegistry.For<Flt64>().NegativeInfinity!.Value;
    private static readonly Flt64 _positiveInfinity = NumericConstantsRegistry.For<Flt64>().PositiveInfinity!.Value;

    private readonly IAbstractQuadraticSolver _solver;
    private readonly IisConfig _config;

    /// <inheritdoc/>
    public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.ElasticFilter;

    /// <inheritdoc/>
    public InfeasibilityAnalyzerCapabilities Capabilities { get; } = new(
        Source: InfeasibilityEvidenceSource.ElasticFilter,
        ModelTypes: new HashSet<SolverModelType> {
            SolverModelType.Qp,
            SolverModelType.Qcp,
        },
        Exact: false
    );

    /// <summary>
    /// **构造一个二次旧式 IIS 分析器 / Construct a quadratic legacy IIS analyzer.**
    /// </summary>
    /// <param name="solver">二次求解器 / The quadratic solver.</param>
    /// <param name="config">IIS 配置 / The IIS config.</param>
    public LegacyElasticQuadraticInfeasibilityAnalyzer(
        IAbstractQuadraticSolver solver,
        IisConfig? config = null) {
        _solver = solver ?? throw new ArgumentNullException(nameof(solver));
        _config = config ?? new IisConfig();
    }

    /// <inheritdoc/>
    public async Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
        IQuadraticTetradModelView model,
        CancellationToken cancellationToken = default) {
        Result<MaterializedInfeasibilityEvidence<QuadraticIisModel>, ErrorCode, Error<ErrorCode>> result =
            await AnalyzeMaterializedAsync(model, cancellationToken).ConfigureAwait(false);
        return result is Ok<MaterializedInfeasibilityEvidence<QuadraticIisModel>, ErrorCode, Error<ErrorCode>> ok
            ? Results.Ok(ok.Value.Evidence)
            : LinearIisCompute
                .Propagate<InfeasibilityEvidence, MaterializedInfeasibilityEvidence<QuadraticIisModel>>(result);
    }

    /// <inheritdoc/>
    public async Task<Result<MaterializedInfeasibilityEvidence<QuadraticIisModel>, ErrorCode, Error<ErrorCode>>>
        AnalyzeMaterializedAsync(
            IQuadraticTetradModelView model,
            CancellationToken cancellationToken = default) {
        var started = Stopwatch.StartNew();
        Result<QuadraticIisModel, ErrorCode, Error<ErrorCode>> result =
            await QuadraticLegacyIisCompute.ComputeLegacyIISAsync(
                model: model,
                solver: _solver,
                config: _config,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result is not Ok<QuadraticIisModel, ErrorCode, Error<ErrorCode>> ok) {
            return LinearIisCompute
                .Propagate<MaterializedInfeasibilityEvidence<QuadraticIisModel>, QuadraticIisModel>(result);
        }
        return Results.Ok(Build(
            model: model,
            iis: ok.Value,
            elapsed: started.Elapsed));
    }

    private static MaterializedInfeasibilityEvidence<QuadraticIisModel> Build(
        IQuadraticTetradModelView model,
        QuadraticIisModel iis,
        TimeSpan elapsed) {
        List<ConstraintId> constraintIds = [.. iis.OriginRows.Select(model.DiagnosticConstraintId)];
        IReadOnlyList<ModelViewVariable> variables = iis.Subsystem.Variables;
        var variableIds = new List<VariableId>(variables.Count);
        foreach (int column in iis.OriginColumns) {
            variableIds.Add(model.Variables[column].DiagnosticVariableId());
        }

        var bounds = new List<VariableBoundRef>();
        var domains = new List<VariableDomainRef>();
        for (int column = 0; column < variables.Count && column < variableIds.Count; column++) {
            ModelViewVariable variable = variables[column];
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

        return new MaterializedInfeasibilityEvidence<QuadraticIisModel>(
            Evidence: new InfeasibilityEvidence {
                // **一律 `ElasticFilter` 与 `Partial`：** **`QuadraticIisModel` 不携带"松弛是否可行"，
                // 而线性版正是用那个标志去在两档之间选。拿不到依据时报弱的那一档。**
                // **Always `ElasticFilter` and `Partial`: `QuadraticIisModel` carries no "was the relaxation feasible"
                // flag, which is what the linear version uses to pick between two grades. Without evidence, report the
                // weaker one.**
                Source = InfeasibilityEvidenceSource.ElasticFilter,
                Exactness = EvidenceExactness.Heuristic,
                Completeness = EvidenceCompleteness.Partial,
                ConstraintIds = new HashSet<ConstraintId>(constraintIds),
                VariableBoundIds = new HashSet<VariableId>(bounds.Select(static bound => bound.VariableId)),
                Elapsed = elapsed,
                Validity = EvidenceValidity.Heuristic,
                Minimality = EvidenceMinimality.NotChecked,
                VariableBoundRefs = new HashSet<VariableBoundRef>(bounds),
                VariableDomainRefs = new HashSet<VariableDomainRef>(domains),
                Members = new HashSet<InfeasibilityMember>(members),
                UnavailableReason = new SolveIssue(
                    Code: "legacy-quadratic-iis-heuristic",
                    Category: SolveIssueCategory.Unsupported,
                    Message: "旧二次 IIS 过滤结果未经过独立 IIS 复验，且未携带松弛可行性 / " +
                        "Legacy quadratic IIS filtering is not an independently verified IIS and carries no "
                        + "relaxation-feasibility flag"),
            },
            Artifact: iis);
    }
}
