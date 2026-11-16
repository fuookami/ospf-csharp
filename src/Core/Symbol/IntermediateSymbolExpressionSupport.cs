#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
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
    private readonly UInt64 _identifier = IdentifierGenerator.Gen();

    /// <summary>组标识符（由 SymbolCombination 设置）/ Group identifier (set by SymbolCombination).</summary>
    internal UInt64? GroupIdentifier { get; set; }
    /// <summary>组内索引（由 SymbolCombination 设置）/ Index within group (set by SymbolCombination).</summary>
    internal int? IndexInGroup { get; set; }

    /// <inheritdoc/>
    public UInt64 Identifier => GroupIdentifier ?? _identifier;
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
    /// <remarks>
    /// **实例持有而不是每次新建**：早先是 {code}=&gt; ExpressionRange&lt;Flt64&gt;.Create{/code}，每次访问都返回一个全新的空范围，于是 {code}symbol.Range.SetRange(...){/code} 设完就丢——`loadAmount[j].range.leq(mla)` 在 C# 里**根本无法表达**，而写上去的那行代码看起来完全正常。
    /// **Instance-held rather than freshly created**: this used to be {code}=&gt; ExpressionRange&lt;Flt64&gt;.Create{/code}, returning a brand-new empty range on every access, so
    /// {code}symbol.Range.SetRange(...){/code} was discarded immediately — its `loadAmount[j].range.leq(mla)` was **inexpressible** in C#, while the line that tried looked fine.
    /// </remarks>
    public ExpressionRange<Flt64> Range { get; } = ExpressionRange<Flt64>.Create();

    /// <inheritdoc/>
    public bool Discrete => false;

    /// <inheritdoc/>
    public bool Cached => false;

    /// <summary>
    /// 是否以中间变量实现（默认否）/ Whether the symbol is realized through an intermediate variable (default false).
    /// </summary>
    /// <remarks>
    /// 而不是把定义表达式原地展开到每处使用。须在符号加入模型之前设置。
    ///
    /// Syncs inlining the definition at every use site. Set it before the symbol is added to a model.
    /// </remarks>
    public bool UseIntermediateVariable { get; set; }

    /// <summary>辅助变量（惰性创建，跨转储稳定）/ The auxiliary variable, created lazily and stable across dumps.</summary>
    /// <remarks>
    /// ponytail: 辅助变量恒为连续变量，靠等式钉住取值；需要更紧的松弛时按 Category 改用整型变量。
    /// ponytail: the auxiliary stays continuous and the equality pins its value; switch to an integer variable per Category for a tighter relaxation.
    /// </remarks>
    private readonly Lazy<RealVar> _realizationVariable;

    /// <inheritdoc/>
    public Fuookami.Ospf.Core.Variable.IVariableItem? IntermediateVariable =>
        UseIntermediateVariable ? _realizationVariable.Value : null;
    /// <summary>
    /// `aux == 定义式` 的等式约束；未开启中间变量实现时为 null /
    /// The `aux == definition` equality, or null when the switch is off.
    /// </summary>
    /// <remarks>
    /// 返回约束而不是直接往模型里加：C# 模型没有"按符号类型注册其自身约束"的分派点（Kotlin 在 MechanismModel 里有），
    /// 与其新造一条隐式副作用，不如把这一步显式交给调用方。
    ///
    /// A constraint is returned rather than added: the C# model has no dispatch point registering a symbol's own constraints (Kotlin has one inside
    /// MechanismModel), and inventing an implicit side effect is worse than handing the step to the caller.
    /// </remarks>
    /// <returns>等式约束或 null / The equality constraint, or null.</returns>
    public LinearInequality<Flt64>? RealizationConstraint() {
        if (!UseIntermediateVariable) {
            return null;
        }
        var lhs = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, _realizationVariable.Value) },
            Flt64.Zero);
        return new LinearInequality<Flt64>(lhs, Polynomial, Comparison.EQ, Name: Name + "__realization");
    }

    public LinearExpressionSymbol(
        MutableLinearPolynomial<Flt64> polynomial,
        Category? category = null,
        IIntermediateSymbol? parent = null,
        string name = "",
        string? displayName = null) {
        _utilsPolynomial = polynomial;
        _realizationVariable = new Lazy<RealVar>(() =>
            new RealVar((string.IsNullOrEmpty(name) ? "expr" : name) + "__realization"));
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
            Flt64? symVal = EvaluateSymbol(
                symbol: m.Symbol,
                values: values,
                tokenTable: values is null || values.Count == 0 ? tokenTable : null,
                zeroIfNone: false
            );
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
    private readonly UInt64 _identifier = IdentifierGenerator.Gen();

    internal UInt64? GroupIdentifier { get; set; }
    internal int? IndexInGroup { get; set; }

    public UInt64 Identifier => GroupIdentifier ?? _identifier;
    public int Index => IndexInGroup ?? 0;
    public Category Category { get; }
    public Category OperationCategory => QuadraticCategory.Instance;
    public IIntermediateSymbol? Parent { get; }
    public string Name { get; set; }
    public string? DisplayName { get; set; }

    public MutableQuadraticPolynomial<Flt64> AsMutable() => _utilsPolynomial;
    public QuadraticPolynomial<Flt64> Polynomial => _utilsPolynomial.ToQuadraticPolynomial();

    internal QuadraticFlattenData<Flt64> FlattenedMonomials =>
        new(Polynomial.Monomials, _utilsPolynomial.Constant);

    public IReadOnlySet<IIntermediateSymbol> Dependencies {
        get {
            var deps = new HashSet<IIntermediateSymbol>();
            foreach (QuadraticMonomial<Flt64> m in Polynomial.Monomials) {
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

    /// <remarks>
    /// 同 <see cref="LinearExpressionSymbol"/>：实例持有，否则对该符号设范围会被静默丢弃。
    /// Same as <see cref="LinearExpressionSymbol"/>: instance-held, or a range set on this symbol is silently discarded.
    /// </remarks>
    public ExpressionRange<Flt64> Range { get; } = ExpressionRange<Flt64>.Create();
    public bool Discrete => false;
    public bool Cached => false;

    /// <summary>是否以中间变量实现（默认否）/ Whether the symbol is realized through an intermediate variable (default false).</summary>
    /// <remarks>
    /// 与 LinearExpressionSymbol 的同名开关一致；开启后用 RealizationConstraint 取回等式加入模型。
    /// Syncs </remarks>
    public bool UseIntermediateVariable { get; set; }

    /// <summary>辅助变量（惰性创建，跨转储稳定）/ The auxiliary variable, created lazily and stable across dumps.</summary>
    private readonly Lazy<RealVar> _realizationVariable;

    /// <inheritdoc/>
    public Fuookami.Ospf.Core.Variable.IVariableItem? IntermediateVariable =>
        UseIntermediateVariable ? _realizationVariable.Value : null;

    /// <summary>
    /// `aux == 定义式` 的二次等式约束；未开启中间变量实现时为 null /
    /// The quadratic `aux == definition` equality, or null when the switch is off.
    /// </summary>
    /// <remarks>
    /// **与线性版同构，但左侧必须是二次多项式：** **把辅助变量包成 `QuadraticMonomial.Linear` 而不是直接塞进线性多项式，**
    /// **否则这条等式在二次模型里会被当成"阶数不一致"而静默走另一条路径。**
    ///
    /// ** homologous to the linear version, with a quadratic left-hand side: the auxiliary is wrapped as `QuadraticMonomial.Linear` rather than pushed into a linear
    /// polynomial, or the equality would look degree-mismatched in a quadratic model and silently take another path.**
    /// </remarks>
    /// <returns>二次等式约束或 null / The quadratic equality, or null.</returns>
    public QuadraticInequalityOf<Flt64>? RealizationConstraint() {
        if (!UseIntermediateVariable) {
            return null;
        }
        var lhs = new QuadraticPolynomial<Flt64>(
            new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, _realizationVariable.Value) },
            Flt64.Zero);
        return new QuadraticInequalityOf<Flt64>(lhs, Polynomial, Comparison.EQ, Name + "__realization");
    }

    public QuadraticExpressionSymbol(
        MutableQuadraticPolynomial<Flt64> polynomial,
        Category? category = null,
        IIntermediateSymbol? parent = null,
        string name = "",
        string? displayName = null) {
        _utilsPolynomial = polynomial;
        _realizationVariable = new Lazy<RealVar>(() =>
            new RealVar((string.IsNullOrEmpty(name) ? "expr" : name) + "__realization"));
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
        foreach (QuadraticMonomial<Flt64> m in Polynomial.Monomials) {
            Flt64? s1Val = EvaluateSymbol(
                symbol: m.Symbol1,
                values: values,
                tokenTable: values is null || values.Count == 0 ? tokenTable : null,
                zeroIfNone: false
            );
            if (s1Val is null) {
                return null;
            }

            Flt64? termVal = m.Symbol2 is not null
                ? (EvaluateSymbol(
                    symbol: m.Symbol2,
                    values: values,
                    tokenTable: values is null || values.Count == 0 ? tokenTable : null,
                    zeroIfNone: false
                ) is { } s2 ? s1Val.Value * s2 : (Flt64?)null)
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
        foreach (QuadraticMonomial<Flt64> m in Polynomial.Monomials) {
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
        foreach (QuadraticMonomial<Flt64> m in Polynomial.Monomials) {
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
        foreach (QuadraticMonomial<Flt64> m in Polynomial.Monomials) {
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
            IEnumerable<string> parts = Polynomial.Monomials
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
