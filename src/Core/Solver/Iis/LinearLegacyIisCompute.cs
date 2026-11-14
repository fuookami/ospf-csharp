#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// 弹性模型的松弛工作区：按索引改写松弛列上界，其余结构与弹性模型共享。
/// Slack workspace of an elastic model: it rewrites slack upper bounds by index and shares the rest with the elastic model.
/// </summary>
/// <remarks>
/// IIS 过滤需要在同一模型上反复求解并只改动松弛列的上界，因此这里持有可变列副本，由 <see cref="Dispose"/> 释放弹性模型。
/// IIS filtering re-solves the same model while only slack upper bounds change, so this keeps a mutable column copy and
/// releases the elastic model through <see cref="Dispose"/>.
/// </remarks>
internal sealed class ElasticSlackWorkspace : ILinearTriadModelView {
    private readonly LinearTriadModel _model;
    private readonly ModelViewVariable[] _variables;

    internal ElasticSlackWorkspace(LinearTriadModel model) {
        _model = model;
        _variables = new ModelViewVariable[model.Variables.Count];
        for (int index = 0; index < _variables.Length; index++) {
            _variables[index] = model.Variables[index];
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModelViewVariable> Variables => _variables;
    /// <inheritdoc/>
    public ModelConstraint<LinearConstraintCell> Constraints => _model.Constraints;
    /// <inheritdoc/>
    public Objective<LinearObjectiveCell> Objective => _model.Objective;
    /// <inheritdoc/>
    public string Name => _model.Name;

    /// <summary>把松弛列的上界替换为给定值，其余字段保持不变 / Replace a slack column's upper bound and keep every other field.</summary>
    /// <param name="index">列索引 / Column index.</param>
    /// <param name="upperBound">新上界 / New upper bound.</param>
    internal void SetSlackUpperBound(int index, Flt64 upperBound) =>
        _variables[index] = _variables[index].WithDomain(
            lowerBound: _variables[index].LowerBound,
            upperBound: upperBound,
            type: _variables[index].Type);

    /// <inheritdoc/>
    public Try ExportLP(StreamWriter? writer) => _model.ExportLP(writer);

    /// <inheritdoc/>
    public void Dispose() => _model.Dispose();
}

/// <summary>弹性过滤的一轮结果 / One round of elastic filtering.</summary>
/// <param name="Feasible">弹性模型是否可行 / Whether the elastic model is feasible.</param>
/// <param name="Values">非零松弛的弹性列索引与取值 / Indices and values of nonzero slack columns.</param>
internal sealed record SlackFilter(bool Feasible, IReadOnlyDictionary<int, Flt64> Values);

/// <summary>删除过滤的一轮结果 / One round of deletion filtering.</summary>
/// <param name="Mis">留在不可行子系统中的弹性列索引 / Elastic column indices kept in the infeasible subsystem.</param>
/// <param name="Guard">守卫约束对应的弹性列索引 / Elastic column indices behind the guard constraints.</param>
internal sealed record DeletionFilter(IReadOnlySet<int> Mis, IReadOnlySet<int> Guard);

/// <summary>
/// 基于弹性与删除过滤的 legacy 线性 IIS 计算。
/// Legacy linear IIS computation based on elastic and deletion filtering.
/// </summary>
/// <remarks>
/// 与 Kotlin <c>computeLegacyIIS</c> 一致：先构造带二值指示列的弹性模型，依次只放开变量界松弛、只放开不等式与变量界
/// 松弛、放开全部松弛，三轮都不可行时改为逐个删除松弛的删除过滤。结果是原模型行列的子集，因此证据只是启发式的。
/// It mirrors Kotlin <c>computeLegacyIIS</c>: build the elastic model with binary indicators, then relax bound slacks only,
/// inequality and bound slacks, and finally every slack; when all three stay infeasible, switch to deletion filtering that
/// drops slacks one by one. The result is a subset of the origin rows and columns, so the evidence stays heuristic.
/// </remarks>
public static class LinearLegacyIisCompute {
    private static readonly Flt64 _negativeInfinity = NumericConstantsRegistry.For<Flt64>().NegativeInfinity!.Value;
    private static readonly Flt64 _positiveInfinity = NumericConstantsRegistry.For<Flt64>().PositiveInfinity!.Value;
    private static readonly IReadOnlyDictionary<int, Flt64> _emptySlacks = new Dictionary<int, Flt64>();

    /// <summary>
    /// 使用 legacy 弹性与删除过滤计算线性 IIS 模型。
    /// Compute a linear IIS model with the legacy elastic and deletion filtering.
    /// </summary>
    /// <remarks>
    /// 弹性模型固定使用"至少 2 个非零松弛、阈值取 <see cref="IisConfig.SlackTolerance"/>"的指示约束，与 Kotlin 一致。
    /// The elastic model always carries the indicator constraints "at least 2 nonzero slacks with the
    /// <see cref="IisConfig.SlackTolerance"/> threshold", matching Kotlin.
    /// </remarks>
    /// <param name="solver">线性求解器 / Linear solver.</param>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <param name="config">IIS 配置 / IIS configuration.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>IIS 模型或错误 / The IIS model or an error.</returns>
    public static async Task<Result<LinearIisModel, ErrorCode, Error<ErrorCode>>> ComputeLegacyIISAsync(
        this IAbstractLinearSolver solver,
        ILinearTriadModelView model,
        IisConfig config,
        CancellationToken cancellationToken = default) {
        if (model is not LinearTriadModel origin) {
            return Results.Failed<LinearIisModel>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "Legacy IIS 需要可构建弹性模型的线性三元模型 / " +
                    "Legacy IIS needs a linear triad model that can build an elastic model"));
        }
        Try valid = config.ValidateRuntime();
        if (!valid.IsOk) {
            return LinearIisCompute.Propagate<LinearIisModel, Success>(valid);
        }
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> elastic = origin.Elastic(
            minmaxSlack: false,
            minSlackAmount: (2UL, config.SlackTolerance));
        if (elastic is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> elasticOk) {
            return LinearIisCompute.Propagate<LinearIisModel, LinearTriadModel>(elastic);
        }
        var started = Stopwatch.StartNew();
        using var workspace = new ElasticSlackWorkspace(elasticOk.Value);
        IReadOnlyList<ConstraintSource> sources = workspace.Constraints.Sources;
        ulong boundAmount = (ulong)sources.Count(
            source => source is ConstraintSource.ElasticLowerBound or ConstraintSource.ElasticUpperBound);
        ulong constraintAmount = (ulong)sources.Count(source => source == ConstraintSource.Elastic);
        Result<SlackFilter, ErrorCode, Error<ErrorCode>> filtering = await PerformElasticFilteringAsync(
            workspace: workspace,
            solver: solver,
            config: config,
            cancellationToken: cancellationToken);
        if (filtering is not Ok<SlackFilter, ErrorCode, Error<ErrorCode>> filter) {
            return LinearIisCompute.Propagate<LinearIisModel, SlackFilter>(filtering);
        }
        if (filter.Value.Feasible) {
            Try notified = Notify(
                config: config,
                elapsed: started.Elapsed,
                status: new IisComputingStatus(
                    RestBoundAmount: (ulong)filter.Value.Values.Keys.Count(
                        index => IsBoundSlack(workspace.Variables[index])),
                    TotalBoundAmount: boundAmount,
                    RestConstraintAmount: (ulong)filter.Value.Values.Keys.Count(
                        index => IsRowSlack(workspace.Variables[index])),
                    TotalConstraintAmount: constraintAmount));
            if (!notified.IsOk) {
                return LinearIisCompute.Propagate<LinearIisModel, Success>(notified);
            }
            return Results.Ok(Dump(
                origin: origin,
                elasticVariables: workspace.Variables,
                misSlacks: filter.Value.Values.Keys,
                guardSlacks: null));
        }
        Try total = Notify(
            config: config,
            elapsed: started.Elapsed,
            status: new IisComputingStatus(
                RestBoundAmount: boundAmount,
                TotalBoundAmount: boundAmount,
                RestConstraintAmount: constraintAmount,
                TotalConstraintAmount: constraintAmount));
        if (!total.IsOk) {
            return LinearIisCompute.Propagate<LinearIisModel, Success>(total);
        }
        Result<DeletionFilter, ErrorCode, Error<ErrorCode>> deletion = await PerformDeletionFilteringAsync(
            workspace: workspace,
            solver: solver,
            config: config,
            started: started,
            boundAmount: boundAmount,
            constraintAmount: constraintAmount,
            cancellationToken: cancellationToken);
        if (deletion is not Ok<DeletionFilter, ErrorCode, Error<ErrorCode>> reduced) {
            return LinearIisCompute.Propagate<LinearIisModel, DeletionFilter>(deletion);
        }
        return Results.Ok(Dump(
            origin: origin,
            elasticVariables: workspace.Variables,
            misSlacks: reduced.Value.Mis,
            guardSlacks: reduced.Value.Guard));
    }

