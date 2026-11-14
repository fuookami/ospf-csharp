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
/// 逻辑与函数 / Logical AND function
/// </summary>
public sealed class AndFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly Flt64? _explicitBigM;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<LinearPolynomial<Flt64>> Polynomials { get; }
    public BinVar ResultVar { get; }
    public IReadOnlyList<BinVar> IndicatorVars { get; }
    public IReadOnlyList<BinVar> SideVars { get; }
    public IReadOnlyList<BinVar>? BinaryInputs { get; }
    public Flt64 Tolerance { get; }
    public Flt64 StrictBoundary { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomials);
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, ResultVar) },
        Flt64.Zero
    );

    public AndFunction(
        IReadOnlyList<LinearPolynomial<Flt64>> polynomials,
        Flt64? bigM = null,
        Flt64? tolerance = null,
        Flt64? strictBoundary = null,
        string name = "and",
        string? displayName = null) {
        if (polynomials is null) {
            throw new ArgumentNullException(nameof(polynomials));
        }
        if (polynomials.Count == 0) {
            throw new ArgumentException(
                "AND requires at least one input polynomial.",
                nameof(polynomials)
            );
        }
        if (polynomials.Any(polynomial => polynomial is null)) {
            throw new ArgumentException(
                "AND polynomials cannot contain null.",
                nameof(polynomials)
            );
        }
        Polynomials = Array.AsReadOnly(polynomials.Select(IndicatorSupport.CopyInput).ToArray());
        _explicitBigM = bigM;
        Tolerance = tolerance ?? IndicatorSupport.DefaultTolerance;
        StrictBoundary = strictBoundary ?? IndicatorSupport.StrictBoundary;
        Name = name;
        DisplayName = displayName;
        ResultVar = new BinVar($"{name}_and");
        IndicatorVars = Enumerable.Range(0, Polynomials.Count)
            .Select(index => new BinVar($"{name}_and_nz{index}"))
            .ToArray();
        SideVars = Enumerable.Range(0, Polynomials.Count)
            .Select(index => new BinVar($"{name}_and_side{index}"))
            .ToArray();
        BinVar?[] binaryInputs = Polynomials
            .Select(IndicatorSupport.AsBinaryVariable)
            .ToArray();
        BinaryInputs = binaryInputs.All(input => input is not null)
            ? Array.AsReadOnly(binaryInputs.Select(input => input!).ToArray())
            : null;
        _helperVariables = BinaryInputs is null
            ? new[] { (IVariableItem)ResultVar }
                .Concat(IndicatorVars)
                .Concat(SideVars)
                .ToArray()
            : new IVariableItem[] { ResultVar };
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        foreach (LinearPolynomial<Flt64> polynomial in Polynomials) {
            Flt64? value = IndicatorSupport.Evaluate(polynomial, values);
            if (value is null) {
                return null;
            }
            if (value.Value.Eq(Flt64.Zero)) {
                return Flt64.Zero;
            }
        }
        return Flt64.One;
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        if (BinaryInputs is { } inputs) {
            var rows = new List<LinearInequality<Flt64>>(inputs.Count + 1);
            for (int index = 0; index < inputs.Count; ++index) {
                rows.Add(IndicatorSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                        new LinearMonomial<Flt64>(Flt64.One.Negate(), inputs[index]),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.LE,
                    rhs: Flt64.Zero,
                    name: $"{Name}_binary_le_{index}"
                ));
            }
            var sumMonomials = inputs
                .Select(input => new LinearMonomial<Flt64>(Flt64.One, input))
                .ToList();
            sumMonomials.Add(new LinearMonomial<Flt64>(
                Flt64.One.Negate(),
                ResultVar
            ));
            rows.Add(IndicatorSupport.Row(
                monomials: sumMonomials,
                constant: Flt64.Zero,
                comparison: Comparison.LE,
                rhs: new Flt64(inputs.Count - 1),
                name: $"{Name}_binary_sum"
            ));
            return IndicatorSupport.AddCheckedConstraints(
                model: model,
                constraints: rows,
                dependencies: Array.Empty<BoundDependencySnapshot<Flt64>>()
            );
        }

        var constraints = new List<LinearInequality<Flt64>>(
            (Polynomials.Count * 5) + 1
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
                name: $"{Name}_and_nz_{index}"
            ));
        }

        var indicatorSum = IndicatorVars
            .Select(indicator => new LinearMonomial<Flt64>(Flt64.One, indicator))
            .ToList();
        indicatorSum.Add(new LinearMonomial<Flt64>(
            Flt64.One.Negate(),
            ResultVar
        ));
        constraints.Add(IndicatorSupport.Row(
            monomials: indicatorSum,
            constant: Flt64.Zero,
            comparison: Comparison.LE,
            rhs: new Flt64(Polynomials.Count - 1),
            name: $"{Name}_and_sum"
        ));
        for (int index = 0; index < IndicatorVars.Count; ++index) {
            constraints.Add(IndicatorSupport.Row(
                monomials: new[] {
                    new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                    new LinearMonomial<Flt64>(Flt64.One.Negate(), IndicatorVars[index]),
                },
                constant: Flt64.Zero,
                comparison: Comparison.LE,
                rhs: Flt64.Zero,
                name: $"{Name}_and_le_{index}"
            ));
        }
        return IndicatorSupport.AddCheckedConstraints(
            model: model,
            constraints: constraints,
            dependencies: dependencies
        );
    }
}

