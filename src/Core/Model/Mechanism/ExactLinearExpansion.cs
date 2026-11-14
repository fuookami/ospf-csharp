#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Model.Mechanism;

internal static class ExactLinearExpansion {
    public static bool Matches<V>(LinearPolynomial<V> input, LinearPolynomial<V> expanded, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        MatchesCore(
            inputs: [(input, false)],
            expanded: expanded,
            converter: converter
        );

    public static bool MatchesDifference<V>(LinearInequality<V> input, LinearFlattenData<V> expanded)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        MatchesCore(
            inputs: [(input.Lhs, false), (input.Rhs, true)],
            expanded: new LinearPolynomial<V>(expanded.Monomials, expanded.Constant),
            converter: null
        );

    private static bool MatchesCore<V>(
        IReadOnlyList<(LinearPolynomial<V> Polynomial, bool Subtract)> inputs,
        LinearPolynomial<V> expanded,
        IIntoValue<V>? converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (!Read(
                value: expanded.Constant,
                converter: converter,
                exact: out ExactBoundValue expandedConstant
            ) || converter is not null && !TryNegate(expanded.Constant, converter)) {
            return false;
        }
        ExactBoundValue constant = ExactBoundValue.Zero;
        var expected = new Dictionary<VariableItemKey, ExactBoundValue>();
        var active = new HashSet<IIntermediateSymbol>(ReferenceEqualityComparer.Instance);
        var frames = new Stack<(IReadOnlyList<LinearMonomial<V>> Terms, int Index, ExactBoundValue Scale, IIntermediateSymbol? Symbol)>();
        foreach ((LinearPolynomial<V> polynomial, bool subtract) in inputs) {
            if (polynomial?.Monomials is null || !Read(
                    value: polynomial.Constant,
                    converter: converter,
                    exact: out ExactBoundValue inputConstant
                )) {
                return false;
            }
            ExactBoundValue direction = new(subtract ? -BigInteger.One : BigInteger.One, BigInteger.One);
            constant = constant.Add(direction.Multiply(inputConstant));
            frames.Push((polynomial.Monomials, 0, direction, null));
            while (frames.Count != 0) {
                (IReadOnlyList<LinearMonomial<V>> Terms, int Index, ExactBoundValue Scale, IIntermediateSymbol? Symbol) frame = frames.Pop();
                if (frame.Index == frame.Terms.Count) {
                    if (frame.Symbol is not null) {
                        active.Remove(frame.Symbol);
                    }
                    continue;
                }
                LinearMonomial<V> term = frame.Terms[frame.Index++];
                frames.Push(frame);
                if (term is null || term.Symbol is null || !Read(
                        value: term.Coefficient,
                        converter: converter,
                        exact: out ExactBoundValue coefficient
                    )) {
                    return false;
                }
                ExactBoundValue scale = frame.Scale.Multiply(coefficient);
                if (term.Symbol is IVariableItem variable) {
                    expected[variable.Key] = (expected.TryGetValue(variable.Key, out ExactBoundValue previous)
                        ? previous : ExactBoundValue.Zero).Add(scale);
                    continue;
                }
                if (term.Symbol is not ILinearIntermediateSymbol<V> symbol ||
                    symbol is LinearFunctionSymbolAdapter<V> adapter && adapter.Function is not IHasResultPolynomial<V>) {
                    return false;
                }
                if (scale.Sign == 0) {
                    continue;
                }
                if (!active.Add(symbol)) {
                    return false;
                }
                LinearPolynomial<V> nested = symbol.Polynomial;
                if (nested?.Monomials is null || !Read(
                        value: nested.Constant,
                        converter: converter,
                        exact: out ExactBoundValue nestedConstant
                    )) {
                    return false;
                }
                constant = constant.Add(scale.Multiply(nestedConstant));
                frames.Push((nested.Monomials, 0, scale, symbol));
            }
        }
        foreach (LinearMonomial<V> term in expanded.Monomials) {
            if (term.Symbol is not IVariableItem variable || !Read(
                    value: term.Coefficient,
                    converter: converter,
                    exact: out ExactBoundValue coefficient
                ) || converter is not null && !TryNegate(term.Coefficient, converter)) {
                return false;
            }
            expected[variable.Key] = (expected.TryGetValue(variable.Key, out ExactBoundValue previous)
                ? previous : ExactBoundValue.Zero).Add(coefficient.Negate());
        }
        return constant.CompareTo(expandedConstant) == 0 && expected.Values.All(value => value.Sign == 0);
    }

    private static bool Read<V>(V value, IIntoValue<V>? converter, out ExactBoundValue exact)
        where V : struct, IRealNumber<V> {
        if (converter is null) {
            // shortcut: 无法精确读取的数值类型保守失败；扩展 ExactBoundValue 后移除 / Reject unreadable number types; remove after extending ExactBoundValue.
            return ExactBoundValue.TryRead(value, out exact) && FunctionEvaluation.IsUsable(value);
        }
        return ExactBoundValue.TryRead(
            value: value,
            converter: converter,
            exact: out exact
        ) && ExactBoundValue.TryRead(converter.FromValue(value), out ExactBoundValue encoded) && exact.CompareTo(encoded) == 0;
    }

    private static bool TryNegate<V>(V value, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Read(
            value: value,
            converter: converter,
            exact: out ExactBoundValue original
        ) && LinearFlattenExtensions.Arithmetic<V>.TryNegate(value, out V negated) && Read(
            value: negated,
            converter: converter,
            exact: out ExactBoundValue actual
        ) && original.Negate().CompareTo(actual) == 0;
}
