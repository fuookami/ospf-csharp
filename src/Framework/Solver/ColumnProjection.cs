#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>共享线性列映射与定价投影；不负责配置可行性、定价算法或联合迭代。</summary>
/// <remarks>
/// Shared linear column/pricing projection without configuration feasibility, pricing algorithms or
/// joint iteration. The source model remains the sole model definition.
/// </remarks>
public sealed class ColumnProjection<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private sealed record PriceState(
        ColumnProjectionRevision Revision,
        IReadOnlyDictionary<string, V> RowPrices);

    private static long _owners;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly long _owner = Interlocked.Increment(ref _owners);
    private readonly IReadOnlyList<ColumnDecision> _decisions;
    private readonly IReadOnlyList<string> _blockIds;
    private readonly HashSet<string> _blockIdSet;
    private long _sequence;
    private IReadOnlyList<ProjectedColumn<V>> _columns =
        Array.AsReadOnly(Array.Empty<ProjectedColumn<V>>());
    private LinearModelSnapshot<V>? _current;
    private ColumnProjectionIndex<V>? _index;
    private PriceState? _prices;
    private IReadOnlyDictionary<string, V> _convexityPrices =
        new Dictionary<string, V>(StringComparer.Ordinal);
    private ColumnMasterRegistration<V>? _active;

    public ColumnProjection(
        LinearMetaModel<V> source,
        IEnumerable<ColumnDecision> decisions,
        IEnumerable<string> blockIds) {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (decisions is null) {
            throw new ArgumentNullException(nameof(decisions));
        }
        if (blockIds is null) {
            throw new ArgumentNullException(nameof(blockIds));
        }
        _decisions = Array.AsReadOnly(decisions.ToArray());
        _blockIds = Array.AsReadOnly(blockIds.ToArray());
        _blockIdSet = new HashSet<string>(_blockIds, StringComparer.Ordinal);
    }

    public LinearMetaModel<V> Source { get; }
    public IReadOnlyList<ColumnDecision> Decisions => _decisions;
    public IReadOnlyList<string> BlockIds => _blockIds;
    public IReadOnlyList<ProjectedColumn<V>> Columns {
        get {
            lock (_gate) {
                return _columns;
            }
        }
    }

    /// <summary>重新读取模型基础层快照，原始模型变化使旧对偶失效。</summary>
    public async Task<Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>>> SnapshotAsync() {
        string? mappingError = ValidateMapping();
        if (mappingError is not null) {
            return FailureSnapshot(ErrorCode.IllegalArgument, mappingError);
        }

        Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> refreshed =
            await Source.State.RefreshAsync();
        if (refreshed is Failed<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<LinearModelSnapshot<V>>(failed.Error);
        }
        if (refreshed is Fatal<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }

        LinearModelSnapshot<V> snapshot = refreshed.Value;
        var variables = snapshot.Variables
            .Select(state => state.Variable.Key)
            .ToHashSet();
        if (_decisions.Any(decision => !variables.Contains(decision.Variable.Key))) {
            return FailureSnapshot(
                ErrorCode.IllegalArgument,
                "列决策引用未知变量 / Column decision references an unknown variable");
        }

        lock (_gate) {
            if (_current is null || _current.Revision != snapshot.Revision) {
                _prices = null;
                _convexityPrices = new Dictionary<string, V>(StringComparer.Ordinal);
            }
            if (_index is null || _index.Revision != snapshot.Revision) {
                _index = new ColumnProjectionIndex<V>(_decisions, _blockIds, snapshot);
            }
            _current = snapshot;
        }
        return Results.Ok(snapshot);
    }

    /// <summary>列生命周期发生变化时立即撤销旧注册和对偶。</summary>
    public void ColumnsChanged() {
        _lifecycleGate.Wait();
        try {
            PublishColumns(CurrentColumns());
        }
        finally {
            _lifecycleGate.Release();
        }
    }

    /// <summary>原子替换完整列池并撤销旧注册和对偶 / Atomically replace the complete column pool and invalidate certificates.</summary>
    public async Task<Try> ReplaceColumnsAsync(IEnumerable<ProjectedColumn<V>> columns) {
        if (!TryMaterialize(
                source: columns,
                name: "列集合 / Column collection",
                materialized: out List<ProjectedColumn<V>> replacement,
                error: out string? enumerationError)) {
            return Failure<Success>(ErrorCode.IllegalArgument, enumerationError!);
        }
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try {
            Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> snapshotResult =
                await SnapshotAsync().ConfigureAwait(false);
            if (!snapshotResult.IsOk) {
                return Propagate<Success, LinearModelSnapshot<V>>(snapshotResult);
            }
            string? error = ValidateColumns(replacement, CurrentIndex());
            if (error is not null) {
                return Failure<Success>(ErrorCode.IllegalArgument, error);
            }
            PublishColumns(replacement);
            return Results.OkInstance;
        }
        finally {
            _lifecycleGate.Release();
        }
    }

    /// <summary>原子追加列；任一列无效或身份冲突时不修改列池 / Atomically append columns, leaving the pool unchanged on any error.</summary>
    public async Task<Try> AddColumnsAsync(IEnumerable<ProjectedColumn<V>> columns) {
        if (!TryMaterialize(
                source: columns,
                name: "列集合 / Column collection",
                materialized: out List<ProjectedColumn<V>> additions,
                error: out string? enumerationError)) {
            return Failure<Success>(ErrorCode.IllegalArgument, enumerationError!);
        }
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try {
            if (additions.Count == 0) {
                return Results.OkInstance;
            }
            Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> snapshotResult =
                await SnapshotAsync().ConfigureAwait(false);
            if (!snapshotResult.IsOk) {
                return Propagate<Success, LinearModelSnapshot<V>>(snapshotResult);
            }
            IReadOnlyList<ProjectedColumn<V>> current = CurrentColumns();
            var existingIds = current
                .Select(column => column.Id)
                .ToHashSet(StringComparer.Ordinal);
            if (additions.Any(column => existingIds.Contains(column.Id))) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    "列身份已存在 / Column identity already exists");
            }
            var combined = current.Concat(additions).ToList();
            string? error = ValidateColumns(combined, CurrentIndex());
            if (error is not null) {
                return Failure<Success>(ErrorCode.IllegalArgument, error);
            }
            PublishColumns(combined);
            return Results.OkInstance;
        }
        finally {
            _lifecycleGate.Release();
        }
    }

    /// <summary>按身份原子删除列；未知身份按幂等成功处理 / Atomically remove columns by identity; unknown identities are idempotent.</summary>
    public async Task<Try> RemoveColumnsAsync(IEnumerable<string> columnIds) {
        if (!TryMaterialize(
                source: columnIds,
                name: "列身份集合 / Column identity collection",
                materialized: out List<string> requested,
                error: out string? enumerationError)) {
            return Failure<Success>(ErrorCode.IllegalArgument, enumerationError!);
        }
        if (requested.Any(string.IsNullOrWhiteSpace)) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "列身份为空 / Column identity is blank");
        }
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try {
            IReadOnlyList<ProjectedColumn<V>> current = CurrentColumns();
            var identities = requested.ToHashSet(StringComparer.Ordinal);
            var retained = current
                .Where(column => !identities.Contains(column.Id))
                .ToList();
            if (retained.Count == current.Count) {
                return Results.OkInstance;
            }
            PublishColumns(retained);
            return Results.OkInstance;
        }
        finally {
            _lifecycleGate.Release();
        }
    }

    /// <summary>检查请求或注册是否仍对应当前模型。</summary>
    public async Task<Try> RequireCurrentAsync(ColumnProjectionRevision revision) {
        Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> snapshotResult = await SnapshotAsync();
        if (!snapshotResult.IsOk) {
            return Propagate<Success, LinearModelSnapshot<V>>(snapshotResult);
        }
        if (revision != CurrentRevision(snapshotResult.Value)) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "列投影注册或定价版本已过期 / Stale column-projection registration or pricing revision");
        }

        ColumnMasterRegistration<V>? registration;
        lock (_gate) {
            registration = _active;
        }
        return registration is null
            ? Results.OkInstance
            : await registration.Model.State.RequireCurrentAsync(registration.ModelRevision);
    }

    /// <summary>校验定价批次仍对应当前模型；应在任何异步定价结果被接受前调用。</summary>
    public async Task<Try> RequireCurrentAsync(ColumnPricingBatch<V> batch) {
        if (batch is null ||
            !ReferenceEquals(batch.Owner, this) ||
            batch.Entries.Count == 0 ||
            batch.Entries.Any(entry => entry.Request.Revision != batch.Revision)) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "定价批次无效 / Invalid pricing batch");
        }
        return await RequireCurrentAsync(batch.Revision);
    }

    /// <summary>使用当前列池重建主问题 / Rebuild the master from the current column pool.</summary>
    public async Task<Result<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>>> MasterAsync(
        bool phaseOne,
        bool integer,
        IReadOnlySet<IVariableItem>? refinedResources = null,
        V? tolerance = null) {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try {
            return await MasterCoreAsync(
                columns: CurrentColumns(),
                phaseOne: phaseOne,
                integer: integer,
                refinedResources: refinedResources,
                tolerance: tolerance).ConfigureAwait(false);
        }
        finally {
            _lifecycleGate.Release();
        }
    }

    /// <summary>重建主问题；全部行及目标来自原始模型快照，不维护第二份模型定义。</summary>
    public async Task<Result<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>>> MasterAsync(
        IEnumerable<ProjectedColumn<V>> columns,
        bool phaseOne,
        bool integer,
        IReadOnlySet<IVariableItem>? refinedResources = null,
        V? tolerance = null) {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try {
            return await MasterCoreAsync(
                columns: columns,
                phaseOne: phaseOne,
                integer: integer,
                refinedResources: refinedResources,
                tolerance: tolerance).ConfigureAwait(false);
        }
        finally {
            _lifecycleGate.Release();
        }
    }

    private async Task<Result<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>>> MasterCoreAsync(
        IEnumerable<ProjectedColumn<V>> columns,
        bool phaseOne,
        bool integer,
        IReadOnlySet<IVariableItem>? refinedResources,
        V? tolerance) {
        if (!TryMaterialize(
                source: columns,
                name: "列集合 / Column collection",
                materialized: out List<ProjectedColumn<V>> columnsList,
                error: out string? enumerationError)) {
            return FailureRegistration(ErrorCode.IllegalArgument, enumerationError!);
        }
        Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> snapshotResult = await SnapshotAsync();
        if (!snapshotResult.IsOk) {
            return Propagate<ColumnMasterRegistration<V>, LinearModelSnapshot<V>>(snapshotResult);
        }

        LinearModelSnapshot<V> snapshot = snapshotResult.Value;
        ColumnProjectionIndex<V> index = CurrentIndex();
        HashSet<IVariableItem> refined = refinedResources is null
            ? new HashSet<IVariableItem>()
            : new HashSet<IVariableItem>(refinedResources);
        if (!index.MappedVariables.IsSupersetOf(refined)) {
            return FailureRegistration(
                ErrorCode.IllegalArgument,
                "资源补全集合不是列决策变量 / Refined resources are not mapped decisions");
        }
        V boundTolerance = tolerance ?? Source.Converter.Zero;
        if (!boundTolerance.IsFinite() || boundTolerance.Ls(Source.Converter.Zero)) {
            return FailureRegistration(
                ErrorCode.IllegalArgument,
                "列范围容差无效 / Invalid column-bound tolerance");
        }

        string? columnError = ValidateColumns(columnsList, index);
        if (columnError is not null) {
            return FailureRegistration(ErrorCode.IllegalArgument, columnError);
        }

        Dictionary<VariableItemKey, LinearVariableState<V>> bounds;
        try {
            bounds = snapshot.Variables.ToDictionary(state => state.Variable.Key);
        }
        catch (ArgumentException) {
            return FailureRegistration(
                ErrorCode.IllegalArgument,
                "模型快照包含重复变量身份 / Model snapshot contains duplicate variable identities");
        }
        foreach (ProjectedColumn<V> column in columnsList) {
            foreach (ColumnDecision decision in index.DecisionsByBlock[column.BlockId]) {
                if (refined.Contains(decision.Variable)) {
                    continue;
                }
                V value = column.Values[decision.Id];
                if (!bounds.TryGetValue(decision.Variable.Key, out LinearVariableState<V>? state)) {
                    return FailureRegistration(
                        ErrorCode.IllegalArgument,
                        "列决策变量缺少快照范围 / Column decision variable has no snapshot bounds");
                }
                if (!WithinBounds(value, state.LowerBound, state.UpperBound, boundTolerance)) {
                    return FailureRegistration(
                        ErrorCode.IllegalArgument,
                        $"旧列不满足当前变量范围，需重新定价或删列 / Column violates current bounds: {column.Id}");
                }
            }

            foreach (LinearModelRow<V> row in index.LocalRows[column.BlockId]
                         .Where(row => !row.Coefficients.Keys.Any(refined.Contains))) {
                Result<V, ErrorCode, Error<ErrorCode>> coefficient =
                    ProjectedCoefficient(index, row.Id, column, refined);
                if (!coefficient.IsOk) {
                    return coefficient is Failed<V, ErrorCode, Error<ErrorCode>> failed
                        ? FailureRegistration(failed.Error.Code, failed.Error.Message)
                        : FailureRegistration(
                            ErrorCode.IllegalArgument,
                            "列局部行系数无效 / Invalid local-row column coefficient");
                }
                if (!RowFeasible(row, coefficient.Value, boundTolerance)) {
                    return FailureRegistration(
                        ErrorCode.IllegalArgument,
                        $"旧列不满足当前局部行，需重新定价或删列 / Column violates local row: {column.Id}, {row.Id}");
                }
            }
        }

        LinearMetaModel<V> master = new(
            name: $"column_master_{phaseOne}_{integer}",
            converter: Source.Converter);
        var lambdas = new Dictionary<string, URealVar>(StringComparer.Ordinal);
        var convexities = new Dictionary<string, MathConstraint>(StringComparer.Ordinal);
        var rows = new Dictionary<string, MathConstraint>(StringComparer.Ordinal);
        try {
            for (int indexOfColumn = 0; indexOfColumn < columnsList.Count; ++indexOfColumn) {
                ProjectedColumn<V> column = columnsList[indexOfColumn];
                var variable = new URealVar($"partition_column_{indexOfColumn}");
                Try added = master.Add(variable);
                if (!added.IsOk) {
                    return CloseAndFailure(master, added, ErrorCode.ApplicationError, "列选择变量添加失败");
                }
                lambdas[column.Id] = variable;

                if (integer) {
                    var selected = new BinVar($"partition_selected_{indexOfColumn}");
                    added = master.Add(selected);
                    if (!added.IsOk) {
                        return CloseAndFailure(master, added, ErrorCode.ApplicationError, "整数选择变量添加失败");
                    }
                    var relation = new LinearInequality<V>(
                        new LinearPolynomial<V>(
                            new[] { new LinearMonomial<V>(Source.Converter.One, variable) },
                            Source.Converter.Zero),
                        new LinearPolynomial<V>(
                            new[] { new LinearMonomial<V>(Source.Converter.One, selected) },
                            Source.Converter.Zero),
                        Comparison.EQ);
                    added = master.AddConstraint(
                        relation: relation,
                        group: null,
                        name: $"column_integral_{indexOfColumn}");
                    if (!added.IsOk) {
                        return CloseAndFailure(master, added, ErrorCode.ApplicationError, "列整数约束添加失败");
                    }
                }
            }

            foreach (LinearVariableState<V> state in snapshot.Variables) {
                if (index.MappedVariables.Contains(state.Variable) && !refined.Contains(state.Variable)) {
                    continue;
                }
                Try added = master.Add(state.Variable);
                if (!added.IsOk) {
                    return CloseAndFailure(master, added, ErrorCode.ApplicationError, "原始变量添加失败");
                }
            }

            for (int blockIndex = 0; blockIndex < _blockIds.Count; ++blockIndex) {
                string blockId = _blockIds[blockIndex];
                var lhs = new LinearPolynomial<V>(
                    columnsList
                        .Where(column => string.Equals(column.BlockId, blockId, StringComparison.Ordinal))
                        .Select(column => new LinearMonomial<V>(
                            Source.Converter.One,
                            lambdas[column.Id]))
                        .ToList(),
                    Source.Converter.Zero);
                var relation = new LinearInequality<V>(
                    lhs,
                    new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), Source.Converter.One),
                    Comparison.EQ);
                Try added = master.AddConstraint(
                    relation: relation,
                    group: null,
                    name: $"column_choose_{blockIndex}");
                if (!added.IsOk) {
                    return CloseAndFailure(master, added, ErrorCode.ApplicationError, "恰选一列约束添加失败");
                }
                convexities[blockId] = master.Constraints[^1];
            }

            var objectiveTerms = new List<LinearMonomial<V>>();
            V objectiveConstant = phaseOne ? Source.Converter.Zero : snapshot.ObjectiveConstant;
            if (!phaseOne) {
                foreach ((IVariableItem variable, V coefficient) in snapshot.Objective) {
                    if (index.MappedVariables.Contains(variable) && !refined.Contains(variable)) {
                        continue;
                    }
                    objectiveTerms.Add(new LinearMonomial<V>(coefficient, variable));
                }
                foreach (ProjectedColumn<V> column in columnsList) {
                    Result<V, ErrorCode, Error<ErrorCode>> coefficient =
                        ObjectiveCoefficient(index, column, refined);
                    if (!coefficient.IsOk) {
                        return CloseAndFailure(
                            master,
                            coefficient,
                            ErrorCode.IllegalArgument,
                            "列目标系数无效");
                    }
                    objectiveTerms.Add(new LinearMonomial<V>(
                        coefficient.Value,
                        lambdas[column.Id]));
                }
            }

            for (int rowIndex = 0; rowIndex < snapshot.Rows.Count; ++rowIndex) {
                LinearModelRow<V> row = snapshot.Rows[rowIndex];
                var lhsTerms = new List<LinearMonomial<V>>();
                foreach ((IVariableItem variable, V coefficient) in row.Coefficients) {
                    if (index.MappedVariables.Contains(variable) && !refined.Contains(variable)) {
                        continue;
                    }
                    lhsTerms.Add(new LinearMonomial<V>(coefficient, variable));
                }
                foreach (ProjectedColumn<V> column in columnsList) {
                    Result<V, ErrorCode, Error<ErrorCode>> coefficient =
                        ProjectedCoefficient(index, row.Id, column, refined);
                    if (!coefficient.IsOk) {
                        return CloseAndFailure(
                            master,
                            coefficient,
                            ErrorCode.IllegalArgument,
                            "列行系数无效");
                    }
                    lhsTerms.Add(new LinearMonomial<V>(
                        coefficient.Value,
                        lambdas[column.Id]));
                }

                if (phaseOne) {
                    if (row.Comparison != Comparison.GE) {
                        var excess = new URealVar($"excess_{rowIndex}");
                        Try added = master.Add(excess);
                        if (!added.IsOk) {
                            return CloseAndFailure(master, added, ErrorCode.ApplicationError, "人工松弛变量添加失败");
                        }
                        lhsTerms.Add(new LinearMonomial<V>(Source.Converter.One.Negate(), excess));
                        objectiveTerms.Add(new LinearMonomial<V>(Source.Converter.One, excess));
                    }
                    if (row.Comparison != Comparison.LE) {
                        var shortage = new URealVar($"shortage_{rowIndex}");
                        Try added = master.Add(shortage);
                        if (!added.IsOk) {
                            return CloseAndFailure(master, added, ErrorCode.ApplicationError, "人工短缺变量添加失败");
                        }
                        lhsTerms.Add(new LinearMonomial<V>(Source.Converter.One, shortage));
                        objectiveTerms.Add(new LinearMonomial<V>(Source.Converter.One, shortage));
                    }
                }

                var relation = new LinearInequality<V>(
                    new LinearPolynomial<V>(lhsTerms, Source.Converter.Zero),
                    new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), row.Rhs),
                    row.Comparison);
                Try rowAdded = master.AddConstraint(
                    relation: relation,
                    group: null,
                    name: row.Id);
                if (!rowAdded.IsOk) {
                    return CloseAndFailure(master, rowAdded, ErrorCode.ApplicationError, "投影行添加失败");
                }
                rows[row.Id] = master.Constraints[^1];
            }

            Try objectiveAdded = master.AddObject(
                category: ObjectCategory.Minimum,
                polynomial: new LinearPolynomial<V>(objectiveTerms, objectiveConstant),
                name: "column_master_objective",
                displayName: null);
            if (!objectiveAdded.IsOk) {
                return CloseAndFailure(master, objectiveAdded, ErrorCode.ApplicationError, "主问题目标添加失败");
            }

            Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> refreshed =
                await master.State.RefreshAsync();
            if (!refreshed.IsOk) {
                master.Dispose();
                return Propagate<ColumnMasterRegistration<V>, LinearModelSnapshot<V>>(refreshed);
            }

            Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> sourceCheck =
                await SnapshotAsync().ConfigureAwait(false);
            if (!sourceCheck.IsOk) {
                master.Dispose();
                return Propagate<ColumnMasterRegistration<V>, LinearModelSnapshot<V>>(sourceCheck);
            }
            if (sourceCheck.Value.Revision != snapshot.Revision) {
                master.Dispose();
                return FailureRegistration(
                    ErrorCode.IllegalArgument,
                    "源模型在主问题重建期间发生变化 / Source model changed during master rebuild");
            }

            // 先按即将发布的序号构造注册，再发布列池；构造失败时旧列池、旧注册和对偶都保持。
            // Build the registration with the sequence the publish will produce, then publish; a construction
            // failure keeps the old pool, registration and duals.
            long pendingSequence = PendingSequence();
            var registration = new ColumnMasterRegistration<V>(
                model: master,
                columnVariables: lambdas,
                refinedResources: refined,
                rows: rows,
                convexities: convexities,
                revision: CurrentRevision(snapshot, pendingSequence),
                modelRevision: refreshed.Value.Revision,
                phaseOne: phaseOne,
                integer: integer,
                columns: columnsList);
            PublishColumns(columnsList);
            lock (_gate) {
                _active = registration;
            }
            return Results.Ok(registration);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            master.Dispose();
            return FailureRegistration(ErrorCode.ApplicationError, error.Message);
        }
    }

    /// <summary>验证源模型、列池及主问题本身均未变化。</summary>
    public async Task<Try> RequireCurrentAsync(ColumnMasterRegistration<V> registration) {
        if (registration is null) {
            return Failure<Success>(ErrorCode.IllegalArgument, "主问题注册为空 / Master registration is null");
        }
        Try valid = await RequireCurrentAsync(registration.Revision);
        if (!valid.IsOk) {
            return valid;
        }
        lock (_gate) {
            if (!ReferenceEquals(_active, registration)) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    "主问题注册已失效 / Master registration is no longer active");
            }
        }
        return Results.OkInstance;
    }

    /// <summary>按实际约束来源绑定对偶，不使用显示名或跨模型列表顺序。</summary>
    public async Task<Try> RefreshShadowPriceAsync(
        ColumnMasterRegistration<V> registration,
        IReadOnlyDictionary<MathConstraint, V> dual) {
        lock (_gate) {
            _prices = null;
            _convexityPrices = new Dictionary<string, V>(StringComparer.Ordinal);
        }
        if (dual is null) {
            return Failure<Success>(ErrorCode.IllegalArgument, "对偶映射为空 / Dual map is null");
        }
        Try valid = await RequireCurrentAsync(registration);
        if (!valid.IsOk) {
            return valid;
        }
        if (registration.Integer || registration.RefinedResources.Count != 0) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "整数或资源补全主问题不能提供定价对偶 / Integer or resource-completion masters cannot supply pricing duals");
        }

        var byOrigin = new Dictionary<MathConstraint, V>(ReferenceEqualityComparer.Instance);
        foreach ((MathConstraint row, V value) in dual) {
            if (row is null || !value.IsFinite()) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    "对偶包含空行或非有限值 / Dual contains a null row or nonfinite value");
            }
            if (!byOrigin.TryAdd(row, value)) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    "对偶包含重复行身份 / Dual contains duplicate row identities");
            }
        }

        var expected = new HashSet<MathConstraint>(
            registration.Rows.Values.Concat(registration.Convexities.Values),
            ReferenceEqualityComparer.Instance);
        if (dual.Count != expected.Count ||
            byOrigin.Count != expected.Count ||
            expected.Any(row => !byOrigin.ContainsKey(row))) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "对偶不属于当前注册或行不完整 / Duals belong to another registration or omit rows");
        }

        var rowPrices = new Dictionary<string, V>(StringComparer.Ordinal);
        foreach ((string rowId, MathConstraint row) in registration.Rows) {
            rowPrices[rowId] = byOrigin[row];
        }
        var convexityPrices = new Dictionary<string, V>(StringComparer.Ordinal);
        foreach ((string blockId, MathConstraint row) in registration.Convexities) {
            convexityPrices[blockId] = byOrigin[row];
        }
        lock (_gate) {
            _prices = new PriceState(registration.Revision, rowPrices);
            _convexityPrices = convexityPrices;
        }
        return Results.OkInstance;
    }

    /// <summary>从同源行生成当前定价目标；只有种子阶段允许不提供对偶。</summary>
    public async Task<Result<ColumnPricingRequest<V>, ErrorCode, Error<ErrorCode>>> PricingRequestAsync(
        string blockId,
        bool phaseOne,
        bool seed = false) {
        Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> snapshotResult = await SnapshotAsync();
        if (!snapshotResult.IsOk) {
            return Propagate<ColumnPricingRequest<V>, LinearModelSnapshot<V>>(snapshotResult);
        }
        if (!_blockIdSet.Contains(blockId)) {
            return FailureRequest(
                ErrorCode.IllegalArgument,
                "未知配置块 / Unknown configuration block");
        }
        Try valid = await ValidatePricingStateAsync(snapshotResult.Value, phaseOne, seed);
        if (!valid.IsOk) {
            return Propagate<ColumnPricingRequest<V>, Success>(valid);
        }
        return PricingRequestInternal(snapshotResult.Value, CurrentIndex(), blockId, phaseOne, seed);
    }

    /// <summary>一次刷新后为多个块准备同版本请求和局部模型。</summary>
    /// <remarks>调用方仍须在接收异步候选前调用 <see cref="RequireCurrentAsync(ColumnPricingBatch{V})"/>。</remarks>
    public async Task<Result<ColumnPricingBatch<V>, ErrorCode, Error<ErrorCode>>> PreparePricingBatchAsync(
        IEnumerable<string> blockIds,
        bool phaseOne,
        bool seed = false) {
        if (!TryMaterialize(
                source: blockIds,
                name: "定价批次 / Pricing batch",
                materialized: out List<string> requestedBlocks,
                error: out string? enumerationError)) {
            return FailureBatch(ErrorCode.IllegalArgument, enumerationError!);
        }
        Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> snapshotResult = await SnapshotAsync();
        if (!snapshotResult.IsOk) {
            return Propagate<ColumnPricingBatch<V>, LinearModelSnapshot<V>>(snapshotResult);
        }
        if (requestedBlocks.Count == 0 ||
            requestedBlocks.Distinct(StringComparer.Ordinal).Count() != requestedBlocks.Count) {
            return FailureBatch(
                ErrorCode.IllegalArgument,
                "定价批次必须包含不同配置块 / Pricing batch must contain distinct blocks");
        }
        if (requestedBlocks.Any(block => !_blockIdSet.Contains(block))) {
            return FailureBatch(
                ErrorCode.IllegalArgument,
                "未知配置块 / Unknown configuration block");
        }
        Try valid = await ValidatePricingStateAsync(snapshotResult.Value, phaseOne, seed);
        if (!valid.IsOk) {
            return Propagate<ColumnPricingBatch<V>, Success>(valid);
        }

        ColumnProjectionIndex<V> index = CurrentIndex();
        var entries = new List<ColumnPricingEntry<V>>();
        foreach (string blockId in requestedBlocks) {
            Result<ColumnPricingRequest<V>, ErrorCode, Error<ErrorCode>> requestResult =
                PricingRequestInternal(snapshotResult.Value, index, blockId, phaseOne, seed);
            if (!requestResult.IsOk) {
                foreach (ColumnPricingEntry<V> entry in entries) {
                    entry.Model.Dispose();
                }
                return Propagate<ColumnPricingBatch<V>, ColumnPricingRequest<V>>(requestResult);
            }
            Result<LinearMetaModel<V>, ErrorCode, Error<ErrorCode>> modelResult =
                PricingModelInternal(requestResult.Value, index);
            if (!modelResult.IsOk) {
                foreach (ColumnPricingEntry<V> entry in entries) {
                    entry.Model.Dispose();
                }
                return Propagate<ColumnPricingBatch<V>, LinearMetaModel<V>>(modelResult);
            }
            entries.Add(new ColumnPricingEntry<V>(requestResult.Value, modelResult.Value));
        }
        return Results.Ok(new ColumnPricingBatch<V>(
            owner: this,
            revision: CurrentRevision(snapshotResult.Value),
            entries: entries));
    }

    /// <summary>创建局部线性定价模型；只有完全属于当前块的行作为局部硬约束。</summary>
    public async Task<Result<LinearMetaModel<V>, ErrorCode, Error<ErrorCode>>> PricingModelAsync(
        ColumnPricingRequest<V> request) {
        if (request is null) {
            return FailureModel(
                ErrorCode.IllegalArgument,
                "定价请求为空 / Pricing request is null");
        }
        Try valid = await RequireCurrentAsync(request.Revision);
        if (!valid.IsOk) {
            return Propagate<LinearMetaModel<V>, Success>(valid);
        }
        return PricingModelInternal(request, CurrentIndex());
    }

    /// <summary>按当前注册的列向量和原变量身份合并解；不假设跨模型 token 顺序一致。</summary>
    public async Task<Result<IReadOnlyList<V>, ErrorCode, Error<ErrorCode>>> ExtractSolutionAsync(
        ColumnMasterRegistration<V> registration,
        IReadOnlyList<V> values,
        V tolerance) {
        if (registration is null || values is null) {
            return FailureSolution(
                ErrorCode.IllegalArgument,
                "注册或解向量为空 / Registration or solution is null");
        }
        Try valid = await RequireCurrentAsync(registration);
        if (!valid.IsOk) {
            return Propagate<IReadOnlyList<V>, Success>(valid);
        }
        if (!registration.Integer ||
            registration.PhaseOne ||
            !tolerance.IsFinite() ||
            tolerance.Ls(Source.Converter.Zero) ||
            !TryAdd(tolerance, tolerance, out V doubledTolerance) ||
            doubledTolerance.Geq(Source.Converter.One)) {
            return FailureSolution(
                ErrorCode.IllegalArgument,
                "提取需要无人工变量的整数主问题及有效容差 / Extraction requires an integer master without artificials and a valid tolerance");
        }

        ColumnProjectionIndex<V> index = CurrentIndex();
        LinearModelSnapshot<V> snapshot = CurrentSnapshot();
        if (values.Count != registration.Model.Tokens.TokensInSolver.Count ||
            values.Any(value => !value.IsFinite())) {
            return FailureSolution(
                ErrorCode.IllegalArgument,
                "主问题解向量不完整或非有限 / Incomplete or nonfinite master solution");
        }

        V? ValueOf(IVariableItem variable) {
            int? position = registration.Model.Tokens.IndexOf(variable);
            return position is { } indexOfVariable &&
                indexOfVariable >= 0 &&
                indexOfVariable < values.Count
                ? values[indexOfVariable]
                : null;
        }

        var full = new Dictionary<VariableItemKey, V>();
        foreach (LinearVariableState<V> state in snapshot.Variables) {
            if (index.MappedVariables.Contains(state.Variable) &&
                !registration.RefinedResources.Contains(state.Variable)) {
                continue;
            }
            V? value = ValueOf(state.Variable);
            if (value is null) {
                return FailureSolution(
                    ErrorCode.DataNotFound,
                    "缺少全局变量值 / Missing global variable value");
            }
            full[state.Variable.Key] = value.Value;
        }

        var columnsByBlock = registration.Columns
            .GroupBy(column => column.BlockId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.Ordinal);
        foreach (string blockId in _blockIds) {
            var selected = new List<ProjectedColumn<V>>();
            foreach (ProjectedColumn<V> column in columnsByBlock.GetValueOrDefault(
                blockId,
                new List<ProjectedColumn<V>>())) {
                V? amount = ValueOf(registration.ColumnVariables[column.Id]);
                if (amount is null) {
                    return FailureSolution(
                        ErrorCode.DataNotFound,
                        "缺少列选择变量值 / Missing column-selection value");
                }
                V upper = Source.Converter.One.Plus(tolerance);
                V lower = Source.Converter.One.Minus(tolerance);
                V zeroUpper = tolerance;
                V zeroLower = Source.Converter.Zero.Minus(tolerance);
                if (amount.Value.Geq(lower) && amount.Value.Leq(upper)) {
                    selected.Add(column);
                }
                else if (!(amount.Value.Geq(zeroLower) && amount.Value.Leq(zeroUpper))) {
                    return FailureSolution(
                        ErrorCode.IllegalArgument,
                        "列选择值不是整数 / Nonintegral column-selection value");
                }
            }
            if (selected.Count != 1) {
                return FailureSolution(
                    ErrorCode.IllegalArgument,
                    "每个配置块必须恰选一列 / Each configuration block must select exactly one column");
            }
            foreach (ColumnDecision decision in index.DecisionsByBlock[blockId]) {
                if (registration.RefinedResources.Contains(decision.Variable)) {
                    continue;
                }
                full[decision.Variable.Key] = selected[0].Values[decision.Id];
            }
        }

        var result = new List<V>(Source.Tokens.TokensInSolver.Count);
        foreach (Core.Token.Token<V> token in Source.Tokens.TokensInSolver) {
            if (!full.TryGetValue(token.Variable.Key, out V value)) {
                return FailureSolution(
                    ErrorCode.DataNotFound,
                    "缺少原始模型变量 / Missing source-model variable");
            }
            result.Add(value);
        }
        return Results.Ok<IReadOnlyList<V>>(result.AsReadOnly());
    }

    private string? ValidateMapping() {
        if (_blockIds.Count == 0 ||
            _blockIds.Any(string.IsNullOrWhiteSpace) ||
            _blockIds.Distinct(StringComparer.Ordinal).Count() != _blockIds.Count) {
            return "配置块集合无效 / Invalid configuration-block set";
        }
        if (_decisions.Any(decision =>
                string.IsNullOrWhiteSpace(decision.Id) ||
                !_blockIdSet.Contains(decision.BlockId))) {
            return "列决策映射无效 / Invalid column-decision mapping";
        }
        if (_decisions.Select(decision => decision.Id).Distinct(StringComparer.Ordinal).Count() != _decisions.Count ||
            _decisions.Select(decision => decision.Variable.Key).Distinct().Count() != _decisions.Count) {
            return "列决策身份重复 / Duplicate column-decision identity";
        }
        return null;
    }

    private string? ValidateColumns(
        IReadOnlyList<ProjectedColumn<V>> columns,
        ColumnProjectionIndex<V> index) {
        if (columns.Any(column => column is null)) {
            return "列集合包含空元素 / Column collection contains a null element";
        }
        if (columns.Select(column => column.Id).Distinct(StringComparer.Ordinal).Count() != columns.Count) {
            return "列向量身份重复 / Duplicate column vector identity";
        }
        foreach (ProjectedColumn<V> column in columns) {
            if (string.IsNullOrWhiteSpace(column.Id) ||
                !_blockIdSet.Contains(column.BlockId) ||
                !column.Values.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(index.IdsByBlock[column.BlockId]) ||
                column.Values.Values.Any(value => !value.IsFinite())) {
                return "列向量或资源映射无效 / Invalid column vector or resource mapping";
            }
        }
        return null;
    }

    private Result<V, ErrorCode, Error<ErrorCode>> ProjectedCoefficient(
        ColumnProjectionIndex<V> index,
        string rowId,
        ProjectedColumn<V> column,
        IReadOnlySet<IVariableItem> refined) {
        V sum = Source.Converter.Zero;
        if (index.RowTerms.TryGetValue(
                rowId,
                out IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<V>>>? termsByBlock) &&
            termsByBlock.TryGetValue(
                column.BlockId,
                out IReadOnlyList<ColumnProjectionTerm<V>>? rowTerms) &&
            rowTerms.Count != 0) {
            foreach (ColumnProjectionTerm<V> term in rowTerms) {
                if (!string.Equals(term.Decision.BlockId, column.BlockId, StringComparison.Ordinal) ||
                    refined.Contains(term.Decision.Variable)) {
                    continue;
                }
                if (!TryMultiply(
                        term.Coefficient,
                        column.Values[term.Decision.Id],
                        out V contribution) ||
                    !TryAdd(sum, contribution, out sum)) {
                    return FailureV(
                        ErrorCode.IllegalArgument,
                        "列投影系数溢出 / Projected column coefficient overflow");
                }
            }
        }
        return sum.IsFinite()
            ? Results.Ok(sum)
            : FailureV(
                ErrorCode.IllegalArgument,
                "列投影系数无效 / Invalid projected column coefficient");
    }

    private Result<V, ErrorCode, Error<ErrorCode>> ObjectiveCoefficient(
        ColumnProjectionIndex<V> index,
        ProjectedColumn<V> column,
        IReadOnlySet<IVariableItem> refined) {
        V sum = Source.Converter.Zero;
        if (index.ObjectiveTerms.TryGetValue(
                column.BlockId,
                out IReadOnlyList<ColumnProjectionTerm<V>>? terms)) {
            foreach (ColumnProjectionTerm<V> term in terms) {
                if (refined.Contains(term.Decision.Variable)) {
                    continue;
                }
                if (!TryMultiply(
                        term.Coefficient,
                        column.Values[term.Decision.Id],
                        out V contribution) ||
                    !TryAdd(sum, contribution, out sum)) {
                    return FailureV(
                        ErrorCode.IllegalArgument,
                        "列目标系数溢出 / Projected objective coefficient overflow");
                }
            }
        }
        return sum.IsFinite()
            ? Results.Ok(sum)
            : FailureV(
                ErrorCode.IllegalArgument,
                "列目标系数无效 / Invalid projected objective coefficient");
    }

    private bool RowFeasible(LinearModelRow<V> row, V value, V tolerance) {
        if (!value.IsFinite() ||
            !TryAdd(row.Rhs, tolerance, out V upper) ||
            !TrySubtract(row.Rhs, tolerance, out V lower)) {
            return false;
        }
        return row.Comparison switch {
            Comparison.LE => value.Leq(upper),
            Comparison.GE => value.Geq(lower),
            Comparison.EQ => value.Geq(lower) && value.Leq(upper),
            _ => false,
        };
    }

    private bool WithinBounds(
        V value,
        V? lowerBound,
        V? upperBound,
        V tolerance) {
        if (!value.IsFinite()) {
            return false;
        }
        if (lowerBound is { } lower) {
            if (!TrySubtract(lower, tolerance, out V lowerLimit) ||
                value.Ls(lowerLimit)) {
                return false;
            }
        }
        if (upperBound is { } upper) {
            if (!TryAdd(upper, tolerance, out V upperLimit) ||
                value.Gr(upperLimit)) {
                return false;
            }
        }
        return true;
    }

    private async Task<Try> ValidatePricingStateAsync(
        LinearModelSnapshot<V> snapshot,
        bool phaseOne,
        bool seed) {
        ColumnProjectionRevision revision = CurrentRevision(snapshot);
        PriceState? prices;
        ColumnMasterRegistration<V>? active;
        lock (_gate) {
            prices = _prices;
            active = _active;
        }
        if (!seed && (prices is null || prices.Revision != revision)) {
            return Failure<Success>(
                ErrorCode.IllegalArgument,
                "定价需要当前注册的对偶 / Pricing requires current-registration duals");
        }
        if (!seed) {
            if (active is null) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    "缺少当前主问题 / Missing active master");
            }
            Try valid = await active.Model.State.RequireCurrentAsync(active.ModelRevision);
            if (!valid.IsOk) {
                return valid;
            }
            if (active.PhaseOne != phaseOne) {
                return Failure<Success>(
                    ErrorCode.IllegalArgument,
                    "定价阶段与对偶不一致 / Pricing phase disagrees with dual phase");
            }
        }
        return Results.OkInstance;
    }

    private Result<ColumnPricingRequest<V>, ErrorCode, Error<ErrorCode>> PricingRequestInternal(
        LinearModelSnapshot<V> snapshot,
        ColumnProjectionIndex<V> index,
        string blockId,
        bool phaseOne,
        bool seed) {
        if (!index.DecisionsByBlock.TryGetValue(blockId, out IReadOnlyList<ColumnDecision>? decisions)) {
            return FailureRequest(
                ErrorCode.IllegalArgument,
                "未知配置块 / Unknown configuration block");
        }
        IReadOnlyDictionary<string, V> dual;
        V convexityPrice;
        lock (_gate) {
            dual = seed
                ? new Dictionary<string, V>(StringComparer.Ordinal)
                : _prices?.RowPrices ?? new Dictionary<string, V>(StringComparer.Ordinal);
            if (seed) {
                convexityPrice = Source.Converter.Zero;
            }
            else if (!_convexityPrices.TryGetValue(blockId, out convexityPrice)) {
                return FailureRequest(
                    ErrorCode.IllegalArgument,
                    "定价缺少配置块对偶 / Pricing is missing the block convexity dual");
            }
        }

        var coefficients = new Dictionary<string, V>(StringComparer.Ordinal);
        foreach (ColumnDecision decision in decisions) {
            V value = phaseOne
                ? Source.Converter.Zero
                : snapshot.Objective.GetValueOrDefault(decision.Variable, Source.Converter.Zero);
            foreach (LinearModelRow<V> row in snapshot.Rows) {
                if (!dual.TryGetValue(row.Id, out V rowPrice) ||
                    !row.Coefficients.TryGetValue(decision.Variable, out V coefficient)) {
                    continue;
                }
                if (!TryMultiply(rowPrice, coefficient, out V contribution) ||
                    !TrySubtract(value, contribution, out value)) {
                    return FailureRequest(
                        ErrorCode.IllegalArgument,
                        "定价投影系数溢出 / Pricing projection coefficient overflow");
                }
            }
            if (!value.IsFinite()) {
                return FailureRequest(
                    ErrorCode.IllegalArgument,
                    "定价投影系数溢出 / Pricing projection coefficient overflow");
            }
            coefficients[decision.Id] = value;
        }
        if (!convexityPrice.IsFinite()) {
            return FailureRequest(
                ErrorCode.IllegalArgument,
                "定价对偶非有限 / Pricing convexity price is nonfinite");
        }
        return Results.Ok(new ColumnPricingRequest<V>(
            blockId: blockId,
            coefficients: coefficients,
            convexityPrice: convexityPrice,
            rowPrices: dual,
            revision: CurrentRevision(snapshot)));
    }

    private Result<LinearMetaModel<V>, ErrorCode, Error<ErrorCode>> PricingModelInternal(
        ColumnPricingRequest<V> request,
        ColumnProjectionIndex<V> index) {
        if (!index.DecisionsByBlock.TryGetValue(
                request.BlockId,
                out IReadOnlyList<ColumnDecision>? decisions) ||
            !request.Coefficients.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(index.IdsByBlock[request.BlockId]) ||
            request.Coefficients.Values.Any(value => !value.IsFinite()) ||
            !request.ConvexityPrice.IsFinite()) {
            return FailureModel(
                ErrorCode.IllegalArgument,
                "定价请求结构无效 / Invalid pricing-request structure");
        }

        LinearMetaModel<V> model = new(
            name: "column_pricing",
            converter: Source.Converter);
        try {
            foreach (ColumnDecision decision in decisions) {
                Try added = model.Add(decision.Variable);
                if (!added.IsOk) {
                    model.Dispose();
                    return Propagate<LinearMetaModel<V>, Success>(added);
                }
            }
            foreach (LinearModelRow<V> row in index.LocalRows[request.BlockId]) {
                var lhsTerms = row.Coefficients
                    .Select(pair => new LinearMonomial<V>(pair.Value, pair.Key))
                    .ToList();
                var relation = new LinearInequality<V>(
                    new LinearPolynomial<V>(lhsTerms, Source.Converter.Zero),
                    new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), row.Rhs),
                    row.Comparison);
                Try added = model.AddConstraint(
                    relation: relation,
                    group: null,
                    name: row.Id);
                if (!added.IsOk) {
                    model.Dispose();
                    return Propagate<LinearMetaModel<V>, Success>(added);
                }
            }
            var objectiveTerms = decisions
                .Select(decision => new LinearMonomial<V>(
                    request.Coefficients[decision.Id],
                    decision.Variable))
                .ToList();
            Try objective = model.AddObject(
                category: ObjectCategory.Minimum,
                polynomial: new LinearPolynomial<V>(
                    objectiveTerms,
                    request.ConvexityPrice.Negate()),
                name: "column_pricing_objective",
                displayName: null);
            if (!objective.IsOk) {
                model.Dispose();
                return Propagate<LinearMetaModel<V>, Success>(objective);
            }
            return Results.Ok(model);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            model.Dispose();
            return FailureModel(ErrorCode.ApplicationError, error.Message);
        }
    }

    private IReadOnlyList<ProjectedColumn<V>> CurrentColumns() {
        lock (_gate) {
            return _columns;
        }
    }

    private void PublishColumns(IEnumerable<ProjectedColumn<V>> columns) {
        ProjectedColumn<V>[] frozen = columns
            .Select(column => new ProjectedColumn<V>(
                id: column.Id,
                blockId: column.BlockId,
                values: column.Values))
            .ToArray();
        lock (_gate) {
            ++_sequence;
            _columns = Array.AsReadOnly(frozen);
            _prices = null;
            _convexityPrices = new Dictionary<string, V>(StringComparer.Ordinal);
            _active = null;
        }
    }

    private ColumnProjectionIndex<V> CurrentIndex() {
        lock (_gate) {
            return _index ?? throw new InvalidOperationException(
                "Column projection has not been snapshotted.");
        }
    }

    private LinearModelSnapshot<V> CurrentSnapshot() {
        lock (_gate) {
            return _current ?? throw new InvalidOperationException(
                "Column projection has not been snapshotted.");
        }
    }

    private ColumnProjectionRevision CurrentRevision(LinearModelSnapshot<V> snapshot) {
        lock (_gate) {
            return CurrentRevision(snapshot, _sequence);
        }
    }

    private ColumnProjectionRevision CurrentRevision(
        LinearModelSnapshot<V> snapshot,
        long sequence) => new(snapshot.Revision, _owner, sequence);

    /// <summary>读取下一次发布将产生的序号 / Read the sequence the next publish will produce.</summary>
    private long PendingSequence() {
        lock (_gate) {
            return _sequence + 1;
        }
    }

    private static bool TryMaterialize<T>(
        IEnumerable<T>? source,
        string name,
        out List<T> materialized,
        out string? error) {
        materialized = new List<T>();
        error = null;
        if (source is null) {
            error = $"{name}不能为空 / {name} is null";
            return false;
        }
        try {
            foreach (T item in source) {
                materialized.Add(item);
            }
            return true;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            materialized.Clear();
            error = $"{name}无法枚举：{exception.Message} / Cannot enumerate {name}: {exception.Message}";
            return false;
        }
    }

    private static bool TryAdd(V lhs, V rhs, out V result) {
        try {
            result = lhs.Plus(rhs);
            return result.IsFinite();
        }
        catch (ArithmeticException) {
            result = default;
            return false;
        }
        catch (InvalidOperationException) {
            result = default;
            return false;
        }
    }

    private static bool TrySubtract(V lhs, V rhs, out V result) {
        try {
            result = lhs.Minus(rhs);
            return result.IsFinite();
        }
        catch (ArithmeticException) {
            result = default;
            return false;
        }
        catch (InvalidOperationException) {
            result = default;
            return false;
        }
    }

    private static bool TryMultiply(V lhs, V rhs, out V result) {
        try {
            result = lhs.Times(rhs);
            return result.IsFinite();
        }
        catch (ArithmeticException) {
            result = default;
            return false;
        }
        catch (InvalidOperationException) {
            result = default;
            return false;
        }
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));

    private static Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> FailureSnapshot(
        ErrorCode code,
        string message) =>
        Failure<LinearModelSnapshot<V>>(code, message);

    private static Result<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>> FailureRegistration(
        ErrorCode code,
        string message) =>
        Failure<ColumnMasterRegistration<V>>(code, message);

    private static Result<ColumnPricingRequest<V>, ErrorCode, Error<ErrorCode>> FailureRequest(
        ErrorCode code,
        string message) =>
        Failure<ColumnPricingRequest<V>>(code, message);

    private static Result<ColumnPricingBatch<V>, ErrorCode, Error<ErrorCode>> FailureBatch(
        ErrorCode code,
        string message) =>
        Failure<ColumnPricingBatch<V>>(code, message);

    private static Result<LinearMetaModel<V>, ErrorCode, Error<ErrorCode>> FailureModel(
        ErrorCode code,
        string message) =>
        Failure<LinearMetaModel<V>>(code, message);

    private static Result<IReadOnlyList<V>, ErrorCode, Error<ErrorCode>> FailureSolution(
        ErrorCode code,
        string message) =>
        Failure<IReadOnlyList<V>>(code, message);

    private static Result<V, ErrorCode, Error<ErrorCode>> FailureV(
        ErrorCode code,
        string message) =>
        Failure<V>(code, message);

    private static Result<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>> CloseAndFailure(
        LinearMetaModel<V> model,
        Try result,
        ErrorCode fallbackCode,
        string fallbackMessage) {
        model.Dispose();
        return Propagate<ColumnMasterRegistration<V>, Success>(result) switch {
            Failed<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<ColumnMasterRegistration<V>>(failed.Error),
            Fatal<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => FailureRegistration(fallbackCode, fallbackMessage),
        };
    }

    private static Result<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>> CloseAndFailure(
        LinearMetaModel<V> model,
        Result<V, ErrorCode, Error<ErrorCode>> result,
        ErrorCode fallbackCode,
        string fallbackMessage) {
        model.Dispose();
        return Propagate<ColumnMasterRegistration<V>, V>(result) switch {
            Failed<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<ColumnMasterRegistration<V>>(failed.Error),
            Fatal<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<ColumnMasterRegistration<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => FailureRegistration(fallbackCode, fallbackMessage),
        };
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        if (result is Failed<S, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<T>(failed.Error);
        }
        if (result is Fatal<S, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        return Failure<T>(
            ErrorCode.ApplicationError,
            "结果传播失败 / Result propagation failed");
    }
}
