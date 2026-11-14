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
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>绝对值函数 / Absolute value function |x|.</summary>
/// <typeparam name="V">多项式与求值数值类型 / Polynomial and evaluation numeric type.</typeparam>
public sealed class AbsFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly LinearPolynomialBounds<V>? _inputBounds;
    private readonly BoundDependencySnapshot<V>? _boundDependencies;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;
    private readonly IIntoValue<V> _converter;
    private readonly bool _invalidBounds;

    /// <summary>输入线性多项式 / Input linear polynomial.</summary>
    public LinearPolynomial<V> Polynomial { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomial);

    /// <summary>绝对值的正部 / Positive part of the absolute value.</summary>
    public URealVar ResultVar { get; }

    /// <summary>输入的正部 / Positive part of the input.</summary>
    public URealVar PositiveVar { get; }

    /// <summary>输入的负部 / Negative part of the input.</summary>
    public URealVar NegativeVar { get; }

    /// <summary>正负部选择 / Positive or negative part selector.</summary>
    public BinVar SignVar { get; }

    /// <summary>正部 Big-M / Big-M for the positive part.</summary>
    public V PositiveBigM { get; }

    /// <summary>负部 Big-M / Big-M for the negative part.</summary>
    public V NegativeBigM { get; }

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        new[] { new LinearMonomial<V>(_converter.One, ResultVar) },
        _converter.Zero
    );

    /// <summary>
    /// 创建绝对值函数 / Create an absolute value function.
    /// </summary>
    /// <param name="polynomial">输入线性多项式 / Input linear polynomial.</param>
    /// <param name="converter">数值与变量域边界转换器 / Numeric and variable-domain boundary converter.</param>
    /// <param name="bigM">可选 Big-M；默认由输入边界推导 / Optional Big-M; inferred from input bounds by default.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public AbsFunction(
        LinearPolynomial<V> polynomial,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "abs",
        string? displayName = null) {
        if (polynomial is null) {
            throw new ArgumentNullException(nameof(polynomial));
        }
        Polynomial = new LinearPolynomial<V>(Array.AsReadOnly(polynomial.Monomials.ToArray()), polynomial.Constant);
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        Name = name;
        DisplayName = displayName;

        ResultVar = new URealVar($"{name}_abs");
        PositiveVar = new URealVar($"{name}_abs_pos");
        NegativeVar = new URealVar($"{name}_abs_neg");
        ValueRange<Flt64> nonnegative = ValueRange<Flt64>.Geq(Flt64.Zero).Value;
        ResultVar.Range.SetRange(nonnegative);
        PositiveVar.Range.SetRange(nonnegative);
        NegativeVar.Range.SetRange(nonnegative);
        SignVar = new BinVar($"{name}_abs_sign");
        _helperVariables = new IVariableItem[] {
            ResultVar,
            PositiveVar,
            NegativeVar,
            SignVar,
        };

        _inputBounds = BigMValues.FiniteBounds(Polynomial, _converter);
        _boundDependencies = _inputBounds is not null || bigM is null
            ? new BoundDependencySnapshot<V>(new[] { Polynomial })
            : null;
        if (_inputBounds is { } inputBounds) {
            if (TryBoundValues(
                    bounds: inputBounds,
                    converter: _converter,
                    positive: out ExactBoundValue positive,
                    negative: out ExactBoundValue negative,
                    positiveBigM: out V positiveBigM,
                    negativeBigM: out V negativeBigM
                )) {
                PositiveBigM = bigM ?? BigMValues.EnsurePositiveBigM(positiveBigM, _converter);
                NegativeBigM = bigM ?? BigMValues.EnsurePositiveBigM(negativeBigM, _converter);
                ApplyUpperBound(PositiveVar, positive);
                ApplyUpperBound(NegativeVar, negative);
                ApplyUpperBound(ResultVar, positive.CompareTo(negative) >= 0 ? positive : negative);
            }
            else {
                _invalidBounds = true;
                PositiveBigM = bigM ?? _converter.Zero;
                NegativeBigM = bigM ?? _converter.Zero;
            }
        }
        else {
            _invalidBounds = bigM is null;
            PositiveBigM = bigM ?? _converter.Zero;
            NegativeBigM = PositiveBigM;
        }
    }

    private static bool TryBoundValues(
        LinearPolynomialBounds<V> bounds,
        IIntoValue<V> converter,
        out ExactBoundValue positive,
        out ExactBoundValue negative,
        out V positiveBigM,
        out V negativeBigM) {
        positive = default;
        negative = default;
        positiveBigM = default;
        negativeBigM = default;
        try {
            if (!ExactBoundValue.TryRead(
                    value: bounds.LowerBound,
                    converter: converter,
                    exact: out ExactBoundValue lower
                ) || !ExactBoundValue.TryRead(
                    value: bounds.UpperBound,
                    converter: converter,
                    exact: out ExactBoundValue upper
                )) {
                return false;
            }
            positive = upper.Sign > 0 ? upper : ExactBoundValue.Zero;
            negative = lower.Sign < 0 ? lower.Negate() : ExactBoundValue.Zero;
            return positive.TryRoundSolverUpper(converter, out positiveBigM) &&
                negative.TryRoundSolverUpper(converter, out negativeBigM);
        }
        catch (ArithmeticException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    private static void ApplyUpperBound(URealVar variable, ExactBoundValue bound) {
        if (bound.TryRound(
                converter: IIntoValue<Flt64>.Identity,
                upper: true,
                result: out Flt64 upper
            )) {
            variable.Range.Leq(upper);
        }
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        FunctionEvaluation.Transform(FunctionEvaluation.Polynomial(Polynomial, values), value => value.Abs());

    // shortcut: 仅证明单变量单位系数的 ABS；需要更广的嵌套证明时扩展 / Prove only unit-coefficient single-variable ABS; extend for broader nested proofs.
    internal bool ProvesResult(V result, IReadOnlyDictionary<ISymbol, V> modelValues) {
        try {
            if (_invalidBounds || _boundDependencies is { } dependencies && !dependencies.IsCurrent()) {
                return false;
            }
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> expanded =
                new SymbolFlatten.LinearFlattenData<V>(Polynomial.Monomials, Polynomial.Constant).ExpandIntermediateSymbols();
            if (!expanded.IsOk || expanded.Value.Monomials.Count != 1 ||
                expanded.Value.Monomials[0].Symbol is not IVariableItem input ||
                HelperVariables.Any(helper => helper.Key == input.Key)) {
                return false;
            }
            var polynomial = new LinearPolynomial<V>(expanded.Value.Monomials, expanded.Value.Constant);
            if (!ExactLinearExpansion.Matches(
                    input: Polynomial,
                    expanded: polynomial,
                    converter: _converter
                ) || !FunctionEvaluation.TryReadConditionValue(
                    value: _converter.One,
                    converter: _converter,
                    exact: out ExactBoundValue one,
                    encoded: out ExactBoundValue encodedOne
                ) || one.CompareTo(encodedOne) != 0 || one.Numerator != one.Denominator ||
                !FunctionEvaluation.TryReadConditionValue(
                    value: _converter.Zero,
                    converter: _converter,
                    exact: out ExactBoundValue zero,
                    encoded: out ExactBoundValue encodedZero
                ) || zero.Sign != 0 || encodedZero.Sign != 0 ||
                !FunctionEvaluation.TryReadConditionValue(
                    value: expanded.Value.Constant,
                    converter: _converter,
                    exact: out ExactBoundValue constant,
                    encoded: out ExactBoundValue encodedConstant
                ) || constant.Sign != 0 || encodedConstant.Sign != 0 ||
                !FunctionEvaluation.TryReadConditionValue(
                    value: expanded.Value.Monomials[0].Coefficient,
                    converter: _converter,
                    exact: out ExactBoundValue coefficient,
                    encoded: out ExactBoundValue encodedCoefficient
                ) || coefficient.Abs().CompareTo(one) != 0 || coefficient.CompareTo(encodedCoefficient) != 0 ||
                !FunctionEvaluation.TryReadConditionValue(
                    value: result,
                    converter: _converter,
                    exact: out ExactBoundValue actual,
                    encoded: out ExactBoundValue encodedActual
                )) {
                return false;
            }
            Result<bool, ErrorCode, Error<ErrorCode>> evaluated = FunctionEvaluation.ExactPolynomial(
                polynomial: polynomial,
                values: modelValues,
                converter: _converter,
                exact: out ExactBoundValue exact,
                encoded: out ExactBoundValue encoded
            );
            return evaluated.IsOk && evaluated.Value && actual.CompareTo(exact.Abs()) == 0 &&
                encodedActual.CompareTo(encoded.Abs()) == 0 &&
                (_boundDependencies is null || _boundDependencies.IsCurrent());
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        if (_invalidBounds || (_boundDependencies is { } dependencies && !dependencies.IsCurrent())) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "ABS 输入范围不可推导、已更改或无法跟踪。 / ABS input bounds are unavailable, changed or untrackable."
            ));
        }
        if (!BigMValues.IsUsable(Polynomial.Constant, _converter) ||
            Polynomial.Monomials.Any(monomial => !BigMValues.IsUsable(monomial.Coefficient, _converter)) ||
            !BigMValues.IsUsable(PositiveBigM, _converter) || !BigMValues.IsUsable(NegativeBigM, _converter)) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "ABS 输入或 Big-M 含非有限、不可表示值。 / ABS input or Big-M contains a non-finite or unrepresentable value."
                )
            );
        }
        if (PositiveBigM.Ls(_converter.Zero) || NegativeBigM.Ls(_converter.Zero)) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "ABS Big-M 必须非负。 / ABS Big-M values must be nonnegative."
                )
            );
        }

        var zeroPolynomial = new LinearPolynomial<V>(
            Array.Empty<LinearMonomial<V>>(),
            _converter.Zero
        );
        LinearInequality<V>[] constraints = new[] {
            new LinearInequality<V>(
                Lhs: new LinearPolynomial<V>(
                    new[] {
                        new LinearMonomial<V>(_converter.One, ResultVar),
                        new LinearMonomial<V>(_converter.One.Negate(), PositiveVar),
                        new LinearMonomial<V>(_converter.One.Negate(), NegativeVar),
                    },
                    _converter.Zero
                ),
                Rhs: zeroPolynomial,
                Comparison: Comparison.EQ,
                Name: $"{Name}_abs_result"
            ),
            new LinearInequality<V>(
                Lhs: new LinearPolynomial<V>(
                    Polynomial.Monomials.Concat(new[] {
                        new LinearMonomial<V>(_converter.One.Negate(), PositiveVar),
                        new LinearMonomial<V>(_converter.One, NegativeVar),
                    }).ToList(),
                    Polynomial.Constant
                ),
                Rhs: zeroPolynomial,
                Comparison: Comparison.EQ,
                Name: $"{Name}_abs_decompose"
            ),
            new LinearInequality<V>(
                Lhs: new LinearPolynomial<V>(
                    new[] {
                        new LinearMonomial<V>(_converter.One, PositiveVar),
                        new LinearMonomial<V>(PositiveBigM.Negate(), SignVar),
                    },
                    _converter.Zero
                ),
                Rhs: zeroPolynomial,
                Comparison: Comparison.LE,
                Name: $"{Name}_abs_pos_ub"
            ),
            new LinearInequality<V>(
                Lhs: new LinearPolynomial<V>(
                    new[] {
                        new LinearMonomial<V>(_converter.One, NegativeVar),
                        new LinearMonomial<V>(NegativeBigM, SignVar),
                    },
                    _converter.Zero
                ),
                Rhs: new LinearPolynomial<V>(
                    Array.Empty<LinearMonomial<V>>(),
                    NegativeBigM
                ),
                Comparison: Comparison.LE,
                Name: $"{Name}_abs_neg_ub"
            ),
        };

        return IndicatorSupport.AddConstraints(
            model: model,
            constraints: constraints,
            afterWrite: () => {
                if (_boundDependencies is { } tracked) {
                    if (!tracked.IsCurrent()) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.IllegalArgument,
                            "ABS 输入范围已变更。 / ABS input bounds have changed."
                        ));
                    }
                    if (model is BasicMechanismModel<V> mechanism) {
                        mechanism.TrackBoundDependencies(tracked);
                    }
                }
                return Results.OkInstance;
            }
        );
    }
}
