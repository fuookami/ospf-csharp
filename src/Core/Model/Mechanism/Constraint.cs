#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using RetFlt64Dual = Fuookami.Ospf.Utils.Functional.Result<
    System.Collections.Generic.IReadOnlyDictionary<Fuookami.Ospf.Core.Model.Mechanism.MathConstraint, Fuookami.Ospf.Math.Algebra.Number.Flt64>,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Model.Mechanism;

// ===== Symbolic Inequality Wrappers =====

/// <summary>
/// 符号线性不等式（包装 LinearInequality）/ Symbolic linear inequality (wraps LinearInequality).
/// </summary>
public sealed record SymbolicLinearInequality<V>(LinearInequality<V> Inequality)
    where V : struct, IRing<V>;

/// <summary>
/// 符号二次不等式（包装 QuadraticInequalityOf）/ Symbolic quadratic inequality (wraps QuadraticInequalityOf).
/// </summary>
public sealed record SymbolicQuadraticInequality<V>(QuadraticInequalityOf<V> Inequality)
    where V : struct, IRing<V>;

// ===== IConstraint<V, P> =====

/// <summary>
/// 约束接口 / Constraint interface.
/// </summary>
public interface IConstraint<V, P>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>左端单元格 / Left-hand side cells</summary>
    IReadOnlyList<ICell<V>> Lhs { get; }
    /// <summary>约束关系符号 / Constraint relation sign</summary>
    ConstraintRelation Sign { get; }
    /// <summary>右端值 / Right-hand side value</summary>
    V Rhs { get; }
    /// <summary>是否为惰性约束 / Whether this is a lazy constraint</summary>
    bool Lazy { get; }
    /// <summary>约束名称 / Constraint name</summary>
    string Name { get; }
    /// <summary>数学约束来源（可空）/ Math constraint origin (nullable)</summary>
    MathConstraint? Origin { get; }
    /// <summary>来源符号与方向（可空）/ Source symbol and direction (nullable)</summary>
    (IIntermediateSymbol Symbol, bool Direction)? From { get; }
    /// <summary>判断约束是否成立（使用缓存结果）/ Check whether the constraint holds (cached)</summary>
    bool? IsTrue();
    /// <summary>判断约束是否成立（使用解向量）/ Check whether the constraint holds (solution vector)</summary>
    bool? IsTrue(IReadOnlyList<V> solution);
}

// ===== ConstraintImpl<V, P> =====

/// <summary>
/// 约束实现基类（抽象记录）/ Constraint implementation base (abstract record).
/// </summary>
public abstract record ConstraintImpl<V, P> : IConstraint<V, P>
    where V : struct, IRealNumber<V>, INumberField<V> {
    public abstract IReadOnlyList<ICell<V>> Lhs { get; }
    public ConstraintRelation Sign { get; init; } = ConstraintRelation.LessEqual;
    public V Rhs { get; init; }
    public bool Lazy { get; init; }
    public string Name { get; init; } = "";
    public MathConstraint? Origin { get; init; }
    public (IIntermediateSymbol Symbol, bool Direction)? From { get; init; }

    public virtual bool? IsTrue() {
        V sum = NumericConstantsRegistry.For<V>().Zero;
        foreach (ICell<V> cell in Lhs) {
            V? v = cell.Evaluate();
            if (v is null) {
                return null;
            }

            sum = sum.Plus(v.Value);
        }
        return Sign.Invoke(sum, Rhs);
    }

    public virtual bool? IsTrue(IReadOnlyList<V> solution) {
        V sum = NumericConstantsRegistry.For<V>().Zero;
        foreach (ICell<V> cell in Lhs) {
            V? v = cell.Evaluate(solution);
            if (v is null) {
                return null;
            }

            sum = sum.Plus(v.Value);
        }
        return Sign.Invoke(sum, Rhs);
    }

    public override string ToString() => $"{Name}: Lhs [{Sign}] {Rhs}";
}

// ===== LinearConstraintImpl<V> =====