    /// <summary>依次只放开变量界、不等式与变量界、以及全部松弛 / Relax bound slacks, then inequality and bound slacks, then every slack.</summary>
    private static async Task<Result<SlackFilter, ErrorCode, Error<ErrorCode>>> PerformElasticFilteringAsync(
        ElasticSlackWorkspace workspace,
        IAbstractLinearSolver solver,
        IisConfig config,
        CancellationToken cancellationToken) {
        Result<SlackFilter, ErrorCode, Error<ErrorCode>> bounds = await RelaxSpecificComponentsAsync(
            workspace: workspace,
            solver: solver,
            config: config,
            relaxCondition: IsBoundSlack,
            cancellationToken: cancellationToken);
        if (bounds is not Ok<SlackFilter, ErrorCode, Error<ErrorCode>> boundsOk || boundsOk.Value.Feasible) {
            return bounds;
        }
        Result<SlackFilter, ErrorCode, Error<ErrorCode>> inequalities = await RelaxSpecificComponentsAsync(
            workspace: workspace,
            solver: solver,
            config: config,
            relaxCondition: IsInequalityOrBoundSlack,
            cancellationToken: cancellationToken);
        if (inequalities is not Ok<SlackFilter, ErrorCode, Error<ErrorCode>> inequalitiesOk
            || inequalitiesOk.Value.Feasible) {
            return inequalities;
        }
        Result<SlackFilter, ErrorCode, Error<ErrorCode>> all = await RelaxSpecificComponentsAsync(
            workspace: workspace,
            solver: solver,
            config: config,
            relaxCondition: HasSlack,
            cancellationToken: cancellationToken);
        if (all is not Ok<SlackFilter, ErrorCode, Error<ErrorCode>> allOk) {
            return all;
        }
        return allOk.Value.Feasible
            ? all
            : Results.Ok(new SlackFilter(
                Feasible: false,
                Values: _emptySlacks));
    }

