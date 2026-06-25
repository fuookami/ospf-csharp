#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Symbol;
/// <summary>
/// 线性表达式符号实现 / Linear expression symbol implementation.
/// <para>由线性多项式支持的中间符号，可在求解器边界进行缓存求值。</para>
/// <para>Intermediate symbol backed by a linear polynomial, supporting cached evaluation at solver boundaries.</para>
/// </summary>
public sealed class LinearExpressionSymbol : ILinearIntermediateSymbol<Flt64> {
    internal MutableLinearPolynomial<Flt64> _utilsPolynomial;

    /// <summary>组标识符（由 SymbolCombination 设置）/ Group identifier (set by SymbolCombination).</summary>
    internal UInt64? GroupIdentifier { get; set; }
    /// <summary>组内索引（由 SymbolCombination 设置）/ Index within group (set by SymbolCombination).</summary>
    internal int? IndexInGroup { get; set; }

    /// <inheritdoc/>
    public UInt64 Identifier => GroupIdentifier ?? IdentifierGenerator.Gen();
    /// <inheritdoc/>
    public int Index => IndexInGroup ?? 0;

    /// <inheritdoc/>
    public Category Category { get; }
    /// <inheritdoc/>
    public Category OperationCategory => LinearCategory.Instance;
    /// <inheritdoc/>
    public IIntermediateSymbol? Parent { get; }
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <summary>可变线性多项式 / Mutable linear polynomial.</summary>
    public MutableLinearPolynomial<Flt64> AsMutable() => _utilsPolynomial;
    /// <summary>不可变线性多项式 / Immutable linear polynomial.</summary>
    public LinearPolynomial<Flt64> Polynomial => _utilsPolynomial.ToLinearPolynomial();

    /// <summary>Flatten data (monomials + constant).</summary>
    internal LinearFlattenData<Flt64> FlattenedMonomials =>
        new(_utilsPolynomial.Monomials, _utilsPolynomial.Constant);

    /// <summary>依赖符号集合 / Set of dependency symbols.</summary>
    public IReadOnlySet<IIntermediateSymbol> Dependencies {
        get {
            var deps = new HashSet<IIntermediateSymbol>();
            foreach (LinearMonomial<Flt64> m in _utilsPolynomial.Monomials) {
                if (m.Symbol is IIntermediateSymbol sym) {
                    deps.Add(sym);
                }
            }
            return deps;
        }
    }

    /// <inheritdoc/>
    public ExpressionRange<Flt64> Range => ExpressionRange<Flt64>.Create();

    /// <inheritdoc/>
    public bool Discrete => false;

    /// <inheritdoc/>
    public bool Cached => false;

