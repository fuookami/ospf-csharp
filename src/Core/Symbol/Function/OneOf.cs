#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 恰选其一函数 / Exactly one-of selection function
/// </summary>
public sealed class OneOfFunction :
    IMathFunctionSymbol<Flt64>,
    IHasResultPolynomial<Flt64> {
    private readonly Flt64? _explicitBigM;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<LinearPolynomial<Flt64>> Polynomials { get; }
    public Flt64 Tolerance { get; }
    public Flt64 StrictBoundary { get; }
    public BinVar ResultVar { get; }
    public IReadOnlyList<BinVar> IndicatorVars { get; }
    public IReadOnlyList<BinVar> SideVars { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomials);
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, ResultVar) },
        Flt64.Zero
    );

    public OneOfFunction(
        IReadOnlyList<LinearPolynomial<Flt64>> polynomials,
        Flt64? bigM = null,
        Flt64? tolerance = null,
        Flt64? strictBoundary = null,
        string name = "oneof",
        string? displayName = null) {
        if (polynomials is null) {
            throw new ArgumentNullException(nameof(polynomials));
        }
        if (polynomials.Count == 0 || polynomials.Any(polynomial => polynomial is null)) {
            throw new ArgumentException(
                "One-of requires at least one non-null input polynomial.",
                nameof(polynomials)
            );
        }
        Polynomials = Array.AsReadOnly(polynomials.Select(IndicatorSupport.CopyInput).ToArray());
        _explicitBigM = bigM;
        Tolerance = tolerance ?? IndicatorSupport.DefaultTolerance;
        StrictBoundary = strictBoundary ?? IndicatorSupport.StrictBoundary;
        Name = name;
        DisplayName = displayName;
        ResultVar = new BinVar($"{name}_oneof");
        IndicatorVars = Enumerable.Range(0, Polynomials.Count)
            .Select(index => new BinVar($"{name}_oneof_nz{index}"))
            .ToArray();
        SideVars = Enumerable.Range(0, Polynomials.Count)
            .Select(index => new BinVar($"{name}_oneof_side{index}"))
            .ToArray();
        _helperVariables = new[] { (IVariableItem)ResultVar }
            .Concat(IndicatorVars)
            .Concat(SideVars)
            .ToArray();
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        int nonzeroCount = 0;
        foreach (LinearPolynomial<Flt64> polynomial in Polynomials) {
            Flt64? value = IndicatorSupport.Evaluate(polynomial, values);
            if (value is null) {
                return null;
            }
            if (!value.Value.Eq(Flt64.Zero)) {
                ++nonzeroCount;
            }
        }
        return nonzeroCount == 1 ? Flt64.One : Flt64.Zero;
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        var constraints = new List<LinearInequality<Flt64>>(
            (Polynomials.Count * 4) + 2
        );
        var dependencies = new List<BoundDependencySnapshot<Flt64>>();
        for (int index = 0; index < Polynomials.Count; ++index) {
            Try prepared = IndicatorSupport.PrepareInput(
                model: model,
                function: this,
                polynomial: Polynomials[index],
                explicitBigM: _explicitBigM,
                input: out LinearPolynomial<Flt64> input,
                bigM: out Flt64 bigM,
                dependencies: out BoundDependencySnapshot<Flt64>? dependency
            );
            if (prepared.IsFailed) {
                return prepared;
            }
            if (dependency is not null) {
                dependencies.Add(dependency);
            }
            Try valid = IndicatorSupport.Validate(
                polynomial: input,
                bigM: bigM,
                tolerance: Tolerance,
                strictBoundary: StrictBoundary
            );
            if (valid.IsFailed) {
                return valid;
            }
            constraints.AddRange(IndicatorSupport.NonzeroConstraints(
                polynomial: input,
                indicator: IndicatorVars[index],
                side: SideVars[index],
                bigM: bigM,
                tolerance: Tolerance,
                strictBoundary: StrictBoundary,
                name: $"{Name}_oneof_nz_{index}"
            ));
        }
        constraints.Add(IndicatorSupport.Row(
            monomials: IndicatorVars
                .Select(indicator => new LinearMonomial<Flt64>(Flt64.One, indicator))
                .ToArray(),
            constant: Flt64.Zero,
            comparison: Comparison.EQ,
            rhs: Flt64.One,
            name: $"{Name}_oneof_exactly_one"
        ));
        constraints.Add(IndicatorSupport.Row(
            monomials: new[] {
                new LinearMonomial<Flt64>(Flt64.One, ResultVar),
            },
            constant: Flt64.Zero,
            comparison: Comparison.EQ,
            rhs: Flt64.One,
            name: $"{Name}_oneof_result"
        ));
        return IndicatorSupport.AddCheckedConstraints(
            model: model,
            constraints: constraints,
            dependencies: dependencies
        );
    }
}
