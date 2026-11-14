#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Report;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// 线性 IIS 模型：从原模型投影出的不可行子系统，外加可选的守卫约束行。
/// Linear IIS model: an infeasible subsystem projected from the origin model, plus optional guard rows.
/// </summary>
/// <remarks>
/// 列索引重排为连续位置，行系数同步重映射，因此子系统可以直接重建后送入后端；<see cref="OriginRows"/> 与
/// <see cref="OriginColumns"/> 保留回到原模型行、列的溯源。存在守卫行时子系统只是"保留这些行才不可行"，
/// 因此 <see cref="RelaxedFeasible"/> 为 false，证据按部分完备处理。
/// Column indices are renumbered to contiguous positions and row coefficients are remapped with them, so the subsystem can
/// be rebuilt by a backend directly; <see cref="OriginRows"/> and <see cref="OriginColumns"/> keep the trace back to origin
/// rows and columns. With guard rows the subsystem is infeasible only while those rows stay, so
/// <see cref="RelaxedFeasible"/> is false and the evidence counts as partial.
/// </remarks>
public sealed class LinearIisModel : BasicLinearTriadModelView {
    private readonly BasicLinearTriadModel _impl;

    /// <summary>IIS 子系统约束行 / Constraint rows of the IIS subsystem.</summary>
    public LinearConstraintBatch Rows { get; }
    /// <summary>守卫约束行；为空表示子系统自身已经不可行 / Guard rows; null when the subsystem alone is infeasible.</summary>
    public LinearConstraintBatch? GuardConstraints { get; }
    /// <summary>每个子系统行对应的原模型行索引 / Origin row index for each subsystem row.</summary>
    public IReadOnlyList<int> OriginRows { get; }
    /// <summary>每个子系统列对应的原模型列索引 / Origin column index for each subsystem column.</summary>
    public IReadOnlyList<int> OriginColumns { get; }
    /// <summary>生成该子系统的原模型视图 / The origin model view the subsystem was projected from.</summary>
    public ILinearTriadModelView Origin { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ModelViewVariable> Variables => _impl.Variables;
    /// <inheritdoc/>
    public ModelConstraint<LinearConstraintCell> Constraints => Rows;
    /// <inheritdoc/>
    public string Name => _impl.Name;
    /// <summary>是否为松弛可行（无需守卫约束）/ Whether relaxed-feasible (no guard constraints needed).</summary>
    public bool RelaxedFeasible => GuardConstraints is null;

    internal LinearIisModel(
        IReadOnlyList<ModelViewVariable> variables,
        LinearConstraintBatch rows,
        LinearConstraintBatch? guardConstraints,
        IReadOnlyList<int> originRows,
        IReadOnlyList<int> originColumns,
        ILinearTriadModelView origin,
        string name) {
        Rows = rows;
        GuardConstraints = guardConstraints;
        OriginRows = originRows;
        OriginColumns = originColumns;
        Origin = origin;
        _impl = new BasicLinearTriadModel(
            variables: variables,
            constraints: rows,
            name: name);
    }

    /// <inheritdoc/>
    public Try ExportLP(StreamWriter? writer) => _impl.ExportLP(writer);

    /// <inheritdoc/>
    public void Dispose() {
        _impl.Dispose();
        GuardConstraints?.Dispose();
    }
}

/// <summary>
/// 线性 IIS 计算结果：物化的子系统模型与其结构化证据。
/// Linear IIS outcome: the materialized subsystem model together with its structured evidence.
/// </summary>
/// <param name="Model">物化的 IIS 模型 / Materialized IIS model.</param>
/// <param name="Evidence">结构化不可行证据 / Structured infeasibility evidence.</param>
public sealed record LinearIisResult(LinearIisModel Model, InfeasibilityEvidence Evidence);

/// <summary>
/// 线性 IIS 计算入口与原生证据物化。
/// Linear IIS entry point and native-evidence materialization.
/// </summary>
public static class LinearIisCompute {
    /// <summary>
    /// 通过诊断编排器计算线性 IIS，必要时回退到 legacy 弹性与删除过滤。
    /// Compute the linear IIS through the diagnostic orchestrator, falling back to the legacy elastic and deletion
    /// filtering when necessary.
    /// </summary>
    /// <remarks>
    /// 编排器返回的证据若没有模型 artifact，先尝试按原生 IIS 物化；仍拿不到模型时才由 legacy 算法亲自构造，
    /// 因此没有原生 IIS 的后端也能拿到归约后的子系统。
    /// When the orchestrator evidence carries no model artifact the native IIS evidence is materialized first, and only after
    /// that fails does the legacy engine build the subsystem itself, so backends without native IIS still return a reduced
    /// model.
    /// </remarks>
    /// <param name="solver">线性求解器 / Linear solver.</param>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <param name="config">IIS 配置 / IIS configuration.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>IIS 结果或错误 / IIS outcome or an error.</returns>
    public static async Task<Result<LinearIisResult, ErrorCode, Error<ErrorCode>>> ComputeIISAsync(
        this IAbstractLinearSolver solver,
        ILinearTriadModelView model,
        IisConfig config,
        CancellationToken cancellationToken = default) {
        var legacy = new LegacyElasticInfeasibilityAnalyzer(
            solver: solver,
            config: config);
        var analyzers = new List<IInfeasibilityAnalyzer<ILinearTriadModelView>>(
            solver.DiagnosticAnalyzers(config)) {
            legacy,
        };
        var orchestrator = new InfeasibilityDiagnosticOrchestrator<ILinearTriadModelView>(
            analyzers: analyzers,
            solverCapabilities: solver.Descriptor?.Capabilities);
        Result<MaterializedInfeasibilityEvidence<LinearIisModel>, ErrorCode, Error<ErrorCode>> result =
            await orchestrator.AnalyzeMaterializedAsync<LinearIisModel>(
                model: model,
                cancellationToken: cancellationToken);
        if (result is not Ok<MaterializedInfeasibilityEvidence<LinearIisModel>, ErrorCode, Error<ErrorCode>> ok) {
            return Propagate<LinearIisResult, MaterializedInfeasibilityEvidence<LinearIisModel>>(result);
        }
        LinearIisModel? artifact = ok.Value.Artifact ?? MaterializeNativeIis(
            model: model,
            evidence: ok.Value.Evidence);
        if (artifact is not null) {
            return Results.Ok(new LinearIisResult(
                Model: artifact,
                Evidence: ok.Value.Evidence));
        }
        // 证据既没有 artifact 也不是原生 IIS，只能让 legacy 算法自己构造模型。
        // The evidence has neither an artifact nor a native IIS, so only the legacy engine can build the model.
        Result<MaterializedInfeasibilityEvidence<LinearIisModel>, ErrorCode, Error<ErrorCode>> fallback =
            await legacy.AnalyzeMaterializedAsync(
                model: model,
                cancellationToken: cancellationToken);
        if (fallback is Ok<MaterializedInfeasibilityEvidence<LinearIisModel>, ErrorCode, Error<ErrorCode>> fallbackOk) {
            return fallbackOk.Value.Artifact is { } legacyModel
                ? Results.Ok(new LinearIisResult(
                    Model: legacyModel,
                    Evidence: fallbackOk.Value.Evidence))
                : Results.Failed<LinearIisResult>(new Err<ErrorCode>(
                    ErrorCode.Other,
                    "Legacy IIS 未返回模型 artifact / Legacy IIS did not return a model artifact"));
        }
        return Propagate<LinearIisResult, MaterializedInfeasibilityEvidence<LinearIisModel>>(fallback);
    }