    public LinearExpressionSymbol(
        MutableLinearPolynomial<Flt64> polynomial,
        Category? category = null,
        IIntermediateSymbol? parent = null,
        string name = "",
        string? displayName = null) {
        _utilsPolynomial = polynomial;
        Category = category ?? LinearCategory.Instance;
        Parent = parent;
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>从变量项创建 / Create from variable item.</summary>
    public static LinearExpressionSymbol FromVariable(IVariableItem item, string? name = null) =>
        new(new MutableLinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, item) },
            Flt64.Zero), name: name ?? item.Name);

    /// <summary>从常量创建 / Create from constant.</summary>
    public static LinearExpressionSymbol FromConstant(Flt64 value, string name = "") =>
        new(new MutableLinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), value), name: name);

    /// <inheritdoc/>
    public Flt64? Prepare(IReadOnlyDictionary<ISymbol, Flt64>? values, IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter) {
        Flt64 ret = _utilsPolynomial.Constant;
        foreach (LinearMonomial<Flt64> m in _utilsPolynomial.Monomials) {
            Flt64? symVal = EvaluateSymbol(m.Symbol, values, tokenTable, false);
            if (symVal is null) {
                return null;
            }

            ret += m.Coefficient * symVal.Value;
        }
        return ret;
    }

    /// <inheritdoc/>
    public void PrepareAndCache(IReadOnlyDictionary<ISymbol, Flt64>? values, IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter) {
        Flt64? result = Prepare(values, tokenTable, converter);
        if (result is { } v) {
            tokenTable.Cache(this, (IReadOnlyList<Flt64>?)null, v);
        }
    }

    /// <inheritdoc/>
    public Flt64? Evaluate(IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) {
        Flt64 ret = _utilsPolynomial.Constant;
        foreach (LinearMonomial<Flt64> m in _utilsPolynomial.Monomials) {
            Flt64? symVal = EvaluateSymbol(m.Symbol, null, tokenTable, zeroIfNone);
            if (symVal is null && !zeroIfNone) {
                return null;
            }

            ret += m.Coefficient * (symVal ?? Flt64.Zero);
        }
        return ret;
    }

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyList<Flt64> results, IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) {
        Flt64 ret = _utilsPolynomial.Constant;
        foreach (LinearMonomial<Flt64> m in _utilsPolynomial.Monomials) {
            Flt64? symVal = m.Symbol switch {
                IVariableItem vi => tokenTable.IndexOf(vi) is { } idx && idx < results.Count ? results[idx] : (zeroIfNone ? Flt64.Zero : null),
                LinearExpressionSymbol les => les.Evaluate(results, tokenTable, converter, zeroIfNone),
                _ => zeroIfNone ? Flt64.Zero : null
            };
            if (symVal is null && !zeroIfNone) {
                return null;
            }

            ret += m.Coefficient * (symVal ?? Flt64.Zero);
        }
        return ret;
    }

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values, IAbstractTokenTable<Flt64>? tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) {
        if (values.TryGetValue(this, out Flt64 selfVal)) {
            return selfVal;
        }

        Flt64 ret = _utilsPolynomial.Constant;
        foreach (LinearMonomial<Flt64> m in _utilsPolynomial.Monomials) {
            Flt64? symVal = EvaluateSymbol(m.Symbol, values, tokenTable, zeroIfNone);
            if (symVal is null && !zeroIfNone) {
                return null;
            }

            ret += m.Coefficient * (symVal ?? Flt64.Zero);
        }
        return ret;
    }

    /// <inheritdoc/>
    public void Flush(bool force = false) { }

    /// <inheritdoc/>
    public string ToRawString(UInt64 unfold) {
        if (unfold != UInt64.Zero) {
            IEnumerable<string> parts = _utilsPolynomial.Monomials
                .Where(m => m.Coefficient != Flt64.Zero)
                .Select(m => {
                    string symStr = m.Symbol switch {
                        IIntermediateSymbol ims => ims.ToRawString(unfold - UInt64.One),
                        ISymbol s => s.Name,
                        _ => "?"
                    };
                    if (m.Coefficient == Flt64.One) {
                        return symStr;
                    }

                    if (m.Coefficient == -Flt64.One) {
                        return $"-{symStr}";
                    }

                    return $"{m.Coefficient} * {symStr}";
                });
            string joined = string.Join(" + ", parts);
            return _utilsPolynomial.Constant != Flt64.Zero
                ? $"{joined} + {_utilsPolynomial.Constant}"
                : joined;
        }
        return DisplayName ?? Name;
    }

    public override string ToString() => DisplayName ?? Name;
    public override int GetHashCode() => Identifier.ToInt32().GetHashCode() * 31 + Index;
    public override bool Equals(object? obj) =>
        obj is LinearExpressionSymbol other && Identifier == other.Identifier && Index == other.Index && Name == other.Name;

    private static Flt64? EvaluateSymbol(ISymbol symbol, IReadOnlyDictionary<ISymbol, Flt64>? values, IAbstractTokenTable<Flt64>? tokenTable, bool zeroIfNone) {
        if (values is not null && values.TryGetValue(symbol, out Flt64 v)) {
            return v;
        }

        if (tokenTable is null) {
            return zeroIfNone ? Flt64.Zero : null;
        }

        return symbol switch {
            IVariableItem vi => tokenTable.Find(vi)?.ResultFlt64 ?? (zeroIfNone ? Flt64.Zero : null),
            LinearExpressionSymbol les => les.Evaluate(tokenTable, default!, zeroIfNone),
            _ => zeroIfNone ? Flt64.Zero : null
        };
    }
}

/// <summary>
/// 二次表达式符号实现 / Quadratic expression symbol implementation.
/// <para>由二次多项式支持的中间符号，可在求解器边界进行缓存求值。</para>
/// </summary>
public sealed class QuadraticExpressionSymbol : IQuadraticIntermediateSymbol<Flt64> {
    internal MutableQuadraticPolynomial<Flt64> _utilsPolynomial;

    internal UInt64? GroupIdentifier { get; set; }
    internal int? IndexInGroup { get; set; }

    public UInt64 Identifier => GroupIdentifier ?? IdentifierGenerator.Gen();
    public int Index => IndexInGroup ?? 0;
    public Category Category { get; }
    public Category OperationCategory => QuadraticCategory.Instance;
    public IIntermediateSymbol? Parent { get; }
    public string Name { get; set; }
    public string? DisplayName { get; set; }

