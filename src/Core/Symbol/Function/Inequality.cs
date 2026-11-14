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
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>比较关系的二值指示函数 / Binary indicator for a comparison.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class InequalityFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private static readonly bool IntegerArithmetic = typeof(V).GetInterfaces().Any(type =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IInteger<>));
    private readonly IIntoValue<V> _converter;
    private readonly V? _bigM;
    private readonly V? _tolerance;
    private readonly V? _strictBoundary;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    /// <summary>比较左侧 / Left side of the comparison.</summary>
    public LinearPolynomial<V> Lhs { get; }
    /// <summary>比较右侧常数 / Right-hand constant.</summary>
    public V Rhs { get; }
    /// <summary>比较关系 / Comparison relation.</summary>
    public Comparison Sign { get; }
    /// <summary>比较结果标志 / Comparison result flag.</summary>
    public BinVar FlagVar { get; }
    /// <summary>零带方向标志，仅 EQ/NE 使用 / Zero-band side flag, used only for EQ/NE.</summary>
    public BinVar? SideVar { get; }
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Lhs);
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(_converter.One, FlagVar) },
        _converter.Zero
    );

    /// <summary>创建比较指示函数 / Create a comparison indicator.</summary>
    /// <param name="lhs">左侧多项式 / Left-hand polynomial.</param>
    /// <param name="rhs">右侧常数 / Right-hand constant.</param>
    /// <param name="sign">比较关系 / Comparison relation.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选 Big-M / Optional Big-M.</param>
    /// <param name="tolerance">非严格比较间隔或零带 / Non-strict gap or zero band.</param>
    /// <param name="strictBoundary">严格比较间隔或带外边界 / Strict gap or outside-band boundary.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public InequalityFunction(
        LinearPolynomial<V> lhs,
        V rhs,
        Comparison sign,
        IIntoValue<V> converter,
        V? bigM = null,
        V? tolerance = null,
        V? strictBoundary = null,
        string name = "ineq",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(lhs);
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        Lhs = new LinearPolynomial<V>(lhs.Monomials.ToArray(), lhs.Constant);
        Rhs = rhs;
        Sign = sign;
        _bigM = bigM;
        _tolerance = tolerance;
        _strictBoundary = strictBoundary;
        Name = name;
        DisplayName = displayName;
        FlagVar = new BinVar($"{name}_flag");
        SideVar = sign is Comparison.EQ or Comparison.NE ? new BinVar($"{name}_side") : null;
        _helperVariables = SideVar is { } side
            ? new IVariableItem[] { FlagVar, side }
            : new IVariableItem[] { FlagVar };
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) {
        try {
            if (!TryBoundaries(out V tolerance, out V strict)) {
                return null;
            }
            V? lhs = EvaluateInput(values);
            if (lhs is null || !BigMValues.IsUsable(lhs.Value, _converter) ||
                !ExtremumValues.CanNegate(Rhs, _converter) ||
                !TrySubtract(
                    lhs: lhs.Value,
                    rhs: Rhs,
                    result: out V difference
                ) ||
                !ExtremumValues.CanNegate(difference, _converter)) {
                return null;
            }

            bool? satisfied = Sign switch {
                Comparison.GT => difference.Geq(strict) ? true : difference.Leq(_converter.Zero) ? false : null,
                Comparison.GE => difference.Geq(_converter.Zero) ? true : difference.Leq(tolerance.Negate()) ? false : null,
                Comparison.LT => difference.Leq(strict.Negate()) ? true : difference.Geq(_converter.Zero) ? false : null,
                Comparison.LE => difference.Leq(_converter.Zero) ? true : difference.Geq(tolerance) ? false : null,
                Comparison.EQ or Comparison.NE => EqualityResult(
                    difference: difference,
                    tolerance: tolerance,
                    strict: strict
                ),
                _ => null,
            };
            return satisfied is null ? null : satisfied.Value ? _converter.One : _converter.Zero;
        }
        catch (ArithmeticException) {
            return null;
        }
        catch (InvalidOperationException) {
            return null;
        }
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> checkedRows = CheckedConstraints(
            tokens: null,
            dependencies: out _
        );
        if (checkedRows is Failed<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        if (checkedRows is Fatal<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        return tokens.Add(HelperVariables);
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> checkedRows = CheckedConstraints(
            tokens: model.Tokens,
            dependencies: out BoundDependencySnapshot<V>? dependencies
        );
        if (checkedRows is Failed<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        if (checkedRows is Fatal<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        IReadOnlyList<LinearInequality<V>> rows =
            ((Ok<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>>)checkedRows).Value;
        var registered = model.Tokens.Tokens.Select(token => token.Variable.Key).ToHashSet();
        if (rows.Any(row => row.Lhs.Monomials.Any(monomial =>
                !monomial.Coefficient.Eq(_converter.Zero) &&
                monomial.Symbol is IVariableItem variable && !registered.Contains(variable.Key)))) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "比较约束引用未注册的变量。 / Comparison constraints reference an unregistered variable."
                )
            );
        }
        if (dependencies is not null && !dependencies.IsCurrent()) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "比较输入范围已变更或无法跟踪。 / Comparison input bounds changed or cannot be tracked."
            ));
        }
        return IndicatorSupport.AddConstraints(
            model: model,
            constraints: rows,
            afterWrite: () => {
                if (dependencies is not null) {
                    if (!dependencies.IsCurrent()) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.IllegalArgument,
                            "比较输入范围已变更或无法跟踪。 / Comparison input bounds changed or cannot be tracked."
                        ));
                    }
                    if (model is BasicMechanismModel<V> mechanism) {
                        mechanism.TrackBoundDependencies(dependencies);
                    }
                }
                return Results.OkInstance;
            }
        );
    }

    private V? EvaluateInput(IReadOnlyDictionary<ISymbol, V> values) {
        V result = Lhs.Constant;
        if (!BigMValues.IsUsable(result, _converter)) {
            return null;
        }
        foreach (LinearMonomial<V> monomial in Lhs.Monomials) {
            if (!values.TryGetValue(monomial.Symbol, out V value) ||
                !BigMValues.IsUsable(value, _converter) ||
                !BigMValues.IsUsable(monomial.Coefficient, _converter)) {
                return null;
            }
            V term = monomial.Coefficient.Times(value);
            if (!BigMValues.IsUsable(term, _converter) ||
                (IntegerArithmetic && !value.Eq(_converter.Zero) &&
                    !term.Div(value).Eq(monomial.Coefficient)) ||
                !TryAdd(
                    lhs: result,
                    rhs: term,
                    result: out result
                )) {
                return null;
            }
        }
        return result;
    }

    private bool? EqualityResult(V difference, V tolerance, V strict) {
        V distance = difference.Abs();
        if (!BigMValues.IsUsable(distance, _converter)) {
            return null;
        }
        bool? equal = distance.Leq(tolerance) ? true : distance.Geq(strict) ? false : null;
        return equal is null ? null : equal.Value == (Sign == Comparison.EQ);
    }

    private bool TryBoundaries(out V tolerance, out V strict) {
        tolerance = _converter.Zero;
        strict = _converter.Zero;
        if (Sign is Comparison.LE or Comparison.GE or Comparison.EQ or Comparison.NE) {
            tolerance = _tolerance ?? _converter.IntoValue(IndicatorSupport.DefaultTolerance);
            if (!ExtremumValues.CanNegate(tolerance, _converter) || tolerance.Ls(_converter.Zero)) {
                return false;
            }
        }
        if (Sign is Comparison.LT or Comparison.GT or Comparison.EQ or Comparison.NE) {
            strict = _strictBoundary ?? _converter.IntoValue(IndicatorSupport.StrictBoundary);
            if (!ExtremumValues.CanNegate(strict, _converter) || !strict.Gr(_converter.Zero)) {
                return false;
            }
        }
        return Sign switch {
            Comparison.LE or Comparison.GE => tolerance.Gr(_converter.Zero),
            Comparison.EQ or Comparison.NE => strict.Gr(tolerance),
            Comparison.LT or Comparison.GT => true,
            _ => false,
        };
    }

    private Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> CheckedConstraints(
        IAbstractTokenTable<V>? tokens,
        out BoundDependencySnapshot<V>? dependencies) {
        dependencies = null;
        try {
            if (!TryBoundaries(out V tolerance, out V strict) ||
                !ExtremumValues.IsUsable(Lhs, _converter) ||
                !ExtremumValues.CanNegate(Rhs, _converter)) {
                return InvalidInput();
            }
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> expanded =
                new SymbolFlatten.LinearFlattenData<V>(Lhs.Monomials, Lhs.Constant).ExpandIntermediateSymbols(tokens);
            if (expanded is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<IReadOnlyList<LinearInequality<V>>>(failed.Error);
            }
            if (expanded is Fatal<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> fatal) {
                return new Fatal<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
            }
            var structuralInput = new LinearPolynomial<V>(expanded.Value.Monomials, expanded.Value.Constant);
            if (!ExtremumValues.IsUsable(structuralInput, _converter) ||
                !TrySubtract(
                    lhs: structuralInput.Constant,
                    rhs: Rhs,
                    result: out V constant
                )) {
                return InvalidInput();
            }
            var difference = new LinearPolynomial<V>(structuralInput.Monomials, constant);
            if (!ExtremumValues.IsUsable(difference, _converter)) {
                return InvalidInput();
            }
            if (_bigM is null) {
                dependencies = new BoundDependencySnapshot<V>(new[] { difference });
                if (!dependencies.IsCurrent()) {
                    return InvalidInput();
                }
            }
            V? inferred = _bigM ?? BigMValues.InferredBigM(difference, _converter);
            if (inferred is not { } bigM ||
                !ExtremumValues.CanNegate(bigM, _converter) || !bigM.Gr(_converter.Zero)) {
                return InvalidInput();
            }

            V gap = Sign is Comparison.LE or Comparison.GE ? tolerance : strict;
            if (!HasDistinctBoundaries(
                    constant: constant,
                    tolerance: Sign is Comparison.EQ or Comparison.NE ? tolerance : _converter.Zero,
                    strict: gap
                ) ||
                !TryAdd(
                    lhs: bigM,
                    rhs: gap,
                    result: out V relaxed
                ) || !relaxed.Gr(bigM)) {
                return InvalidInput();
            }
            IReadOnlyList<LinearInequality<V>> rows;
            if (Sign is Comparison.EQ or Comparison.NE && SideVar is { } side) {
                if (!TryAdd(
                        lhs: bigM,
                        rhs: tolerance,
                        result: out V bandM
                    ) ||
                    (tolerance.Gr(_converter.Zero) && !bandM.Gr(bigM)) ||
                    !relaxed.Gr(bandM) ||
                    !TryAdd(
                        lhs: bandM,
                        rhs: tolerance,
                        result: out _
                    ) ||
                    !TrySubtract(
                        lhs: strict,
                        rhs: relaxed,
                        result: out V outsideRhs
                    ) ||
                    (Sign == Comparison.NE && !TrySubtract(
                        lhs: outsideRhs,
                        rhs: relaxed,
                        result: out _
                    ))) {
                    return InvalidInput();
                }
                rows = Sign == Comparison.EQ
                    ? IndicatorSupport.ZeroConstraints(
                        polynomial: difference,
                        indicator: FlagVar,
                        side: side,
                        bigM: bigM,
                        tolerance: tolerance,
                        strictBoundary: strict,
                        name: $"{Name}_eq"
                    )
                    : IndicatorSupport.NonzeroConstraints(
                        polynomial: difference,
                        indicator: FlagVar,
                        side: side,
                        bigM: bigM,
                        tolerance: tolerance,
                        strictBoundary: strict,
                        name: $"{Name}_ne"
                    );
            }
            else {
                LinearPolynomial<V> input = Sign is Comparison.LT or Comparison.GE ? -difference : difference;
                rows = Sign is Comparison.LE or Comparison.GE
                    ? InvertedPositiveConstraints(
                        input: input,
                        bigM: bigM,
                        relaxed: relaxed,
                        gap: gap
                    )
                    : IndicatorSupport.PositiveConstraints(
                        polynomial: input,
                        indicator: FlagVar,
                        bigM: bigM,
                        tolerance: gap,
                        name: Name,
                        zero: _converter.Zero
                    );
            }
            return rows.All(IsUsableRow) ? Results.Ok(rows) : InvalidInput();
        }
        catch (ArithmeticException) {
            return InvalidInput();
        }
        catch (InvalidOperationException) {
            return InvalidInput();
        }
    }

    private bool HasDistinctBoundaries(V constant, V tolerance, V strict) {
        return TryAdd(
                lhs: constant,
                rhs: tolerance,
                result: out V positiveBand
            ) &&
            TryAdd(
                lhs: constant,
                rhs: strict,
                result: out V positiveBoundary
            ) &&
            TrySubtract(
                lhs: constant,
                rhs: tolerance,
                result: out V negativeBand
            ) &&
            TrySubtract(
                lhs: constant,
                rhs: strict,
                result: out V negativeBoundary
            ) &&
            positiveBoundary.Gr(positiveBand) && negativeBoundary.Ls(negativeBand);
    }

    private IReadOnlyList<LinearInequality<V>> InvertedPositiveConstraints(
        LinearPolynomial<V> input,
        V bigM,
        V relaxed,
        V gap) => new[] {
            IndicatorSupport.Row(
                monomials: input.Monomials.Concat(new[] { new LinearMonomial<V>(bigM, FlagVar) }).ToArray(),
                constant: input.Constant,
                comparison: Comparison.LE,
                rhs: bigM,
                name: $"{Name}_satisfied"
            ),
            IndicatorSupport.Row(
                monomials: input.Monomials.Concat(new[] { new LinearMonomial<V>(relaxed, FlagVar) }).ToArray(),
                constant: input.Constant,
                comparison: Comparison.GE,
                rhs: gap,
                name: $"{Name}_violated"
            ),
        };

    private bool IsUsableRow(LinearInequality<V> row) {
        if (!ExtremumValues.IsUsable(row.Lhs, _converter) ||
            !ExtremumValues.IsUsable(row.Rhs, _converter) ||
            !TrySubtract(
                lhs: row.Lhs.Constant,
                rhs: row.Rhs.Constant,
                result: out V constant
            ) ||
            !ExtremumValues.CanNegate(constant, _converter)) {
            return false;
        }
        var coefficients = new Dictionary<VariableItemKey, V>();
        foreach (LinearMonomial<V> monomial in row.Lhs.Monomials) {
            if (monomial.Symbol is not IVariableItem variable) {
                return false;
            }
            V previous = coefficients.TryGetValue(variable.Key, out V value) ? value : _converter.Zero;
            if (!TryAdd(
                    lhs: previous,
                    rhs: monomial.Coefficient,
                    result: out V coefficient
                ) ||
                !ExtremumValues.CanNegate(coefficient, _converter)) {
                return false;
            }
            coefficients[variable.Key] = coefficient;
        }
        return row.Rhs.Monomials.Count == 0;
    }

    private bool TryAdd(V lhs, V rhs, out V result) {
        result = lhs.Plus(rhs);
        return BigMValues.IsUsable(result, _converter) &&
            !(lhs.Geq(_converter.Zero) && rhs.Gr(_converter.Zero) && result.Ls(lhs)) &&
            !(lhs.Leq(_converter.Zero) && rhs.Ls(_converter.Zero) && result.Gr(lhs));
    }

    private bool TrySubtract(V lhs, V rhs, out V result) {
        if (!ExtremumValues.CanNegate(rhs, _converter)) {
            result = _converter.Zero;
            return false;
        }
        return TryAdd(
            lhs: lhs,
            rhs: rhs.Negate(),
            result: out result
        );
    }

    private static Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> InvalidInput() =>
        Results.Failed<IReadOnlyList<LinearInequality<V>>>(
            new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "比较输入或指示约束参数无效。 / Invalid comparison input or indicator constraint parameters."
            )
        );
}
