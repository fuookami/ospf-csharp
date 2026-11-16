#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MathUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

using LinearFlattenData = Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Fuookami.Ospf.Math.Algebra.Number.Flt64>;
namespace Fuookami.Ospf.Core.Model.Mechanism;
// ===== MetaModelConfiguration =====

/// <summary>
/// 元模型配置 / Meta model configuration.
/// </summary>
public sealed record MetaModelConfiguration(
    bool ManualTokenAddition = true,
    bool Concurrent = true,
    bool DumpBlocking = false,
    bool WithRangeSet = false,
    bool CheckTokenExists = false);

// ===== IMetaModelWriteTransaction =====

/// <summary>
/// 元模型批量写入事务 / Meta-model batch-write transaction.
/// </summary>
/// <remarks>
/// 事务捕获 token、约束、目标、派生行状态和稳定身份；调用期间不得并发或重入修改。
/// The transaction captures tokens, constraints, objectives, derived-row state, and stable identities;
/// concurrent or reentrant mutation is unsupported.
/// </remarks>
public interface IMetaModelWriteTransaction {
    /// <summary>提交事务 / Commit the transaction.</summary>
    /// <returns>提交结果 / Commit result.</returns>
    Try Commit();

    /// <summary>回滚事务 / Roll back the transaction.</summary>
    /// <returns>回滚结果 / Rollback result.</returns>
    Try Rollback();
}

// ===== IMetaModel<V> =====

/// <summary>
/// 元模型装配轴心 / Meta-model assembly axis.
/// </summary>
public interface IMetaModel<V> : IDisposable
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>值转换器 / Value converter</summary>
    IFlt64ValueConverter<V> Converter { get; }
    /// <summary>模型名称 / Model name</summary>
    string Name { get; }
    /// <summary>约束列表 / Constraint list</summary>
    IReadOnlyList<MathConstraint> Constraints { get; }
    /// <summary>目标分类 / Objective category</summary>
    ObjectCategory ObjectCategory { get; }
    /// <summary>子目标列表 / Sub-objective list</summary>
    IReadOnlyList<SubObject<V>> SubObjects { get; }
    /// <summary>可变符号表 / Mutable token table</summary>
    Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> Tokens { get; }
    /// <summary>符号依赖关系 / Symbol dependencies</summary>
    IReadOnlyDictionary<IIntermediateSymbol, IReadOnlySet<IIntermediateSymbol>> SymbolDependencies { get; }
    /// <summary>模型类别 / Model category</summary>
    Category Category { get; }
    /// <summary>可选的稳定身份注册表 / Optional stable identity registry</summary>
    ModelElementIdentityRegistry? IdentityRegistry { get; }
    /// <summary>模型是否已关闭 / Whether the model is closed</summary>
    bool IsClosed { get; }

    Try Add(IVariableItem item);
    Try Add(IEnumerable<IVariableItem> items);
    void Remove(IVariableItem item);

    Try Add(IIntermediateSymbol symbol);
    Try Add(IEnumerable<IIntermediateSymbol> symbols);
    void Remove(IIntermediateSymbol symbol);

    /// <summary>注册约束组 / Register a constraint group</summary>
    void RegisterConstraintGroup(IMetaConstraintGroup group);
    /// <summary>约束组索引范围 / Index range of a constraint group</summary>
    Range? IndicesOfConstraintGroup(IMetaConstraintGroup group);
    /// <summary>约束组下的约束 / Constraints under a group</summary>
    IReadOnlyList<MathConstraint> ConstraintsOfGroup(IMetaConstraintGroup group);

    /// <summary>
    /// 移除指定约束对象 / Remove the specified constraint object.
    /// </summary>
    /// <param name="constraint">待移除约束 / Constraint to remove.</param>
    /// <returns>操作结果；未知约束按幂等成功处理 / Operation result; unknown constraints are idempotent successes.</returns>
    Try RemoveConstraint(MathConstraint constraint) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            "当前元模型不支持约束移除。 / This meta model does not support constraint removal."));

    /// <summary>
    /// 将约束回滚到指定数量 / Roll constraints back to the specified count.
    /// </summary>
    /// <param name="size">回滚后的约束数量 / Constraint count after rollback.</param>
    /// <returns>回滚结果 / Rollback result.</returns>
    Try RollbackConstraintsTo(int size) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            "当前元模型不支持约束回滚。 / This meta model does not support constraint rollback."));

    /// <summary>
    /// 从所有目标子项中移除指定变量项 / Remove terms for the specified variables from every objective sub-object.
    /// </summary>
    /// <param name="variables">待移除的变量 / Variables whose terms should be removed.</param>
    /// <returns>操作结果 / Operation result.</returns>
    Try RemoveObjectiveTerms(IReadOnlySet<IVariableItem> variables) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            "当前元模型不支持目标项移除。 / This meta model does not support objective-term removal."));

    /// <summary>开始元模型批量写入事务 / Begin a meta-model batch-write transaction.</summary>
    /// <returns>事务或不支持错误 / Transaction or an unsupported error.</returns>
    Result<IMetaModelWriteTransaction, ErrorCode, Error<ErrorCode>> BeginWrite() =>
        Results.Failed<IMetaModelWriteTransaction>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            "当前元模型不支持写入事务。 / This meta model does not support write transactions."));

    void SetSolution(IReadOnlyList<V> solution);
    void SetSolution(IReadOnlyDictionary<IVariableItem, V> solution);
    void ClearSolution();

    /// <summary>刷新元模型状态 / Flush the meta-model state</summary>
    void Flush(bool force = false);

    Task<Try> ExportAsync();
    Task<Try> ExportAsync(string name);
    Task<Try> ExportAsync(string path, bool unfold);
    Task<Try> ExportAsync(string path, MathUInt64 unfold);
}

// ===== IAbstractLinearMetaModel<V> =====

/// <summary>
/// 线性元模型抽象接口 / Abstract linear meta-model interface.
/// </summary>
public interface IAbstractLinearMetaModel<V> : IMetaModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>添加变量即约束 / Add variable-as-constraint</summary>
    Try AddConstraint(
        IVariableItem constraint,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        bool? withRangeSet = false);

    /// <summary>添加多项式即约束 / Add polynomial-as-constraint</summary>
    Try AddConstraint(
        LinearPolynomial<V> constraint,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        bool? withRangeSet = false);

    /// <summary>添加中间符号即约束 / Add intermediate-symbol-as-constraint</summary>
    Try AddConstraint(
        IIntermediateSymbol constraint,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        bool? withRangeSet = false);

    /// <summary>使用 LinearInequality 添加约束 / Add constraint using LinearInequality</summary>
    Try AddConstraint(
        LinearInequality<V> relation,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        int? priority = null,
        bool? withRangeSet = false);

    /// <summary>添加线性目标 / Add linear objective</summary>
    Try AddObject(
        ObjectCategory category,
        SymbolFlatten.LinearFlattenData<V> flattenData,
        string name,
        string? displayName);

    Try Partition(
        IEnumerable<IVariableItem> variables,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null);

    Try Partition(
        LinearPolynomial<V> polynomial,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null);
}

// ===== IAbstractQuadraticMetaModel<V> =====

