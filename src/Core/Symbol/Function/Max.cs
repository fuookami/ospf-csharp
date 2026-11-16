#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

internal static class ExtremumValues {
    public static bool ApplyResultBounds<V>(
        RealVar result,
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        IIntoValue<V> converter,
        bool minimum)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            return ApplyResultBoundsChecked(
                result: result,
                polynomials: polynomials,
                converter: converter,
                minimum: minimum
            );
        }
        catch (ArithmeticException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    private static bool ApplyResultBoundsChecked<V>(
        RealVar result,
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        IIntoValue<V> converter,
        bool minimum)
        where V : struct, IRealNumber<V>, INumberField<V> {
        IReadOnlyList<LinearPolynomialBounds<V>?> bounds =
            polynomials.Select(polynomial => BigMValues.FiniteBounds(polynomial, converter)).ToList();
        if (bounds.Any(bound => bound is null)) {
            return false;
        }

        if (!ExactBoundValue.TryRead(
                value: bounds[0]!.LowerBound,
                converter: converter,
                exact: out ExactBoundValue lower
            ) || !ExactBoundValue.TryRead(
                value: bounds[0]!.UpperBound,
                converter: converter,
                exact: out ExactBoundValue upper
            )) {
            return false;
        }
        for (int index = 1; index < bounds.Count; ++index) {
            LinearPolynomialBounds<V> bound = bounds[index]!;
            if (!ExactBoundValue.TryRead(
                    value: bound.LowerBound,
                    converter: converter,
                    exact: out ExactBoundValue nextLower
                ) || !ExactBoundValue.TryRead(
                    value: bound.UpperBound,
                    converter: converter,
                    exact: out ExactBoundValue nextUpper
                )) {
                return false;
            }
            if (minimum) {
                lower = nextLower.CompareTo(lower) < 0 ? nextLower : lower;
                upper = nextUpper.CompareTo(upper) < 0 ? nextUpper : upper;
            }
            else {
                lower = nextLower.CompareTo(lower) > 0 ? nextLower : lower;
                upper = nextUpper.CompareTo(upper) > 0 ? nextUpper : upper;
            }
        }
        if (!lower.TryRound(
                converter: IIntoValue<Flt64>.Identity,
                upper: false,
                result: out Flt64 lowerValue
            ) || !upper.TryRound(
                converter: IIntoValue<Flt64>.Identity,
                upper: true,
                result: out Flt64 upperValue
            )) {
            return false;
        }
        result.Range.Geq(lowerValue);
        result.Range.Leq(upperValue);
        return true;
    }

    public static IReadOnlyList<V>? BigMs<V>(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        V? explicitBigM,
        IIntoValue<V> converter,
        bool minimum)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (explicitBigM is { } value) {
            return Enumerable.Repeat(value, polynomials.Count).ToArray();
        }

        IReadOnlyList<LinearPolynomialBounds<V>?> bounds =
            polynomials.Select(polynomial => BigMValues.FiniteBounds(polynomial, converter)).ToList();
        if (bounds.Any(bound => bound is null)) {
            return null;
        }

        if (!ExactBoundValue.TryRead(
                value: minimum ? bounds[0]!.LowerBound : bounds[0]!.UpperBound,
                converter: converter,
                exact: out ExactBoundValue anchor
            )) {
            return null;
        }
        for (int index = 1; index < bounds.Count; ++index) {
            LinearPolynomialBounds<V> bound = bounds[index]!;
            if (!ExactBoundValue.TryRead(
                    value: minimum ? bound.LowerBound : bound.UpperBound,
                    converter: converter,
                    exact: out ExactBoundValue next
                )) {
                return null;
            }
            if (minimum ? next.CompareTo(anchor) < 0 : next.CompareTo(anchor) > 0) {
                anchor = next;
            }
        }
        var values = new List<V>(bounds.Count);
        foreach (LinearPolynomialBounds<V>? bound in bounds) {
            if (!ExactBoundValue.TryRead(
                    value: minimum ? bound!.UpperBound : bound!.LowerBound,
                    converter: converter,
                    exact: out ExactBoundValue edge
                )) {
                return null;
            }
            ExactBoundValue distance = minimum ? edge.Add(anchor.Negate()) : anchor.Add(edge.Negate());
            if (distance.Sign < 0 || !distance.TryRoundSolverUpper(converter, out V rounded)) {
                return null;
            }
            values.Add(BigMValues.EnsurePositiveBigM(rounded, converter));
        }
        return values;
    }

    public static bool IsUsable<V>(LinearPolynomial<V> polynomial, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        CanNegate(polynomial.Constant, converter) &&
        polynomial.Monomials.All(monomial => CanNegate(monomial.Coefficient, converter));

    public static bool CanNegate<V>(V value, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (!BigMValues.IsUsable(value, converter)) {
            return false;
        }
        try {
            V negative = value.Negate();
            return value.Eq(converter.Zero) ||
                (value.Ls(converter.Zero) ? negative.Gr(converter.Zero) : negative.Ls(converter.Zero));
        }
        catch (OverflowException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    public static IReadOnlyList<LinearInequality<V>> CreateConstraints<V>(
        IReadOnlyList<LinearPolynomial<V>> inputs,
        RealVar result,
        IReadOnlyList<BinVar> selectors,
        IReadOnlyList<V> bigMs,
        string name,
        bool minimum,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var zero = new LinearPolynomial<V>(
            Array.Empty<LinearMonomial<V>>(),
            converter.Zero
        );
        var constraints = new List<LinearInequality<V>>((inputs.Count * 2) + 1);
        for (int index = 0; index < inputs.Count; ++index) {
            LinearPolynomial<V> input = inputs[index];
            var monomials = new List<LinearMonomial<V>>(input.Monomials.Count + 1) {
                new(converter.One, result),
            };
            monomials.AddRange(input.Monomials.Select(monomial =>
                new LinearMonomial<V>(monomial.Coefficient.Negate(), monomial.Symbol)));
            constraints.Add(new LinearInequality<V>(
                Lhs: new LinearPolynomial<V>(monomials, input.Constant.Negate()),
                Rhs: zero,
                Comparison: minimum ? Comparison.LE : Comparison.GE,
                Name: minimum
                    ? $"{name}_min_ub_{index}"
                    : $"{name}_max_lb_{index}"
            ));
        }

        for (int index = 0; index < inputs.Count; ++index) {
            LinearPolynomial<V> input = inputs[index];
            V bigM = bigMs[index];
            var monomials = new List<LinearMonomial<V>>(input.Monomials.Count + 2) {
                new(converter.One, result),
            };
            monomials.AddRange(input.Monomials.Select(monomial =>
                new LinearMonomial<V>(monomial.Coefficient.Negate(), monomial.Symbol)));
            monomials.Add(new LinearMonomial<V>(
                minimum ? bigM.Negate() : bigM,
                selectors[index]
            ));
            constraints.Add(new LinearInequality<V>(
                Lhs: new LinearPolynomial<V>(monomials, input.Constant.Negate()),
                Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), minimum ? bigM.Negate() : bigM),
                Comparison: minimum ? Comparison.GE : Comparison.LE,
                Name: minimum
                    ? $"{name}_min_lb_{index}"
                    : $"{name}_max_ub_{index}"
            ));
        }

        constraints.Add(new LinearInequality<V>(
            Lhs: new LinearPolynomial<V>(
                selectors.Select(selector => new LinearMonomial<V>(converter.One, selector)).ToList(),
                converter.Zero
            ),
            Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), converter.One),
            Comparison: Comparison.EQ,
            Name: minimum ? $"{name}_min_select_one" : $"{name}_max_select_one"
        ));
        return constraints;
    }
}

