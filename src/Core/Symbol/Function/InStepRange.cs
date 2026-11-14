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
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 步长区间归属函数 / Step range membership function
/// </summary>
public sealed class InStepRangeFunction :
    IMathFunctionSymbol<Flt64>,
    IHasResultPolynomial<Flt64> {
    private readonly LinearPolynomial<Flt64> _resultPolynomial;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public LinearPolynomial<Flt64> LowerBound { get; }
    public LinearPolynomial<Flt64> UpperBound { get; }
    public Flt64 Step { get; }
    public LinearPolynomial<Flt64> Difference { get; }
    public FloorFunction<Flt64> Floor { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => Floor.HelperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(new[] { LowerBound, UpperBound });
    public LinearPolynomial<Flt64> ResultPolynomial => _resultPolynomial;

    public InStepRangeFunction(
        LinearPolynomial<Flt64> lowerBound,
        LinearPolynomial<Flt64> upperBound,
        Flt64 step,
        string name = "inStepRange",
        string? displayName = null) {
        LowerBound = lowerBound ?? throw new ArgumentNullException(nameof(lowerBound));
        UpperBound = upperBound ?? throw new ArgumentNullException(nameof(upperBound));
        if (!BigMValues.IsUsable(step) || !step.Gr(Flt64.Zero)) {
            throw new ArgumentException(
                "Step must be positive and finite.",
                nameof(step)
            );
        }
        if (!RoundingSupport.IsUsable(LowerBound) ||
            !RoundingSupport.IsUsable(UpperBound)) {
            throw new ArgumentException("Step-range bounds must be finite.");
        }

        Step = step;
        Name = name;
        DisplayName = displayName;
        Difference = DifferenceOf(UpperBound, LowerBound, step);
        if (!RoundingSupport.IsUsable(Difference)) {
            throw new ArgumentException("Scaled step-range bounds must be finite.");
        }
        Floor = new FloorFunction<Flt64>(
            polynomial: Difference,
            converter: IIntoValue<Flt64>.Identity,
            name: $"{name}_q"
        );
        _resultPolynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(step, Floor.ResultVar) }
                .Concat(LowerBound.Monomials)
                .ToArray(),
            LowerBound.Constant
        );
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        Flt64? lower = RoundingSupport.Evaluate(LowerBound, values);
        Flt64? upper = RoundingSupport.Evaluate(UpperBound, values);
        if (lower is null || upper is null ||
            !BigMValues.IsUsable(lower.Value) ||
            !BigMValues.IsUsable(upper.Value) ||
            lower.Value.Gr(upper.Value)) {
            return null;
        }
        Flt64 quotient = upper.Value.Minus(lower.Value).Div(Step).Floor();
        return lower.Value.Plus(quotient.Times(Step));
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        Floor.RegisterAuxiliaryTokens(tokens);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Try floorResult = Floor.RegisterConstraints(model);
        if (floorResult.IsFailed) {
            return floorResult;
        }
        return RoundingSupport.AddConstraints(
            model,
            new[] {
                RoundingSupport.Row(
                    monomials: LowerBound.Monomials
                        .Concat(UpperBound.Monomials.Select(monomial =>
                            new LinearMonomial<Flt64>(
                                monomial.Coefficient.Negate(),
                                monomial.Symbol
                            )))
                        .ToArray(),
                    constant: LowerBound.Constant.Minus(UpperBound.Constant),
                    comparison: Comparison.LE,
                    rhs: Flt64.Zero,
                    name: $"{Name}_bounds"
                ),
            }
        );
    }

    private static LinearPolynomial<Flt64> DifferenceOf(
        LinearPolynomial<Flt64> upper,
        LinearPolynomial<Flt64> lower,
        Flt64 step) {
        var monomials = new List<LinearMonomial<Flt64>>(
            upper.Monomials.Count + lower.Monomials.Count
        );
        monomials.AddRange(upper.Monomials.Select(monomial =>
            new LinearMonomial<Flt64>(
                monomial.Coefficient.Div(step),
                monomial.Symbol
            )));
        monomials.AddRange(lower.Monomials.Select(monomial =>
            new LinearMonomial<Flt64>(
                monomial.Coefficient.Negate().Div(step),
                monomial.Symbol
            )));
        return new LinearPolynomial<Flt64>(
            monomials,
            upper.Constant.Minus(lower.Constant).Div(step)
        );
    }
}
