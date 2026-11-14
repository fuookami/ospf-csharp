#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Token;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>子目标基类 / Sub-objective base.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public abstract record SubObject<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>目标类别 / Objective category.</summary>
    public ObjectCategory Category { get; init; }
    /// <summary>子目标名称 / Sub-objective name.</summary>
    public string Name { get; init; } = "";
    /// <summary>子目标显示名称 / Sub-objective display name.</summary>
    public string? DisplayName { get; init; }
    /// <summary>来源元模型子目标 / Originating meta-model sub-objective.</summary>
    public object? Origin { get; init; }
    /// <summary>解析后的单元格 / Resolved cells.</summary>
    public abstract IReadOnlyList<ICell<V>> Cells { get; }
    /// <summary>常量项 / Constant term.</summary>
    public abstract V Constant { get; }

    /// <summary>使用 token 结果求值 / Evaluate using token results.</summary>
    /// <returns>目标值；任一项未知时返回 null / Objective value, or null if a term is unknown.</returns>
    public virtual V? Evaluate() {
        V sum = Constant;
        foreach (ICell<V> cell in Cells) {
            if (cell.Evaluate() is not { } value) {
                return null;
            }
            sum = sum.Plus(value);
        }
        return sum;
    }

    /// <summary>使用解向量求值 / Evaluate using a solution vector.</summary>
    /// <param name="solution">求解器顺序的解向量 / Solution vector in solver order.</param>
    /// <returns>目标值；任一项未知时返回 null / Objective value, or null if a term is unknown.</returns>
    public virtual V? Evaluate(IReadOnlyList<V> solution) {
        V sum = Constant;
        foreach (ICell<V> cell in Cells) {
            if (cell.Evaluate(solution) is not { } value) {
                return null;
            }
            sum = sum.Plus(value);
        }
        return sum;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} [{Category}]";
}

/// <summary>线性子目标 / Linear sub-objective.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed record LinearSubObject<V> : SubObject<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly SymbolFlatten.LinearFlattenData<V> _flattenData;
    private readonly IReadOnlyList<ILinearCell<V>> _cells;

    /// <summary>线性单项式快照 / Linear monomial snapshot.</summary>
    /// <returns>非零线性项 / Nonzero linear terms.</returns>
    public IReadOnlyList<LinearMonomial<V>> LinearTerms() => _flattenData.Monomials;
    /// <inheritdoc/>
    public override IReadOnlyList<ICell<V>> Cells => _cells;
    /// <inheritdoc/>
    public override V Constant => _flattenData.Constant;

    private LinearSubObject(
        SymbolFlatten.LinearFlattenData<V> flattenData,
        IReadOnlyList<ILinearCell<V>> cells,
        ObjectCategory category,
        string name,
        string? displayName) {
        _flattenData = flattenData;
        _cells = cells;
        Category = category;
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>解析并创建线性子目标 / Resolve and create a linear sub-objective.</summary>
    /// <param name="flattenData">展平数据 / Flatten data.</param>
    /// <param name="tokens">变量 token 表 / Variable token table.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="category">目标类别 / Objective category.</param>
    /// <param name="name">子目标名称 / Sub-objective name.</param>
    /// <param name="displayName">子目标显示名称 / Sub-objective display name.</param>
    /// <returns>子目标或无法解析的模型项错误 / Sub-objective or an unresolved-term error.</returns>
    public static Result<LinearSubObject<V>, ErrorCode, Error<ErrorCode>> Create(
        SymbolFlatten.LinearFlattenData<V> flattenData,
        IAbstractTokenTable<V> tokens,
        IFlt64ValueConverter<V> converter,
        ObjectCategory category,
        string name = "",
        string? displayName = null) {
        Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> expanded =
            flattenData.ExpandIntermediateSymbols(tokens);
        if (expanded is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<LinearSubObject<V>>(failed.Error);
        }
        if (expanded is Fatal<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<LinearSubObject<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        var snapshot = new SymbolFlatten.LinearFlattenData<V>(
            expanded.Value.Monomials.Where(term => !term.Coefficient.Eq(converter.Zero)).ToList().AsReadOnly(),
            expanded.Value.Constant);
        return ConstraintCellFactory.CreateLinearCells(
            monomials: snapshot.Monomials,
            tokens: tokens,
            converter: converter
        ).Map(cells => new LinearSubObject<V>(
            flattenData: snapshot,
            cells: cells.AsReadOnly(),
            category: category,
            name: name,
            displayName: displayName
        ));
    }

    /// <inheritdoc/>
    public override string ToString() => $"Linear: {Name} [{Category}]";
}

/// <summary>二次子目标 / Quadratic sub-objective.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed record QuadraticSubObject<V> : SubObject<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly SymbolFlatten.QuadraticFlattenData<V> _flattenData;
    private readonly IReadOnlyList<IQuadraticCell<V>> _cells;

    /// <summary>二次单项式快照 / Quadratic monomial snapshot.</summary>
    /// <returns>非零二次或线性项 / Nonzero quadratic or linear terms.</returns>
    public IReadOnlyList<QuadraticMonomial<V>> QuadraticTerms() => _flattenData.Monomials;
    /// <inheritdoc/>
    public override IReadOnlyList<ICell<V>> Cells => _cells;
    /// <inheritdoc/>
    public override V Constant => _flattenData.Constant;

    private QuadraticSubObject(
        SymbolFlatten.QuadraticFlattenData<V> flattenData,
        IReadOnlyList<IQuadraticCell<V>> cells,
        ObjectCategory category,
        string name,
        string? displayName) {
        _flattenData = flattenData;
        _cells = cells;
        Category = category;
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>解析并创建二次子目标 / Resolve and create a quadratic sub-objective.</summary>
    /// <param name="flattenData">展平数据 / Flatten data.</param>
    /// <param name="tokens">变量 token 表 / Variable token table.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="category">目标类别 / Objective category.</param>
    /// <param name="name">子目标名称 / Sub-objective name.</param>
    /// <param name="displayName">子目标显示名称 / Sub-objective display name.</param>
    /// <returns>子目标或无法解析的模型项错误 / Sub-objective or an unresolved-term error.</returns>
    public static Result<QuadraticSubObject<V>, ErrorCode, Error<ErrorCode>> Create(
        SymbolFlatten.QuadraticFlattenData<V> flattenData,
        IAbstractTokenTable<V> tokens,
        IFlt64ValueConverter<V> converter,
        ObjectCategory category,
        string name = "",
        string? displayName = null) {
        Result<SymbolFlatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> expanded =
            flattenData.ExpandIntermediateSymbols(tokens);
        if (expanded is Failed<SymbolFlatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<QuadraticSubObject<V>>(failed.Error);
        }
        if (expanded is Fatal<SymbolFlatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<QuadraticSubObject<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        var snapshot = new SymbolFlatten.QuadraticFlattenData<V>(
            expanded.Value.Monomials.Where(term => !term.Coefficient.Eq(converter.Zero)).ToList().AsReadOnly(),
            expanded.Value.Constant);
        return ConstraintCellFactory.CreateQuadraticCells(
            monomials: snapshot.Monomials,
            tokens: tokens,
            converter: converter
        ).Map(cells => new QuadraticSubObject<V>(
            flattenData: snapshot,
            cells: cells.AsReadOnly(),
            category: category,
            name: name,
            displayName: displayName
        ));
    }

    /// <inheritdoc/>
    public override string ToString() => $"Quadratic: {Name} [{Category}]";
}
