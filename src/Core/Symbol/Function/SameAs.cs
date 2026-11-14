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
/// 相同满足状态函数 / Same satisfaction-status function
/// <para>非严格比较和等式在容差与违反边界之间返回空值。 / Non-strict comparisons and equalities return null between tolerance and violation boundaries.</para>
/// </summary>
public sealed class SameAsFunction :
    IMathFunctionSymbol<Flt64>,
    IHasResultPolynomial<Flt64> {
    private readonly Flt64? _explicitBigM;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<LinearInequality<Flt64>> Inequalities { get; }
    public bool Constraint { get; }
    public Flt64 Epsilon { get; }
    /// <summary>违反状态边界 / Boundary of the violated state.</summary>
    public Flt64 StrictBoundary { get; }
    public BinVar ResultVar { get; }
    public IReadOnlyList<BinVar> SatisfactionFlags { get; }
    public IReadOnlyList<BinVar> DiffVars { get; }
    /// <summary>非严格不等式的违反标志 / Violation flags for non-strict inequalities.</summary>
    public IReadOnlyList<BinVar?> ViolationFlags { get; }
    /// <summary>等式正向带外标志 / Positive outside-band equality flags.</summary>
    public IReadOnlyList<BinVar?> EqPositiveVars { get; }
    /// <summary>等式负向带外标志 / Negative outside-band equality flags.</summary>
    public IReadOnlyList<BinVar?> EqNegativeVars { get; }
    /// <summary>偏移或嵌套输入的差值变量 / Residual variables for shifted or nested inputs.</summary>
    public IReadOnlyList<RealVar?> ResidualVars { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Inequalities);
    public LinearPolynomial<Flt64> ResultPolynomial => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, ResultVar) },
        Flt64.Zero
    );

    /// <summary>创建相同状态函数 / Create a same-status function.</summary>
    /// <param name="inequalities">待比较的不等式 / Inequalities to compare.</param>
    /// <param name="epsilon">满足状态的容差 / Tolerance for the satisfied state.</param>
    /// <param name="constraint">是否强制状态相同 / Whether equal states are enforced.</param>
    /// <param name="bigM">可选 Big-M，调用方负责充分性 / Optional Big-M, whose sufficiency is the caller's responsibility.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <param name="strictBoundary">违反状态边界，默认为两倍 epsilon / Violation boundary, defaulting to twice epsilon.</param>
    public SameAsFunction(
        IReadOnlyList<LinearInequality<Flt64>> inequalities,
        Flt64 epsilon,
        bool constraint = true,
        Flt64? bigM = null,
        string name = "sameAs",
        string? displayName = null,
        Flt64? strictBoundary = null) {
        if (inequalities is null) {
            throw new ArgumentNullException(nameof(inequalities));
        }
        if (inequalities.Count == 0 || inequalities.Any(inequality => inequality is null)) {
            throw new ArgumentException(
                "Same-as requires at least one non-null inequality.",
                nameof(inequalities)
            );
        }
        Inequalities = Array.AsReadOnly(inequalities.Select(inequality => inequality with {
            Lhs = IndicatorSupport.CopyInput(inequality.Lhs),
            Rhs = IndicatorSupport.CopyInput(inequality.Rhs),
        }).ToArray());
        Constraint = constraint;
        Epsilon = epsilon;
        StrictBoundary = strictBoundary ?? epsilon.Plus(epsilon);
        _explicitBigM = bigM;
        Name = name;
        DisplayName = displayName;
        ResultVar = new BinVar($"{name}_same");
        SatisfactionFlags = Enumerable.Range(0, Inequalities.Count)
            .Select(index => new BinVar($"{name}_u{index}"))
            .ToArray();
        DiffVars = !constraint && Inequalities.Count > 1
            ? Enumerable.Range(1, Inequalities.Count - 1)
                .Select(index => new BinVar($"{name}_diff{index}"))
                .ToArray()
            : Array.Empty<BinVar>();
        ViolationFlags = Inequalities
            .Select((inequality, index) =>
                inequality.Comparison is Comparison.LE or Comparison.GE
                    ? new BinVar($"{name}_violated{index}")
                    : null
            )
            .ToArray();
        EqPositiveVars = Inequalities
            .Select((inequality, index) =>
                inequality.Comparison == Comparison.EQ
                    ? new BinVar($"{name}_eq_positive{index}")
                    : null
            )
            .ToArray();
        EqNegativeVars = Inequalities
            .Select((inequality, index) =>
                inequality.Comparison == Comparison.EQ
                    ? new BinVar($"{name}_eq_negative{index}")
                    : null
            )
            .ToArray();
        ResidualVars = Inequalities
            .Select((inequality, index) => SatisfiedAmountFunction.NeedsResidual(inequality)
                ? SatisfiedAmountFunction.CreateResidual($"{name}_residual{index}")
                : null)
            .ToArray();
        _helperVariables = new[] { (IVariableItem)ResultVar }
            .Concat(SatisfactionFlags)
            .Concat(DiffVars)
            .Concat(ViolationFlags.OfType<BinVar>())
            .Concat(EqPositiveVars.OfType<BinVar>())
            .Concat(EqNegativeVars.OfType<BinVar>())
            .Concat(ResidualVars.OfType<RealVar>())
            .ToArray();
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        if (!ValidBoundaries()) {
            return null;
        }
        bool? firstSatisfied = null;
        bool allSame = true;
        foreach (LinearInequality<Flt64> inequality in Inequalities) {
            Flt64? lhs = IndicatorSupport.Evaluate(inequality.Lhs, values);
            Flt64? rhs = IndicatorSupport.Evaluate(inequality.Rhs, values);
            if (lhs is null || rhs is null || !BigMValues.IsUsable(lhs.Value) || !BigMValues.IsUsable(rhs.Value)) {
                return null;
            }
            Flt64 difference = lhs.Value.Minus(rhs.Value);
            if (!BigMValues.IsUsable(difference)) {
                return null;
            }
            bool? satisfied = inequality.Comparison switch {
                Comparison.LE => difference.Leq(Epsilon) ? true : difference.Geq(StrictBoundary) ? false : null,
                Comparison.LT => difference.Ls(Epsilon),
                Comparison.GE => difference.Geq(Epsilon.Negate()) ? true : difference.Leq(StrictBoundary.Negate()) ? false : null,
                Comparison.GT => difference.Gr(Epsilon.Negate()),
                Comparison.EQ => difference.Abs().Leq(Epsilon) ? true : difference.Abs().Geq(StrictBoundary) ? false : null,
                Comparison.NE => difference.Abs().Gr(Epsilon),
                _ => null,
            };
            if (satisfied is null) {
                return null;
            }
            if (firstSatisfied is null) {
                firstSatisfied = satisfied.Value;
            } else if (firstSatisfied.Value != satisfied.Value) {
                allSame = false;
            }
        }
        return allSame ? Flt64.One : Flt64.Zero;
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        if (!ValidBoundaries()) {
            return InvalidValue();
        }

        var constraints = new List<LinearInequality<Flt64>>();
        var dependencies = new List<BoundDependencySnapshot<Flt64>>();
        for (int index = 0; index < Inequalities.Count; ++index) {
            LinearInequality<Flt64> inequality = Inequalities[index];
            if (inequality.Comparison is not (Comparison.LE or Comparison.GE or Comparison.EQ)) {
                return Unsupported(inequality.Comparison);
            }
            LinearPolynomial<Flt64> originalDifference = IndicatorSupport.Difference(inequality);
            if (!ExactBoundValue.TryRead(inequality.Lhs.Constant, out ExactBoundValue lhsConstant) ||
                !ExactBoundValue.TryRead(inequality.Rhs.Constant, out ExactBoundValue rhsConstant) ||
                !ExactBoundValue.TryRead(originalDifference.Constant, out ExactBoundValue differenceConstant) ||
                lhsConstant.Add(rhsConstant.Negate()).CompareTo(differenceConstant) != 0) {
                return InvalidValue();
            }
            Try prepared = IndicatorSupport.PrepareInput(
                model: model,
                function: this,
                polynomial: originalDifference,
                explicitBigM: _explicitBigM,
                input: out LinearPolynomial<Flt64> difference,
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
                polynomial: difference,
                bigM: bigM,
                tolerance: Epsilon,
                strictBoundary: StrictBoundary
            );
            if (valid.IsFailed) {
                return valid;
            }

            Try appended = AppendIndicatorRows(
                model: model,
                constraints: constraints,
                index: index,
                difference: difference,
                bigM: bigM
            );
            if (appended.IsFailed) {
                return appended;
            }
        }

        if (Constraint) {
            for (int index = 1; index < SatisfactionFlags.Count; ++index) {
                constraints.Add(IndicatorSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, SatisfactionFlags[0]),
                        new LinearMonomial<Flt64>(Flt64.One.Negate(), SatisfactionFlags[index]),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.EQ,
                    rhs: Flt64.Zero,
                    name: $"{Name}_equal_{index}"
                ));
            }
            constraints.Add(IndicatorSupport.Row(
                monomials: new[] {
                    new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                },
                constant: Flt64.Zero,
                comparison: Comparison.EQ,
                rhs: Flt64.One,
                name: $"{Name}_result_link"
            ));
        } else if (SatisfactionFlags.Count == 1) {
            constraints.Add(IndicatorSupport.Row(
                monomials: new[] {
                    new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                },
                constant: Flt64.Zero,
                comparison: Comparison.EQ,
                rhs: Flt64.One,
                name: $"{Name}_result_single"
            ));
        } else {
            for (int index = 1; index < SatisfactionFlags.Count; ++index) {
                BinVar differenceVar = DiffVars[index - 1];
                BinVar first = SatisfactionFlags[0];
                BinVar current = SatisfactionFlags[index];
                constraints.Add(IndicatorSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, differenceVar),
                        new LinearMonomial<Flt64>(Flt64.One.Negate(), current),
                        new LinearMonomial<Flt64>(Flt64.One, first),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.GE,
                    rhs: Flt64.Zero,
                    name: $"{Name}_diff_ge_{index}"
                ));
                constraints.Add(IndicatorSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, differenceVar),
                        new LinearMonomial<Flt64>(Flt64.One, current),
                        new LinearMonomial<Flt64>(Flt64.One.Negate(), first),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.GE,
                    rhs: Flt64.Zero,
                    name: $"{Name}_diff_le_{index}"
                ));
                constraints.Add(IndicatorSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, differenceVar),
                        new LinearMonomial<Flt64>(Flt64.One.Negate(), current),
                        new LinearMonomial<Flt64>(Flt64.One.Negate(), first),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.LE,
                    rhs: Flt64.Zero,
                    name: $"{Name}_diff_sum_ub_{index}"
                ));
                constraints.Add(IndicatorSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, differenceVar),
                        new LinearMonomial<Flt64>(Flt64.One, current),
                        new LinearMonomial<Flt64>(Flt64.One, first),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.LE,
                    rhs: new Flt64(2.0),
                    name: $"{Name}_diff_sum_lb_{index}"
                ));
                constraints.Add(IndicatorSupport.Row(
                    monomials: new[] {
                        new LinearMonomial<Flt64>(Flt64.One, ResultVar),
                        new LinearMonomial<Flt64>(Flt64.One, differenceVar),
                    },
                    constant: Flt64.Zero,
                    comparison: Comparison.LE,
                    rhs: Flt64.One,
                    name: $"{Name}_result_diff_{index}"
                ));
            }
            var resultMonomials = new List<LinearMonomial<Flt64>>(DiffVars.Count + 1) {
                new(Flt64.One, ResultVar),
            };
            resultMonomials.AddRange(DiffVars.Select(variable =>
                new LinearMonomial<Flt64>(Flt64.One, variable)));
            constraints.Add(IndicatorSupport.Row(
                monomials: resultMonomials,
                constant: Flt64.Zero,
                comparison: Comparison.GE,
                rhs: Flt64.One,
                name: $"{Name}_result_sum"
            ));
        }
        return IndicatorSupport.AddCheckedConstraints(
            model: model,
            constraints: constraints,
            dependencies: dependencies
        );
    }

    private Try AppendIndicatorRows(
        IAbstractLinearMechanismModel<Flt64> model,
        List<LinearInequality<Flt64>> constraints,
        int index,
        LinearPolynomial<Flt64> difference,
        Flt64 bigM) {
        if (!ExactBoundValue.TryRead(bigM, out ExactBoundValue bound) ||
            !ExactBoundValue.TryRead(StrictBoundary, out ExactBoundValue strict) ||
            !bound.Add(strict).TryRoundSolverUpper(IIntoValue<Flt64>.Identity, out Flt64 outM) ||
            !BigMValues.IsUsable(outM)) {
            return InvalidValue();
        }
        RealVar? residual = ResidualVars[index];
        LinearPolynomial<Flt64> input = residual is null
            ? difference
            : new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, residual) },
                Flt64.Zero
            );
        BinVar satisfied = SatisfactionFlags[index];
        string name = $"{Name}_ineq_{index}";
        // 独热标志避免有效阈值与大 M 相消 / One-hot flags keep active thresholds free of big-M cancellation.
        var rows = new List<(LinearInequality<Flt64> Row, bool Reversed, IVariableItem Helper, Flt64 Coefficient, IVariableItem? Other, Flt64 OtherCoefficient)>();
        IReadOnlyList<LinearMonomial<Flt64>> oneHot;
        if (Inequalities[index].Comparison is Comparison.LE or Comparison.GE) {
            if (ViolationFlags[index] is not BinVar violated) {
                return InvalidValue();
            }
            bool reversed = Inequalities[index].Comparison == Comparison.GE;
            LinearPolynomial<Flt64> oriented = reversed ? SatisfiedAmountFunction.Negate(input) : input;
            rows.Add((IndicatorSupport.Row(
                monomials: Shift(oriented, new LinearMonomial<Flt64>(bigM.Negate(), violated)),
                constant: oriented.Constant,
                comparison: Comparison.LE,
                rhs: Epsilon,
                name: $"{name}_sat"
            ), reversed, violated, bigM.Negate(), null, Flt64.Zero));
            rows.Add((IndicatorSupport.Row(
                monomials: Shift(oriented, new LinearMonomial<Flt64>(outM, satisfied)),
                constant: oriented.Constant,
                comparison: Comparison.GE,
                rhs: StrictBoundary,
                name: $"{name}_violated"
            ), reversed, satisfied, outM, null, Flt64.Zero));
            oneHot = new[] {
                new LinearMonomial<Flt64>(Flt64.One, satisfied),
                new LinearMonomial<Flt64>(Flt64.One, violated),
            };
        }
        else {
            if (EqPositiveVars[index] is not BinVar positive || EqNegativeVars[index] is not BinVar negative) {
                return InvalidValue();
            }
            LinearPolynomial<Flt64> reversed = SatisfiedAmountFunction.Negate(input);
            rows.Add((IndicatorSupport.Row(
                monomials: Shift(
                    polynomial: input,
                    additional: new[] {
                        new LinearMonomial<Flt64>(bigM.Negate(), positive),
                        new LinearMonomial<Flt64>(bigM.Negate(), negative),
                    }
                ),
                constant: input.Constant,
                comparison: Comparison.LE,
                rhs: Epsilon,
                name: $"{name}_zero_band_ub"
            ), false, positive, bigM.Negate(), negative, bigM.Negate()));
            rows.Add((IndicatorSupport.Row(
                monomials: Shift(
                    polynomial: reversed,
                    additional: new[] {
                        new LinearMonomial<Flt64>(bigM.Negate(), positive),
                        new LinearMonomial<Flt64>(bigM.Negate(), negative),
                    }
                ),
                constant: reversed.Constant,
                comparison: Comparison.LE,
                rhs: Epsilon,
                name: $"{name}_zero_band_lb"
            ), true, positive, bigM.Negate(), negative, bigM.Negate()));
            rows.Add((IndicatorSupport.Row(
                monomials: Shift(
                    polynomial: input,
                    additional: new[] {
                        new LinearMonomial<Flt64>(outM, satisfied),
                        new LinearMonomial<Flt64>(outM, negative),
                    }
                ),
                constant: input.Constant,
                comparison: Comparison.GE,
                rhs: StrictBoundary,
                name: $"{name}_zero_out_lb"
            ), false, satisfied, outM, negative, outM));
            rows.Add((IndicatorSupport.Row(
                monomials: Shift(
                    polynomial: input,
                    additional: new[] {
                        new LinearMonomial<Flt64>(outM.Negate(), satisfied),
                        new LinearMonomial<Flt64>(outM.Negate(), positive),
                    }
                ),
                constant: input.Constant,
                comparison: Comparison.LE,
                rhs: StrictBoundary.Negate(),
                name: $"{name}_zero_out_ub"
            ), false, satisfied, outM.Negate(), positive, outM.Negate()));
            oneHot = new[] {
                new LinearMonomial<Flt64>(Flt64.One, satisfied),
                new LinearMonomial<Flt64>(Flt64.One, positive),
                new LinearMonomial<Flt64>(Flt64.One, negative),
            };
        }
        foreach ((LinearInequality<Flt64> row, bool reversed, IVariableItem helper, Flt64 coefficient, IVariableItem? other, Flt64 otherCoefficient) in rows) {
            Try valid = CheckIndicatorRow(
                model: model,
                row: row,
                input: input,
                reversed: reversed,
                helper: helper,
                coefficient: coefficient,
                other: other,
                otherCoefficient: otherCoefficient
            );
            if (valid.IsFailed) {
                return valid;
            }
            constraints.Add(row);
        }
        constraints.Add(IndicatorSupport.Row(
            monomials: oneHot,
            constant: Flt64.Zero,
            comparison: Comparison.EQ,
            rhs: Flt64.One,
            name: $"{name}_one_hot"
        ));
        if (residual is not null) {
            LinearInequality<Flt64> residualRow = IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, residual) }
                    .Concat(difference.Monomials.Select(term =>
                        new LinearMonomial<Flt64>(term.Coefficient.Negate(), term.Symbol)))
                    .ToArray(),
                constant: Flt64.Zero,
                comparison: Comparison.EQ,
                rhs: difference.Constant,
                name: $"{name}_residual"
            );
            Try valid = CheckIndicatorRow(
                model: model,
                row: residualRow,
                input: new LinearPolynomial<Flt64>(difference.Monomials, Flt64.Zero),
                reversed: true,
                helper: residual,
                coefficient: Flt64.One
            );
            if (valid.IsFailed) {
                return valid;
            }
            constraints.Add(residualRow);
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static Try CheckIndicatorRow(
        IAbstractLinearMechanismModel<Flt64> model,
        LinearInequality<Flt64> row,
        LinearPolynomial<Flt64> input,
        bool reversed,
        IVariableItem helper,
        Flt64 coefficient,
        IVariableItem? other = null,
        Flt64 otherCoefficient = default) {
        Result<SymbolFlatten.LinearFlattenData<Flt64>, ErrorCode, Error<ErrorCode>> flattened =
            row.ToLinearFlattenData(model.Tokens);
        if (!flattened.IsOk) {
            return flattened.Map(_ => Results.SuccessInstance);
        }
        if (!SatisfiedAmountFunction.HasCoefficient(
                row: flattened.Value,
                variable: helper,
                coefficient: coefficient
            ) || (other is not null && !SatisfiedAmountFunction.HasCoefficient(
                row: flattened.Value,
                variable: other,
                coefficient: otherCoefficient
            )) ||
            !SatisfiedAmountFunction.MatchesCoefficients(
                input: new SymbolFlatten.LinearFlattenData<Flt64>(input.Monomials, input.Constant),
                row: flattened.Value,
                helper: helper,
                reversed: reversed,
                otherHelper: other
            ) ||
            !ExactBoundValue.TryRead(input.Constant, out ExactBoundValue inputConstant) ||
            !ExactBoundValue.TryRead(row.Rhs.Constant, out ExactBoundValue rhs) ||
            !ExactBoundValue.TryRead(flattened.Value.Constant, out ExactBoundValue actual) ||
            (reversed ? inputConstant.Negate() : inputConstant).Add(rhs.Negate()).CompareTo(actual) != 0) {
            return InvalidValue();
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private bool ValidBoundaries() =>
        BigMValues.IsUsable(Epsilon) && Epsilon.Gr(Flt64.Zero) &&
        BigMValues.IsUsable(StrictBoundary) && StrictBoundary.Gr(Epsilon);

    private static Try Unsupported(Comparison comparison) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            $"Same-as indicator registration does not support {comparison}. / 相同满足状态指示注册不支持该比较关系。"
        ));

    private static IReadOnlyList<LinearMonomial<Flt64>> Shift(
        LinearPolynomial<Flt64> polynomial,
        params LinearMonomial<Flt64>[] additional) {
        var monomials = new List<LinearMonomial<Flt64>>(
            polynomial.Monomials.Count + additional.Length
        );
        monomials.AddRange(polynomial.Monomials);
        monomials.AddRange(additional);
        return monomials;
    }

    private static Try InvalidValue() =>
        Results.Failed<Success>(
            new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "相同满足状态的参数、边界或约束无法精确编码。 / Same-as parameters, boundaries, or rows cannot be encoded safely."
            )
        );
}
