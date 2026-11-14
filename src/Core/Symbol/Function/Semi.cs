#nullable enable

using System;
using System.Collections.Generic;
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
/// 半连续变量函数 / Semi-continuous variable function
/// </summary>
public sealed class SemiFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public Flt64 LowerBound { get; }
    public Flt64 UpperBound { get; }
    public RealVar ResultVar { get; }
    public BinVar IndicatorVar { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => new ISymbol[] { IndicatorVar, ResultVar };
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, ResultVar) },
        Flt64.Zero
    );

    public SemiFunction(
        Flt64? lb = null,
        Flt64? ub = null,
        string name = "semi",
        string? displayName = null) {
        LowerBound = lb ?? Flt64.Zero;
        UpperBound = ub ?? new Flt64(BigMValues.Default);
        if (!BigMValues.IsUsable(LowerBound) ||
            !BigMValues.IsUsable(UpperBound) ||
            LowerBound.Gr(UpperBound)) {
            throw new ArgumentException(
                "Semi-continuous bounds must be finite and ordered."
            );
        }
        Name = name;
        DisplayName = displayName;
        ResultVar = new RealVar($"{name}_result");
        IndicatorVar = new BinVar($"{name}_indicator");
        _helperVariables = new IVariableItem[] { ResultVar, IndicatorVar };
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        if (!values.TryGetValue(IndicatorVar, out Flt64 indicator) ||
            !values.TryGetValue(ResultVar, out Flt64 result)) {
            return null;
        }
        if (indicator.Eq(Flt64.Zero)) {
            return Flt64.Zero;
        }
        return result.Ls(LowerBound)
            ? LowerBound
            : result.Gr(UpperBound) ? UpperBound : result;
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) =>
        RoundingSupport.AddConstraints(
            model,
            new[] {
                RoundingSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                        new LinearMonomial<Flt64>(UpperBound.Negate(), IndicatorVar),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.LE,
                    rhs: Flt64.Zero,
                    name: $"{Name}_semi_upper"
                ),
                RoundingSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                        new LinearMonomial<Flt64>(LowerBound.Negate(), IndicatorVar),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.GE,
                    rhs: Flt64.Zero,
                    name: $"{Name}_semi_lower"
                ),
            }
        );

    /// <summary>
    /// 从变量的有限边界创建半连续函数 / Create a semi-continuous function from finite variable bounds.
    /// </summary>
    /// <param name="variable">用于推导边界的变量 / Variable whose bounds are used.</param>
    /// <param name="lb">显式激活下界 / Explicit active lower bound.</param>
    /// <param name="ub">显式激活上界 / Explicit active upper bound.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>半连续函数 / Semi-continuous function.</returns>
    public static SemiFunction From(
        IVariableItem variable,
        Flt64? lb = null,
        Flt64? ub = null,
        string name = "semi",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(variable);
        Flt64 inferredLower = variable.LowerBound?.Value.UnwrapOrNull() ?? Flt64.Zero;
        Flt64 inferredUpper =
            variable.UpperBound?.Value.UnwrapOrNull() ?? new Flt64(BigMValues.Default);
        return new SemiFunction(
            lb: lb ?? inferredLower,
            ub: ub ?? inferredUpper,
            name: name,
            displayName: displayName
        );
    }
}