    /// <summary>
    /// 按谓词放开松弛列上界并求解，收集取值达到容差的松弛列。
    /// Open slack upper bounds by the predicate, solve, and collect the slacks reaching the tolerance.
    /// </summary>
    private static async Task<Result<SlackFilter, ErrorCode, Error<ErrorCode>>> RelaxSpecificComponentsAsync(
        ElasticSlackWorkspace workspace,
        IAbstractLinearSolver solver,
        IisConfig config,
        Func<ModelViewVariable, bool> relaxCondition,
        CancellationToken cancellationToken) {
        IReadOnlyList<ModelViewVariable> variables = workspace.Variables;
        for (int index = 0; index < variables.Count; index++) {
            if (variables[index].Slack is not null) {
                workspace.SetSlackUpperBound(
                    index: index,
                    upperBound: relaxCondition(variables[index]) ? _positiveInfinity : Flt64.Zero);
            }
        }
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await solver.InvokeReportAsync(
            model: workspace,
            solvingStatusCallBack: null,
            cancellationToken: cancellationToken);
        if (report is Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return failed.Error.Code is ErrorCode.ORModelInfeasible or ErrorCode.ORModelInfeasibleOrUnbounded
                ? Results.Ok(new SlackFilter(
                    Feasible: false,
                    Values: _emptySlacks))
                : Results.Failed<SlackFilter>(failed.Error);
        }
        if (report is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return LinearIisCompute.Propagate<SlackFilter, SolveReport<Flt64>>(report);
        }
        if (ok.Value.ProblemStatus is ProblemStatus.Infeasible or ProblemStatus.InfeasibleOrUnbounded) {
            return Results.Ok(new SlackFilter(
                Feasible: false,
                Values: _emptySlacks));
        }
        if (ok.Value.ProblemStatus != ProblemStatus.Feasible) {
            return Results.Failed<SlackFilter>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                $"弹性线性求解返回无法判定的终态：{ok.Value.ProblemStatus} / " +
                    $"Elastic linear solve returned an inconclusive terminal status: {ok.Value.ProblemStatus}"));
        }
        if (ok.Value.Solution is null) {
            return Results.Failed<SlackFilter>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "弹性线性求解完成但没有解 / Elastic linear solve completed without a solution"));
        }
        Solution<Flt64> values = ok.Value.Solution.Values;
        var slacks = new Dictionary<int, Flt64>();
        for (int index = 0; index < variables.Count; index++) {
            if (variables[index].Slack is not null && values.Length > index
                && values[index].Geq(config.SlackTolerance)) {
                slacks[index] = values[index];
            }
        }
        return Results.Ok(new SlackFilter(
            Feasible: true,
            Values: slacks));
    }

    /// <summary>逐个把松弛列上界归零做可行性判定，保留仍导致不可行的松弛 / Zero slack upper bounds one by one and keep the ones that still cause infeasibility.</summary>
    private static async Task<Result<DeletionFilter, ErrorCode, Error<ErrorCode>>> PerformDeletionFilteringAsync(
        ElasticSlackWorkspace workspace,
        IAbstractLinearSolver solver,
        IisConfig config,
        Stopwatch started,
        ulong boundAmount,
        ulong constraintAmount,
        CancellationToken cancellationToken) {
        var candidates = new List<int>();
        for (int index = 0; index < workspace.Variables.Count; index++) {
            if (workspace.Variables[index].Slack is not null) {
                candidates.Add(index);
            }
        }
        if (candidates.Count == 0) {
            return Results.Ok(new DeletionFilter(
                Mis: new SortedSet<int>(),
                Guard: new SortedSet<int>()));
        }
        var active = new SortedSet<int>(candidates);
        foreach (int candidate in candidates) {
            if (!active.Contains(candidate)) {
                continue;
            }
            workspace.SetSlackUpperBound(
                index: candidate,
                upperBound: Flt64.Zero);
            Result<bool, ErrorCode, Error<ErrorCode>> feasible = await SolveFeasibilityAsync(
                workspace: workspace,
                solver: solver,
                cancellationToken: cancellationToken);
            if (feasible is not Ok<bool, ErrorCode, Error<ErrorCode>> probe) {
                return LinearIisCompute.Propagate<DeletionFilter, bool>(feasible);
            }
            if (probe.Value) {
                active.Remove(candidate);
            }
            else {
                workspace.SetSlackUpperBound(
                    index: candidate,
                    upperBound: _positiveInfinity);
            }
            Try notified = Notify(
                config: config,
                elapsed: started.Elapsed,
                status: new IisComputingStatus(
                    RestBoundAmount: (ulong)active.Count(index => IsBoundSlack(workspace.Variables[index])),
                    TotalBoundAmount: boundAmount,
                    RestConstraintAmount: (ulong)active.Count(index => IsRowSlack(workspace.Variables[index])),
                    TotalConstraintAmount: constraintAmount));
            if (!notified.IsOk) {
                return LinearIisCompute.Propagate<DeletionFilter, Success>(notified);
            }
        }
        return Results.Ok(new DeletionFilter(
            Mis: active,
            Guard: new SortedSet<int>()));
    }

    /// <summary>只判定可行性，不读取解向量 / Decide feasibility only and never read the solution vector.</summary>
    private static async Task<Result<bool, ErrorCode, Error<ErrorCode>>> SolveFeasibilityAsync(
        ElasticSlackWorkspace workspace,
        IAbstractLinearSolver solver,
        CancellationToken cancellationToken) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await solver.InvokeReportAsync(
            model: workspace,
            solvingStatusCallBack: null,
            cancellationToken: cancellationToken);
        if (report is Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return failed.Error.Code is ErrorCode.ORModelInfeasible or ErrorCode.ORModelInfeasibleOrUnbounded
                ? Results.Ok(false)
                : Results.Failed<bool>(failed.Error);
        }
        if (report is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return LinearIisCompute.Propagate<bool, SolveReport<Flt64>>(report);
        }
        return ok.Value.ProblemStatus switch {
            ProblemStatus.Feasible => Results.Ok(true),
            ProblemStatus.Infeasible => Results.Ok(false),
            ProblemStatus.InfeasibleOrUnbounded => Results.Ok(false),
            _ => Results.Failed<bool>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                $"IIS 删除过滤收到无法判定可行性的终态：{ok.Value.ProblemStatus} / " +
                    $"IIS deletion filtering received an inconclusive terminal status: {ok.Value.ProblemStatus}")),
        };
    }

    /// <summary>
    /// 由松弛列集合回溯原模型行、列并构造 IIS 模型。
    /// Trace origin rows and columns from the slack set and build the IIS model.
    /// </summary>
    /// <remarks>
    /// 子系统只包含原模型的行与被这些行或界松弛引用的列，弹性列本身不会进入结果；<paramref name="guardSlacks"/> 为 null
    /// 表示弹性过滤直接命中，否则守卫批次可以为空行集但仍表示"需要保留守卫"。
    /// The subsystem keeps only origin rows and the columns referenced by those rows or by the kept bound slacks, so elastic
    /// columns never enter the result; a null <paramref name="guardSlacks"/> means the elastic filter hit directly, otherwise
    /// the guard batch may hold zero rows and still mean that guards are required.
    /// </remarks>
    private static LinearIisModel Dump(
        LinearTriadModel origin,
        IReadOnlyList<ModelViewVariable> elasticVariables,
        IEnumerable<int> misSlacks,
        IEnumerable<int>? guardSlacks) {
        List<int> mis = [.. misSlacks];
        List<int> guard = guardSlacks is null ? [] : [.. guardSlacks];
        var batch = (LinearConstraintBatch)origin.Constraints;
        List<int> rows = RelatedRows(elasticVariables, mis);
        List<int> guardRows = guardSlacks is null ? [] : RelatedRows(elasticVariables, guard);
        var keepLower = new HashSet<int>();
        var keepUpper = new HashSet<int>();
        var columns = new SortedSet<int>();
        foreach (int index in mis.Concat(guard)) {
            VariableSlack? slack = elasticVariables[index].Slack;
            if (slack?.LowerBound is { } lower) {
                keepLower.Add(lower.Index);
                columns.Add(lower.Index);
            }
            else if (slack?.UpperBound is { } upper) {
                keepUpper.Add(upper.Index);
                columns.Add(upper.Index);
            }
        }
        // 行系数所在列全部纳入，投影后不会丢弃任何非零项。
        // Every column holding a row coefficient is included, so the projection never drops a nonzero entry.
        foreach (int row in rows.Concat(guardRows)) {
            foreach (LinearConstraintCell cell in batch.Lhs[row]) {
                if (cell.ColIndex >= 0 && cell.ColIndex < origin.Variables.Count) {
                    columns.Add(cell.ColIndex);
                }
            }
        }
        int[] columnArray = columns.ToArray();
        int[] columnMap = new int[origin.Variables.Count];
        Array.Fill(columnMap, -1);
        var variables = new List<ModelViewVariable>(columnArray.Length);
        for (int newIndex = 0; newIndex < columnArray.Length; newIndex++) {
            int oldIndex = columnArray[newIndex];
            columnMap[oldIndex] = newIndex;
            ModelViewVariable source = origin.Variables[oldIndex];
            variables.Add(LinearIisCompute.ProjectColumn(
                source: source,
                index: newIndex,
                lowerBound: keepLower.Contains(oldIndex) ? source.LowerBound : _negativeInfinity,
                upperBound: keepUpper.Contains(oldIndex) ? source.UpperBound : _positiveInfinity));
        }
        return new LinearIisModel(
            variables: variables,
            rows: LinearIisCompute.ProjectRows(
                source: batch,
                rows: rows,
                columnMap: columnMap),
            guardConstraints: guardSlacks is null
                ? null
                : LinearIisCompute.ProjectRows(
                    source: batch,
                    rows: guardRows,
                    columnMap: columnMap),
            originRows: rows,
            originColumns: columnArray,
            origin: origin,
            name: $"{origin.Name}_iis");
    }

    // `RelatedRows` 与 `HasSlack` 都与线性无关，已提取到 `IisStructuralSupport` 供二次侧复用。
    // `RelatedRows` and `HasSlack` are not linear-specific; they now live in `IisStructuralSupport` so the quadratic side can reuse them.
    private static List<int> RelatedRows(
        IReadOnlyList<ModelViewVariable> elasticVariables,
        IEnumerable<int> slackIndices) =>
        IisStructuralSupport.RelatedRows(elasticVariables, slackIndices);

    private static bool HasSlack(ModelViewVariable variable) => IisStructuralSupport.HasSlack(variable);

    private static bool IsBoundSlack(ModelViewVariable variable) =>
        variable.Slack is { LowerBound: not null } or { UpperBound: not null };

    private static bool IsRowSlack(ModelViewVariable variable) => variable.Slack?.Constraint is not null;

    private static bool IsInequalityOrBoundSlack(ModelViewVariable variable) {
        if (variable.Slack?.Constraint is { } constraint
            && (constraint.Relation == ConstraintRelation.LessEqual
                || constraint.Relation == ConstraintRelation.GreaterEqual)) {
            return true;
        }
        return IsBoundSlack(variable);
    }

    private static Try Notify(IisConfig config, TimeSpan elapsed, IisComputingStatus status) =>
        config.ComputingStatusCallBack?.Invoke(
            isFeasible: true,
            time: elapsed,
            status: status) ?? Results.OkInstance;
}