/// <summary>
/// 二次元模型抽象接口 / Abstract quadratic meta-model interface.
/// </summary>
public interface IAbstractQuadraticMetaModel<V> : IMetaModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    Try AddConstraint(
        QuadraticPolynomial<V> constraint,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        bool? withRangeSet = null);

    Try AddConstraint(
        IIntermediateSymbol constraint,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        bool? withRangeSet = null);

    /// <summary>使用 QuadraticInequalityOf 添加约束 / Add constraint using QuadraticInequalityOf</summary>
    Try AddConstraint(
        QuadraticInequalityOf<V> relation,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        int? priority = null,
        bool? withRangeSet = null);

    /// <summary>添加二次目标 / Add quadratic objective</summary>
    Try AddObject(
        ObjectCategory category,
        SymbolFlatten.QuadraticFlattenData<V> flattenData,
        string name,
        string? displayName);

    Try Partition(
        QuadraticPolynomial<V> polynomial,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null);
}

// ===== MetaSubObject<V> (nested in IMetaModel) =====

/// <summary>
/// 元模型子目标 / Meta-model sub-objective.
/// </summary>
public sealed class MetaSubObject<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    public IMetaModel<V> Parent { get; }
    public ObjectCategory Category { get; }
    public string Name { get; }
    public string? DisplayName { get; }
    public LinearPolynomial<V> Polynomial { get; }

    public MetaSubObject(
        IMetaModel<V> parent,
        ObjectCategory category,
        string name,
        string? displayName,
        LinearPolynomial<V> polynomial) {
        Parent = parent;
        Category = category;
        Name = name;
        DisplayName = displayName;
        Polynomial = polynomial;
    }

    /// <summary>使用父模型符号表求值 / Evaluate using the parent token table.</summary>
    /// <param name="zeroIfNone">未知时返回整个目标的零值 / Return zero for the whole objective if unknown.</param>
    /// <returns>目标值或未知值 / Objective value, or null if unknown.</returns>
    public V? Evaluate(bool zeroIfNone = false) => Evaluate(Parent.Tokens, zeroIfNone);

    /// <summary>使用指定符号表求值 / Evaluate using the specified token table.</summary>
    /// <param name="tokenTable">变量 token 表 / Variable token table.</param>
    /// <param name="zeroIfNone">未知时返回整个目标的零值 / Return zero for the whole objective if unknown.</param>
    /// <returns>目标值或未知值 / Objective value, or null if unknown.</returns>
    public V? Evaluate(Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokenTable, bool zeroIfNone = false) {
        V result = Polynomial.Constant;
        foreach (LinearMonomial<V> m in Polynomial.Monomials) {
            if (m.Symbol is not IVariableItem variable) {
                return zeroIfNone ? Parent.Converter.Zero : (V?)null;
            }

            Token<V>? token = tokenTable.Find(variable);
            if (token?.Result is not { } value) {
                return zeroIfNone ? Parent.Converter.Zero : (V?)null;
            }

            result = result.Plus(m.Coefficient.Times(value));
        }
        return result;
    }

    /// <summary>使用解向量按索引求值 / Evaluate using the solution vector by index.</summary>
    /// <param name="results">求解器顺序的解向量 / Solution vector in solver order.</param>
    /// <param name="zeroIfNone">未知时返回整个目标的零值 / Return zero for the whole objective if unknown.</param>
    /// <returns>目标值或未知值 / Objective value, or null if unknown.</returns>
    public V? Evaluate(IReadOnlyList<V> results, bool zeroIfNone = false) => Evaluate(
        results: results,
        tokenTable: Parent.Tokens,
        zeroIfNone: zeroIfNone
    );

    /// <summary>使用指定符号表与解向量求值 / Evaluate using the specified token table and solution vector.</summary>
    /// <param name="results">求解器顺序的解向量 / Solution vector in solver order.</param>
    /// <param name="tokenTable">变量 token 表 / Variable token table.</param>
    /// <param name="zeroIfNone">未知时返回整个目标的零值 / Return zero for the whole objective if unknown.</param>
    /// <returns>目标值或未知值 / Objective value, or null if unknown.</returns>
    public V? Evaluate(IReadOnlyList<V> results, Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokenTable, bool zeroIfNone = false) {
        V result = Polynomial.Constant;
        foreach (LinearMonomial<V> m in Polynomial.Monomials) {
            if (m.Symbol is not IVariableItem variable) {
                return zeroIfNone ? Parent.Converter.Zero : (V?)null;
            }

            int? idx = tokenTable.IndexOf(variable);
            if (idx is null || idx.Value < 0 || idx.Value >= results.Count) {
                return zeroIfNone ? Parent.Converter.Zero : (V?)null;
            }

            result = result.Plus(m.Coefficient.Times(results[idx.Value]));
        }
        return result;
    }

    public void Flush(bool force = false) { }
}

// ===== AbstractMetaModel<V> =====

