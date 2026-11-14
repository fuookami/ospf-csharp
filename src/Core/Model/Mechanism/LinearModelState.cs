#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>模型会话及其结构版本；不能跨模型误用 / Model session and structural revision, never interchangeable across models.</summary>
/// <remarks>仅在当前进程内标识已观察的结构，不证明 token 身份或解的有效性。 / Identifies observed structure only within the current process, not token identity or solution validity.</remarks>
/// <param name="Owner">模型会话身份 / Model-session identity.</param>
/// <param name="Sequence">结构版本 / Structural revision.</param>
public readonly record struct LinearModelRevision(long Owner, long Sequence) {
    private static long _owners;
    internal static long AllocateOwner() => Interlocked.Increment(ref _owners);
}

/// <summary>不可变的已展开线性行；未显式绑定的 ID 仅在当前模型会话内有效。</summary>
/// <remarks>
/// Immutable expanded linear row; unbound IDs are local to this model session.
/// Names are not identities.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class LinearModelRow<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>稳定行身份 / Stable row identity.</summary>
    public string Id { get; }
    /// <summary>原始显示名称，不用作身份 / Original display name, not an identity.</summary>
    public string SourceName { get; }
    /// <summary>按变量身份索引的系数 / Coefficients keyed by variable identity.</summary>
    public IReadOnlyDictionary<IVariableItem, V> Coefficients { get; }
    /// <summary>规范化右端 / Normalized right-hand side.</summary>
    public V Rhs { get; }
    /// <summary>比较方向 / Comparison direction.</summary>
    public Comparison Comparison { get; }

    public LinearModelRow(
        string id,
        string sourceName,
        IReadOnlyDictionary<IVariableItem, V> coefficients,
        V rhs,
        Comparison comparison) {
        Id = id;
        SourceName = sourceName;
        Coefficients = new ReadOnlyDictionary<IVariableItem, V>(
            new Dictionary<IVariableItem, V>(coefficients));
        Rhs = rhs;
        Comparison = comparison;
    }

    /// <summary>比较行的结构内容 / Compare structural row content.</summary>
    /// <param name="other">另一个行 / Another row.</param>
    /// <returns>内容是否相同 / Whether the content is identical.</returns>
    public bool SameAs(LinearModelRow<V>? other) {
        if (other is null ||
            !string.Equals(Id, other.Id, StringComparison.Ordinal) ||
            !string.Equals(SourceName, other.SourceName, StringComparison.Ordinal) ||
            !StructuralValueEquality<V>.Same(Rhs, other.Rhs) ||
            Comparison != other.Comparison ||
            Coefficients.Count != other.Coefficients.Count) {
            return false;
        }

        foreach ((IVariableItem variable, V coefficient) in Coefficients) {
            if (!other.Coefficients.TryGetValue(variable, out V otherCoefficient) ||
                !StructuralValueEquality<V>.Same(coefficient, otherCoefficient)) {
                return false;
            }
        }
        return true;
    }
}

