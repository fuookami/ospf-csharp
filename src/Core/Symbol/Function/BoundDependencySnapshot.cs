#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Symbol.Function;

internal sealed class BoundDependencySnapshot<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly List<(ISymbol Symbol, object? Range)> _ranges = new();
    private bool _trackable = true;

    public BoundDependencySnapshot(IEnumerable<LinearPolynomial<V>> polynomials) {
        try {
            foreach (LinearPolynomial<V> polynomial in polynomials) {
                foreach (LinearMonomial<V> monomial in polynomial.Monomials) {
                    if (!TryRange(monomial.Symbol, out object? range)) {
                        _trackable = false;
                    }
                    else {
                        _ranges.Add((monomial.Symbol, range));
                    }
                }
            }
        }
        catch (ArithmeticException) {
            _trackable = false;
        }
        catch (InvalidOperationException) {
            _trackable = false;
        }
    }

    public bool IsCurrent() {
        if (!_trackable) {
            return false;
        }
        try {
            foreach ((ISymbol symbol, object? range) in _ranges) {
                if (!TryRange(symbol, out object? current) || !SameRange(symbol, range, current)) {
                    return false;
                }
            }
            return true;
        }
        catch (ArithmeticException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    private static bool SameRange(ISymbol symbol, object? first, object? second) {
        if (symbol is IVariableItem) {
            // shortcut: 任何范围替换都失效；有类型安全的包含比较时再放宽 / Any range replacement invalidates; relax with type-safe containment.
            return ReferenceEquals(first, second);
        }
        if (first is not ValueRange<V> left || second is not ValueRange<V> right) {
            return first is null && second is null;
        }
        return SameBound(left.LowerBound, right.LowerBound) && SameBound(left.UpperBound, right.UpperBound);
    }

    private static bool SameBound(Bound<V> left, Bound<V> right) {
        if (left.Interval.GetType() != right.Interval.GetType() || left.Value.GetType() != right.Value.GetType()) {
            return false;
        }
        return left.Value is not ValueWrapper<V>.Value first ||
            (right.Value is ValueWrapper<V>.Value second && StructuralValueEquality<V>.Same(first.Number, second.Number));
    }

    private static bool TryRange(ISymbol symbol, out object? range) {
        if (symbol is IVariableItem) {
            if (symbol is IVariableRangeSnapshot variable) {
                range = variable.CurrentRange;
                return true;
            }
        }
        else if (symbol is IIntermediateSymbol<V> intermediate && intermediate.Range is { } expressionRange) {
            range = expressionRange.Range;
            return true;
        }
        range = null;
        return false;
    }
}
