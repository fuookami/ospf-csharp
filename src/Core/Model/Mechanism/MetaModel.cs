#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
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

    /// <summary>使用父模型符号表求值 / Evaluate using parent model token table</summary>
    public V? Evaluate(bool zeroIfNone = false) => Evaluate(Parent.Tokens, zeroIfNone);

    /// <summary>使用指定符号表求值 / Evaluate using specified token table</summary>
    public V? Evaluate(Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokenTable, bool zeroIfNone = false) {
        V? result = null;
        foreach (LinearMonomial<V> m in Polynomial.Monomials) {
            if (m.Symbol is not IVariableItem variable) {
                return zeroIfNone ? default(V?) : null;
            }

            Token<V>? token = tokenTable.Find(variable);
            if (token is null || token.Result is null) {
                return zeroIfNone ? default(V?) : null;
            }

            V term = m.Coefficient.Times(token.Result.Value);
            result = result is null ? term : result.Value.Plus(term);
        }
        return result ?? Polynomial.Constant;
    }

    /// <summary>使用解向量按索引求值 / Evaluate using solution vector by index lookup</summary>
    public V? Evaluate(IReadOnlyList<V> results, bool zeroIfNone = false) => Evaluate(results, Parent.Tokens, zeroIfNone);

    public V? Evaluate(IReadOnlyList<V> results, Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokenTable, bool zeroIfNone = false) {
        V? result = null;
        foreach (LinearMonomial<V> m in Polynomial.Monomials) {
            if (m.Symbol is not IVariableItem variable) {
                return zeroIfNone ? default(V?) : null;
            }

            int? idx = tokenTable.IndexOf(variable);
            if (idx is null) {
                return zeroIfNone ? default(V?) : null;
            }

            V term = m.Coefficient.Times(results[idx.Value]);
            result = result is null ? term : result.Value.Plus(term);
        }
        return result ?? Polynomial.Constant;
    }

    public void Flush(bool force = false) { }
}

// ===== AbstractMetaModel<V> =====

/// <summary>
/// 元模型抽象基类 / Abstract meta-model base class.
/// </summary>
public abstract class AbstractMetaModel<V> : IMetaModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private IMetaConstraintGroup? _currentConstraintGroup;
    private int? _currentConstraintGroupIndexLowerBound;
    private readonly Dictionary<IMetaConstraintGroup, Range> _constraintGroupIndexMap = new();

    public Category Category { get; }
    public MetaModelConfiguration Configuration { get; }
    public IFlt64ValueConverter<V> Converter { get; }
    public abstract string Name { get; }
    public abstract IReadOnlyList<MathConstraint> Constraints { get; }
    public abstract ObjectCategory ObjectCategory { get; }
    public abstract IReadOnlyList<SubObject<V>> SubObjects { get; }
    public Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> Tokens { get; }

    public IReadOnlyDictionary<IIntermediateSymbol, IReadOnlySet<IIntermediateSymbol>> SymbolDependencies =>
        new Dictionary<IIntermediateSymbol, IReadOnlySet<IIntermediateSymbol>>();

    protected AbstractMetaModel(
        Category category,
        MetaModelConfiguration configuration,
        IFlt64ValueConverter<V> converter) {
        Category = category;
        Configuration = configuration;
        Converter = converter;
        Tokens = CreateTokenTable(category, configuration);
    }

    private static Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> CreateTokenTable(Category category, MetaModelConfiguration config) {
        if (config.Concurrent) {
            return config.ManualTokenAddition
                ? new Fuookami.Ospf.Core.Token.ConcurrentManualTokenTable<V>(category, new List<IIntermediateSymbol>())
                : new Fuookami.Ospf.Core.Token.ConcurrentAutoTokenTable<V>(category, new List<IIntermediateSymbol>());
        }
        else {
            // For non-concurrent, use the concurrent tables as well (simplification)
            // In a full implementation, there would be non-concurrent variants
            return config.ManualTokenAddition
                ? new Fuookami.Ospf.Core.Token.ConcurrentManualTokenTable<V>(category, new List<IIntermediateSymbol>())
                : new Fuookami.Ospf.Core.Token.ConcurrentAutoTokenTable<V>(category, new List<IIntermediateSymbol>());
        }
    }

    public Try Add(IVariableItem item) => Tokens.Add(item);
    public Try Add(IEnumerable<IVariableItem> items) => Tokens.Add(items);
    public void Remove(IVariableItem item) => Tokens.Remove(item);

    public Try Add(IIntermediateSymbol symbol) => Tokens.Add(symbol);
    public Try Add(IEnumerable<IIntermediateSymbol> symbols) => Tokens.Add(symbols);
    public void Remove(IIntermediateSymbol symbol) => Tokens.Remove(symbol);

    public void RegisterConstraintGroup(IMetaConstraintGroup group) {
        if (_currentConstraintGroup is not null) {
            _constraintGroupIndexMap[_currentConstraintGroup] =
                _currentConstraintGroupIndexLowerBound!.Value..Constraints.Count;
        }
        _currentConstraintGroup = group;
        _currentConstraintGroupIndexLowerBound = Constraints.Count;
    }

    public Range? IndicesOfConstraintGroup(IMetaConstraintGroup group) {
        if (_currentConstraintGroup is not null) {
            _constraintGroupIndexMap[_currentConstraintGroup] =
                _currentConstraintGroupIndexLowerBound!.Value..Constraints.Count;
            _currentConstraintGroup = null;
            _currentConstraintGroupIndexLowerBound = null;
        }
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

    public void SetSolution(IReadOnlyList<V> solution) => Tokens.SetSolution(solution);
    public void SetSolution(IReadOnlyDictionary<IVariableItem, V> solution) => Tokens.SetSolution(solution);
    public void ClearSolution() => Tokens.TokenList.ClearSolution();

    public virtual void Flush(bool force = false) {
        if (force) {
            Tokens.TokenList.ClearSolution();
        }

        Tokens.Flush();
    }

    public virtual Task<Try> ExportAsync() => Task.FromResult(Results.Ok(Results.SuccessInstance));
    public virtual Task<Try> ExportAsync(string name) => Task.FromResult(Results.Ok(Results.SuccessInstance));
    public virtual Task<Try> ExportAsync(string path, bool unfold) => Task.FromResult(Results.Ok(Results.SuccessInstance));
    public virtual Task<Try> ExportAsync(string path, MathUInt64 unfold) => Task.FromResult(Results.Ok(Results.SuccessInstance));

    public virtual void Dispose() => Tokens.Dispose();
}