/// <summary>极值函数共享实现 / Shared extremum-function implementation.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public abstract class ExtremumFunctionBase<V> :
    IMathFunctionSymbol<V>,
    IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IIntoValue<V> _converter;
    private readonly bool _minimum;
    private readonly BoundDependencySnapshot<V>? _boundDependencies;

    public IReadOnlyList<LinearPolynomial<V>> Polynomials { get; }
    public RealVar ResultVar { get; }
    public IReadOnlyList<BinVar> SelectorVars { get; }
    public IReadOnlyList<IVariableItem> HelperVariables { get; }
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomials);
    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(_converter.One, ResultVar) },
        _converter.Zero
    );

    private protected ExtremumFunctionBase(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        IIntoValue<V> converter,
        V? bigM,
        string name,
        string? displayName,
        bool minimum) {
        if (polynomials is null) {
            throw new ArgumentNullException(nameof(polynomials));
        }
        if (polynomials.Count == 0) {
            throw new ArgumentException("Extremum functions require at least one polynomial.", nameof(polynomials));
        }
        if (polynomials.Any(polynomial => polynomial is null)) {
            throw new ArgumentException("Extremum polynomials cannot contain null.", nameof(polynomials));
        }

        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        Polynomials = Array.AsReadOnly(polynomials.Select(polynomial =>
            new LinearPolynomial<V>(Array.AsReadOnly(polynomial.Monomials.ToArray()), polynomial.Constant)).ToArray());
        Name = name;
        DisplayName = displayName;
        _minimum = minimum;
        string suffix = minimum ? "min" : "max";
        ResultVar = new RealVar($"{name}_{suffix}");
        ResultVar.Range.SetRange(ValueRange<Flt64>.Full());
        SelectorVars = Enumerable.Range(0, Polynomials.Count)
            .Select(index => new BinVar($"{name}_{suffix}_sel{index}"))
            .ToArray();
        HelperVariables = new[] { (IVariableItem)ResultVar }.Concat(SelectorVars).ToArray();
        ExplicitBigM = bigM;
        bool capturedBounds = ExtremumValues.ApplyResultBounds(
            result: ResultVar,
            polynomials: Polynomials,
            converter: _converter,
            minimum: minimum
        );
        _boundDependencies = capturedBounds || bigM is null ? new BoundDependencySnapshot<V>(Polynomials) : null;
    }

    /// <summary>
    /// **是否取最小值 / Whether this computes the minimum.**
    /// </summary>
    /// <remarks>
    /// **`Min` 与 `Max` 是同一个类的两个构造，而"是哪一种"此前只存在于私有字段里——
    /// 于是任何想<b>读</b>这个区分的代码（比如把符号投影成延迟结构）都做不到，
    /// 而写错方向的后果是静默的：`min` 会被当成 `max` 物化，模型照样能解。**
    ///
    /// **Added. `Min` and `Max` are two constructions of one class, and which one it is existed only in a private
    /// field — so any code that needs to <b>read</b> the distinction (projecting the symbol into a deferred structure,
    /// say) could not, and getting it wrong is silent: a `min` materialises as a `max` and the model still solves.**
    /// </remarks>
    public bool IsMinimum => _minimum;

    /// <summary>
    /// **显式给出的大 M（可能为 null）/ The explicitly supplied Big-M, if any.**
    /// </summary>
    /// <remarks>
    /// **它的可见性从 `private protected` 放宽到 `public`。**
    ///
    /// **理由：投影需要用它<b>重算</b>那组大 M（构造时算出的值没有存下来）。**
    /// **而放宽可见性比"把算出的那组存下来"更小：后者要多一个字段、多一处赋值，
    /// 且要保证两处永远一致。**
    ///
    /// **Its visibility is relaxed from `private protected` to `public`.**
    ///
    /// **The projection needs it to <b>recompute</b> the Big-M set (the values computed at construction time were not kept).
    /// Widening the visibility is smaller than storing the computed set: the latter adds a field, an assignment, and an
    /// invariant that must hold forever.**
    /// </remarks>
    public V? ExplicitBigM { get; }

    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        try {
            return EvaluateChecked(values);
        }
        catch (ArithmeticException) {
            return FunctionEvaluation.Invalid<V>();
        }
        catch (InvalidOperationException) {
            return FunctionEvaluation.Invalid<V>();
        }
    }

    private Result<V?, ErrorCode, Error<ErrorCode>> EvaluateChecked(IReadOnlyDictionary<ISymbol, V> values) {
        V? result = null;
        foreach (LinearPolynomial<V> polynomial in Polynomials) {
            Result<V?, ErrorCode, Error<ErrorCode>> evaluated = FunctionEvaluation.Polynomial(polynomial, values);
            if (evaluated.IsFailed || evaluated.Value is not { } value) {
                return evaluated;
            }
            if (result is { } previous && !FunctionEvaluation.Comparable(value, previous)) {
                return FunctionEvaluation.Invalid<V>();
            }
            if (result is null || (_minimum ? value.Ls(result.Value) : value.Gr(result.Value))) {
                result = value;
            }
        }
        return Results.Ok(result);
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (_boundDependencies is { } dependencies && !dependencies.IsCurrent()) {
            return InvalidBounds();
        }
        if (Polynomials.Any(polynomial => !ExtremumValues.IsUsable(polynomial, _converter))) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "极值函数输入含非有限、不可表示值。 / Extremum input contains a non-finite or unrepresentable value."
                )
            );
        }

        IReadOnlyList<V>? bigMs;
        try {
            bigMs = ExtremumValues.BigMs(
                polynomials: Polynomials,
                explicitBigM: ExplicitBigM,
                converter: _converter,
                minimum: _minimum
            );
        }
        catch (OverflowException) {
            return InvalidBigM();
        }
        catch (InvalidOperationException) {
            return InvalidBigM();
        }
        if (bigMs is null || bigMs.Any(bigM => !BigMValues.IsUsable(bigM, _converter) || bigM.Ls(_converter.Zero))) {
            return InvalidBigM();
        }

        IReadOnlyList<LinearInequality<V>> constraints = ExtremumValues.CreateConstraints(
            inputs: Polynomials,
            result: ResultVar,
            selectors: SelectorVars,
            bigMs: bigMs,
            name: Name,
            minimum: _minimum,
            converter: _converter
        );
        return IndicatorSupport.AddConstraints(
            model: model,
            constraints: constraints,
            afterWrite: () => {
                if (_boundDependencies is { } tracked) {
                    if (!tracked.IsCurrent()) {
                        return InvalidBounds();
                    }
                    if (model is BasicMechanismModel<V> mechanism) {
                        mechanism.TrackBoundDependencies(tracked);
                    }
                }
                return Results.OkInstance;
            }
        );
    }

    private static Try InvalidBigM() => Results.Failed<Success>(
        new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "极值函数 Big-M 不可推导或无效。 / Extremum Big-M is unavailable or invalid."
        )
    );

    private static Try InvalidBounds() => Results.Failed<Success>(
        new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "极值函数的输入范围已更改或无法跟踪。 / Extremum input bounds changed or cannot be tracked."
        )
    );

}

/// <summary>最大值函数 / Maximum function.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class MaxFunction<V> : ExtremumFunctionBase<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>创建最大值函数 / Create a maximum function.</summary>
    /// <param name="polynomials">输入多项式 / Input polynomials.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选 Big-M / Optional Big-M.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public MaxFunction(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "max",
        string? displayName = null)
        : base(
            polynomials: polynomials,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName,
            minimum: false
        ) { }
}

/// <summary>最小值函数 / Minimum function.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class MinFunction<V> : ExtremumFunctionBase<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>创建最小值函数 / Create a minimum function.</summary>
    /// <param name="polynomials">输入多项式 / Input polynomials.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选 Big-M / Optional Big-M.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public MinFunction(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "min",
        string? displayName = null)
        : base(
            polynomials: polynomials,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName,
            minimum: true
        ) { }
}