    public MutableQuadraticPolynomial<Flt64> AsMutable() => _utilsPolynomial;
    public QuadraticPolynomial<Flt64> Polynomial => _utilsPolynomial.ToQuadraticPolynomial();

    internal QuadraticFlattenData<Flt64> FlattenedMonomials =>
        new(_utilsPolynomial.Monomials, _utilsPolynomial.Constant);

    public IReadOnlySet<IIntermediateSymbol> Dependencies {
        get {
            var deps = new HashSet<IIntermediateSymbol>();
            foreach (QuadraticMonomial<Flt64> m in _utilsPolynomial.Monomials) {
                if (m.Symbol1 is IIntermediateSymbol s1) {
                    deps.Add(s1);
                }

                if (m.Symbol2 is IIntermediateSymbol s2) {
                    deps.Add(s2);
                }
            }
            return deps;
        }
    }

    public ExpressionRange<Flt64> Range => ExpressionRange<Flt64>.Create();
    public bool Discrete => false;
    public bool Cached => false;

    public QuadraticExpressionSymbol(
        MutableQuadraticPolynomial<Flt64> polynomial,
        Category? category = null,
        IIntermediateSymbol? parent = null,
        string name = "",
        string? displayName = null) {
        _utilsPolynomial = polynomial;
        Category = category ?? QuadraticCategory.Instance;
        Parent = parent;
        Name = name;
        DisplayName = displayName;
    }