/// <summary>
/// 逻辑或函数 / Logical OR function
/// </summary>
public sealed class OrFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly Flt64? _explicitBigM;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<LinearPolynomial<Flt64>> Polynomials { get; }
    public BinVar ResultVar { get; }
    public IReadOnlyList<BinVar> IndicatorVars { get; }
    public IReadOnlyList<BinVar> SideVars { get; }
    public IReadOnlyList<BinVar>? BinaryInputs { get; }
    public Flt64 Tolerance { get; }
    public Flt64 StrictBoundary { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomials);
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, ResultVar) },
        Flt64.Zero
    );

    public OrFunction(
        IReadOnlyList<LinearPolynomial<Flt64>> polynomials,
        Flt64? bigM = null,
        Flt64? tolerance = null,
        Flt64? strictBoundary = null,
        string name = "or",
        string? displayName = null) {
        if (polynomials is null) {
            throw new ArgumentNullException(nameof(polynomials));
        }
        if (polynomials.Count == 0) {
            throw new ArgumentException(
                "OR requires at least one input polynomial.",
                nameof(polynomials)
            );
        }
        if (polynomials.Any(polynomial => polynomial is null)) {
            throw new ArgumentException(
                "OR polynomials cannot contain null.",
                nameof(polynomials)
            );
        }
        Polynomials = Array.AsReadOnly(polynomials.Select(IndicatorSupport.CopyInput).ToArray());
        _explicitBigM = bigM;
        Tolerance = tolerance ?? IndicatorSupport.DefaultTolerance;
        StrictBoundary = strictBoundary ?? IndicatorSupport.StrictBoundary;
        Name = name;
        DisplayName = displayName;
        ResultVar = new BinVar($"{name}_or");
        IndicatorVars = Enumerable.Range(0, Polynomials.Count)
            .Select(index => new BinVar($"{name}_or_nz{index}"))
            .ToArray();
        SideVars = Enumerable.Range(0, Polynomials.Count)
            .Select(index => new BinVar($"{name}_or_side{index}"))
            .ToArray();
        BinVar?[] binaryInputs = Polynomials
            .Select(IndicatorSupport.AsBinaryVariable)
            .ToArray();
        BinaryInputs = binaryInputs.All(input => input is not null)
            ? Array.AsReadOnly(binaryInputs.Select(input => input!).ToArray())
            : null;
        _helperVariables = BinaryInputs is null
            ? new[] { (IVariableItem)ResultVar }
                .Concat(IndicatorVars)
                .Concat(SideVars)
                .ToArray()
            : new IVariableItem[] { ResultVar };
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        foreach (LinearPolynomial<Flt64> polynomial in Polynomials) {
            Flt64? value = IndicatorSupport.Evaluate(polynomial, values);
            if (value is null) {
                return null;
            }
            if (!value.Value.Eq(Flt64.Zero)) {
                return Flt64.One;
            }
        }
        return Flt64.Zero;
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        if (BinaryInputs is { } inputs) {
            var rows = new List<LinearInequality<Flt64>>(inputs.Count + 1);
            for (int index = 0; index < inputs.Count; ++index) {
                rows.Add(IndicatorSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                        new LinearMonomial<Flt64>(Flt64.One.Negate(), inputs[index]),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.GE,
                    rhs: Flt64.Zero,
                    name: $"{Name}_binary_ge_{index}"
                ));
            }
            var sumMonomials = new List<LinearMonomial<Flt64>> {
                new(Flt64.One, ResultVar),
            };
            sumMonomials.AddRange(inputs.Select(input =>
                new LinearMonomial<Flt64>(Flt64.One.Negate(), input)));
            rows.Add(IndicatorSupport.Row(
                monomials: sumMonomials,
                constant: Flt64.Zero,
                comparison: Comparison.LE,
                rhs: Flt64.Zero,
                name: $"{Name}_binary_sum"
            ));
            return IndicatorSupport.AddCheckedConstraints(
                model: model,
                constraints: rows,
                dependencies: Array.Empty<BoundDependencySnapshot<Flt64>>()
            );
        }

        var constraints = new List<LinearInequality<Flt64>>(
            (Polynomials.Count * 5) + 1
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
                name: $"{Name}_or_nz_{index}"
            ));
        }

        var indicatorSum = IndicatorVars
            .Select(indicator => new LinearMonomial<Flt64>(Flt64.One, indicator))
            .ToList();
        indicatorSum.Add(new LinearMonomial<Flt64>(Flt64.One.Negate(), ResultVar));
        constraints.Add(IndicatorSupport.Row(
            monomials: indicatorSum,
            constant: Flt64.Zero,
            comparison: Comparison.GE,
            rhs: Flt64.Zero,
            name: $"{Name}_or_sum"
        ));
        for (int index = 0; index < IndicatorVars.Count; ++index) {
            constraints.Add(IndicatorSupport.Row(
                monomials: new[] {
                    new LinearMonomial<Flt64>(Flt64.One, IndicatorVars[index]),
                    new LinearMonomial<Flt64>(Flt64.One.Negate(), ResultVar),
                },
                constant: Flt64.Zero,
                comparison: Comparison.LE,
                rhs: Flt64.Zero,
                name: $"{Name}_or_ge_{index}"
            ));
        }
        return IndicatorSupport.AddCheckedConstraints(
            model: model,
            constraints: constraints,
            dependencies: dependencies
        );
    }
}

