#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
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
/// 取模函数 / Modulo function
/// </summary>
public sealed class ModFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public LinearPolynomial<Flt64> Polynomial { get; }
    public Flt64 Divisor { get; }
    public Flt64? BigM { get; }
    public IntVar QuotientVar { get; }
    public URealVar RemainderVar { get; }
    public URealVar ResultVar { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomial);
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, ResultVar) },
        Flt64.Zero
    );

    public ModFunction(
        LinearPolynomial<Flt64> polynomial,
        Flt64 divisor,
        Flt64? bigM = null,
        string name = "mod",
        string? displayName = null) {
        Polynomial = polynomial ?? throw new ArgumentNullException(nameof(polynomial));
        if (!BigMValues.IsUsable(divisor) || !divisor.Gr(Flt64.Zero)) {
            throw new ArgumentException(
                "Modulo divisor must be positive and finite.",
                nameof(divisor)
            );
        }
        Divisor = divisor;
        BigM = bigM;
        Name = name;
        DisplayName = displayName;
        QuotientVar = new IntVar($"{name}_q");
        RemainderVar = new URealVar($"{name}_r");
        ResultVar = new URealVar($"{name}_mod");
        _helperVariables = new IVariableItem[] {
            QuotientVar,
            RemainderVar,
            ResultVar,
        };
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        Flt64? value = RoundingSupport.Evaluate(Polynomial, values);
        return value?.Minus(
            value.Value.Div(Divisor).Floor().Times(Divisor)
        );
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        if (!RoundingSupport.IsUsable(Polynomial, Divisor)) {
            return RoundingSupport.InvalidValue("MOD");
        }

        Flt64 epsilon = new(RoundingSupport.Tolerance);
        var decomposeMonomials = new List<LinearMonomial<Flt64>>(
            Polynomial.Monomials.Count + 2
        ) {
            new(Flt64.One, RemainderVar),
            new(Divisor, QuotientVar),
        };
        decomposeMonomials.AddRange(Polynomial.Monomials.Select(monomial =>
            new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol)));

        return RoundingSupport.AddConstraints(
            model,
            new[] {
                RoundingSupport.Row(
                    monomials: decomposeMonomials,
                    constant: Polynomial.Constant.Negate(),
                    comparison: Comparison.EQ,
                    rhs: Flt64.Zero,
                    name: $"{Name}_mod_decompose"
                ),
                RoundingSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, RemainderVar),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.LE,
                    rhs: Divisor.Minus(epsilon),
                    name: $"{Name}_mod_r_ub"
                ),
                RoundingSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                        new LinearMonomial<Flt64>(Flt64.One.Negate(), RemainderVar),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.EQ,
                    rhs: Flt64.Zero,
                    name: $"{Name}_mod_result"
                ),
            }
        );
    }
}
