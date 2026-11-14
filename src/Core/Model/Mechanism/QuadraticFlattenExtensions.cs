#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Token = Fuookami.Ospf.Core.Token;
namespace Fuookami.Ospf.Core.Model.Mechanism;

internal static class QuadraticFlattenExtensions {
    public static Result<QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> ExpandIntermediateSymbols<V>(
        this QuadraticFlattenData<V> data,
        Token.IAbstractTokenTable<V>? tokens = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            V zero = data.Constant.Constants.Zero;
            var active = new HashSet<IIntermediateSymbol>(ReferenceEqualityComparer.Instance);
            var frames = new Stack<Frame<V>>();
            frames.Push(new Frame<V>(
                monomials: data.Monomials,
                constant: data.Constant,
                symbol: null
            ));
            while (frames.Count != 0) {
                Frame<V> frame = frames.Peek();
                if (frame.Index == frame.Monomials.Count) {
                    frames.Pop();
                    if (frame.Symbol is not null) {
                        active.Remove(frame.Symbol);
                    }
                    var completed = new QuadraticFlattenData<V>(frame.Terms, frame.Constant);
                    if (frames.Count == 0) {
                        return Results.Ok(completed);
                    }
                    Frame<V> parent = frames.Peek();
                    if (parent.First is null) {
                        parent.First = completed;
                    }
                    else {
                        parent.Second = completed;
                    }
                    continue;
                }
                QuadraticMonomial<V> term = frame.Monomials[frame.Index];
                if (frame.Symbol is not null && !LinearFlattenExtensions.Arithmetic<V>.IsValid(term.Coefficient)) {
                    return Invalid<V>("二次系数无效 / Invalid quadratic coefficient");
                }
                if (term.Coefficient.Eq(zero)) {
                    ++frame.Index;
                    continue;
                }
                if (term.Symbol1 is IVariableItem && (term.Symbol2 is null || term.Symbol2 is IVariableItem)) {
                    frame.Terms.Add(term);
                    ++frame.Index;
                    continue;
                }
                if (frame.First is not null && (term.Symbol2 is null || frame.Second is not null)) {
                    string? error = AppendProduct(
                        frame: frame,
                        scale: term.Coefficient,
                        first: frame.First,
                        second: frame.Second
                    );
                    if (error is not null) {
                        return Invalid<V>(error);
                    }
                    frame.First = null;
                    frame.Second = null;
                    ++frame.Index;
                    continue;
                }

                ISymbol factor = frame.First is null ? term.Symbol1 : term.Symbol2!;
                if (factor is IVariableItem variable) {
                    var variableData = new QuadraticFlattenData<V>(
                        [new QuadraticMonomial<V>(zero.Constants.One, variable)], zero);
                    if (frame.First is null) {
                        frame.First = variableData;
                    }
                    else {
                        frame.Second = variableData;
                    }
                    continue;
                }
                if (factor is not IIntermediateSymbol symbol ||
                    symbol is LinearFunctionSymbolAdapter<V> adapter && adapter.Function is not IHasResultPolynomial<V>) {
                    return Invalid<V>($"无法展开二次符号 / Cannot expand quadratic symbol: {factor.Name}");
                }
                if ((symbol is IMathFunctionSymbolBase<V> || symbol is IQuadraticMathFunctionSymbolBase<V>) &&
                    (tokens is null || !tokens.Symbols.Any(registered => ReferenceEquals(registered, symbol)))) {
                    return Invalid<V>($"函数符号未注册 / Function symbol is not registered: {symbol.Name}");
                }
                if (!active.Add(symbol)) {
                    return Invalid<V>($"二次符号存在循环依赖 / Cyclic quadratic symbol: {symbol.Name}");
                }
                QuadraticPolynomial<V> polynomial;
                if (symbol is ILinearIntermediateSymbol<V> linear) {
                    polynomial = linear.Polynomial.ToQuadraticPolynomial();
                }
                else if (symbol is IQuadraticIntermediateSymbol<V> quadratic) {
                    polynomial = quadratic.Polynomial;
                }
                else {
                    return Invalid<V>($"无法展开二次符号 / Cannot expand quadratic symbol: {symbol.Name}");
                }
                if (!LinearFlattenExtensions.Arithmetic<V>.IsValid(polynomial.Constant)) {
                    return Invalid<V>("二次常量无效 / Invalid quadratic constant");
                }
                // shortcut: 不缓存共享子图；大规模 DAG 出现性能瓶颈时再引入版本化缓存 / No shared-DAG cache; add versioned caching if large DAGs become a bottleneck.
                frames.Push(new Frame<V>(
                    monomials: polynomial.Monomials,
                    constant: polynomial.Constant,
                    symbol: symbol
                ));
            }
            return Invalid<V>("二次表达式展开失败 / Quadratic expression expansion failed");
        }
        catch (ArithmeticException) {
            return Invalid<V>("二次表达式数值无效 / Invalid quadratic expression arithmetic");
        }
        catch (InvalidOperationException) {
            return Invalid<V>("二次表达式数值无效 / Invalid quadratic expression arithmetic");
        }
    }

    private static string? AppendProduct<V>(
        Frame<V> frame,
        V scale,
        QuadraticFlattenData<V> first,
        QuadraticFlattenData<V>? second)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V zero = scale.Constants.Zero;
        V secondConstant = second?.Constant ?? scale.Constants.One;
        if (!TryProduct(
                scale: scale,
                first: first.Constant,
                second: secondConstant,
                result: out V contribution
            )) {
            return "二次常量溢出 / Quadratic constant overflow";
        }
        if (!contribution.Eq(zero)) {
            if (!LinearFlattenExtensions.Arithmetic<V>.TryAdd(
                    lhs: frame.Constant,
                    rhs: contribution,
                    result: out V constant
                )) {
                return "二次常量溢出 / Quadratic constant overflow";
            }
            frame.Constant = constant;
        }
        foreach (QuadraticMonomial<V> term in first.Monomials) {
            if (!TryProduct(
                    scale: scale,
                    first: term.Coefficient,
                    second: secondConstant,
                    result: out V coefficient
                )) {
                return "二次系数溢出 / Quadratic coefficient overflow";
            }
            if (!coefficient.Eq(zero)) {
                frame.Terms.Add(term with { Coefficient = coefficient });
            }
        }
        if (second is null) {
            return null;
        }
        foreach (QuadraticMonomial<V> term in second.Monomials) {
            if (!TryProduct(
                    scale: scale,
                    first: first.Constant,
                    second: term.Coefficient,
                    result: out V coefficient
                )) {
                return "二次系数溢出 / Quadratic coefficient overflow";
            }
            if (!coefficient.Eq(zero)) {
                frame.Terms.Add(term with { Coefficient = coefficient });
            }
        }
        foreach (QuadraticMonomial<V> left in first.Monomials) {
            foreach (QuadraticMonomial<V> right in second.Monomials) {
                if (!TryProduct(
                        scale: scale,
                        first: left.Coefficient,
                        second: right.Coefficient,
                        result: out V coefficient
                    )) {
                    return "二次系数溢出 / Quadratic coefficient overflow";
                }
                if (coefficient.Eq(zero)) {
                    continue;
                }
                if (left.Symbol2 is not null || right.Symbol2 is not null) {
                    return "表达式超过二次 / Expression exceeds quadratic degree";
                }
                frame.Terms.Add(new QuadraticMonomial<V>(
                    Coefficient: coefficient,
                    Symbol1: left.Symbol1,
                    Symbol2: right.Symbol1
                ));
            }
        }
        return null;
    }

    private static bool TryProduct<V>(V scale, V first, V second, out V result)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V zero = scale.Constants.Zero;
        if (scale.Eq(zero) || first.Eq(zero) || second.Eq(zero)) {
            result = zero;
            return true;
        }
        if (!LinearFlattenExtensions.Arithmetic<V>.TryMultiply(
                lhs: scale,
                rhs: first,
                result: out V partial
            )) {
            result = zero;
            return false;
        }
        return LinearFlattenExtensions.Arithmetic<V>.TryMultiply(
            lhs: partial,
            rhs: second,
            result: out result
        );
    }

    public static Result<QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> ToQuadraticFlattenData<V>(
        this QuadraticInequalityOf<V> inequality,
        Token.IAbstractTokenTable<V> tokens)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> lhs =
            new QuadraticFlattenData<V>(inequality.Lhs.Monomials, inequality.Lhs.Constant).ExpandIntermediateSymbols(tokens);
        if (!lhs.IsOk) {
            return lhs;
        }
        Result<QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> rhs =
            new QuadraticFlattenData<V>(inequality.Rhs.Monomials, inequality.Rhs.Constant).ExpandIntermediateSymbols(tokens);
        if (!rhs.IsOk) {
            return rhs;
        }
        try {
            var merged = new Dictionary<(VariableItemKey, VariableItemKey?), QuadraticMonomial<V>>();
            foreach ((QuadraticFlattenData<V> side, bool subtract) in new[] { (lhs.Value, false), (rhs.Value, true) }) {
                foreach (QuadraticMonomial<V> term in side.Monomials) {
                    var first = (IVariableItem)term.Symbol1;
                    var second = (IVariableItem?)term.Symbol2;
                    if (second is not null && (second.Identifier < first.Identifier ||
                        second.Identifier == first.Identifier && second.Index < first.Index)) {
                        (first, second) = (second, first);
                    }
                    (VariableItemKey, VariableItemKey?) key = (first.Key, second?.Key);
                    if (!LinearFlattenExtensions.Arithmetic<V>.IsValid(term.Coefficient)) {
                        return Invalid<V>("二次系数无效 / Invalid quadratic coefficient");
                    }
                    V coefficient;
                    if (merged.TryGetValue(key, out QuadraticMonomial<V>? existing)) {
                        bool valid = subtract
                            ? LinearFlattenExtensions.Arithmetic<V>.TrySubtract(
                                lhs: existing.Coefficient,
                                rhs: term.Coefficient,
                                result: out coefficient
                            )
                            : LinearFlattenExtensions.Arithmetic<V>.TryAdd(
                                lhs: existing.Coefficient,
                                rhs: term.Coefficient,
                                result: out coefficient
                            );
                        if (!valid) {
                            return Invalid<V>("二次系数溢出 / Quadratic coefficient overflow");
                        }
                    }
                    else if (subtract) {
                        if (!LinearFlattenExtensions.Arithmetic<V>.TryNegate(term.Coefficient, out coefficient)) {
                            return Invalid<V>("二次系数溢出 / Quadratic coefficient overflow");
                        }
                    }
                    else {
                        coefficient = term.Coefficient;
                    }
                    merged[key] = new QuadraticMonomial<V>(
                        Coefficient: coefficient,
                        Symbol1: first,
                        Symbol2: second
                    );
                }
            }
            if (!LinearFlattenExtensions.Arithmetic<V>.TrySubtract(
                    lhs: lhs.Value.Constant,
                    rhs: rhs.Value.Constant,
                    result: out V constant
                ) || !LinearFlattenExtensions.Arithmetic<V>.TryNegate(constant, out _)) {
                return Invalid<V>("二次常量溢出 / Quadratic constant overflow");
            }
            return Results.Ok(new QuadraticFlattenData<V>(merged.Values.ToList(), constant));
        }
        catch (ArithmeticException) {
            return Invalid<V>("二次表达式数值无效 / Invalid quadratic expression arithmetic");
        }
        catch (InvalidOperationException) {
            return Invalid<V>("二次表达式数值无效 / Invalid quadratic expression arithmetic");
        }
    }

    private static Result<QuadraticFlattenData<V>, ErrorCode, Error<ErrorCode>> Invalid<V>(string message)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Results.Failed<QuadraticFlattenData<V>>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private sealed class Frame<V>(IReadOnlyList<QuadraticMonomial<V>> monomials, V constant, IIntermediateSymbol? symbol)
        where V : struct, IRealNumber<V>, INumberField<V> {
        public IReadOnlyList<QuadraticMonomial<V>> Monomials { get; } = monomials;
        public List<QuadraticMonomial<V>> Terms { get; } = [];
        public V Constant { get; set; } = constant;
        public IIntermediateSymbol? Symbol { get; } = symbol;
        public int Index { get; set; }
        public QuadraticFlattenData<V>? First { get; set; }
        public QuadraticFlattenData<V>? Second { get; set; }
    }
}