/// <summary>
/// 线性约束实现 / Linear constraint implementation (sealed record).
/// </summary>
public sealed record LinearConstraintImpl<V> : ConstraintImpl<V, LinearCategory>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IReadOnlyList<ILinearCell<V>> _linearCells;

    public IReadOnlyList<ILinearCell<V>> LinearCells => _linearCells;
    public override IReadOnlyList<ICell<V>> Lhs { get; }

    internal LinearConstraintImpl(
        IReadOnlyList<ILinearCell<V>> linearCells,
        ConstraintRelation sign,
        V rhs,
        bool lazy,
        string name,
        MathConstraint? origin,
        (IIntermediateSymbol Symbol, bool Direction)? from) {
        _linearCells = linearCells;
        Lhs = new List<ICell<V>>(linearCells);
        Sign = sign;
        Rhs = rhs;
        Lazy = lazy;
        Name = name;
        Origin = origin;
        From = from;
    }

    /// <summary>
    /// 从 LinearRelation 创建线性约束 / Create from a LinearRelation.
    /// </summary>
    public static Result<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> Create(
        ILinearRelation<V> relation,
        IAbstractTokenTable<V> tokens,
        IFlt64ValueConverter<V> converter,
        bool lazy = false,
        string name = "",
        MathConstraint? origin = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null) {
        Result<ConstraintRelation, ErrorCode, Error<ErrorCode>> signResult = relation.ConstraintRelation();
        if (signResult is Failed<ConstraintRelation, ErrorCode, Error<ErrorCode>> invalidSign) {
            return Results.Failed<LinearConstraintImpl<V>>(invalidSign.Error);
        }
        if (signResult is Fatal<ConstraintRelation, ErrorCode, Error<ErrorCode>> fatalSign) {
            return new Fatal<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>>(fatalSign.Errors);
        }
        Symbol.Flatten.LinearFlattenData<V> flattenData = relation.FlattenData;
        Result<List<ILinearCell<V>>, ErrorCode, Error<ErrorCode>> cells = ConstraintCellFactory.CreateLinearCells(
            monomials: flattenData.Monomials,
            tokens: tokens,
            converter: converter
        );
        if (cells is Failed<List<ILinearCell<V>>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<LinearConstraintImpl<V>>(failed.Error);
        }
        V rhs = flattenData.Constant.Negate();
        return Results.Ok<LinearConstraintImpl<V>>(new LinearConstraintImpl<V>(
            linearCells: cells.Value,
            sign: signResult.Value,
            rhs: rhs,
            lazy: lazy,
            name: name,
            origin: origin,
            from: from
        ));
    }

    public override string ToString() => $"Linear: {Name} [{Sign}] {Rhs}";
}

// ===== QuadraticConstraintImpl<V> =====

/// <summary>
/// 二次约束实现 / Quadratic constraint implementation (sealed record).
/// </summary>
public sealed record QuadraticConstraintImpl<V> : ConstraintImpl<V, QuadraticCategory>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IReadOnlyList<IQuadraticCell<V>> _quadraticCells;

    public IReadOnlyList<IQuadraticCell<V>> QuadraticCells => _quadraticCells;
    public override IReadOnlyList<ICell<V>> Lhs { get; }

    internal QuadraticConstraintImpl(
        IReadOnlyList<IQuadraticCell<V>> quadraticCells,
        ConstraintRelation sign,
        V rhs,
        bool lazy,
        string name,
        MathConstraint? origin,
        (IIntermediateSymbol Symbol, bool Direction)? from) {
        _quadraticCells = quadraticCells;
        Lhs = new List<ICell<V>>(quadraticCells);
        Sign = sign;
        Rhs = rhs;
        Lazy = lazy;
        Name = name;
        Origin = origin;
        From = from;
    }

    /// <summary>
    /// 从 QuadraticRelation 创建二次约束 / Create from a QuadraticRelation.
    /// </summary>
    public static Result<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> Create(
        IQuadraticRelation<V> relation,
        IAbstractTokenTable<V> tokens,
        IFlt64ValueConverter<V> converter,
        bool lazy = false,
        string name = "",
        MathConstraint? origin = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null) {
        Result<ConstraintRelation, ErrorCode, Error<ErrorCode>> signResult = relation.ConstraintRelation();
        if (signResult is Failed<ConstraintRelation, ErrorCode, Error<ErrorCode>> invalidSign) {
            return Results.Failed<QuadraticConstraintImpl<V>>(invalidSign.Error);
        }
        if (signResult is Fatal<ConstraintRelation, ErrorCode, Error<ErrorCode>> fatalSign) {
            return new Fatal<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>>(fatalSign.Errors);
        }
        Result<Symbol.Flatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> expanded =
            relation.FlattenData.ExpandIntermediateSymbols(tokens);
        if (expanded is Failed<Symbol.Flatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> invalidTerms) {
            return Results.Failed<QuadraticConstraintImpl<V>>(invalidTerms.Error);
        }
        if (expanded is Fatal<Symbol.Flatten.QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> fatalTerms) {
            return new Fatal<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>>(fatalTerms.Errors);
        }
        Symbol.Flatten.QuadraticFlattenData<V> flattenData = expanded.Value;
        Result<List<IQuadraticCell<V>>, ErrorCode, Error<ErrorCode>> cells = ConstraintCellFactory.CreateQuadraticCells(
            monomials: flattenData.Monomials,
            tokens: tokens,
            converter: converter
        );
        if (cells is Failed<List<IQuadraticCell<V>>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<QuadraticConstraintImpl<V>>(failed.Error);
        }
        V rhs;
        try {
            if (!LinearFlattenExtensions.Arithmetic<V>.TryNegate(flattenData.Constant, out rhs)) {
                return Results.Failed<QuadraticConstraintImpl<V>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "二次常量溢出 / Quadratic constant overflow"));
            }
        }
        catch (ArithmeticException) {
            return Results.Failed<QuadraticConstraintImpl<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "二次常量无效 / Invalid quadratic constant"));
        }
        catch (InvalidOperationException) {
            return Results.Failed<QuadraticConstraintImpl<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "二次常量无效 / Invalid quadratic constant"));
        }
        return Results.Ok<QuadraticConstraintImpl<V>>(new QuadraticConstraintImpl<V>(
            quadraticCells: cells.Value,
            sign: signResult.Value,
            rhs: rhs,
            lazy: lazy,
            name: name,
            origin: origin,
            from: from
        ));
    }

    public override string ToString() => $"Quadratic: {Name} [{Sign}] {Rhs}";
}