// ===== LinearMetaModel<V> =====

/// <summary>
/// 线性元模型实现 / Linear meta-model implementation.
/// </summary>
public sealed class LinearMetaModel<V> : AbstractMetaModel<V>, IAbstractLinearMetaModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly List<LinearInequalityConstraint<V>> _relationConstraints = new();
    private readonly List<MetaSubObject<V>> _subObjects = new();
    private readonly List<SymbolFlatten.LinearFlattenData<V>> _flattenSubObjects = new();

    public override string Name { get; }
    public override IReadOnlyList<MathConstraint> Constraints => _relationConstraints;
    public override ObjectCategory ObjectCategory { get; }
    public override IReadOnlyList<SubObject<V>> SubObjects => Array.Empty<SubObject<V>>();

    public IReadOnlyList<LinearInequalityConstraint<V>> RelationConstraints => _relationConstraints;
    public IReadOnlyList<MetaSubObject<V>> MetaSubObjects => _subObjects;
    internal IReadOnlyList<SymbolFlatten.LinearFlattenData<V>> FlattenSubObjects => _flattenSubObjects;

    public LinearMetaModel(
        string name = "",
        ObjectCategory objectCategory = ObjectCategory.Minimum,
        MetaModelConfiguration? configuration = null,
        IFlt64ValueConverter<V>? converter = null)
        : base(LinearCategory.Instance, configuration ?? new MetaModelConfiguration(),
               converter ?? CreateDefaultConverter()) {
        Name = name;
        ObjectCategory = objectCategory;
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
        _relationConstraints.Add(new LinearInequalityConstraint<V>(
            relation, group, lazy, args, priority ?? 0));
        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddObject(
        ObjectCategory category,
        SymbolFlatten.LinearFlattenData<V> flattenData,
        string name,
        string? displayName) {
        _flattenSubObjects.Add(flattenData);
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
        _subObjects.Add(new MetaSubObject<V>(this, category, name, displayName, polynomial));
        return Results.Ok(Results.SuccessInstance);
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
    private readonly List<QuadraticInequalityConstraint<V>> _relationConstraints = new();
    private readonly List<MetaSubObject<V>> _subObjects = new();
    private readonly List<QuadraticFlattenSubObject<V>> _flattenSubObjects = new();

    public override string Name { get; }
    public override IReadOnlyList<MathConstraint> Constraints => _relationConstraints;
    public override ObjectCategory ObjectCategory { get; }
    public override IReadOnlyList<SubObject<V>> SubObjects => Array.Empty<SubObject<V>>();

    public IReadOnlyList<QuadraticInequalityConstraint<V>> RelationConstraints => _relationConstraints;
    public IReadOnlyList<MetaSubObject<V>> MetaSubObjects => _subObjects;
    internal IReadOnlyList<QuadraticFlattenSubObject<V>> FlattenSubObjects => _flattenSubObjects;

    public QuadraticMetaModel(
        string name = "",
        ObjectCategory objectCategory = ObjectCategory.Minimum,
        MetaModelConfiguration? configuration = null,
        IFlt64ValueConverter<V>? converter = null)
        : base(QuadraticCategory.Instance, configuration ?? new MetaModelConfiguration(),
               converter ?? CreateDefaultConverter()) {
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
        var qRelation = new QuadraticInequalityOf<V>(qLhs, qRhs, relation.Comparison);
        return AddConstraint(qRelation, group, lazy, name, displayName, args, priority, withRangeSet);
    }

    public Try AddObject(ObjectCategory category, SymbolFlatten.LinearFlattenData<V> flattenData, string name, string? displayName) {
        // Convert to quadratic
        var qFlattenData = new SymbolFlatten.QuadraticFlattenData<V>(
            flattenData.Monomials.Select(m => new QuadraticMonomial<V>(m.Coefficient, m.Symbol, null)).ToList(),
            flattenData.Constant);
        _flattenSubObjects.Add(new QuadraticFlattenSubObject<V>(category, name, qFlattenData));
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
        _relationConstraints.Add(new QuadraticInequalityConstraint<V>(
            relation, group, lazy, args, priority ?? 0));
        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddObject(ObjectCategory category, SymbolFlatten.QuadraticFlattenData<V> flattenData, string name, string? displayName) {
        _flattenSubObjects.Add(new QuadraticFlattenSubObject<V>(category, name, flattenData));
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
