#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 平衡三值化函数 / Balance ternaryzation function
/// </summary>
public sealed class BalanceTernaryzationFunction :
    IMathFunctionSymbol<Flt64>,
    IHasResultPolynomial<Flt64> {
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public LinearPolynomial<Flt64> Polynomial { get; }
    public Flt64 Epsilon { get; }
    public Flt64 FallbackBigM { get; }
    public Flt64 StrictBoundary { get; }
    public RealVar ResultVar { get; }
    public BinVar PositiveVar { get; }
    public BinVar NegativeVar { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomial);
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, ResultVar) },
        Flt64.Zero
    );

    public BalanceTernaryzationFunction(
        LinearPolynomial<Flt64> polynomial,
        Flt64? epsilon = null,
        Flt64? fallbackBigM = null,
        Flt64? strictBoundary = null,
        string name = "bter",
        string? displayName = null) {
        Polynomial = polynomial ?? throw new ArgumentNullException(nameof(polynomial));
        Epsilon = epsilon ?? new Flt64(1e-6);
        FallbackBigM = fallbackBigM ?? new Flt64(BigMValues.Default);
        StrictBoundary = strictBoundary ?? IndicatorSupport.DefaultTolerance;
        if (!BigMValues.IsUsable(Epsilon) || Epsilon.Ls(Flt64.Zero)) {
            throw new ArgumentException(
                "Balance ternaryzation epsilon must be finite and non-negative.",
                nameof(epsilon)
            );
        }
        if (!BigMValues.IsUsable(StrictBoundary) || !StrictBoundary.Gr(Flt64.Zero)) {
            throw new ArgumentException(
                "Balance ternaryzation strict boundary must be finite and positive.",
                nameof(strictBoundary)
            );
        }
        if (!BigMValues.IsUsable(FallbackBigM) ||
            FallbackBigM.ToDouble() <= Epsilon.ToDouble() + StrictBoundary.ToDouble()) {
            throw new ArgumentException(
                "Balance ternaryzation fallback Big-M must exceed epsilon plus the strict boundary.",
                nameof(fallbackBigM)
            );
        }
        Name = name;
        DisplayName = displayName;
        ResultVar = new RealVar($"{name}_bter");
        PositiveVar = new BinVar($"{name}_bter_positive");
        NegativeVar = new BinVar($"{name}_bter_negative");
        _helperVariables = new IVariableItem[] {
            ResultVar,
            PositiveVar,
            NegativeVar,
        };
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        Flt64? value = IndicatorSupport.Evaluate(Polynomial, values);
        if (value is null) {
            return null;
        }
        Flt64 positiveBoundary = Epsilon.Plus(StrictBoundary);
        Flt64 negativeBoundary = positiveBoundary.Negate();
        if (value.Value.Geq(positiveBoundary)) {
            return Flt64.One;
        }
        if (value.Value.Gr(Epsilon)) {
            return null;
        }
        if (value.Value.Leq(negativeBoundary)) {
            return new Flt64(-1.0);
        }
        if (value.Value.Ls(Epsilon.Negate())) {
            return null;
        }
        return Flt64.Zero;
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Flt64 bigM = BigM();
        LinearPolynomial<Flt64> positiveInput = new(
            Polynomial.Monomials,
            Polynomial.Constant.Minus(Epsilon)
        );
        LinearPolynomial<Flt64> negativeInput = new(
            Polynomial.Monomials.Select(monomial =>
                new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol)
            ).ToArray(),
            Polynomial.Constant.Negate().Minus(Epsilon)
        );
        // Kotlin drives each branch from one finite condition range [delta - M, M] instead of a
        // Big-M pair, so the two rows come from the range and the true/false branch gap directly.
        Flt64 conditionLower = StrictBoundary.Minus(bigM);
        var constraints = new List<LinearInequality<Flt64>>(6);
        Try positiveRows = AddRelationRows(
            polynomial: positiveInput,
            indicator: PositiveVar,
            lower: conditionLower,
            upper: bigM,
            namePrefix: $"{Name}_bter_positive",
            constraints: constraints);
        if (positiveRows.IsFailed) {
            return positiveRows;
        }
        Try negativeRows = AddRelationRows(
            polynomial: negativeInput,
            indicator: NegativeVar,
            lower: conditionLower,
            upper: bigM,
            namePrefix: $"{Name}_bter_negative",
            constraints: constraints);
        if (negativeRows.IsFailed) {
            return negativeRows;
        }
        constraints.Add(IndicatorSupport.Row(
            monomials: new[] {
                new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                new LinearMonomial<Flt64>(Flt64.One.Negate(), PositiveVar),
                new LinearMonomial<Flt64>(Flt64.One, NegativeVar),
            },
            constant: Flt64.Zero,
            comparison: Comparison.EQ,
            rhs: Flt64.Zero,
            name: $"{Name}_bter_result"
        ));
        constraints.Add(IndicatorSupport.Row(
            monomials: new[] {
                new LinearMonomial<Flt64>(Flt64.One, PositiveVar),
                new LinearMonomial<Flt64>(Flt64.One, NegativeVar),
            },
            constant: Flt64.Zero,
            comparison: Comparison.LE,
            rhs: Flt64.One,
            name: $"{Name}_bter_exclusive"
        ));
        return IndicatorSupport.AddConstraints(model, constraints);
    }

    private Flt64 BigM() {
        LinearPolynomialBounds<Flt64>? bounds = BigMValues.FiniteBounds(
            Polynomial,
            IIntoValue<Flt64>.Identity
        );
        if (bounds is null) {
            return FallbackBigM;
        }

        double lowerMagnitude = System.Math.Abs(bounds.LowerBound.ToDouble());
        double upperMagnitude = System.Math.Abs(bounds.UpperBound.ToDouble());
        double candidate = System.Math.Max(lowerMagnitude, upperMagnitude) +
            Epsilon.ToDouble() +
            StrictBoundary.ToDouble();
        Flt64 bigM = new(candidate);
        return BigMValues.IsUsable(bigM) && bigM.Gr(Flt64.Zero)
            ? bigM
            : FallbackBigM;
    }

    /// <summary>为一个分支追加两行条件指示约束 / Append the two relation rows for one branch.</summary>
    private Try AddRelationRows(
        LinearPolynomial<Flt64> polynomial,
        BinVar indicator,
        Flt64 lower,
        Flt64 upper,
        string namePrefix,
        List<LinearInequality<Flt64>> constraints) {
        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> rows =
            ConditionSupport.RelationConstraints(
                polynomial: polynomial,
                indicator: indicator,
                relation: Comparison.GT,
                lower: lower,
                upper: upper,
                strictBoundary: StrictBoundary,
                namePrefix: namePrefix
            );
        if (rows is Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
            failed) {
            return Results.Failed<Success>(failed.Error);
        }
        constraints.AddRange(
            ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)rows).Value
        );
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