// ===== Cell creation helpers (internal) =====

internal static class ConstraintCellFactory {
    public static Result<List<ILinearCell<V>>, ErrorCode, Error<ErrorCode>> CreateLinearCells<V>(
        IReadOnlyList<LinearMonomial<V>> monomials,
        IAbstractTokenTable<V> tokens,
        IFlt64ValueConverter<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var cells = new List<ILinearCell<V>>();
        foreach (LinearMonomial<V> mono in monomials) {
            if (mono.Coefficient.Eq(converter.Zero)) {
                continue;
            }
            Result<Token<V>, ErrorCode, Error<ErrorCode>> resolved = ResolveToken(mono.Symbol, tokens);
            if (resolved is Failed<Token<V>, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<List<ILinearCell<V>>>(failed.Error);
            }
            cells.Add(new LinearCellImpl<V>(
                tokenTable: tokens,
                coefficient: mono.Coefficient,
                token: resolved.Value
            ));
        }
        return Results.Ok(cells);
    }

    public static Result<List<IQuadraticCell<V>>, ErrorCode, Error<ErrorCode>> CreateQuadraticCells<V>(
        IReadOnlyList<QuadraticMonomial<V>> monomials,
        IAbstractTokenTable<V> tokens,
        IFlt64ValueConverter<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var cells = new List<IQuadraticCell<V>>();
        foreach (QuadraticMonomial<V> mono in monomials) {
            if (mono.Coefficient.Eq(converter.Zero)) {
                continue;
            }
            Result<Token<V>, ErrorCode, Error<ErrorCode>> first = ResolveToken(mono.Symbol1, tokens);
            if (first is Failed<Token<V>, ErrorCode, Error<ErrorCode>> failedFirst) {
                return Results.Failed<List<IQuadraticCell<V>>>(failedFirst.Error);
            }
            Token<V>? token2 = null;
            if (mono.Symbol2 is { } secondSymbol) {
                Result<Token<V>, ErrorCode, Error<ErrorCode>> second = ResolveToken(secondSymbol, tokens);
                if (second is Failed<Token<V>, ErrorCode, Error<ErrorCode>> failedSecond) {
                    return Results.Failed<List<IQuadraticCell<V>>>(failedSecond.Error);
                }
                token2 = second.Value;
            }
            cells.Add(new QuadraticCellImpl<V>(
                tokenTable: tokens,
                coefficient: mono.Coefficient,
                token1: first.Value,
                token2: token2
            ));
        }
        return Results.Ok(cells);
    }