    /// <summary>
    /// 把原生 IIS 证据物化为归约后的线性模型。
    /// Materialize native IIS evidence as a reduced linear model.
    /// </summary>
    /// <remarks>
    /// 只有证据来源为原生 IIS 时才物化；被选中的列包含证据命中的变量界所在列与选中行的全部非零列，列边界只保留
    /// 证据指出的那一侧，另一侧放开到无穷。行列都为空时返回 null，让下一条策略继续。
    /// Only native IIS evidence materializes; the selected columns are those holding an evidenced variable bound plus every
    /// nonzero column of the selected rows, and a selected column keeps only the evidenced bound side while the other opens
    /// to infinity. It returns null when neither rows nor columns are selected so the next strategy can continue.
    /// </remarks>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <param name="evidence">结构化不可行证据 / Structured infeasibility evidence.</param>
    /// <returns>IIS 模型，无法物化时为 null / The IIS model, or null when it cannot be materialized.</returns>
    internal static LinearIisModel? MaterializeNativeIis(
        ILinearTriadModelView model,
        InfeasibilityEvidence evidence) {
        if (evidence.Source != InfeasibilityEvidenceSource.NativeIis
            || model.Constraints is not LinearConstraintBatch batch) {
            return null;
        }
        var rows = new List<int>();
        for (int index = 0; index < batch.Size; index++) {
            if (evidence.ConstraintIds.Contains(model.DiagnosticConstraintId(index))
                || evidence.ConstraintIds.Contains(new ConstraintId($"constraint-{index}"))) {
                rows.Add(index);
            }
        }
        ILookup<VariableId, VariableBoundRef> boundByVariable =
            evidence.VariableBoundRefs.ToLookup(reference => reference.VariableId);
        var selected = new SortedSet<int>();
        foreach (int row in rows) {
            foreach (LinearConstraintCell cell in batch.Lhs[row]) {
                if (cell.ColIndex >= 0 && cell.ColIndex < model.Variables.Count) {
                    selected.Add(cell.ColIndex);
                }
            }
        }
        for (int index = 0; index < model.Variables.Count; index++) {
            VariableId[] ids = [
                model.Variables[index].DiagnosticVariableId(),
                new VariableId($"variable-{index}"),
            ];
            if (ids.Any(boundByVariable.Contains)
                || evidence.VariableDomainRefs.Any(reference => ids.Contains(reference.VariableId))) {
                selected.Add(index);
            }
        }
        if (rows.Count == 0 && selected.Count == 0) {
            return null;
        }
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        int[] columns = selected.ToArray();
        int[] columnMap = new int[model.Variables.Count];
        Array.Fill(columnMap, -1);
        var variables = new List<ModelViewVariable>(columns.Length);
        for (int newIndex = 0; newIndex < columns.Length; newIndex++) {
            int oldIndex = columns[newIndex];
            columnMap[oldIndex] = newIndex;
            ModelViewVariable source = model.Variables[oldIndex];
            List<VariableBoundRef> refs = [
                .. boundByVariable[source.DiagnosticVariableId()],
                .. boundByVariable[new VariableId($"variable-{oldIndex}")],
            ];
            variables.Add(ProjectColumn(
                source: source,
                index: newIndex,
                lowerBound: refs.Any(reference => reference.Side == BoundSide.Lower)
                    ? source.LowerBound : constants.NegativeInfinity!.Value,
                upperBound: refs.Any(reference => reference.Side == BoundSide.Upper)
                    ? source.UpperBound : constants.PositiveInfinity!.Value));
        }
        return new LinearIisModel(
            variables: variables,
            rows: ProjectRows(
                source: batch,
                rows: rows,
                columnMap: columnMap),
            guardConstraints: null,
            originRows: rows,
            originColumns: columns,
            origin: model,
            name: $"{model.Name}_iis");
    }

