#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
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
        V sum = default;
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
        V sum = default;
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
        ConstraintRelation constraintRelation = relation.ConstraintRelation();
        Symbol.Flatten.LinearFlattenData<V> flattenData = relation.FlattenData;
        // Convert V monomials to Flt64 monomials for cell creation
        var flt64Monomials = flattenData.Monomials
            .Select(m => new LinearMonomial<Flt64>(converter.FromValue(m.Coefficient), m.Symbol))
            .ToList();
        List<ILinearCell<V>> cells = ConstraintCellFactory.CreateLinearCells(flt64Monomials, tokens, converter);
        V rhs = default(V).Minus(flattenData.Constant);
        return Results.Ok<LinearConstraintImpl<V>>(new LinearConstraintImpl<V>(
            cells, constraintRelation, rhs, lazy, name, origin, from));
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
        ConstraintRelation constraintRelation = relation.ConstraintRelation();
        Symbol.Flatten.QuadraticFlattenData<V> flattenData = relation.FlattenData;
        // Convert V monomials to Flt64 monomials for cell creation
        var flt64Monomials = flattenData.Monomials
            .Select(m => new QuadraticMonomial<Flt64>(converter.FromValue(m.Coefficient), m.Symbol1, m.Symbol2))
            .ToList();
        List<IQuadraticCell<V>> cells = ConstraintCellFactory.CreateQuadraticCells(flt64Monomials, tokens, converter);
        V rhs = default(V).Minus(flattenData.Constant);
        return Results.Ok<QuadraticConstraintImpl<V>>(new QuadraticConstraintImpl<V>(
            cells, constraintRelation, rhs, lazy, name, origin, from));
    }

    public override string ToString() => $"Quadratic: {Name} [{Sign}] {Rhs}";
}

// ===== Cell creation helpers (internal) =====

internal static class ConstraintCellFactory {
    public static List<ILinearCell<V>> CreateLinearCells<V>(
        IReadOnlyList<LinearMonomial<Flt64>> monomials,
        IAbstractTokenTable<V> tokens,
        IFlt64ValueConverter<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var cells = new List<ILinearCell<V>>();
        foreach (LinearMonomial<Flt64> mono in monomials) {
            if (mono.Symbol is not IVariableItem variable) {
                continue;
            }

            Token<V>? token = tokens.Find(variable);
            if (token is not null && mono.Coefficient != Flt64.Zero) {
                cells.Add(new LinearCellImpl<V>(tokens, mono.Coefficient, token, converter));
            }
        }
        return cells;
    }

    public static List<IQuadraticCell<V>> CreateQuadraticCells<V>(
        IReadOnlyList<QuadraticMonomial<Flt64>> monomials,
        IAbstractTokenTable<V> tokens,
        IFlt64ValueConverter<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var cells = new List<IQuadraticCell<V>>();
        foreach (QuadraticMonomial<Flt64> mono in monomials) {
            if (mono.Symbol1 is not IVariableItem variable1) {
                continue;
            }

            Token<V>? token1 = tokens.Find(variable1);
            if (token1 is null) {
                continue;
            }

            Token<V>? token2 = null;
            if (mono.Symbol2 is IVariableItem variable2) {
                token2 = tokens.Find(variable2);
                if (token2 is null) {
                    continue;
                }
            }
            if (mono.Coefficient != Flt64.Zero) {
                cells.Add(new QuadraticCellImpl<V>(tokens, mono.Coefficient, token1, token2, converter));
            }
        }
        return cells;
    }
}

// ===== MetaDualSolution =====

/// <summary>
/// 元对偶解 / Meta dual solution.
/// <para>构造仅通过 PUBLIC 工厂 Create / ToMeta 暴露 -- 取代 Kotlin 侧依赖反射的桥接路径。</para>
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
}