    private static Result<Token<V>, ErrorCode, Error<ErrorCode>> ResolveToken<V>(
        ISymbol symbol,
        IAbstractTokenTable<V> tokens)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (symbol is not IVariableItem variable) {
            return Results.Failed<Token<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"无法解析模型项：符号 {symbol.Name} 尚未展开 / Cannot resolve model term: symbol {symbol.Name} is not flattened"
            ));
        }
        Token<V>? token = tokens.Find(variable);
        return token is null
            ? Results.Failed<Token<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"无法解析模型项：变量 {variable.Name} 未注册 / Cannot resolve model term: variable {variable.Name} is not registered"
            ))
            : Results.Ok(token);
    }
}

// ===== MetaDualSolution =====

/// <summary>
/// 元对偶解 / Meta dual solution.
/// <para>构造仅通过 PUBLIC 工厂 Create / ToMeta 暴露 -- 取代 依赖反射的桥接路径。</para>
/// </summary>
public sealed record MetaDualSolution(
    IReadOnlyDictionary<MathConstraint, Flt64> Constraints,
    IReadOnlyDictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>> Symbols) {
    /// <summary>
    /// 从线性约束对偶映射构建 / Build from linear constraint dual map.
    /// </summary>
    public static MetaDualSolution Create(IReadOnlyDictionary<IConstraint<Flt64, LinearCategory>, Flt64> dualSolution) {
        var constraints = dualSolution
            .Where(kv => kv.Key.Origin is not null)
            .ToDictionary(kv => kv.Key.Origin!, kv => kv.Value);
        var symbols = dualSolution
            .Where(kv => kv.Key.From is not null)
            .GroupBy(kv => kv.Key.From!.Value.Symbol)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<(IConstraint<Flt64, LinearCategory>, Flt64)>)g
                    .Select(kv => (kv.Key, kv.Value))
                    .ToList());
        return new MetaDualSolution(constraints, symbols);
    }

    /// <summary>
    /// 从二次约束对偶映射构建 / Build from quadratic constraint dual map.
    /// </summary>
    public static MetaDualSolution Create(IReadOnlyDictionary<IConstraint<Flt64, QuadraticCategory>, Flt64> dualSolution) {
        var constraints = dualSolution
            .Where(kv => kv.Key.Origin is not null)
            .ToDictionary(kv => kv.Key.Origin!, kv => kv.Value);
        // For quadratic, we store as empty symbols since the type differs
        var symbols = new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory>, Flt64)>>();
        return new MetaDualSolution(constraints, symbols);
    }

    public static MetaDualSolution Empty { get; } = new(
        new Dictionary<MathConstraint, Flt64>(),
        new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory>, Flt64)>>());
}

/// <summary>
/// 元对偶解扩展方法 / Meta dual solution extension methods.
/// </summary>
public static class MetaDualSolutionExtensions {
    public static MetaDualSolution ToMeta(this IReadOnlyDictionary<IConstraint<Flt64, LinearCategory>, Flt64> dualSolution)
        => MetaDualSolution.Create(dualSolution);

    public static MetaDualSolution ToMeta(this IReadOnlyDictionary<IConstraint<Flt64, QuadraticCategory>, Flt64> dualSolution)
        => MetaDualSolution.Create(dualSolution);

    /// <summary>
    /// 从求解器返回的元约束对偶映射构建 / Build from the solver's meta-constraint dual map.
    /// </summary>
    /// <remarks>
    /// **求解器直接按 `MathConstraint` 返回对偶**，而符号级价格需要 `IConstraint` 才能归到中间符号。因此这条路径只填充约束级价格，
    /// `Symbols` 为空——调用方若要按符号取价，必须走 `IConstraint` 版本，不能把空表当成"价格为零"。
    /// **The solver returns duals keyed by `MathConstraint`**, while symbol-level prices need `IConstraint` to be attributed to an
    /// intermediate symbol. This path therefore fills constraint-level prices only and leaves `Symbols` empty; a caller needing
    /// per-symbol prices must use the `IConstraint` overload rather than reading the empty map as "price zero".
    /// </remarks>
    /// <param name="dualSolution">求解器对偶映射 / The solver dual map.</param>
    /// <returns>元对偶解 / The meta dual solution.</returns>
    public static MetaDualSolution ToMeta(this IReadOnlyDictionary<MathConstraint, Flt64> dualSolution) {
        System.ArgumentNullException.ThrowIfNull(dualSolution);
        return new MetaDualSolution(
            dualSolution,
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
    }
}
