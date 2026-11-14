#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Token = Fuookami.Ospf.Core.Token;
namespace Fuookami.Ospf.Core.Model.Mechanism;

internal static class LinearFlattenExtensions {
    public static Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> ExpandIntermediateSymbols<V>(
        this LinearFlattenData<V> data,
        Token.IAbstractTokenTable<V>? tokens = null,
        bool allowUnregisteredFunctions = true,
        ISet<IIntermediateSymbol>? functions = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            V zero = data.Constant.Constants.Zero;
            var terms = new List<LinearMonomial<V>>();
            var active = new HashSet<IIntermediateSymbol>(ReferenceEqualityComparer.Instance);
            var frames = new Stack<ExpansionFrame<V>>();
            frames.Push(new ExpansionFrame<V>(
                monomials: data.Monomials,
                constant: data.Constant,
                scale: null,
                symbol: null
            ));
            while (frames.Count != 0) {
                ExpansionFrame<V> frame = frames.Peek();
                if (frame.Index == frame.Monomials.Count) {
                    frames.Pop();
                    if (frame.Symbol is not null) {
                        active.Remove(frame.Symbol);
                        ExpansionFrame<V> parent = frames.Peek();
                        if (!Arithmetic<V>.TryAdd(
                                lhs: parent.Constant,
                                rhs: frame.Constant,
                                result: out V constant
                            )) {
                            return Invalid<V>("线性常量溢出 / Linear constant overflow");
                        }
                        parent.Constant = constant;
                    }
                    else {
                        return Results.Ok(new LinearFlattenData<V>(terms, frame.Constant));
                    }
                    continue;
                }

                LinearMonomial<V> term = frame.Monomials[frame.Index++];
                if (term.Coefficient.Eq(zero)) {
                    continue;
                }
                V coefficient = term.Coefficient;
                if (frame.Scale is { } scale &&
                    !Arithmetic<V>.TryMultiply(
                        lhs: scale,
                        rhs: coefficient,
                        result: out coefficient
                    )) {
                    return Invalid<V>("线性系数溢出 / Linear coefficient overflow");
                }
                if (coefficient.Eq(zero)) {
                    continue;
                }
                if (term.Symbol is IVariableItem variable) {
                    terms.Add(new LinearMonomial<V>(coefficient, variable));
                    continue;
                }
                if (term.Symbol is not ILinearIntermediateSymbol<V> symbol ||
                    symbol is LinearFunctionSymbolAdapter<V> adapter && adapter.Function is not IHasResultPolynomial<V>) {
                    return Invalid<V>($"无法展开线性符号 / Cannot expand linear symbol: {term.Symbol?.Name ?? "<null>"}");
                }
                if (symbol is IMathFunctionSymbolBase<V>) {
                    if (tokens is null && !allowUnregisteredFunctions) {
                        return Invalid<V>($"无法保留函数注册状态 / Cannot retain function registration: {symbol.Name}");
                    }
                    if (tokens is not null && !tokens.Symbols.Any(registered => ReferenceEquals(registered, symbol))) {
                        return Invalid<V>($"函数符号未注册 / Function symbol is not registered: {symbol.Name}");
                    }
                    functions?.Add(symbol);
                }
                if (!active.Add(symbol)) {
                    return Invalid<V>($"线性符号存在循环依赖 / Cyclic linear symbol: {symbol.Name}");
                }
                LinearPolynomial<V> polynomial = symbol.Polynomial;
                if (!Arithmetic<V>.TryMultiply(
                        lhs: coefficient,
                        rhs: polynomial.Constant,
                        result: out V contribution
                    )) {
                    return Invalid<V>("线性常量溢出 / Linear constant overflow");
                }
                frames.Push(new ExpansionFrame<V>(
                    monomials: polynomial.Monomials,
                    constant: contribution,
                    scale: coefficient,
                    symbol: symbol
                ));
            }
            return Invalid<V>("线性表达式展开失败 / Linear expression expansion failed");
        }
        catch (ArithmeticException) {
            return Invalid<V>("线性表达式数值无效 / Invalid linear expression arithmetic");
        }
        catch (InvalidOperationException) {
            return Invalid<V>("线性表达式数值无效 / Invalid linear expression arithmetic");
        }
    }

    public static Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> ToLinearFlattenData<V>(
        this LinearInequality<V> inequality,
        Token.IAbstractTokenTable<V>? tokens = null,
        bool allowUnregisteredFunctions = true,
        ISet<IIntermediateSymbol>? functions = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> lhs =
            new LinearFlattenData<V>(inequality.Lhs.Monomials, inequality.Lhs.Constant).ExpandIntermediateSymbols(
                tokens: tokens,
                allowUnregisteredFunctions: allowUnregisteredFunctions,
                functions: functions
            );
        if (!lhs.IsOk) {
            return lhs;
        }
        Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> rhs =
            new LinearFlattenData<V>(inequality.Rhs.Monomials, inequality.Rhs.Constant).ExpandIntermediateSymbols(
                tokens: tokens,
                allowUnregisteredFunctions: allowUnregisteredFunctions,
                functions: functions
            );
        if (!rhs.IsOk) {
            return rhs;
        }
        try {
            var merged = new Dictionary<VariableItemKey, LinearMonomial<V>>();
            foreach (LinearMonomial<V> term in lhs.Value.Monomials) {
                var variable = (IVariableItem)term.Symbol;
                if (!Arithmetic<V>.IsValid(term.Coefficient)) {
                    return Invalid<V>("线性系数无效 / Invalid linear coefficient");
                }
                if (merged.TryGetValue(variable.Key, out LinearMonomial<V>? existing)) {
                    if (!Arithmetic<V>.TryAdd(
                            lhs: existing.Coefficient,
                            rhs: term.Coefficient,
                            result: out V coefficient
                        )) {
                        return Invalid<V>("线性系数溢出 / Linear coefficient overflow");
                    }
                    merged[variable.Key] = new LinearMonomial<V>(coefficient, existing.Symbol);
                }
                else {
                    merged[variable.Key] = term;
                }
            }
            foreach (LinearMonomial<V> term in rhs.Value.Monomials) {
                var variable = (IVariableItem)term.Symbol;
                if (merged.TryGetValue(variable.Key, out LinearMonomial<V>? existing)) {
                    if (!Arithmetic<V>.TrySubtract(
                            lhs: existing.Coefficient,
                            rhs: term.Coefficient,
                            result: out V coefficient
                        )) {
                        return Invalid<V>("线性系数溢出 / Linear coefficient overflow");
                    }
                    merged[variable.Key] = new LinearMonomial<V>(coefficient, existing.Symbol);
                }
                else if (Arithmetic<V>.TryNegate(term.Coefficient, out V coefficient)) {
                    merged[variable.Key] = new LinearMonomial<V>(coefficient, variable);
                }
                else {
                    return Invalid<V>("线性系数无效 / Invalid linear coefficient");
                }
            }
            if (!Arithmetic<V>.TrySubtract(
                    lhs: lhs.Value.Constant,
                    rhs: rhs.Value.Constant,
                    result: out V constant
                ) ||
                !Arithmetic<V>.TryNegate(constant, out _)) {
                return Invalid<V>("线性常量溢出 / Linear constant overflow");
            }
            return Results.Ok(new LinearFlattenData<V>(merged.Values.ToList(), constant));
        }
        catch (ArithmeticException) {
            return Invalid<V>("线性表达式数值无效 / Invalid linear expression arithmetic");
        }
        catch (InvalidOperationException) {
            return Invalid<V>("线性表达式数值无效 / Invalid linear expression arithmetic");
        }
    }

    private static Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> Invalid<V>(string message)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Results.Failed<LinearFlattenData<V>>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private sealed class ExpansionFrame<V>
        where V : struct, IRealNumber<V>, INumberField<V> {
        public IReadOnlyList<LinearMonomial<V>> Monomials { get; }
        public V Constant { get; set; }
        public V? Scale { get; }
        public IIntermediateSymbol? Symbol { get; }
        public int Index { get; set; }

        public ExpansionFrame(
            IReadOnlyList<LinearMonomial<V>> monomials,
            V constant,
            V? scale,
            IIntermediateSymbol? symbol) {
            Monomials = monomials;
            Constant = constant;
            Scale = scale;
            Symbol = symbol;
        }
    }

    internal static class Arithmetic<V>
        where V : struct, IRealNumber<V>, INumberField<V> {
        private static readonly bool Integer = typeof(V).GetInterfaces().Any(type =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IInteger<>));

        public static bool IsValid(V value) => NumericValueValidation.IsValid(value);

        public static bool TryAdd(V lhs, V rhs, out V result) {
            result = lhs.Plus(rhs);
            V zero = lhs.Constants.Zero;
            return IsValid(lhs) && IsValid(rhs) && IsValid(result) && (!Integer ||
                !(rhs.Gr(zero) && result.Ls(lhs)) && !(rhs.Ls(zero) && result.Gr(lhs)));
        }

        public static bool TrySubtract(V lhs, V rhs, out V result) {
            result = lhs.Minus(rhs);
            V zero = lhs.Constants.Zero;
            return IsValid(lhs) && IsValid(rhs) && IsValid(result) && (!Integer ||
                !(rhs.Gr(zero) && result.Gr(lhs)) && !(rhs.Ls(zero) && result.Ls(lhs)));
        }

        public static bool TryNegate(V value, out V result) {
            result = value.Negate();
            V zero = value.Constants.Zero;
            return IsValid(value) && IsValid(result) && (!Integer || value.Eq(zero) ||
                (value.Gr(zero) ? result.Ls(zero) : result.Gr(zero)));
        }

        public static bool TryMultiply(V lhs, V rhs, out V result) {
            result = lhs.Times(rhs);
            V zero = lhs.Constants.Zero;
            return IsValid(lhs) && IsValid(rhs) && IsValid(result) &&
                (lhs.Eq(zero) || rhs.Eq(zero) || !result.Eq(zero)) && (!Integer ||
                lhs.Eq(zero) || rhs.Eq(zero) ||
                ((lhs.Gr(zero) == rhs.Gr(zero) ? result.Gr(zero) : result.Ls(zero)) && result.Div(rhs).Eq(lhs)));
        }
    }
}