    /// <summary>按选中行与列投影线性约束批次 / Project a linear constraint batch onto the selected rows and columns.</summary>
    /// <param name="source">原约束批次 / Origin constraint batch.</param>
    /// <param name="rows">选中行索引 / Selected row indices.</param>
    /// <param name="columnMap">原列到投影列的索引映射，负值表示丢弃 / Origin-to-projected map; a negative value drops the entry.</param>
    /// <returns>投影后的约束批次 / The projected constraint batch.</returns>
    internal static LinearConstraintBatch ProjectRows(
        LinearConstraintBatch source,
        IReadOnlyList<int> rows,
        IReadOnlyList<int> columnMap) {
        var projected = new List<SparseVector>(rows.Count);
        foreach (int row in rows) {
            var entries = new List<SparseVectorEntry>();
            foreach (LinearConstraintCell cell in source.Lhs[row]) {
                int target = cell.ColIndex >= 0 && cell.ColIndex < columnMap.Count
                    ? columnMap[cell.ColIndex] : -1;
                if (target >= 0) {
                    entries.Add(new SparseVectorEntry(target, cell.Coefficient));
                }
            }
            projected.Add(new SparseVector(entries));
        }
        IReadOnlyList<ModelElementIdentity>? identities = source.Identities.Count == source.Size
            ? [.. rows.Select(index => source.Identities[index])]
            : null;
        return new LinearConstraintBatch(
            lhs: new SparseMatrix(projected),
            signs: [.. rows.Select(index => source.Signs[index])],
            rhs: [.. rows.Select(index => source.Rhs[index])],
            names: [.. rows.Select(index => source.Names[index])],
            sources: [.. rows.Select(index => source.Sources[index])],
            identities: identities,
            identityNamespace: source.IdentityNamespace,
            identitySchemaVersion: source.IdentitySchemaVersion);
    }

    /// <summary>以新索引与新边界复制列，保留名称、类型、松弛与身份 / Copy a column with a new index and bounds, keeping name, type, slack and identity.</summary>
    /// <param name="source">原列 / Origin column.</param>
    /// <param name="index">投影后的列索引 / Projected column index.</param>
    /// <param name="lowerBound">下界 / Lower bound.</param>
    /// <param name="upperBound">上界 / Upper bound.</param>
    /// <returns>投影后的列 / The projected column.</returns>
    internal static ModelViewVariable ProjectColumn(
        ModelViewVariable source,
        int index,
        Flt64 lowerBound,
        Flt64 upperBound) => new(
            index: index,
            lowerBound: lowerBound,
            upperBound: upperBound,
            type: source.Type,
            name: source.Name,
            initialResult: source.InitialResult,
            slack: source.Slack,
            origin: source.Origin,
            identity: source.Identity,
            identityNamespace: source.IdentityNamespace,
            identitySchemaVersion: source.IdentitySchemaVersion);

    /// <summary>传播非成功结果，成功分支属于调用错误 / Propagate a non-success result; the success branch is a caller error.</summary>
    /// <typeparam name="T">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="S">来源成功类型 / Source success type.</typeparam>
    /// <param name="result">来源结果 / Source result.</param>
    /// <returns>转换后的失败或致命结果 / The converted failure or fatal result.</returns>
    internal static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "不可行分析结果状态无效 / Invalid infeasibility analyzer result state")),
        };
}