/// <summary>
/// 元模型抽象基类 / Abstract meta-model base class.
/// </summary>
public abstract class AbstractMetaModel<V> : IMetaModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private sealed record ConstraintGroupWriteState(
        IMetaConstraintGroup? CurrentGroup,
        int? CurrentLowerBound,
        KeyValuePair<IMetaConstraintGroup, Range>[] Ranges);

    private IMetaConstraintGroup? _currentConstraintGroup;
    private int? _currentConstraintGroupIndexLowerBound;
    private readonly Dictionary<IMetaConstraintGroup, Range> _constraintGroupIndexMap = new();
    private readonly object _lifecycleLock = new();
    private volatile bool _closed;
    private bool _writeTransactionActive;

    public Category Category { get; }
    public MetaModelConfiguration Configuration { get; }
    public IFlt64ValueConverter<V> Converter { get; }
    public ModelElementIdentityRegistry? IdentityRegistry { get; }
    public abstract string Name { get; }
    public abstract IReadOnlyList<MathConstraint> Constraints { get; }
    public abstract ObjectCategory ObjectCategory { get; }
    public abstract IReadOnlyList<SubObject<V>> SubObjects { get; }
    public Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> Tokens { get; }
    public bool IsClosed => _closed;

    public IReadOnlyDictionary<IIntermediateSymbol, IReadOnlySet<IIntermediateSymbol>> SymbolDependencies =>
        new Dictionary<IIntermediateSymbol, IReadOnlySet<IIntermediateSymbol>>();

    protected AbstractMetaModel(
        Category category,
        MetaModelConfiguration configuration,
        IFlt64ValueConverter<V> converter,
        ModelElementIdentityRegistry? identityRegistry = null) {
        Category = category;
        Configuration = configuration;
        Converter = converter;
        IdentityRegistry = identityRegistry;
        Tokens = CreateTokenTable(category, configuration);
    }

    private Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> CreateTokenTable(Category category, MetaModelConfiguration config) =>
        config.ManualTokenAddition
            ? new Fuookami.Ospf.Core.Token.ConcurrentManualTokenTable<V>(
                category: category,
                symbols: new List<IIntermediateSymbol>(),
                converter: Converter,
                checkTokenExists: config.CheckTokenExists
            )
            : new Fuookami.Ospf.Core.Token.ConcurrentAutoTokenTable<V>(
                category: category,
                symbols: new List<IIntermediateSymbol>(),
                converter: Converter,
                checkTokenExists: config.CheckTokenExists
            );

    /// <summary>检查模型生命周期是否允许修改 / Check whether the model lifecycle permits mutation.</summary>
    /// <returns>打开状态的成功结果或关闭错误 / Success when open, otherwise a closed error.</returns>
    protected Try EnsureOpen() =>
        _closed
            ? Results.Failed<Success>(
                new Err<ErrorCode>(
                    ErrorCode.ApplicationStopped,
                    "模型已关闭 / Model is closed"))
            : Results.Ok(Results.SuccessInstance);

    /// <summary>开始元模型批量写入事务 / Begin a meta-model batch-write transaction.</summary>
    /// <returns>事务或错误 / Transaction or an error.</returns>
    public Result<IMetaModelWriteTransaction, ErrorCode, Error<ErrorCode>> BeginWrite() {
        lock (_lifecycleLock) {
            Try open = EnsureOpen();
            if (!open.IsOk) {
                return Propagate<IMetaModelWriteTransaction, Success>(open);
            }
            if (_writeTransactionActive) {
                return Results.Failed<IMetaModelWriteTransaction>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "元模型写入事务不可重入 / Meta-model write transactions are not reentrant"));
            }

            Result<ITokenRegistrationTransaction, ErrorCode, Error<ErrorCode>> started;
            try {
                started = Tokens.BeginTokenRegistration();
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
                return Results.Failed<IMetaModelWriteTransaction>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    $"开始元模型写入事务失败：{error.Message} / Cannot begin meta-model write transaction: {error.Message}"));
            }
            if (!started.IsOk) {
                return Propagate<IMetaModelWriteTransaction, ITokenRegistrationTransaction>(started);
            }
            if (started.Value is not { } tokens) {
                return Results.Failed<IMetaModelWriteTransaction>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "符号表写入事务为空 / Token-table write transaction is null"));
            }

            object objectiveState;
            object constraintState;
            object? identityState;
            try {
                objectiveState = CaptureWriteState();
                constraintState = CaptureConstraintState();
                identityState = IdentityRegistry?.CaptureState();
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
                Try failure = Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    $"捕获元模型写入状态失败：{error.Message} / Cannot capture meta-model write state: {error.Message}"));
                return Propagate<IMetaModelWriteTransaction, Success>(
                    Combine(failure, tokens.Rollback()));
            }

            _writeTransactionActive = true;
            return Results.Ok<IMetaModelWriteTransaction>(new WriteTransaction(
                owner: this,
                tokens: tokens,
                constraintState: constraintState,
                objectiveState: objectiveState,
                identityState: identityState));
        }
    }

    private protected abstract object CaptureConstraintState();

    private protected abstract Try RestoreConstraintState(object state);

    private protected abstract object CaptureWriteState();

    private protected abstract Try RestoreWriteState(object state);

    private protected object CaptureConstraintGroupState() => new ConstraintGroupWriteState(
        CurrentGroup: _currentConstraintGroup,
        CurrentLowerBound: _currentConstraintGroupIndexLowerBound,
        Ranges: _constraintGroupIndexMap.ToArray());

    private protected Try RestoreConstraintGroupState(object state) {
        if (state is not ConstraintGroupWriteState snapshot) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "约束组写入状态无效 / Invalid constraint-group write state"));
        }
        _currentConstraintGroup = snapshot.CurrentGroup;
        _currentConstraintGroupIndexLowerBound = snapshot.CurrentLowerBound;
        _constraintGroupIndexMap.Clear();
        foreach (KeyValuePair<IMetaConstraintGroup, Range> pair in snapshot.Ranges) {
            _constraintGroupIndexMap.Add(pair.Key, pair.Value);
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private sealed class WriteTransaction : IMetaModelWriteTransaction {
        private readonly AbstractMetaModel<V> _owner;
        private readonly ITokenRegistrationTransaction _tokens;
        private readonly object _constraintState;
        private readonly object _objectiveState;
        private readonly object? _identityState;
        private bool _finished;

        internal WriteTransaction(
            AbstractMetaModel<V> owner,
            ITokenRegistrationTransaction tokens,
            object constraintState,
            object objectiveState,
            object? identityState) {
            _owner = owner;
            _tokens = tokens;
            _constraintState = constraintState;
            _objectiveState = objectiveState;
            _identityState = identityState;
        }

        public Try Commit() {
            lock (_owner._lifecycleLock) {
                if (_finished) {
                    return Invalid();
                }
                Try result = _owner.EnsureOpen();
                if (result.IsOk) {
                    result = _tokens.Commit();
                }
                if (result.IsOk) {
                    Finish();
                    return Results.Ok(Results.SuccessInstance);
                }

                Try rollback = RestoreState();
                Finish();
                return rollback.IsOk ? result : Combine(result, rollback);
            }
        }

        public Try Rollback() {
            lock (_owner._lifecycleLock) {
                if (_finished) {
                    return Invalid();
                }
                Try rollback = RestoreState();
                Finish();
                return rollback;
            }
        }

        private Try RestoreState() {
            var errors = new List<Error<ErrorCode>>();
            Collect(errors, _owner.RestoreConstraintState(_constraintState));
            Collect(errors, _owner.RestoreWriteState(_objectiveState));
            if (_identityState is not null && _owner.IdentityRegistry is { } registry) {
                Collect(errors, registry.RestoreState(_identityState));
            }
            Collect(errors, _tokens.Rollback());
            return errors.Count switch {
                0 => Results.Ok(Results.SuccessInstance),
                1 => Results.Failed<Success>(errors[0]),
                _ => new Fatal<Success, ErrorCode, Error<ErrorCode>>(errors),
            };
        }

        private void Finish() {
            _finished = true;
            _owner._writeTransactionActive = false;
        }

        private static Try Invalid() => Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            "元模型写入事务状态无效 / Invalid meta-model write-transaction state"));
    }

    private static void Collect(List<Error<ErrorCode>> errors, Try result) {
        switch (result) {
            case Failed<Success, ErrorCode, Error<ErrorCode>> failed:
                errors.Add(failed.Error);
                break;
            case Fatal<Success, ErrorCode, Error<ErrorCode>> fatal:
                errors.AddRange(fatal.Errors);
                break;
        }
    }

    private static Try Combine(Try first, Try second) {
        var errors = new List<Error<ErrorCode>>();
        Collect(errors, first);
        Collect(errors, second);
        return errors.Count switch {
            0 => Results.Ok(Results.SuccessInstance),
            1 => Results.Failed<Success>(errors[0]),
            _ => new Fatal<Success, ErrorCode, Error<ErrorCode>>(errors),
        };
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) => result switch {
        Failed<S, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
        Fatal<S, ErrorCode, Error<ErrorCode>> fatal => new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
        _ => Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            "元模型写入事务结果无效 / Invalid meta-model write-transaction result")),
    };

    /// <summary>解除已移除约束的稳定身份，使同一 ID 可在后续重新使用 / Release the stable identity of a removed constraint so the ID can be reused.</summary>
    /// <param name="constraint">已从约束列表移除的对象 / Object already removed from the constraint list.</param>
    private protected void ReleaseConstraintIdentity(MathConstraint constraint) {
        if (IdentityRegistry is { } registry && registry.Identity(constraint) is not null) {
            _ = registry.Release(constraint);
        }
    }

    internal Try CommitTokenPreparation(Func<Try> commit) {
        lock (_lifecycleLock) {
            Try open = EnsureOpen();
            return open.IsOk ? commit() : open;
        }
    }

    public Try Add(IVariableItem item) {
        Try open = EnsureOpen();
        return open is Ok<Success, ErrorCode, Error<ErrorCode>> ? Tokens.Add(item) : open;
    }

    public Try Add(IEnumerable<IVariableItem> items) {
        Try open = EnsureOpen();
        return open is Ok<Success, ErrorCode, Error<ErrorCode>> ? Tokens.Add(items) : open;
    }

    public void Remove(IVariableItem item) {
        if (!_closed) {
            Tokens.Remove(item);
        }
    }

    public Try Add(IIntermediateSymbol symbol) {
        Try open = EnsureOpen();
        if (open is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return open;
        }

        return TokenRegistrationTransactionSupport.Run(Tokens, () => AddSymbol(symbol));
    }

    public Try Add(IEnumerable<IIntermediateSymbol> symbols) {
        Try open = EnsureOpen();
        if (open is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return open;
        }
        return TokenRegistrationTransactionSupport.Run(Tokens, () => {
            foreach (IIntermediateSymbol symbol in symbols) {
                Try result = AddSymbol(symbol);
                if (!result.IsOk) {
                    return result;
                }
            }
            return Results.OkInstance;
        });
    }

    private Try AddSymbol(IIntermediateSymbol symbol) {
        Try open = EnsureOpen();
        if (!open.IsOk) {
            return open;
        }
        Try added = TokenRegistrationTransactionSupport.AddSymbol(Tokens, symbol);
        if (!added.IsOk) {
            return added;
        }
        return symbol is IIntermediateSymbol<V> typed ? typed.RegisterAuxiliaryTokens(Tokens) : added;
    }

    public void Remove(IIntermediateSymbol symbol) {
        if (!_closed) {
            Tokens.Remove(symbol);
        }
    }

    public void RegisterConstraintGroup(IMetaConstraintGroup group) {
        if (_closed) {
            return;
        }
        if (Equals(_currentConstraintGroup, group)) {
            return;
        }
        FinalizeCurrentConstraintGroup();
        if (_constraintGroupIndexMap.TryGetValue(group, out Range existing)) {
            // 非连续复用与受检路径一致地拒绝；void 入口不能覆盖既有范围。
            // Non-contiguous reuse is rejected like the checked path; the void entry must not overwrite the existing range.
            if (existing.End.Value != Constraints.Count) {
                return;
            }
            _constraintGroupIndexMap.Remove(group);
            _currentConstraintGroup = group;
            _currentConstraintGroupIndexLowerBound = existing.Start.Value;
            return;
        }
        _currentConstraintGroup = group;
        _currentConstraintGroupIndexLowerBound = Constraints.Count;
    }

    /// <summary>切换到目标约束组并拒绝非连续复用 / Switch to a constraint group and reject non-contiguous reuse.</summary>
    private protected Try PrepareConstraintGroup(IMetaConstraintGroup? group) {
        if (Equals(_currentConstraintGroup, group)) {
            return Results.Ok(Results.SuccessInstance);
        }
        FinalizeCurrentConstraintGroup();
        if (group is null) {
            return Results.Ok(Results.SuccessInstance);
        }
        if (_constraintGroupIndexMap.TryGetValue(group, out Range existing)) {
            if (existing.End.Value != Constraints.Count) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"约束组只能连续追加：{group.Name} / Constraint groups must be extended contiguously: {group.Name}"));
            }
            _constraintGroupIndexMap.Remove(group);
            _currentConstraintGroup = group;
            _currentConstraintGroupIndexLowerBound = existing.Start.Value;
            return Results.Ok(Results.SuccessInstance);
        }
        _currentConstraintGroup = group;
        _currentConstraintGroupIndexLowerBound = Constraints.Count;
        return Results.Ok(Results.SuccessInstance);
    }

    private void FinalizeCurrentConstraintGroup() {
        if (_currentConstraintGroup is not null) {
            _constraintGroupIndexMap[_currentConstraintGroup] =
                _currentConstraintGroupIndexLowerBound!.Value..Constraints.Count;
        }
        _currentConstraintGroup = null;
        _currentConstraintGroupIndexLowerBound = null;
    }

    public Range? IndicesOfConstraintGroup(IMetaConstraintGroup group) {
        FinalizeCurrentConstraintGroup();
        return _constraintGroupIndexMap.TryGetValue(group, out Range range) ? range : null;
    }

    public IReadOnlyList<MathConstraint> ConstraintsOfGroup(IMetaConstraintGroup group) {
        Range? indices = IndicesOfConstraintGroup(group);
        if (indices is null) {
            return Array.Empty<MathConstraint>();
        }

        (int offset, int length) = indices.Value.GetOffsetAndLength(Constraints.Count);
        var result = new List<MathConstraint>();
        for (int i = offset; i < offset + length; i++) {
            result.Add(Constraints[i]);
        }

        return result;
    }

    /// <summary>
    /// 回滚约束组索引到指定约束数量 / Roll constraint-group indexes back to the specified constraint count.
    /// </summary>
    /// <param name="size">回滚后的约束数量 / Constraint count after rollback.</param>
    protected void RollbackConstraintGroupStateTo(int size) {
        foreach (IMetaConstraintGroup group in _constraintGroupIndexMap.Keys.ToList()) {
            Range range = _constraintGroupIndexMap[group];
            int start = range.Start.Value;
            int end = range.End.Value;
            if (start >= size) {
                _constraintGroupIndexMap.Remove(group);
            }
            else if (end > size) {
                _constraintGroupIndexMap[group] = start..size;
            }
        }
        if (_currentConstraintGroupIndexLowerBound is { } lowerBound && lowerBound > size) {
            _currentConstraintGroupIndexLowerBound = size;
        }
    }

    /// <summary>
    /// 在移除指定位置约束后调整约束组索引 / Adjust constraint-group indexes after removing a constraint.
    /// </summary>
    /// <param name="index">被移除约束的位置 / Removed constraint index.</param>
    protected void RemoveConstraintGroupIndexAt(int index) {
        foreach (IMetaConstraintGroup group in _constraintGroupIndexMap.Keys.ToList()) {
            Range range = _constraintGroupIndexMap[group];
            int start = range.Start.Value;
            int end = range.End.Value;
            if (index < start) {
                _constraintGroupIndexMap[group] = (start - 1)..(end - 1);
            }
            else if (index < end) {
                if (start == end - 1) {
                    _constraintGroupIndexMap.Remove(group);
                }
                else {
                    _constraintGroupIndexMap[group] = start..(end - 1);
                }
            }
        }
        if (_currentConstraintGroupIndexLowerBound is { } lowerBound && lowerBound > index) {
            _currentConstraintGroupIndexLowerBound = lowerBound - 1;
        }
    }

    public void SetSolution(IReadOnlyList<V> solution) {
        if (!_closed) {
            Tokens.SetSolution(solution);
        }
    }

    public void SetSolution(IReadOnlyDictionary<IVariableItem, V> solution) {
        if (!_closed) {
            Tokens.SetSolution(solution);
        }
    }

    public void ClearSolution() {
        if (!_closed) {
            Tokens.TokenList.ClearSolution();
        }
    }

    public virtual void Flush(bool force = false) {
        if (_closed) {
            return;
        }
        if (force) {
            Tokens.TokenList.ClearSolution();
        }

        Tokens.Flush();
    }

    public virtual Task<Try> ExportAsync() =>
        MetaModelOpmExporter.ExportAsync(
            model: this,
            path: $"{Name}.opm",
            unfold: MathUInt64.Zero);
    public virtual Task<Try> ExportAsync(string name) =>
        MetaModelOpmExporter.ExportAsync(
            model: this,
            path: name,
            unfold: MathUInt64.Zero);
    public virtual Task<Try> ExportAsync(string path, bool unfold) =>
        MetaModelOpmExporter.ExportAsync(
            model: this,
            path: path,
            unfold: unfold ? MathUInt64.One : MathUInt64.Zero);
    public virtual Task<Try> ExportAsync(string path, MathUInt64 unfold) =>
        MetaModelOpmExporter.ExportAsync(
            model: this,
            path: path,
            unfold: unfold);

    /// <summary>关闭模型并释放可变 token 表 / Close the model and release the mutable token table.</summary>
    public virtual void Close() {
        lock (_lifecycleLock) {
            if (_closed) {
                return;
            }
            _closed = true;
            Tokens.Dispose();
        }
    }

    public virtual void Dispose() => Close();
}