/// <summary>
/// 逻辑非函数 / Logical NOT function
/// </summary>
public sealed class NotFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly Flt64? _explicitBigM;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public LinearPolynomial<Flt64> Polynomial { get; }
    public BinVar ResultVar { get; }
    public BinVar IndicatorVar { get; }
    public BinVar SideVar { get; }
    public BinVar? BinaryInput { get; }
    public Flt64 Tolerance { get; }
    public Flt64 StrictBoundary { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomial);
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, ResultVar) },
        Flt64.Zero
    );

    public NotFunction(
        LinearPolynomial<Flt64> polynomial,
        Flt64? bigM = null,
        Flt64? tolerance = null,
        Flt64? strictBoundary = null,
        string name = "not",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(polynomial);
        Polynomial = IndicatorSupport.CopyInput(polynomial);
        _explicitBigM = bigM;
        Tolerance = tolerance ?? IndicatorSupport.DefaultTolerance;
        StrictBoundary = strictBoundary ?? IndicatorSupport.StrictBoundary;
        Name = name;
        DisplayName = displayName;
        IndicatorVar = new BinVar($"{name}_not_nz");
        SideVar = new BinVar($"{name}_not_side");
        ResultVar = new BinVar($"{name}_not");
        BinaryInput = IndicatorSupport.AsBinaryVariable(Polynomial);
        _helperVariables = BinaryInput is null
            ? new IVariableItem[] { IndicatorVar, SideVar, ResultVar }
            : new IVariableItem[] { ResultVar };
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        Flt64? value = IndicatorSupport.Evaluate(Polynomial, values);
        return value is null
            ? null
            : value.Value.Eq(Flt64.Zero) ? Flt64.One : Flt64.Zero;
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        if (BinaryInput is { } input) {
            return IndicatorSupport.AddCheckedConstraints(
                model: model,
                constraints: new[] {
                    IndicatorSupport.Row(
                        monomials: new[] {
                            new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                            new LinearMonomial<Flt64>(Flt64.One, input),
                        },
                        constant: Flt64.Zero,
                        comparison: Comparison.EQ,
                        rhs: Flt64.One,
                        name: $"{Name}_binary_result"
                    ),
                },
                dependencies: Array.Empty<BoundDependencySnapshot<Flt64>>()
            );
        }

        Try prepared = IndicatorSupport.PrepareInput(
            model: model,
            function: this,
            polynomial: Polynomial,
            explicitBigM: _explicitBigM,
            input: out LinearPolynomial<Flt64> expanded,
            bigM: out Flt64 bigM,
            dependencies: out BoundDependencySnapshot<Flt64>? dependency
        );
        if (prepared.IsFailed) {
            return prepared;
        }
        Try valid = IndicatorSupport.Validate(
            polynomial: expanded,
            bigM: bigM,
            tolerance: Tolerance,
            strictBoundary: StrictBoundary
        );
        if (valid.IsFailed) {
            return valid;
        }
        var constraints = new List<LinearInequality<Flt64>>(
            IndicatorSupport.NonzeroConstraints(
                polynomial: expanded,
                indicator: IndicatorVar,
                side: SideVar,
                bigM: bigM,
                tolerance: Tolerance,
                strictBoundary: StrictBoundary,
                name: $"{Name}_not_nz"
            )
        ) {
            IndicatorSupport.Row(
                monomials: new[] {
                    new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                    new LinearMonomial<Flt64>(Flt64.One, IndicatorVar),
                },
                constant: Flt64.Zero,
                comparison: Comparison.EQ,
                rhs: Flt64.One,
                name: $"{Name}_not_result"
            ),
        };
        return IndicatorSupport.AddCheckedConstraints(
            model: model,
            constraints: constraints,
            dependencies: dependency is null ? Array.Empty<BoundDependencySnapshot<Flt64>>() : new[] { dependency }
        );
    }
}