/// <summary>变量范围快照，不保留可变范围对象 / Variable-bound snapshot without retaining mutable range objects.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed record LinearVariableState<V>(
    IVariableItem Variable,
    V? LowerBound,
    V? UpperBound)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>从原始模型派生的结构快照；目标统一为最小化 / Structural snapshot derived from the source, with a minimizing objective.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class LinearModelSnapshot<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>会话及版本 / Session and revision.</summary>
    public LinearModelRevision Revision { get; }
    /// <summary>包含展开辅助变量的范围 / Bounds including expanded auxiliary variables.</summary>
    public IReadOnlyList<LinearVariableState<V>> Variables { get; }
    /// <summary>完整已展开行 / Complete expanded rows.</summary>
    public IReadOnlyList<LinearModelRow<V>> Rows { get; }
    /// <summary>规范化目标系数 / Normalized objective coefficients.</summary>
    public IReadOnlyDictionary<IVariableItem, V> Objective { get; }
    /// <summary>只计算一次的目标常数 / Objective constant counted once.</summary>
    public V ObjectiveConstant { get; }
    /// <summary>原始目标方向 / Original objective direction.</summary>
    public ObjectCategory ObjectCategory { get; }

    public LinearModelSnapshot(
        LinearModelRevision revision,
        IReadOnlyList<LinearVariableState<V>> variables,
        IReadOnlyList<LinearModelRow<V>> rows,
        IReadOnlyDictionary<IVariableItem, V> objective,
        V objectiveConstant,
        ObjectCategory objectCategory) {
        Revision = revision;
        Variables = new ReadOnlyCollection<LinearVariableState<V>>(variables.ToList());
        Rows = new ReadOnlyCollection<LinearModelRow<V>>(rows.ToList());
        Objective = new ReadOnlyDictionary<IVariableItem, V>(
            new Dictionary<IVariableItem, V>(objective));
        ObjectiveConstant = objectiveConstant;
        ObjectCategory = objectCategory;
    }

    /// <summary>判断除版本外的结构内容是否相同 / Compare structural content except the revision.</summary>
    /// <param name="other">另一个快照 / Another snapshot.</param>
    /// <returns>结构内容是否相同 / Whether the structural content is identical.</returns>
    public bool SameStructure(LinearModelSnapshot<V>? other) {
        if (other is null ||
            Variables.Count != other.Variables.Count ||
            Rows.Count != other.Rows.Count ||
            Objective.Count != other.Objective.Count ||
            !StructuralValueEquality<V>.Same(ObjectiveConstant, other.ObjectiveConstant) ||
            ObjectCategory != other.ObjectCategory) {
            return false;
        }

        for (int i = 0; i < Variables.Count; i++) {
            LinearVariableState<V> left = Variables[i];
            LinearVariableState<V> right = other.Variables[i];
            if (!EqualityComparer<IVariableItem>.Default.Equals(left.Variable, right.Variable) ||
                !StructuralValueEquality<V>.Same(left.LowerBound, right.LowerBound) ||
                !StructuralValueEquality<V>.Same(left.UpperBound, right.UpperBound)) {
                return false;
            }
        }
        for (int i = 0; i < Rows.Count; i++) {
            if (!Rows[i].SameAs(other.Rows[i])) {
                return false;
            }
        }
        foreach ((IVariableItem variable, V coefficient) in Objective) {
            if (!other.Objective.TryGetValue(variable, out V otherCoefficient) ||
                !StructuralValueEquality<V>.Same(coefficient, otherCoefficient)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>比较包含版本在内的完整快照 / Compare the complete snapshot including its revision.</summary>
    /// <param name="obj">另一个对象 / Another object.</param>
    /// <returns>是否相等 / Whether equal.</returns>
    public override bool Equals(object? obj) =>
        obj is LinearModelSnapshot<V> other &&
        Revision == other.Revision &&
        SameStructure(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Revision, Variables.Count, Rows.Count, Objective.Count);
}

/// <summary>模型自有的结构观察器；原始 MetaModel 是唯一模型定义来源。</summary>
/// <remarks>
/// Model-owned structural observer; the original MetaModel remains the sole model definition source.
/// Expansion and comparison happen at explicit refresh boundaries.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class LinearModelState<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private sealed record WriteState(
        KeyValuePair<MathConstraint, string>[] RowIds,
        KeyValuePair<
            string,
            (
                MathConstraint Origin,
                LinearModelRow<V> Definition,
                IReadOnlyDictionary<IIntermediateSymbol, FunctionProjection> Functions
            )>[] NamedRows,
        LinearModelSnapshot<V>? Latest);

    private readonly long _owner = LinearModelRevision.AllocateOwner();
    private readonly LinearMetaModel<V> _model;
    private readonly Dictionary<MathConstraint, string> _rowIds =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, (
        MathConstraint Origin,
        LinearModelRow<V> Definition,
        IReadOnlyDictionary<IIntermediateSymbol, FunctionProjection> Functions)> _namedRows =
        new(StringComparer.Ordinal);
    private LinearModelSnapshot<V>? _latest;

    internal LinearModelState(LinearMetaModel<V> model) {
        _model = model;
    }

    internal object CaptureWriteState() => new WriteState(
        RowIds: _rowIds.ToArray(),
        NamedRows: _namedRows.ToArray(),
        Latest: _latest);

    internal Try RestoreWriteState(object state) {
        if (state is not WriteState snapshot) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "线性模型状态事务快照无效 / Invalid linear model-state transaction snapshot"));
        }
        try {
            _rowIds.Clear();
            foreach ((MathConstraint constraint, string id) in snapshot.RowIds) {
                _rowIds.Add(constraint, id);
            }
            _namedRows.Clear();
            foreach ((string id, (
                         MathConstraint Origin,
                         LinearModelRow<V> Definition,
                         IReadOnlyDictionary<IIntermediateSymbol, FunctionProjection> Functions) row) in snapshot.NamedRows) {
                _namedRows.Add(id, row);
            }
            _latest = snapshot.Latest;
            return Results.Ok(Results.SuccessInstance);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                $"恢复线性模型状态失败：{error.Message} / Cannot restore linear model state: {error.Message}"));
        }
    }

    /// <summary>注册已有变量上的不可变线性行；同 ID 同内容幂等，冲突则失败。</summary>
    /// <remarks>
    /// Register an immutable linear row over existing variables; identical IDs and contents are idempotent.
    /// </remarks>
    /// <param name="id">显式稳定行身份 / Explicit stable row identity.</param>
    /// <param name="relation">变量及已注册函数的嵌套线性表达式 / Nested linear expression over variables and registered functions.</param>
    /// <returns>注册结果 / Registration result.</returns>
    public Try RegisterRow(ConstraintId id, LinearInequality<V> relation) {
        if (_model.IsClosed) {
            return Failure(ErrorCode.ApplicationStopped, "模型已关闭 / Model is closed");
        }
        Try identityCheck = new ModelElementIdentityRegistry().RegisterConstraint(
            element: new object(),
            id: id);
        if (!identityCheck.IsOk) {
            return identityCheck;
        }
        if (relation.Comparison is not (Comparison.LE or Comparison.EQ or Comparison.GE)) {
            return Failure(
                ErrorCode.IllegalArgument,
                "动态线性行只支持非严格比较 / Dynamic linear rows require non-strict comparisons");
        }

        var functions = new HashSet<IIntermediateSymbol>(ReferenceEqualityComparer.Instance);
        Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened = relation.ToLinearFlattenData(
            tokens: _model.Tokens,
            functions: functions
        );
        if (flattened is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        if (flattened is Fatal<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }

        var normalized = new Dictionary<IVariableItem, V>();
        foreach (LinearMonomial<V> term in flattened.Value.Monomials) {
            var variable = (IVariableItem)term.Symbol;
            if (_model.Tokens.Find(variable) is null) {
                return Failure(
                    ErrorCode.IllegalArgument,
                    "动态行引用未知变量 / Dynamic row references an unknown variable");
            }
            if (!term.Coefficient.Eq(_model.Converter.Zero)) {
                normalized[variable] = term.Coefficient;
            }
        }
        V rhs;
        try {
            if (!LinearFlattenExtensions.Arithmetic<V>.TryNegate(flattened.Value.Constant, out rhs)) {
                return Failure(
                    ErrorCode.IllegalArgument,
                    "动态行常量无效 / Invalid dynamic-row constant");
            }
        }
        catch (ArithmeticException) {
            return Failure(
                ErrorCode.IllegalArgument,
                "动态行常量无效 / Invalid dynamic-row constant");
        }
        catch (InvalidOperationException) {
            return Failure(
                ErrorCode.IllegalArgument,
                "动态行常量无效 / Invalid dynamic-row constant");
        }

        var projections = new Dictionary<IIntermediateSymbol, FunctionProjection>(ReferenceEqualityComparer.Instance);
        foreach (IIntermediateSymbol function in functions) {
            Result<FunctionProjection, ErrorCode, Error<ErrorCode>> projection = CaptureProjection(function, _model.Tokens);
            if (!projection.IsOk) {
                return projection.Map(_ => Results.SuccessInstance);
            }
            projections.Add(function, projection.Value);
        }
        if (functions.Count != 0) {
            var checkedFunctions = new HashSet<IIntermediateSymbol>(ReferenceEqualityComparer.Instance);
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> checkedRow = relation.ToLinearFlattenData(
                tokens: _model.Tokens,
                functions: checkedFunctions
            );
            if (!checkedRow.IsOk) {
                return checkedRow.Map(_ => Results.SuccessInstance);
            }
            if (!functions.SetEquals(checkedFunctions) || !SameFlattenedRow(flattened.Value, checkedRow.Value)) {
                return Failure(
                    ErrorCode.IllegalArgument,
                    "动态行在注册期间发生变化 / Dynamic row changed during registration");
            }
        }

        var definition = new LinearModelRow<V>(
            id: id.Value,
            sourceName: id.Value,
            coefficients: normalized,
            rhs: rhs,
            comparison: relation.Comparison);
        // 直接 RemoveConstraint 移除动态行不会经过 RemoveRow，先清理已脱离模型的身份。
        // Removing a dynamic row through RemoveConstraint bypasses RemoveRow; prune identities no longer in the model.
        if (_namedRows.TryGetValue(id.Value, out (
                MathConstraint Origin,
                LinearModelRow<V> Definition,
                IReadOnlyDictionary<IIntermediateSymbol, FunctionProjection> Functions) stale) &&
            !_model.Constraints.Any(constraint => ReferenceEquals(constraint, stale.Origin))) {
            _rowIds.Remove(stale.Origin);
            _namedRows.Remove(id.Value);
        }
        if (_namedRows.TryGetValue(id.Value, out (
                MathConstraint Origin,
                LinearModelRow<V> Definition,
                IReadOnlyDictionary<IIntermediateSymbol, FunctionProjection> Functions) previous)) {
            if (_model.Constraints.Any(constraint => ReferenceEquals(constraint, previous.Origin)) &&
                previous.Definition.SameAs(definition) &&
                previous.Functions.Count == projections.Count &&
                previous.Functions.Keys.All(projections.ContainsKey)) {
                return ValidateDynamicRow(previous.Origin, _model.Tokens);
            }
            return Failure(
                ErrorCode.IllegalArgument,
                $"行身份已被不同内容占用 / Row identity already has different content: {id.Value}");
        }
        var frozenRelation = new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(
                Monomials: normalized
                    .Select(pair => new LinearMonomial<V>(pair.Value, pair.Key))
                    .ToList(),
                Constant: _model.Converter.Zero),
            Rhs: new LinearPolynomial<V>(
                Monomials: Array.Empty<LinearMonomial<V>>(),
                Constant: definition.Rhs),
            Comparison: definition.Comparison);
        Try added = _model.AddConstraint(
            relation: frozenRelation,
            group: null,
            name: id.Value);
        if (!added.IsOk) {
            return added;
        }

        MathConstraint origin = _model.RelationConstraints[^1];
        Try? registered = _model.IdentityRegistry?.RegisterConstraint(origin, id);
        if (registered is not null && !registered.IsOk) {
            _model.RollbackConstraint(origin);
            return registered;
        }
        _rowIds[origin] = id.Value;
        _namedRows[id.Value] = (origin, definition, projections);
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 删除动态行；未知行按幂等成功处理 / Remove a dynamic row; unknown rows are idempotent successes.
    /// </summary>
    /// <param name="id">动态行身份 / Dynamic-row identity.</param>
    /// <returns>删除结果 / Removal result.</returns>
    public Try RemoveRow(ConstraintId id) {
        if (_model.IsClosed) {
            return Failure(
                ErrorCode.ApplicationStopped,
                "模型已关闭 / Model is closed");
        }
        if (string.IsNullOrWhiteSpace(id.Value)) {
            return Failure(
                ErrorCode.IllegalArgument,
                "动态行身份为空 / Dynamic-row identity is blank");
        }
        if (!_namedRows.TryGetValue(id.Value, out (
                MathConstraint Origin,
                LinearModelRow<V> Definition,
                IReadOnlyDictionary<IIntermediateSymbol, FunctionProjection> Functions) row)) {
            return Results.Ok(Results.SuccessInstance);
        }

        Try removed = _model.RemoveConstraint(row.Origin);
        if (!removed.IsOk) {
            return removed;
        }
        _rowIds.Remove(row.Origin);
        _namedRows.Remove(id.Value);
        return Results.Ok(Results.SuccessInstance);
    }

    internal Try ValidateDynamicRow(MathConstraint origin, IAbstractTokenTable<V> tokens) {
        if (!_rowIds.TryGetValue(origin, out string? id) ||
            !_namedRows.TryGetValue(id, out (
                MathConstraint Origin,
                LinearModelRow<V> Definition,
                IReadOnlyDictionary<IIntermediateSymbol, FunctionProjection> Functions) row) ||
            !ReferenceEquals(origin, row.Origin)) {
            return Results.OkInstance;
        }
        foreach ((IIntermediateSymbol function, FunctionProjection expected) in row.Functions) {
            Result<FunctionProjection, ErrorCode, Error<ErrorCode>> actual = CaptureProjection(function, tokens);
            if (!actual.IsOk) {
                return actual.Map(_ => Results.SuccessInstance);
            }
            if (!expected.SameValues(actual.Value) ||
                !expected.Dependencies.SetEquals(actual.Value.Dependencies)) {
                return Failure(
                    ErrorCode.IllegalArgument,
                    $"动态行函数结果已改变 / Dynamic-row function result changed: {function.Name}");
            }
        }
        return Results.OkInstance;
    }

    private static Result<FunctionProjection, ErrorCode, Error<ErrorCode>> CaptureProjection(
        IIntermediateSymbol function,
        IAbstractTokenTable<V> tokens) {
        if (function is not ILinearIntermediateSymbol<V> linear ||
            !tokens.Symbols.Any(symbol => ReferenceEquals(symbol, function))) {
            return Results.Failed<FunctionProjection>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"动态行函数未注册 / Dynamic-row function is not registered: {function.Name}"));
        }
        try {
            LinearPolynomial<V> polynomial = linear.Polynomial;
            var dependencies = new HashSet<IIntermediateSymbol>(ReferenceEqualityComparer.Instance);
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> expanded =
                new SymbolFlatten.LinearFlattenData<V>(polynomial.Monomials, polynomial.Constant).ExpandIntermediateSymbols(
                    tokens: tokens,
                    functions: dependencies
                );
            if (expanded is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<FunctionProjection>(failed.Error);
            }
            if (expanded is Fatal<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> fatal) {
                return new Fatal<FunctionProjection, ErrorCode, Error<ErrorCode>>(fatal.Errors);
            }
            if (!LinearFlattenExtensions.Arithmetic<V>.IsValid(expanded.Value.Constant)) {
                return InvalidProjection(function);
            }
            var terms = new List<(VariableItemKey Variable, V Coefficient)>();
            foreach (LinearMonomial<V> term in expanded.Value.Monomials) {
                if (term.Symbol is not IVariableItem variable || tokens.Find(variable) is null ||
                    !LinearFlattenExtensions.Arithmetic<V>.IsValid(term.Coefficient)) {
                    return InvalidProjection(function);
                }
                terms.Add((variable.Key, term.Coefficient));
            }
            return Results.Ok(new FunctionProjection(expanded.Value.Constant, terms, dependencies));
        }
        catch (ArithmeticException) {
            return InvalidProjection(function);
        }
        catch (InvalidOperationException) {
            return InvalidProjection(function);
        }
    }

    private static Result<FunctionProjection, ErrorCode, Error<ErrorCode>> InvalidProjection(IIntermediateSymbol function) =>
        Results.Failed<FunctionProjection>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            $"动态行函数结果无效 / Invalid dynamic-row function result: {function.Name}"));

    private static bool SameFlattenedRow(
        SymbolFlatten.LinearFlattenData<V> first,
        SymbolFlatten.LinearFlattenData<V> second) {
        if (!StructuralValueEquality<V>.Same(first.Constant, second.Constant) ||
            first.Monomials.Count != second.Monomials.Count) {
            return false;
        }
        for (int i = 0; i < first.Monomials.Count; ++i) {
            LinearMonomial<V> left = first.Monomials[i];
            LinearMonomial<V> right = second.Monomials[i];
            if (!StructuralValueEquality<V>.Same(left.Coefficient, right.Coefficient) ||
                !((IVariableItem)left.Symbol).Key.Equals(((IVariableItem)right.Symbol).Key)) {
                return false;
            }
        }
        return true;
    }

    private sealed record FunctionProjection(
        V Constant,
        IReadOnlyList<(VariableItemKey Variable, V Coefficient)> Terms,
        IReadOnlySet<IIntermediateSymbol> Dependencies) {
        public bool SameValues(FunctionProjection other) {
            if (!StructuralValueEquality<V>.Same(Constant, other.Constant) || Terms.Count != other.Terms.Count) {
                return false;
            }
            for (int i = 0; i < Terms.Count; ++i) {
                if (!Terms[i].Variable.Equals(other.Terms[i].Variable) ||
                    !StructuralValueEquality<V>.Same(Terms[i].Coefficient, other.Terms[i].Coefficient)) {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>刷新不可变快照，检测直接模型修改；调用期间不得并发修改模型。</summary>
    /// <remarks>Refresh an immutable snapshot and detect direct edits; concurrent model mutation is unsupported.</remarks>
    /// <returns>当前结构及版本 / Current structure and revision.</returns>
    public async Task<Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>>> RefreshAsync() {
        if (_model.IsClosed) {
            return FailureResult(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型已关闭 / Model is closed"));
        }

        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> expandedResult =
            await LinearMechanismModel<V>.InvokeAsync(_model);
        if (expandedResult is Failed<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> failed) {
            return FailureResult(failed.Error);
        }
        if (expandedResult is Fatal<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }

        using LinearMechanismModel<V> full = expandedResult.Value;
        try {
            return Snapshot(full);
        }
        catch (ArithmeticException) {
            return FailureResult("模型快照数值无效 / Invalid model snapshot arithmetic");
        }
        catch (InvalidOperationException) {
            return FailureResult("模型快照数值无效 / Invalid model snapshot arithmetic");
        }
    }

    private Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> Snapshot(LinearMechanismModel<V> full) {
        if (full.ValidateBoundDependencies() is Failed<Success, ErrorCode, Error<ErrorCode>> staleBounds) {
            return FailureResult(staleBounds.Error);
        }
        var variables = full.Tokens.TokensInSolver
            .Select(token => new LinearVariableState<V>(
                token.Variable,
                BoundValue(token.Variable.LowerBound),
                BoundValue(token.Variable.UpperBound)))
            .ToList();

        var rows = new List<LinearModelRow<V>>();
        var pendingRowIds = new Dictionary<MathConstraint, string>(ReferenceEqualityComparer.Instance);
        int derivedIndex = 0;
        foreach (LinearConstraintImpl<V> constraint in full.LinearConstraints) {
            if (!LinearFlattenExtensions.Arithmetic<V>.IsValid(constraint.Rhs)) {
                return FailureResult("模型快照右端无效 / Invalid model snapshot right-hand side");
            }
            var coefficients = new Dictionary<IVariableItem, V>();
            foreach (ILinearCell<V> cell in constraint.LinearCells) {
                if (coefficients.TryGetValue(cell.Token.Variable, out V existing)) {
                    if (!LinearFlattenExtensions.Arithmetic<V>.TryAdd(
                            lhs: existing,
                            rhs: cell.Coefficient,
                            result: out V sum
                        )) {
                        return FailureResult("模型快照行系数溢出 / Model snapshot row coefficient overflow");
                    }
                    coefficients[cell.Token.Variable] = sum;
                }
                else {
                    if (!LinearFlattenExtensions.Arithmetic<V>.IsValid(cell.Coefficient)) {
                        return FailureResult("模型快照行系数无效 / Invalid model snapshot row coefficient");
                    }
                    coefficients[cell.Token.Variable] = cell.Coefficient;
                }
            }
            foreach ((IVariableItem variable, V coefficient) in coefficients.ToList()) {
                if (coefficient.Eq(_model.Converter.Zero)) {
                    coefficients.Remove(variable);
                }
                else if (!LinearFlattenExtensions.Arithmetic<V>.IsValid(coefficient)) {
                    return FailureResult("模型快照包含非有限系数 / Model snapshot contains nonfinite coefficients");
                }
            }

            string? explicitId = constraint.Origin is { } origin
                ? _model.IdentityRegistry?.Identity(origin)?.Id.Value
                : null;
            string rowId;
            if (explicitId is not null) {
                rowId = explicitId;
            }
            else if (constraint.Origin is { } localOrigin) {
                if (!_rowIds.TryGetValue(localOrigin, out rowId!) &&
                    !pendingRowIds.TryGetValue(localOrigin, out rowId!)) {
                    rowId = $"model-local-row:{_owner}:{_rowIds.Count + pendingRowIds.Count}";
                    pendingRowIds.Add(localOrigin, rowId);
                }
            }
            else {
                rowId = $"model-local-derived:{_owner}:{derivedIndex++}";
            }

            rows.Add(new LinearModelRow<V>(
                id: rowId,
                sourceName: constraint.Name,
                coefficients: coefficients,
                rhs: constraint.Rhs,
                comparison: constraint.Sign.ToComparison()));
        }
        if (rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != rows.Count) {
            return FailureResult("已展开行身份重复 / Duplicate expanded row identities");
        }

        var objective = new Dictionary<IVariableItem, V>();
        V constant = _model.Converter.Zero;
        foreach (SubObject<V> part in full.ObjectFunction.GetSubObjects<V>()) {
            V sign = _model.Converter.One;
            if (part.Category != ObjectCategory.Minimum &&
                !LinearFlattenExtensions.Arithmetic<V>.TryNegate(sign, out sign)) {
                return FailureResult("模型快照目标方向无效 / Invalid model snapshot objective direction");
            }
            if (!LinearFlattenExtensions.Arithmetic<V>.TryMultiply(
                    lhs: sign,
                    rhs: part.Constant,
                    result: out V contribution
                ) || !LinearFlattenExtensions.Arithmetic<V>.TryAdd(
                    lhs: constant,
                    rhs: contribution,
                    result: out constant
                )) {
                return FailureResult("模型快照目标常量溢出 / Model snapshot objective constant overflow");
            }
            if (part is not LinearSubObject<V> linearPart) {
                continue;
            }
            foreach (LinearMonomial<V> term in linearPart.LinearTerms()) {
                if (term.Symbol is not IVariableItem variable) {
                    return FailureResult("目标必须仅包含已展开变量 / Objective must contain expanded variables only");
                }
                if (!LinearFlattenExtensions.Arithmetic<V>.TryMultiply(
                        lhs: sign,
                        rhs: term.Coefficient,
                        result: out V value
                    )) {
                    return FailureResult("模型快照目标系数无效 / Invalid model snapshot objective coefficient");
                }
                if (objective.TryGetValue(variable, out V existing)) {
                    if (!LinearFlattenExtensions.Arithmetic<V>.TryAdd(
                            lhs: existing,
                            rhs: value,
                            result: out value
                        )) {
                        return FailureResult("模型快照目标系数溢出 / Model snapshot objective coefficient overflow");
                    }
                }
                objective[variable] = value;
            }
        }
        foreach ((IVariableItem variable, V coefficient) in objective.ToList()) {
            if (coefficient.Eq(_model.Converter.Zero)) {
                objective.Remove(variable);
            }
            else if (!LinearFlattenExtensions.Arithmetic<V>.IsValid(coefficient)) {
                return FailureResult("模型快照包含非有限目标系数 / Model snapshot contains nonfinite objective coefficients");
            }
        }
        if (!LinearFlattenExtensions.Arithmetic<V>.IsValid(constant)) {
            return FailureResult("模型快照包含非有限目标常数 / Model snapshot contains a nonfinite objective constant");
        }

        LinearModelRevision revision = _latest?.Revision ?? new LinearModelRevision(_owner, 0);
        var snapshot = new LinearModelSnapshot<V>(
            revision: revision,
            variables: variables,
            rows: rows,
            objective: objective,
            objectiveConstant: constant,
            objectCategory: _model.ObjectCategory);
        if (_latest is not null && !_latest.SameStructure(snapshot)) {
            snapshot = new LinearModelSnapshot<V>(
                revision: new LinearModelRevision(_owner, _latest.Revision.Sequence + 1),
                variables: snapshot.Variables,
                rows: snapshot.Rows,
                objective: snapshot.Objective,
                objectiveConstant: snapshot.ObjectiveConstant,
                objectCategory: snapshot.ObjectCategory);
        }
        if (full.ValidateBoundDependencies() is Failed<Success, ErrorCode, Error<ErrorCode>> changedBounds) {
            return FailureResult(changedBounds.Error);
        }
        foreach ((MathConstraint origin, string id) in pendingRowIds) {
            _rowIds.Add(origin, id);
        }
        _latest = snapshot;
        return Results.Ok(snapshot);
    }

    /// <summary>刷新不可变快照 / Refresh an immutable snapshot.</summary>
    /// <returns>当前结构及版本 / Current structure and revision.</returns>
    public Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> Refresh() =>
        RefreshAsync().GetAwaiter().GetResult();

    /// <summary>拒绝不同模型或过期版本 / Reject foreign-model or stale revisions.</summary>
    /// <remarks>只验证刷新时的结构，不验证 token 身份或解。 / Checks refreshed structure, not token identity or a solution.</remarks>
    /// <param name="revision">被使用的模型版本 / Model revision being consumed.</param>
    /// <returns>当前版本验证 / Current-revision validation.</returns>
    public async Task<Try> RequireCurrentAsync(LinearModelRevision revision) {
        Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> result = await RefreshAsync();
        if (result is Failed<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        if (result is Fatal<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        return result.Value.Revision == revision
            ? Results.Ok(Results.SuccessInstance)
            : Failure(
                ErrorCode.IllegalArgument,
                "模型版本已过期或属于其他模型 / Stale or foreign model revision");
    }

    /// <summary>同步验证模型版本 / Validate a model revision synchronously.</summary>
    /// <param name="revision">被使用的模型版本 / Model revision being consumed.</param>
    /// <returns>当前版本验证 / Current-revision validation.</returns>
    public Try RequireCurrent(LinearModelRevision revision) =>
        RequireCurrentAsync(revision).GetAwaiter().GetResult();

    private V? BoundValue(Fuookami.Ospf.Math.Algebra.ValueRange.Bound<Flt64>? bound) {
        if (bound is null) {
            return null;
        }
        return _model.Converter.IntoValue(bound.Value.ToFlt64());
    }

    private static Try Failure(ErrorCode code, string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(code, message));

    private static Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> FailureResult(string message) =>
        Results.Failed<LinearModelSnapshot<V>>(
            new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<LinearModelSnapshot<V>, ErrorCode, Error<ErrorCode>> FailureResult(
        Error<ErrorCode> error) =>
        Results.Failed<LinearModelSnapshot<V>>(error);
}