// ===== LinearMetaModel<V> =====

/// <summary>
/// 线性元模型实现 / Linear meta-model implementation.
/// </summary>
public sealed class LinearMetaModel<V> : AbstractMetaModel<V>, IAbstractLinearMetaModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private sealed record LinearConstraintWriteState(
        LinearInequalityConstraint<V>[] Constraints,
        object GroupState);

    private sealed record LinearWriteState(
        MetaSubObject<V>[] Objectives,
        LinearModelState<V>? State,
        object? StateSnapshot);

    private readonly List<LinearInequalityConstraint<V>> _relationConstraints = new();
    private readonly List<MetaSubObject<V>> _subObjects = new();

    public override string Name { get; }
    public override IReadOnlyList<MathConstraint> Constraints => _relationConstraints;
    public override ObjectCategory ObjectCategory { get; }
    public override IReadOnlyList<SubObject<V>> SubObjects => Array.Empty<SubObject<V>>();

    public IReadOnlyList<LinearInequalityConstraint<V>> RelationConstraints => _relationConstraints;
    /// <summary>按注册顺序列出普通及扁平目标 / Polynomial and flattened objectives in registration order.</summary>
    public IReadOnlyList<MetaSubObject<V>> MetaSubObjects => _subObjects;

    /// <summary>
    /// **目标快照：每个子项都展平到变量级** / The objective snapshot: every sub-object flattened to variables.
    /// </summary>
    /// <returns>展平后的子项；无法展平时为 null / The flattened sub-objects, or null when one cannot be flattened.</returns>
    /// <remarks>
    /// 对齐基线的 `MetaModel.objectiveSnapshot`。**这一步不是可选的**：`MetaSubObjects` 的单项式里可以有**表达式符号**
    /// （`LinearExpressionSymbol` 等 `ILinearIntermediateSymbol`），它们不是变量、也不在解表里。
    /// 直接用 `MetaSubObjects` 求值，只要某个域的目标引用表达式符号，就会得到"目标引用缺少变量值"——
    /// **而解表其实是全的**，那句措辞会把人送去查错的地方。
    ///
    /// 复现：分区列生成走真 SCIP 后端跑到收尾复核，就停在这一步。
    ///
    /// Mirrors the baseline's `MetaModel.objectiveSnapshot`. **This step is not optional**: a monomial inside `MetaSubObjects` may carry an **expression symbol**
    /// (a `LinearExpressionSymbol` or another `ILinearIntermediateSymbol`), which is not a variable and is not in the solution table. Evaluating `MetaSubObjects`
    /// directly then reports "missing objective variable" for any domain objective that references one — **while the solution table is in fact complete**, so the
    /// wording sends you to the wrong place. Reproduced from into : partition column generation on the real SCIP backend reached the final check and stopped here.
    /// </remarks>
    public IReadOnlyList<MetaSubObject<V>>? ObjectiveSnapshot() {
        var snapshot = new List<MetaSubObject<V>>(_subObjects.Count);
        foreach (MetaSubObject<V> part in _subObjects) {
            var data = new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<V>(part.Polynomial.Monomials, part.Polynomial.Constant);
            Result<Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened =
                data.ExpandIntermediateSymbols(Tokens);
            if (flattened is not Ok<Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattenedOk) {
                return null;
            }
            snapshot.Add(new MetaSubObject<V>(
                parent: part.Parent,
                category: part.Category,
                name: part.Name,
                displayName: part.DisplayName,
                polynomial: new LinearPolynomial<V>(flattenedOk.Value.Monomials, flattenedOk.Value.Constant)));
        }
        return snapshot;
    }

    public LinearMetaModel(
        string name = "",
        ObjectCategory objectCategory = ObjectCategory.Minimum,
        MetaModelConfiguration? configuration = null,
        IFlt64ValueConverter<V>? converter = null,
        ModelElementIdentityRegistry? identityRegistry = null)
        : base(LinearCategory.Instance, configuration ?? new MetaModelConfiguration(),
               converter ?? CreateDefaultConverter(), identityRegistry) {
        Name = name;
        ObjectCategory = objectCategory;
    }

    /// <summary>模型状态、结构版本和稳定动态行入口 / Model state, structural revisions, and stable dynamic rows.</summary>
    public LinearModelState<V> State => _state ??= new LinearModelState<V>(this);
    private LinearModelState<V>? _state;

    internal Try ValidateDynamicRows(IAbstractTokenTable<V> tokens) {
        if (_state is not null) {
            foreach (LinearInequalityConstraint<V> constraint in _relationConstraints) {
                Try result = _state.ValidateDynamicRow(constraint, tokens);
                if (!result.IsOk) {
                    return result;
                }
            }
        }
        return Results.OkInstance;
    }

    public Try AddConstraint(
        IVariableItem constraint,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        bool? withRangeSet = false) {
        var lhs = new LinearPolynomial<V>(
            new List<LinearMonomial<V>> { new(Converter.One, constraint) }, Converter.Zero);
        var rhs = new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), Converter.One);
        var relation = new LinearInequality<V>(lhs, rhs, Comparison.EQ);
        return AddConstraint(relation, group, lazy, name, displayName, args, null, withRangeSet);
    }

    public Try AddConstraint(
        LinearPolynomial<V> constraint,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        bool? withRangeSet = false) {
        var onePoly = new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), Converter.One);
        return AddConstraint(new LinearInequality<V>(constraint, onePoly, Comparison.EQ),
            group, lazy, name, displayName, args, null, withRangeSet);
    }

    public Try AddConstraint(
        IIntermediateSymbol constraint,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        bool? withRangeSet = false) {
        // Delegate to variable-item form if it's a variable
        if (constraint is IVariableItem vi) {
            return AddConstraint(vi, group, lazy, name, displayName, args, withRangeSet);
        }
        // Otherwise, it's an intermediate symbol - add as symbol and create constraint
        Try result = Add(constraint);
        if (result is Failed<Success, ErrorCode, Error<ErrorCode>> f) {
            return f;
        }

        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddConstraint(
        LinearInequality<V> relation,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null,
        int? priority = null,
        bool? withRangeSet = false) {
        Try open = EnsureOpen();
        if (open is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return open;
        }
        Try prepared = PrepareConstraintGroup(group);
        if (!prepared.IsOk) {
            return prepared;
        }
        _relationConstraints.Add(new LinearInequalityConstraint<V>(
            relation, group, lazy, args, priority ?? 0, name, displayName));
        return Results.Ok(Results.SuccessInstance);
    }

    internal void RollbackConstraint(MathConstraint constraint) {
        if (_relationConstraints.Count > 0 &&
            ReferenceEquals(_relationConstraints[^1], constraint)) {
            _ = RollbackConstraintsTo(_relationConstraints.Count - 1);
        }
    }

    /// <summary>
    /// 移除指定线性约束对象 / Remove the specified linear constraint object.
    /// </summary>
    /// <param name="constraint">待移除约束 / Constraint to remove.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try RemoveConstraint(MathConstraint constraint) {
        Try open = EnsureOpen();
        if (!open.IsOk) {
            return open;
        }
        if (constraint is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "待移除约束为空 / Constraint to remove is null"));
        }
        int index = _relationConstraints.FindIndex(item => ReferenceEquals(item, constraint));
        if (index < 0) {
            return Results.Ok(Results.SuccessInstance);
        }
        _relationConstraints.RemoveAt(index);
        ReleaseConstraintIdentity(constraint);
        RemoveConstraintGroupIndexAt(index);
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 将线性约束回滚到指定数量 / Roll linear constraints back to the specified count.
    /// </summary>
    /// <param name="size">回滚后的约束数量 / Constraint count after rollback.</param>
    /// <returns>回滚结果 / Rollback result.</returns>
    public Try RollbackConstraintsTo(int size) {
        if (size < 0 || size > _relationConstraints.Count) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"约束回滚位置无效：{size} / Invalid constraint rollback position: {size}"));
        }
        for (int index = size; index < _relationConstraints.Count; ++index) {
            ReleaseConstraintIdentity(_relationConstraints[index]);
        }
        _relationConstraints.RemoveRange(size, _relationConstraints.Count - size);
        RollbackConstraintGroupStateTo(size);
        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddObject(
        ObjectCategory category,
        SymbolFlatten.LinearFlattenData<V> flattenData,
        string name,
        string? displayName) {
        Try open = EnsureOpen();
        if (open is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return open;
        }
        _subObjects.Add(new MetaSubObject<V>(
            parent: this,
            category: category,
            name: name,
            displayName: displayName,
            polynomial: new LinearPolynomial<V>(flattenData.Monomials, flattenData.Constant)
        ));
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 使用 LinearPolynomial 添加目标 / Add objective using LinearPolynomial
    /// </summary>
    public Try AddObject(
        ObjectCategory category,
        LinearPolynomial<V> polynomial,
        string name,
        string? displayName) {
        Try open = EnsureOpen();
        if (open is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return open;
        }
        _subObjects.Add(new MetaSubObject<V>(this, category, name, displayName, polynomial));
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 从所有目标子项中移除指定变量项 / Remove terms for the specified variables from every objective sub-object.
    /// </summary>
    /// <param name="variables">待移除的变量 / Variables whose terms should be removed.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try RemoveObjectiveTerms(IReadOnlySet<IVariableItem> variables) {
        Try open = EnsureOpen();
        if (!open.IsOk) {
            return open;
        }
        if (variables is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "目标项移除变量集合为空 / Objective-term removal variable set is null"));
        }
        if (variables.Count == 0) {
            return Results.Ok(Results.SuccessInstance);
        }

        for (int index = 0; index < _subObjects.Count; ++index) {
            MetaSubObject<V> source = _subObjects[index];
            LinearMonomial<V>[] retained = source.Polynomial.Monomials
                .Where(term =>
                    term.Symbol is not IVariableItem variable ||
                    !variables.Contains(variable))
                .ToArray();
            if (retained.Length != source.Polynomial.Monomials.Count) {
                _subObjects[index] = new MetaSubObject<V>(
                    parent: this,
                    category: source.Category,
                    name: source.Name,
                    displayName: source.DisplayName,
                    polynomial: new LinearPolynomial<V>(
                        Monomials: retained,
                        Constant: source.Polynomial.Constant));
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private protected override object CaptureWriteState() => new LinearWriteState(
        Objectives: _subObjects.ToArray(),
        State: _state,
        StateSnapshot: _state?.CaptureWriteState());

    private protected override object CaptureConstraintState() => new LinearConstraintWriteState(
        Constraints: _relationConstraints.ToArray(),
        GroupState: CaptureConstraintGroupState());

    private protected override Try RestoreConstraintState(object state) {
        if (state is not LinearConstraintWriteState snapshot) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "线性约束写入状态无效 / Invalid linear constraint write state"));
        }
        _relationConstraints.Clear();
        _relationConstraints.AddRange(snapshot.Constraints);
        return RestoreConstraintGroupState(snapshot.GroupState);
    }

    private protected override Try RestoreWriteState(object state) {
        if (state is not LinearWriteState snapshot) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "线性元模型写入状态无效 / Invalid linear meta-model write state"));
        }
        _subObjects.Clear();
        _subObjects.AddRange(snapshot.Objectives);
        _state = snapshot.State;
        return snapshot.State is null || snapshot.StateSnapshot is null
            ? Results.Ok(Results.SuccessInstance)
            : snapshot.State.RestoreWriteState(snapshot.StateSnapshot);
    }

    public Try Partition(
        IEnumerable<IVariableItem> variables,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null) {
        var polynomial = new LinearPolynomial<V>(
            variables.Select(v => new LinearMonomial<V>(Converter.One, v)).ToList(),
            Converter.Zero);
        return Partition(polynomial, group, lazy, name, displayName, args);
    }

    public Try Partition(
        LinearPolynomial<V> polynomial,
        IMetaConstraintGroup? group,
        bool lazy = false,
        string? name = null,
        string? displayName = null,
        object? args = null) {
        var onePoly = new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), Converter.One);
        return AddConstraint(new LinearInequality<V>(polynomial, onePoly, Comparison.EQ),
            group, lazy, name, displayName, args);
    }

    private static IFlt64ValueConverter<V> CreateDefaultConverter() {
        // When V = Flt64, return identity converter
        if (typeof(V) == typeof(Flt64)) {
            return (IFlt64ValueConverter<V>)(object)new IdentityFlt64Converter();
        }

        throw new InvalidOperationException($"No default converter for type {typeof(V).Name}");
    }
}