/// <summary>
/// 逻辑异或函数 / Logical XOR function
/// </summary>
public sealed class XorFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly Flt64? _explicitBigM;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<LinearPolynomial<Flt64>> Polynomials { get; }
    public BinVar ResultVar { get; }
    public IReadOnlyList<BinVar> IndicatorVars { get; }
    public IReadOnlyList<BinVar> SideVars { get; }
    public Flt64 Tolerance { get; }
    public Flt64 StrictBoundary { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomials);
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, ResultVar) },
        Flt64.Zero
    );

    public XorFunction(
        IReadOnlyList<LinearPolynomial<Flt64>> polynomials,
        Flt64? bigM = null,
        Flt64? tolerance = null,
        Flt64? strictBoundary = null,
        string name = "xor",
        string? displayName = null) {
        if (polynomials is null) {
            throw new ArgumentNullException(nameof(polynomials));
        }
        if (polynomials.Count == 0) {
            throw new ArgumentException(
                "XOR requires at least one input polynomial.",
                nameof(polynomials)
            );
        }
        if (polynomials.Any(polynomial => polynomial is null)) {
            throw new ArgumentException(
                "XOR polynomials cannot contain null.",
                nameof(polynomials)
            );
        }
        Polynomials = Array.AsReadOnly(polynomials.Select(IndicatorSupport.CopyInput).ToArray());
        _explicitBigM = bigM;
        Tolerance = tolerance ?? IndicatorSupport.DefaultTolerance;
        StrictBoundary = strictBoundary ?? IndicatorSupport.StrictBoundary;
        if (Tolerance.Ls(Flt64.Zero) || !StrictBoundary.Gr(Tolerance)) {
            throw new ArgumentException(
                "XOR tolerance must be nonnegative and strict boundary must exceed it.",
                nameof(strictBoundary)
            );
        }
        Name = name;
        DisplayName = displayName;
        ResultVar = new BinVar($"{name}_xor");
        IndicatorVars = Enumerable.Range(0, Polynomials.Count)
            .Select(index => new BinVar($"{name}_xor_nz{index}"))
            .ToArray();
        SideVars = Enumerable.Range(0, Polynomials.Count)
            .Select(index => new BinVar($"{name}_xor_side{index}"))
            .ToArray();
        _helperVariables = new[] { (IVariableItem)ResultVar }
            .Concat(IndicatorVars)
            .Concat(SideVars)
            .ToArray();
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        int count = 0;
        foreach (LinearPolynomial<Flt64> polynomial in Polynomials) {
            Flt64? value = IndicatorSupport.Evaluate(polynomial, values);
            if (value is null) {
                return null;
            }
            Flt64 magnitude = value.Value.Abs();
            if (magnitude.Ls(StrictBoundary) && magnitude.Gr(Tolerance)) {
                return null;
            }
            if (magnitude.Geq(StrictBoundary)) {
                ++count;
            }
        }
        return count == 1 ? Flt64.One : Flt64.Zero;
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        var constraints = new List<LinearInequality<Flt64>>(
            (Polynomials.Count * 4) +
            1 +
            Polynomials.Count +
            ((Polynomials.Count * (Polynomials.Count - 1)) / 2)
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
                name: $"{Name}_xor_nz_{index}"
            ));
        }

        var sumUpper = new List<LinearMonomial<Flt64>> {
            new(Flt64.One, ResultVar),
        };
        sumUpper.AddRange(IndicatorVars.Select(indicator =>
            new LinearMonomial<Flt64>(Flt64.One.Negate(), indicator)));
        constraints.Add(IndicatorSupport.Row(
            monomials: sumUpper,
            constant: Flt64.Zero,
            comparison: Comparison.LE,
            rhs: Flt64.Zero,
            name: $"{Name}_xor_sum_ub"
        ));

        for (int index = 0; index < IndicatorVars.Count; ++index) {
            var single = new List<LinearMonomial<Flt64>> {
                new(Flt64.One, ResultVar),
            };
            for (int other = 0; other < IndicatorVars.Count; ++other) {
                single.Add(new LinearMonomial<Flt64>(
                    other == index ? Flt64.One.Negate() : Flt64.One,
                    IndicatorVars[other]
                ));
            }
            constraints.Add(IndicatorSupport.Row(
                monomials: single,
                constant: Flt64.Zero,
                comparison: Comparison.GE,
                rhs: Flt64.Zero,
                name: $"{Name}_xor_single_{index}"
            ));
        }

        for (int first = 0; first < IndicatorVars.Count; ++first) {
            for (int second = first + 1; second < IndicatorVars.Count; ++second) {
                constraints.Add(IndicatorSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                        new LinearMonomial<Flt64>(Flt64.One, IndicatorVars[first]),
                        new LinearMonomial<Flt64>(Flt64.One, IndicatorVars[second]),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.LE,
                    rhs: new Flt64(2.0),
                    name: $"{Name}_xor_pair_{first}_{second}"
                ));
            }
        }
        return IndicatorSupport.AddCheckedConstraints(
            model: model,
            constraints: constraints,
            dependencies: dependencies
        );
    }
}
