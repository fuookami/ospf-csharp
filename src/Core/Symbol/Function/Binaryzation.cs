#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
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

/// <summary>
/// 二值化函数 (0/1) / Binaryzation function mapping to 0 or 1
/// </summary>
public sealed class BinaryzationFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly IIntoValue<Flt64> _converter = IIntoValue<Flt64>.Identity;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;
    private readonly BoundDependencySnapshot<Flt64>? _boundDependencies;
    private readonly bool _missingAutomaticBounds;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public LinearPolynomial<Flt64> Polynomial { get; }
    public BinVar ResultVar { get; }
    public Flt64 BigM { get; }
    public Flt64 Tolerance { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomial);
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, ResultVar) },
        Flt64.Zero
    );

    public BinaryzationFunction(
        LinearPolynomial<Flt64> polynomial,
        Flt64? bigM = null,
        string name = "bin",
        string? displayName = null,
        Flt64? tolerance = null) {
        if (polynomial is null) {
            throw new ArgumentNullException(nameof(polynomial));
        }
        Polynomial = new LinearPolynomial<Flt64>(Array.AsReadOnly(polynomial.Monomials.ToArray()), polynomial.Constant);
        _boundDependencies = bigM is null
            ? new BoundDependencySnapshot<Flt64>(new[] { Polynomial })
            : null;
        Flt64? inferred = bigM is null ? BigMValues.InferredBigM(Polynomial, _converter) : null;
        _missingAutomaticBounds = bigM is null && inferred is null;
        BigM = bigM ?? inferred ?? Flt64.Zero;
        Tolerance = tolerance ?? IndicatorSupport.DefaultTolerance;
        Name = name;
        DisplayName = displayName;
        ResultVar = new BinVar($"{name}_bin");
        _helperVariables = new IVariableItem[] { ResultVar };
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        Flt64? value = IndicatorSupport.Evaluate(Polynomial, values);
        if (value is null) {
            return null;
        }
        if (value.Value.Geq(Tolerance)) {
            return Flt64.One;
        }
        return value.Value.Leq(Flt64.Zero) ? Flt64.Zero : null;
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Try valid = IndicatorSupport.Validate(Polynomial, BigM, Tolerance);
        if (valid.IsFailed) {
            return valid;
        }
        if (!Tolerance.Gr(Flt64.Zero)) {
            return IndicatorSupport.InvalidValue();
        }
        if (_missingAutomaticBounds || (_boundDependencies is { } dependencies && !dependencies.IsCurrent())) {
            return IndicatorSupport.InvalidValue();
        }
        Result<SymbolFlatten.LinearFlattenData<Flt64>, ErrorCode, Error<ErrorCode>> input =
            new SymbolFlatten.LinearFlattenData<Flt64>(Polynomial.Monomials, Polynomial.Constant)
                .ExpandIntermediateSymbols(model.Tokens);
        if (!input.IsOk) {
            return input.Map(_ => Results.SuccessInstance);
        }
        if (input.Value.Monomials.Any(term =>
                term.Symbol is IVariableItem variable && variable.Key == ResultVar.Key)) {
            return IndicatorSupport.InvalidValue();
        }
        IReadOnlyList<LinearInequality<Flt64>> rows = IndicatorSupport.PositiveConstraints(
            polynomial: Polynomial,
            indicator: ResultVar,
            bigM: BigM,
            tolerance: Tolerance,
            name: $"{Name}_bin",
            zero: Flt64.Zero
        );
        Result<SymbolFlatten.LinearFlattenData<Flt64>, ErrorCode, Error<ErrorCode>> upper = rows[0].ToLinearFlattenData(model.Tokens);
        if (!upper.IsOk) {
            return upper.Map(_ => Results.SuccessInstance);
        }
        Result<SymbolFlatten.LinearFlattenData<Flt64>, ErrorCode, Error<ErrorCode>> lower = rows[1].ToLinearFlattenData(model.Tokens);
        if (!lower.IsOk) {
            return lower.Map(_ => Results.SuccessInstance);
        }
        LinearMonomial<Flt64>? upperFlag = upper.Value.Monomials.SingleOrDefault(term =>
            term.Symbol is IVariableItem variable && variable.Key == ResultVar.Key);
        LinearMonomial<Flt64>? lowerFlag = lower.Value.Monomials.SingleOrDefault(term =>
            term.Symbol is IVariableItem variable && variable.Key == ResultVar.Key);
        Flt64 relaxed = BigM.Plus(Tolerance);
        Flt64 upperBoundary = upper.Value.Constant.Negate();
        Flt64 lowerBoundary = lower.Value.Constant.Negate().Plus(relaxed);
        if (upperFlag is null || lowerFlag is null ||
            !upperFlag.Coefficient.Eq(BigM.Negate()) || !lowerFlag.Coefficient.Eq(relaxed.Negate()) ||
            !BigMValues.IsUsable(relaxed) || !BigMValues.IsUsable(upperBoundary) ||
            !BigMValues.IsUsable(lowerBoundary) || !lowerBoundary.Gr(upperBoundary)) {
            return IndicatorSupport.InvalidValue();
        }
        var registered = model.Tokens.Tokens.Select(token => token.Variable.Key).ToHashSet();
        if (upper.Value.Monomials.Concat(lower.Value.Monomials).Any(term =>
                !term.Coefficient.Eq(Flt64.Zero) && term.Symbol is IVariableItem variable &&
                !registered.Contains(variable.Key))) {
            return IndicatorSupport.InvalidValue();
        }
        return IndicatorSupport.AddConstraints(
            model: model,
            constraints: rows,
            afterWrite: () => {
                if (_boundDependencies is { } tracked) {
                    if (!tracked.IsCurrent()) {
                        return IndicatorSupport.InvalidValue();
                    }
                    if (model is BasicMechanismModel<Flt64> mechanism) {
                        mechanism.TrackBoundDependencies(tracked);
                    }
                }
                return Results.OkInstance;
            }
        );
    }
}