// ===== QuadraticMetaModel<V> =====

/// <summary>
/// 二次元模型实现 / Quadratic meta-model implementation.
/// </summary>
public sealed class QuadraticMetaModel<V> : AbstractMetaModel<V>, IAbstractLinearMetaModel<V>, IAbstractQuadraticMetaModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private sealed record QuadraticConstraintWriteState(
        QuadraticInequalityConstraint<V>[] Constraints,
        object GroupState);

    private sealed record QuadraticWriteState(
        MetaSubObject<V>[] Objectives,
        QuadraticFlattenSubObject<V>[] FlattenObjectives);

    private readonly List<QuadraticInequalityConstraint<V>> _relationConstraints = new();
    private readonly List<MetaSubObject<V>> _subObjects = new();
    private readonly List<QuadraticFlattenSubObject<V>> _flattenSubObjects = new();

    public override string Name { get; }
    public override IReadOnlyList<MathConstraint> Constraints => _relationConstraints;
    public override ObjectCategory ObjectCategory { get; }
    public override IReadOnlyList<SubObject<V>> SubObjects => Array.Empty<SubObject<V>>();

    public IReadOnlyList<QuadraticInequalityConstraint<V>> RelationConstraints => _relationConstraints;
    public IReadOnlyList<MetaSubObject<V>> MetaSubObjects => _subObjects;
    /// <summary>按注册顺序列出二次展平目标 / Flattened quadratic objectives in registration order.</summary>
    public IReadOnlyList<QuadraticFlattenSubObject<V>> FlattenSubObjects => _flattenSubObjects.AsReadOnly();

    public QuadraticMetaModel(
        string name = "",
        ObjectCategory objectCategory = ObjectCategory.Minimum,
        MetaModelConfiguration? configuration = null,
        IFlt64ValueConverter<V>? converter = null,
        ModelElementIdentityRegistry? identityRegistry = null)
        : base(QuadraticCategory.Instance, configuration ?? new MetaModelConfiguration(),
               converter ?? CreateDefaultConverter(), identityRegistry) {
        Name = name;
        ObjectCategory = objectCategory;
    }

    // === IAbstractLinearMetaModel<V> ===

    public Try AddConstraint(
        IVariableItem constraint,
        IMetaConstraintGroup? group,
        bool lazy = false, string? name = null, string? displayName = null,
        object? args = null, bool? withRangeSet = false) {
        var lhs = new LinearPolynomial<V>(
            new List<LinearMonomial<V>> { new(Converter.One, constraint) }, Converter.Zero);
        var rhs = new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), Converter.One);
        var relation = new LinearInequality<V>(lhs, rhs, Comparison.EQ);
        return AddConstraint(relation, group, lazy, name, displayName, args);
    }

    public Try AddConstraint(
        LinearPolynomial<V> constraint,
        IMetaConstraintGroup? group,
        bool lazy = false, string? name = null, string? displayName = null,
        object? args = null, bool? withRangeSet = false) {
        var onePoly = new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), Converter.One);
        return AddConstraint(new LinearInequality<V>(constraint, onePoly, Comparison.EQ),
            group, lazy, name, displayName, args);
    }

    public Try AddConstraint(
        IIntermediateSymbol constraint,
        IMetaConstraintGroup? group,
        bool lazy = false, string? name = null, string? displayName = null,
        object? args = null, bool? withRangeSet = false) {
        if (constraint is IVariableItem vi) {
            return AddConstraint(vi, group, lazy, name, displayName, args, withRangeSet);
        }

        Try result = Add(constraint);
        if (result is Failed<Success, ErrorCode, Error<ErrorCode>> f) {
            return f;
        }

        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>LinearInequality overload (promotes to quadratic)</summary>
    public Try AddConstraint(
        LinearInequality<V> relation,
        IMetaConstraintGroup? group,
        bool lazy = false, string? name = null, string? displayName = null,
        object? args = null, int? priority = null, bool? withRangeSet = false) {
        var qLhs = new QuadraticPolynomial<V>(
            relation.Lhs.Monomials.Select(m => new QuadraticMonomial<V>(m.Coefficient, m.Symbol, null)).ToList(),
            relation.Lhs.Constant);
        var qRhs = new QuadraticPolynomial<V>(
            relation.Rhs.Monomials.Select(m => new QuadraticMonomial<V>(m.Coefficient, m.Symbol, null)).ToList(),
            relation.Rhs.Constant);
        var qRelation = new QuadraticInequalityOf<V>(
            Lhs: qLhs,
            Rhs: qRhs,
            Comparison: relation.Comparison,
            Name: relation.Name,
            DisplayName: relation.DisplayName
        );
        return AddConstraint(qRelation, group, lazy, name, displayName, args, priority, withRangeSet);
    }

    public Try AddObject(ObjectCategory category, SymbolFlatten.LinearFlattenData<V> flattenData, string name, string? displayName) {
        Try open = EnsureOpen();
        if (open is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return open;
        }
        // Convert to quadratic
        var qFlattenData = new SymbolFlatten.QuadraticFlattenData<V>(
            flattenData.Monomials.Select(m => new QuadraticMonomial<V>(m.Coefficient, m.Symbol, null)).ToList(),
            flattenData.Constant);
        _flattenSubObjects.Add(new QuadraticFlattenSubObject<V>(
            category: category,
            name: name,
            flattenData: qFlattenData,
            displayName: displayName
        ));
        return Results.Ok(Results.SuccessInstance);
    }

    public Try Partition(
        IEnumerable<IVariableItem> variables, IMetaConstraintGroup? group,
        bool lazy = false, string? name = null, string? displayName = null, object? args = null) {
        var polynomial = new LinearPolynomial<V>(
            variables.Select(v => new LinearMonomial<V>(Converter.One, v)).ToList(), Converter.Zero);
        return Partition(polynomial, group, lazy, name, displayName, args);
    }

    public Try Partition(
        LinearPolynomial<V> polynomial, IMetaConstraintGroup? group,
        bool lazy = false, string? name = null, string? displayName = null, object? args = null) {
        var qPoly = new QuadraticPolynomial<V>(
            polynomial.Monomials.Select(m => new QuadraticMonomial<V>(m.Coefficient, m.Symbol, null)).ToList(),
            polynomial.Constant);
        return Partition(qPoly, group, lazy, name, displayName, args);
    }

    // === IAbstractQuadraticMetaModel<V> ===

    public Try AddConstraint(
        QuadraticPolynomial<V> constraint, IMetaConstraintGroup? group,
        bool lazy = false, string? name = null, string? displayName = null,
        object? args = null, bool? withRangeSet = null) {
        var onePoly = new QuadraticPolynomial<V>(Array.Empty<QuadraticMonomial<V>>(), Converter.One);
        return AddConstraint(new QuadraticInequalityOf<V>(constraint, onePoly, Comparison.EQ),
            group, lazy, name, displayName, args);
    }

    Try IAbstractQuadraticMetaModel<V>.AddConstraint(
        IIntermediateSymbol constraint, IMetaConstraintGroup? group,
        bool lazy, string? name, string? displayName, object? args, bool? withRangeSet) {
        if (constraint is IVariableItem vi) {
            return AddConstraint(vi, group, lazy, name, displayName, args, withRangeSet);
        }

        Try result = Add(constraint);
        if (result is Failed<Success, ErrorCode, Error<ErrorCode>> f) {
            return f;
        }

        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddConstraint(
        QuadraticInequalityOf<V> relation, IMetaConstraintGroup? group,
        bool lazy = false, string? name = null, string? displayName = null,
        object? args = null, int? priority = null, bool? withRangeSet = null) {
        Try open = EnsureOpen();
        if (open is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return open;
        }
        Try prepared = PrepareConstraintGroup(group);
        if (!prepared.IsOk) {
            return prepared;
        }
        _relationConstraints.Add(new QuadraticInequalityConstraint<V>(
            relation, group, lazy, args, priority ?? 0, name, displayName));
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 将二次约束回滚到指定数量 / Roll quadratic constraints back to the specified count.
    /// </summary>
    /// <param name="size">回滚后的约束数量 / Constraint count after rollback.</param>
    /// <returns>回滚结果 / Rollback result.</returns>
    public Try RollbackConstraintsTo(int size) {
        if (size < 0 || size > _relationConstraints.Count) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"约束回滚位置无效：{size} / Invalid constraint rollback position: {size}"));
        }
        for (int index = size; index < _relationConstraints.Count; ++index) {
            ReleaseConstraintIdentity(_relationConstraints[index]);
        }
        _relationConstraints.RemoveRange(size, _relationConstraints.Count - size);
        RollbackConstraintGroupStateTo(size);
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 移除指定二次约束对象 / Remove the specified quadratic constraint object.
    /// </summary>
    /// <param name="constraint">待移除约束 / Constraint to remove.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try RemoveConstraint(MathConstraint constraint) {
        Try open = EnsureOpen();
        if (!open.IsOk) {
            return open;
        }
        if (constraint is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "待移除约束为空 / Constraint to remove is null"));
        }
        int index = _relationConstraints.FindIndex(item => ReferenceEquals(item, constraint));
        if (index < 0) {
            return Results.Ok(Results.SuccessInstance);
        }
        _relationConstraints.RemoveAt(index);
        ReleaseConstraintIdentity(constraint);
        RemoveConstraintGroupIndexAt(index);
        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddObject(ObjectCategory category, SymbolFlatten.QuadraticFlattenData<V> flattenData, string name, string? displayName) {
        Try open = EnsureOpen();
        if (open is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return open;
        }
        _flattenSubObjects.Add(new QuadraticFlattenSubObject<V>(
            category: category,
            name: name,
            flattenData: flattenData,
            displayName: displayName
        ));
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 从所有目标子项中移除指定变量项 / Remove terms for the specified variables from every objective sub-object.
    /// </summary>
    /// <param name="variables">待移除的变量 / Variables whose terms should be removed.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try RemoveObjectiveTerms(IReadOnlySet<IVariableItem> variables) {
        Try open = EnsureOpen();
        if (!open.IsOk) {
            return open;
        }
        if (variables is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "目标项移除变量集合为空 / Objective-term removal variable set is null"));
        }
        if (variables.Count == 0) {
            return Results.Ok(Results.SuccessInstance);
        }

        bool IsRemoved(ISymbol symbol) =>
            symbol is IVariableItem variable && variables.Contains(variable);

        for (int index = 0; index < _subObjects.Count; ++index) {
            MetaSubObject<V> source = _subObjects[index];
            LinearMonomial<V>[] retained = source.Polynomial.Monomials
                .Where(term => !IsRemoved(term.Symbol))
                .ToArray();
            if (retained.Length != source.Polynomial.Monomials.Count) {
                _subObjects[index] = new MetaSubObject<V>(
                    parent: this,
                    category: source.Category,
                    name: source.Name,
                    displayName: source.DisplayName,
                    polynomial: new LinearPolynomial<V>(
                        Monomials: retained,
                        Constant: source.Polynomial.Constant));
            }
        }

        for (int index = 0; index < _flattenSubObjects.Count; ++index) {
            QuadraticFlattenSubObject<V> source = _flattenSubObjects[index];
            QuadraticMonomial<V>[] retained = source.FlattenData.Monomials
                .Where(term =>
                    !IsRemoved(term.Symbol1) &&
                    (term.Symbol2 is null || !IsRemoved(term.Symbol2)))
                .ToArray();
            if (retained.Length != source.FlattenData.Monomials.Count) {
                _flattenSubObjects[index] = new QuadraticFlattenSubObject<V>(
                    category: source.Category,
                    name: source.Name,
                    flattenData: new SymbolFlatten.QuadraticFlattenData<V>(
                        Monomials: retained,
                        Constant: source.FlattenData.Constant),
                    displayName: source.DisplayName);
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private protected override object CaptureWriteState() => new QuadraticWriteState(
        Objectives: _subObjects.ToArray(),
        FlattenObjectives: _flattenSubObjects.ToArray());

    private protected override object CaptureConstraintState() => new QuadraticConstraintWriteState(
        Constraints: _relationConstraints.ToArray(),
        GroupState: CaptureConstraintGroupState());

    private protected override Try RestoreConstraintState(object state) {
        if (state is not QuadraticConstraintWriteState snapshot) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "二次约束写入状态无效 / Invalid quadratic constraint write state"));
        }
        _relationConstraints.Clear();
        _relationConstraints.AddRange(snapshot.Constraints);
        return RestoreConstraintGroupState(snapshot.GroupState);
    }

    private protected override Try RestoreWriteState(object state) {
        if (state is not QuadraticWriteState snapshot) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "二次元模型写入状态无效 / Invalid quadratic meta-model write state"));
        }
        _subObjects.Clear();
        _subObjects.AddRange(snapshot.Objectives);
        _flattenSubObjects.Clear();
        _flattenSubObjects.AddRange(snapshot.FlattenObjectives);
        return Results.Ok(Results.SuccessInstance);
    }

    public Try Partition(
        QuadraticPolynomial<V> polynomial, IMetaConstraintGroup? group,
        bool lazy = false, string? name = null, string? displayName = null, object? args = null) {
        var onePoly = new QuadraticPolynomial<V>(Array.Empty<QuadraticMonomial<V>>(), Converter.One);
        return AddConstraint(new QuadraticInequalityOf<V>(polynomial, onePoly, Comparison.EQ),
            group, lazy, name, displayName, args);
    }

    private static IFlt64ValueConverter<V> CreateDefaultConverter() {
        if (typeof(V) == typeof(Flt64)) {
            return (IFlt64ValueConverter<V>)(object)new IdentityFlt64Converter();
        }

        throw new InvalidOperationException($"No default converter for type {typeof(V).Name}");
    }
}

// ===== Identity converter helper =====

internal sealed class IdentityFlt64Converter : IFlt64ValueConverter<Flt64> {
    public Flt64 Zero => Flt64.Zero;
    public Flt64 One => Flt64.One;
    public Flt64 IntoValue(Flt64 value) => value;
    public Flt64 FromValue(Flt64 value) => value;
}