    public static QuadraticExpressionSymbol FromVariable(IVariableItem item, string? name = null) =>
        new(new MutableQuadraticPolynomial<Flt64>(
            new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, item) },
            constant: Flt64.Zero), name: name ?? item.Name);

    public static QuadraticExpressionSymbol FromConstant(Flt64 value, string name = "") =>
        new(new MutableQuadraticPolynomial<Flt64>(new List<QuadraticMonomial<Flt64>>(), constant: value), name: name);

    public Flt64? Prepare(IReadOnlyDictionary<ISymbol, Flt64>? values, IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter) {
        Flt64 ret = _utilsPolynomial.Constant;
        foreach (QuadraticMonomial<Flt64> m in _utilsPolynomial.Monomials) {
            Flt64? s1Val = EvaluateSymbol(m.Symbol1, values, tokenTable, false);
            if (s1Val is null) {
                return null;
            }

            Flt64? termVal = m.Symbol2 is not null
                ? (EvaluateSymbol(m.Symbol2!, values, tokenTable, false) is { } s2 ? s1Val.Value * s2 : (Flt64?)null)
                : s1Val;
            if (termVal is null) {
                return null;
            }

            ret += m.Coefficient * termVal.Value;
        }
        return ret;
    }

    public void PrepareAndCache(IReadOnlyDictionary<ISymbol, Flt64>? values, IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter) {
        Flt64? result = Prepare(values, tokenTable, converter);
        if (result is { } v) {
            tokenTable.Cache(this, (IReadOnlyList<Flt64>?)null, v);
        }
    }

    public Flt64? Evaluate(IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) {
        Flt64 ret = _utilsPolynomial.Constant;
        foreach (QuadraticMonomial<Flt64> m in _utilsPolynomial.Monomials) {
            Flt64? s1Val = EvaluateSymbol(m.Symbol1, null, tokenTable, zeroIfNone);
            if (s1Val is null && !zeroIfNone) {
                return null;
            }

            Flt64 termVal;
            if (m.Symbol2 is not null) {
                Flt64? s2Val = EvaluateSymbol(m.Symbol2!, null, tokenTable, zeroIfNone);
                if (s2Val is null && !zeroIfNone) {
                    return null;
                }

                termVal = (s1Val ?? Flt64.Zero) * (s2Val ?? Flt64.Zero);
            }
            else {
                termVal = s1Val ?? Flt64.Zero;
            }

            ret += m.Coefficient * termVal;
        }
        return ret;
    }

    public Flt64? Evaluate(IReadOnlyList<Flt64> results, IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) {
        Flt64 ret = _utilsPolynomial.Constant;
        foreach (QuadraticMonomial<Flt64> m in _utilsPolynomial.Monomials) {
            Flt64? s1Val = EvaluateSymbolFromResults(m.Symbol1, results, tokenTable, zeroIfNone);
            if (s1Val is null && !zeroIfNone) {
                return null;
            }

            Flt64 termVal;
            if (m.Symbol2 is not null) {
                Flt64? s2Val = EvaluateSymbolFromResults(m.Symbol2!, results, tokenTable, zeroIfNone);
                if (s2Val is null && !zeroIfNone) {
                    return null;
                }

                termVal = (s1Val ?? Flt64.Zero) * (s2Val ?? Flt64.Zero);
            }
            else {
                termVal = s1Val ?? Flt64.Zero;
            }

            ret += m.Coefficient * termVal;
        }
        return ret;
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values, IAbstractTokenTable<Flt64>? tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) {
        if (values.TryGetValue(this, out Flt64 selfVal)) {
            return selfVal;
        }

        Flt64 ret = _utilsPolynomial.Constant;
        foreach (QuadraticMonomial<Flt64> m in _utilsPolynomial.Monomials) {
            Flt64? s1Val = EvaluateSymbol(m.Symbol1, values, tokenTable, zeroIfNone);
            if (s1Val is null && !zeroIfNone) {
                return null;
            }

            Flt64 termVal;
            if (m.Symbol2 is not null) {
                Flt64? s2Val = EvaluateSymbol(m.Symbol2!, values, tokenTable, zeroIfNone);
                if (s2Val is null && !zeroIfNone) {
                    return null;
                }

                termVal = (s1Val ?? Flt64.Zero) * (s2Val ?? Flt64.Zero);
            }
            else {
                termVal = s1Val ?? Flt64.Zero;
            }

            ret += m.Coefficient * termVal;
        }
        return ret;
    }

    public void Flush(bool force = false) { }

    public string ToRawString(UInt64 unfold) {
        if (unfold != UInt64.Zero) {
            IEnumerable<string> parts = _utilsPolynomial.Monomials
                .Where(m => m.Coefficient != Flt64.Zero)
                .Select(m => {
                    string s1Str = m.Symbol1 switch {
                        IIntermediateSymbol ims => ims.ToRawString(unfold - UInt64.One),
                        ISymbol s => s.Name,
                        _ => "?"
                    };
                    string termStr = m.Symbol2 is not null
                        ? (m.Symbol1 == m.Symbol2 ? $"{s1Str}^2" : $"{s1Str} * {m.Symbol2.Name}")
                        : s1Str;
                    if (m.Coefficient == Flt64.One) {
                        return termStr;
                    }

                    if (m.Coefficient == -Flt64.One) {
                        return $"-{termStr}";
                    }

                    return $"{m.Coefficient} * {termStr}";
                });
            string joined = string.Join(" + ", parts);
            return _utilsPolynomial.Constant != Flt64.Zero
                ? $"{joined} + {_utilsPolynomial.Constant}"
                : joined;
        }
        return DisplayName ?? Name;
    }

    public override string ToString() => DisplayName ?? Name;
    public override int GetHashCode() => Identifier.ToInt32().GetHashCode() * 31 + Index;
    public override bool Equals(object? obj) =>
        obj is QuadraticExpressionSymbol other && Identifier == other.Identifier && Index == other.Index && Name == other.Name;

    private static Flt64? EvaluateSymbol(ISymbol symbol, IReadOnlyDictionary<ISymbol, Flt64>? values, IAbstractTokenTable<Flt64>? tokenTable, bool zeroIfNone) {
        if (values is not null && values.TryGetValue(symbol, out Flt64 v)) {
            return v;
        }

        if (tokenTable is null) {
            return zeroIfNone ? Flt64.Zero : null;
        }

        return symbol switch {
            IVariableItem vi => tokenTable.Find(vi)?.ResultFlt64 ?? (zeroIfNone ? Flt64.Zero : null),
            LinearExpressionSymbol les => les.Evaluate(tokenTable, default!, zeroIfNone),
            QuadraticExpressionSymbol qes => qes.Evaluate(tokenTable, default!, zeroIfNone),
            _ => zeroIfNone ? Flt64.Zero : null
        };
    }

    private static Flt64? EvaluateSymbolFromResults(ISymbol symbol, IReadOnlyList<Flt64> results, IAbstractTokenTable<Flt64> tokenTable, bool zeroIfNone) {
        return symbol switch {
            IVariableItem vi => tokenTable.IndexOf(vi) is { } idx && idx < results.Count ? results[idx] : (zeroIfNone ? Flt64.Zero : null),
            LinearExpressionSymbol les => les.Evaluate(results, tokenTable, default!, zeroIfNone),
            QuadraticExpressionSymbol qes => qes.Evaluate(results, tokenTable, default!, zeroIfNone),
            _ => zeroIfNone ? Flt64.Zero : null
        };
    }
}
