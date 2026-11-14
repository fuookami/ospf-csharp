#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 满足数量计数函数 / Satisfied amount counting function.
/// <para>未指定阈值时结果为满足数量；指定阈值时强制数量达到阈值，模型结果为 1。</para>
/// <para>Without a threshold the result is the count; a threshold enforces the count and yields 1 in feasible models.</para>
/// </summary>
public sealed class SatisfiedAmountFunction :
    IMathFunctionSymbol<Flt64>,
    IHasResultPolynomial<Flt64> {
    private readonly Flt64? _explicitBigM;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<LinearInequality<Flt64>> Inequalities { get; }
    public UInt64? Amount { get; }
    public Flt64 Epsilon { get; }
    /// <summary>等式带外边界 / Outside boundary for equality bands.</summary>
    public Flt64 StrictBoundary { get; }
    public IReadOnlyList<BinVar> FlagVars { get; }
    /// <summary>等式正向带外标志 / Positive outside-band flags for equalities.</summary>
    public IReadOnlyList<BinVar?> EqPositiveVars { get; }
    /// <summary>等式负向带外标志 / Negative outside-band flags for equalities.</summary>
    public IReadOnlyList<BinVar?> EqNegativeVars { get; }
    /// <summary>非零或嵌套比较输入的差值变量 / Residual variables for shifted or nested comparisons.</summary>
    public IReadOnlyList<RealVar?> ResidualVars { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Inequalities);

    /// <summary>计数或已强制成立的阈值结果 / Count or enforced threshold result.</summary>
    public LinearPolynomial<Flt64> ResultPolynomial => Amount is null
        ? new LinearPolynomial<Flt64>(
            FlagVars.Select(flag => new LinearMonomial<Flt64>(Flt64.One, flag)).ToArray(),
            Flt64.Zero
        )
        : new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One);

    /// <summary>创建满足数量函数 / Create a satisfied-amount function.</summary>
    /// <param name="inequalities">待检查的不等式 / Inequalities to check.</param>
    /// <param name="epsilon">非严格比较间隔及等式容差 / Non-strict gap and equality tolerance.</param>
    /// <param name="amount">可选的硬性最低数量 / Optional hard minimum count.</param>
    /// <param name="bigM">可选 Big-M，调用方负责验证其充分性 / Optional Big-M, whose sufficiency is the caller's responsibility.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <param name="strictBoundary">等式带外边界，默认为 epsilon 的两倍 / Equality outside boundary, defaulting to twice epsilon.</param>
    public SatisfiedAmountFunction(
        IReadOnlyList<LinearInequality<Flt64>> inequalities,
        Flt64 epsilon,
        UInt64? amount = null,
        Flt64? bigM = null,
        string name = "satisfied_amount",
        string? displayName = null,
        Flt64? strictBoundary = null) {
        if (inequalities is null) {
            throw new ArgumentNullException(nameof(inequalities));
        }
        if (inequalities.Count == 0 || inequalities.Any(inequality => inequality is null)) {
            throw new ArgumentException(
                "Satisfied amount requires at least one non-null inequality. / 满足数量函数至少需要一个不等式。",
                nameof(inequalities)
            );
        }
        Inequalities = inequalities.ToArray();
        Epsilon = epsilon;
        StrictBoundary = strictBoundary ?? epsilon.Plus(epsilon);
        Amount = amount;
        _explicitBigM = bigM;
        Name = name;
        DisplayName = displayName;
        FlagVars = Enumerable.Range(0, Inequalities.Count)
            .Select(index => new BinVar($"{name}_u_{index}"))
            .ToArray();
        EqPositiveVars = Inequalities
            .Select((inequality, index) => inequality.Comparison == Comparison.EQ
                ? new BinVar($"{name}_eq_positive_{index}")
                : (BinVar?)null)
            .ToArray();
        EqNegativeVars = Inequalities
            .Select((inequality, index) => inequality.Comparison == Comparison.EQ
                ? new BinVar($"{name}_eq_negative_{index}")
                : (BinVar?)null)
            .ToArray();
        ResidualVars = Inequalities
            .Select((inequality, index) => NeedsResidual(inequality)
                ? CreateResidual($"{name}_residual_{index}")
                : null)
            .ToArray();
        _helperVariables = FlagVars
            .Cast<IVariableItem>()
            .Concat(EqPositiveVars.OfType<BinVar>())
            .Concat(EqNegativeVars.OfType<BinVar>())
            .Concat(ResidualVars.OfType<RealVar>())
            .ToArray();
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        if (!BigMValues.IsUsable(Epsilon) || !Epsilon.Gr(Flt64.Zero)) {
            return null;
        }
        int count = 0;
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
                Comparison.LT => lhs.Value.Ls(rhs.Value.Minus(Epsilon)),
                Comparison.GT => lhs.Value.Gr(rhs.Value.Plus(Epsilon)),
                Comparison.NE => difference.Abs().Gr(Epsilon),
                _ => IsSatisfied(inequality.Comparison, difference),
            };
            if (satisfied is null) {
                return null;
            }
            if (satisfied.Value) {
                ++count;
            }
        }
        return Amount is { } threshold
            ? new UInt64(count).Geq(threshold) ? Flt64.One : Flt64.Zero
            : new Flt64(count);
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        if (!BigMValues.IsUsable(Epsilon) || !Epsilon.Gr(Flt64.Zero)) {
            return Invalid("epsilon 必须为有限且大于 0。 / epsilon must be finite and greater than zero.");
        }
        if (_explicitBigM is { } explicitM &&
            (!BigMValues.IsUsable(explicitM) || !explicitM.Gr(Flt64.Zero))) {
            return Invalid("Big-M 必须为有限且大于 0。 / Big-M must be finite and greater than zero.");
        }

        var constraints = new List<LinearInequality<Flt64>>();
        var comparisons = new List<(int Index, Flt64 BigM, Flt64 RelaxedM, Flt64 Constant, BinVar Flag)>();
        var coefficients = new List<(int Index, SymbolFlatten.LinearFlattenData<Flt64> Input, IVariableItem Helper, bool Reversed)>();
        var equalities = new List<(int Index, SymbolFlatten.LinearFlattenData<Flt64> Input, Flt64 Constant, Flt64 BigM, Flt64 OutM, BinVar Flag, BinVar Positive, BinVar Negative)>();
        var boundDependencies = new List<BoundDependencySnapshot<Flt64>>();
        var helpers = HelperVariables.Select(variable => variable.Key).ToHashSet();
        for (int index = 0; index < Inequalities.Count; ++index) {
            LinearInequality<Flt64> inequality = Inequalities[index];
            LinearPolynomial<Flt64> difference = IndicatorSupport.Difference(inequality);
            if (!IsUsable(difference)) {
                return Invalid("差值多项式含非有限或不可编码系数。 / The difference polynomial contains non-finite or unrepresentable coefficients.");
            }
            var functions = new HashSet<IIntermediateSymbol>(ReferenceEqualityComparer.Instance);
            Result<SymbolFlatten.LinearFlattenData<Flt64>, ErrorCode, Error<ErrorCode>> input =
                new SymbolFlatten.LinearFlattenData<Flt64>(difference.Monomials, difference.Constant)
                    .ExpandIntermediateSymbols(tokens: model.Tokens, functions: functions);
            if (!input.IsOk) {
                return input.Map(_ => Results.SuccessInstance);
            }
            if (functions.OfType<LinearFunctionSymbolAdapter<Flt64>>().Any(adapter => ReferenceEquals(adapter.Function, this)) ||
                input.Value.Monomials.Any(term =>
                    term.Symbol is IVariableItem variable && helpers.Contains(variable.Key))) {
                return Invalid("输入不能引用自身函数或辅助变量。 / Input cannot reference the function itself or its helper variables.");
            }
            if (_explicitBigM is null) {
                boundDependencies.Add(new BoundDependencySnapshot<Flt64>(new[] {
                    new LinearPolynomial<Flt64>(input.Value.Monomials, input.Value.Constant),
                }));
            }
            Flt64 bigM = _explicitBigM ?? BigMValues.InferredBigM(
                new LinearPolynomial<Flt64>(input.Value.Monomials, input.Value.Constant),
                IIntoValue<Flt64>.Identity
            ) ?? Flt64.Zero;
            if (!BigMValues.IsUsable(bigM) || !bigM.Gr(Flt64.Zero)) {
                return Invalid("Big-M 必须为有限且大于 0。 / Big-M must be finite and greater than zero.");
            }
            if (inequality.Comparison is Comparison.LE or Comparison.GE or Comparison.EQ &&
                (!ExactBoundValue.TryRead(inequality.Lhs.Constant, out ExactBoundValue lhsConstant) ||
                    !ExactBoundValue.TryRead(inequality.Rhs.Constant, out ExactBoundValue rhsConstant) ||
                    !ExactBoundValue.TryRead(difference.Constant, out ExactBoundValue differenceConstant) ||
                    lhsConstant.Add(rhsConstant.Negate()).CompareTo(differenceConstant) != 0)) {
                return Invalid("比较输入的常量差值不可精确表示。 / The comparison input's constant difference is not exactly representable.");
            }
            Flt64 relaxedM = bigM;
            if (inequality.Comparison is Comparison.LE or Comparison.GE &&
                (!ExactBoundValue.TryRead(bigM, out ExactBoundValue bound) ||
                    !ExactBoundValue.TryRead(Epsilon, out ExactBoundValue gap) ||
                    !bound.Add(gap).TryRoundSolverUpper(IIntoValue<Flt64>.Identity, out relaxedM))) {
                return Invalid("Big-M 与 epsilon 的松弛量不可表示。 / The Big-M and epsilon relaxation is unrepresentable.");
            }

            BinVar flag = FlagVars[index];
            RealVar? residual = ResidualVars[index];
            LinearPolynomial<Flt64> branch = residual is null
                ? difference
                : new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, residual) },
                    Flt64.Zero
                );
            Flt64 branchConstant = residual is null ? input.Value.Constant : Flt64.Zero;
            SymbolFlatten.LinearFlattenData<Flt64> branchInput = residual is null
                ? input.Value
                : new SymbolFlatten.LinearFlattenData<Flt64>(branch.Monomials, branch.Constant);
            switch (inequality.Comparison) {
                case Comparison.LE:
                    comparisons.Add((constraints.Count, bigM, relaxedM, branchConstant, flag));
                    coefficients.Add((constraints.Count, branchInput, flag, false));
                    coefficients.Add((constraints.Count + 1, branchInput, flag, true));
                    // d + M*u <= M / -d - (M+eps)*u <= -eps
                    constraints.Add(IndicatorSupport.Row(
                        monomials: Shift(branch, new LinearMonomial<Flt64>(bigM, flag)),
                        constant: branch.Constant,
                        comparison: Comparison.LE,
                        rhs: bigM,
                        name: $"{Name}_sat_le_ub_{index}"
                    ));
                    constraints.Add(IndicatorSupport.Row(
                        monomials: Shift(Negate(branch), new LinearMonomial<Flt64>(relaxedM.Negate(), flag)),
                        constant: branch.Constant.Negate(),
                        comparison: Comparison.LE,
                        rhs: Epsilon.Negate(),
                        name: $"{Name}_sat_le_lb_{index}"
                    ));
                    break;
                case Comparison.GE:
                    comparisons.Add((constraints.Count, bigM, relaxedM, branchConstant.Negate(), flag));
                    coefficients.Add((constraints.Count, branchInput, flag, true));
                    coefficients.Add((constraints.Count + 1, branchInput, flag, false));
                    // -d + M*u <= M / d - (M+eps)*u <= -eps
                    constraints.Add(IndicatorSupport.Row(
                        monomials: Shift(Negate(branch), new LinearMonomial<Flt64>(bigM, flag)),
                        constant: branch.Constant.Negate(),
                        comparison: Comparison.LE,
                        rhs: bigM,
                        name: $"{Name}_sat_ge_ub_{index}"
                    ));
                    constraints.Add(IndicatorSupport.Row(
                        monomials: Shift(branch, new LinearMonomial<Flt64>(relaxedM.Negate(), flag)),
                        constant: branch.Constant,
                        comparison: Comparison.LE,
                        rhs: Epsilon.Negate(),
                        name: $"{Name}_sat_ge_lb_{index}"
                    ));
                    break;
                case Comparison.EQ:
                    if (EqPositiveVars[index] is not BinVar positive || EqNegativeVars[index] is not BinVar negative) {
                        return Invalid("等式指示缺少符号侧变量。 / The equality indicator is missing its side variable.");
                    }
                    if (!BigMValues.IsUsable(StrictBoundary) || !StrictBoundary.Gr(Epsilon) ||
                        !ExactBoundValue.TryRead(bigM, out ExactBoundValue exactM) ||
                        !ExactBoundValue.TryRead(StrictBoundary, out ExactBoundValue exactStrict) ||
                        !exactM.Add(exactStrict).TryRoundSolverUpper(IIntoValue<Flt64>.Identity, out Flt64 outM)) {
                        return Invalid("等式带外边界或松弛量不可表示。 / The equality outside boundary or relaxation is unrepresentable.");
                    }
                    equalities.Add((constraints.Count, branchInput, branchConstant, bigM, outM, flag, positive, negative));
                    // 独热分支将有效阈值直接放在右侧，避免大数相消 / One-hot branches keep active thresholds on the right, avoiding big-M cancellation.
                    constraints.Add(IndicatorSupport.Row(
                        monomials: Shift(
                            polynomial: branch,
                            indicatorMonomials: new[] {
                                new LinearMonomial<Flt64>(bigM.Negate(), positive),
                                new LinearMonomial<Flt64>(bigM.Negate(), negative),
                            }
                        ),
                        constant: branch.Constant,
                        comparison: Comparison.LE,
                        rhs: Epsilon,
                        name: $"{Name}_sat_eq_{index}_band_ub"
                    ));
                    constraints.Add(IndicatorSupport.Row(
                        monomials: Shift(
                            polynomial: Negate(branch),
                            indicatorMonomials: new[] {
                                new LinearMonomial<Flt64>(bigM.Negate(), positive),
                                new LinearMonomial<Flt64>(bigM.Negate(), negative),
                            }
                        ),
                        constant: branch.Constant.Negate(),
                        comparison: Comparison.LE,
                        rhs: Epsilon,
                        name: $"{Name}_sat_eq_{index}_band_lb"
                    ));
                    constraints.Add(IndicatorSupport.Row(
                        monomials: Shift(
                            polynomial: branch,
                            indicatorMonomials: new[] {
                                new LinearMonomial<Flt64>(outM, flag),
                                new LinearMonomial<Flt64>(outM, negative),
                            }
                        ),
                        constant: branch.Constant,
                        comparison: Comparison.GE,
                        rhs: StrictBoundary,
                        name: $"{Name}_sat_eq_{index}_out_lb"
                    ));
                    constraints.Add(IndicatorSupport.Row(
                        monomials: Shift(
                            polynomial: branch,
                            indicatorMonomials: new[] {
                                new LinearMonomial<Flt64>(outM.Negate(), flag),
                                new LinearMonomial<Flt64>(outM.Negate(), positive),
                            }
                        ),
                        constant: branch.Constant,
                        comparison: Comparison.LE,
                        rhs: StrictBoundary.Negate(),
                        name: $"{Name}_sat_eq_{index}_out_ub"
                    ));
                    constraints.Add(IndicatorSupport.Row(
                        monomials: new[] {
                            new LinearMonomial<Flt64>(Flt64.One, flag),
                            new LinearMonomial<Flt64>(Flt64.One, positive),
                            new LinearMonomial<Flt64>(Flt64.One, negative),
                        },
                        constant: Flt64.Zero,
                        comparison: Comparison.EQ,
                        rhs: Flt64.One,
                        name: $"{Name}_sat_eq_{index}_one_hot"
                    ));
                    break;
                default:
                    return Results.Failed<Success>(
                        new Err<ErrorCode>(
                            ErrorCode.OREngineModelingException,
                            $"SatisfiedAmountFunction 不支持 {inequality.Comparison.Symbol()} 约束。 / SatisfiedAmountFunction does not support {inequality.Comparison.Symbol()} constraints."
                        )
                    );
            }
            if (residual is not null) {
                coefficients.Add((constraints.Count, input.Value, residual, true));
                constraints.Add(IndicatorSupport.Row(
                    monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, residual) }
                        .Concat(input.Value.Monomials.Select(term =>
                            new LinearMonomial<Flt64>(term.Coefficient.Negate(), term.Symbol)))
                        .ToArray(),
                    constant: Flt64.Zero,
                    comparison: Comparison.EQ,
                    rhs: input.Value.Constant,
                    name: $"{Name}_sat_residual_{index}"
                ));
            }
        }

        if (Amount is { } threshold) {
            constraints.Add(IndicatorSupport.Row(
                monomials: FlagVars
                    .Select(flag => new LinearMonomial<Flt64>(Flt64.One, flag))
                    .ToArray(),
                constant: Flt64.Zero,
                comparison: Comparison.GE,
                rhs: RepeatOne(threshold),
                name: $"{Name}_amount"
            ));
        }

        if (constraints.Any(constraint => !IsUsable(constraint.Lhs) || !IsUsable(constraint.Rhs))) {
            return Invalid("生成的满足数量约束含非有限或不可编码系数。 / The generated satisfied-amount constraints contain non-finite or unrepresentable coefficients.");
        }
        var flattened = new List<SymbolFlatten.LinearFlattenData<Flt64>>(constraints.Count);
        var registered = model.Tokens.Tokens.Select(token => token.Variable.Key).ToHashSet();
        foreach (LinearInequality<Flt64> constraint in constraints) {
            Result<SymbolFlatten.LinearFlattenData<Flt64>, ErrorCode, Error<ErrorCode>> row =
                constraint.ToLinearFlattenData(model.Tokens);
            if (!row.IsOk) {
                return row.Map(_ => Results.SuccessInstance);
            }
            if (row.Value.Monomials.Any(term => !term.Coefficient.Eq(Flt64.Zero) &&
                    term.Symbol is IVariableItem variable && !registered.Contains(variable.Key))) {
                return Invalid("约束引用未注册变量。 / Constraint references an unregistered variable.");
            }
            flattened.Add(row.Value);
        }
        foreach ((int index, SymbolFlatten.LinearFlattenData<Flt64> input, IVariableItem helper, bool reversed) in coefficients) {
            if (!MatchesCoefficients(
                    input: input,
                    row: flattened[index],
                    helper: helper,
                    reversed: reversed
                )) {
                return Invalid("满足数量约束的输入系数不可精确合并。 / The satisfied-amount input coefficients cannot be merged exactly.");
            }
        }
        foreach ((int index, Flt64 bigM, Flt64 relaxedM, Flt64 constant, BinVar flag) in comparisons) {
            SymbolFlatten.LinearFlattenData<Flt64> upper = flattened[index];
            SymbolFlatten.LinearFlattenData<Flt64> lower = flattened[index + 1];
            LinearMonomial<Flt64>? upperFlag = upper.Monomials.SingleOrDefault(term =>
                term.Symbol is IVariableItem variable && variable.Key == flag.Key);
            LinearMonomial<Flt64>? lowerFlag = lower.Monomials.SingleOrDefault(term =>
                term.Symbol is IVariableItem variable && variable.Key == flag.Key);
            if (upperFlag is null || lowerFlag is null || !upperFlag.Coefficient.Eq(bigM) ||
                !lowerFlag.Coefficient.Eq(relaxedM.Negate()) ||
                !ExactBoundValue.TryRead(upper.Constant, out ExactBoundValue upperConstant) ||
                !ExactBoundValue.TryRead(lower.Constant, out ExactBoundValue lowerConstant) ||
                !ExactBoundValue.TryRead(bigM, out ExactBoundValue bound) ||
                !ExactBoundValue.TryRead(constant, out ExactBoundValue inputConstant) ||
                !ExactBoundValue.TryRead(Epsilon, out ExactBoundValue gap) ||
                upperConstant.Add(bound).CompareTo(inputConstant) != 0 ||
                lowerConstant.CompareTo(gap.Add(inputConstant.Negate())) != 0) {
                return Invalid("满足数量约束的端点不可精确表示。 / The satisfied-amount constraint endpoints are not exactly representable.");
            }
        }
        foreach ((int index, SymbolFlatten.LinearFlattenData<Flt64> input, Flt64 constant, Flt64 bigM, Flt64 outM, BinVar flag, BinVar positiveFlag, BinVar negativeFlag) in equalities) {
            SymbolFlatten.LinearFlattenData<Flt64> upper = flattened[index];
            SymbolFlatten.LinearFlattenData<Flt64> lower = flattened[index + 1];
            SymbolFlatten.LinearFlattenData<Flt64> positive = flattened[index + 2];
            SymbolFlatten.LinearFlattenData<Flt64> negative = flattened[index + 3];
            SymbolFlatten.LinearFlattenData<Flt64> oneHot = flattened[index + 4];
            if (!HasCoefficient(upper, positiveFlag, bigM.Negate()) || !HasCoefficient(upper, negativeFlag, bigM.Negate()) ||
                !HasCoefficient(lower, positiveFlag, bigM.Negate()) || !HasCoefficient(lower, negativeFlag, bigM.Negate()) ||
                !HasCoefficient(positive, flag, outM) || !HasCoefficient(positive, negativeFlag, outM) ||
                !HasCoefficient(negative, flag, outM.Negate()) || !HasCoefficient(negative, positiveFlag, outM.Negate()) ||
                !HasCoefficient(oneHot, flag, Flt64.One) || !HasCoefficient(oneHot, positiveFlag, Flt64.One) ||
                !HasCoefficient(oneHot, negativeFlag, Flt64.One) || !oneHot.Constant.Eq(Flt64.One.Negate()) ||
                !MatchesCoefficients(
                    input: input,
                    row: upper,
                    helper: positiveFlag,
                    reversed: false,
                    otherHelper: negativeFlag
                ) || !MatchesCoefficients(
                    input: input,
                    row: lower,
                    helper: positiveFlag,
                    reversed: true,
                    otherHelper: negativeFlag
                ) || !MatchesCoefficients(
                    input: input,
                    row: positive,
                    helper: flag,
                    reversed: false,
                    otherHelper: negativeFlag
                ) || !MatchesCoefficients(
                    input: input,
                    row: negative,
                    helper: flag,
                    reversed: false,
                    otherHelper: positiveFlag
                ) ||
                !ExactBoundValue.TryRead(upper.Constant, out ExactBoundValue upperConstant) ||
                !ExactBoundValue.TryRead(lower.Constant, out ExactBoundValue lowerConstant) ||
                !ExactBoundValue.TryRead(positive.Constant, out ExactBoundValue positiveConstant) ||
                !ExactBoundValue.TryRead(negative.Constant, out ExactBoundValue negativeConstant) ||
                !ExactBoundValue.TryRead(constant, out ExactBoundValue inputConstant) ||
                !ExactBoundValue.TryRead(Epsilon, out ExactBoundValue gap) ||
                !ExactBoundValue.TryRead(StrictBoundary, out ExactBoundValue strict) ||
                upperConstant.Add(gap).CompareTo(inputConstant) != 0 ||
                lowerConstant.Add(gap).CompareTo(inputConstant.Negate()) != 0 ||
                positiveConstant.Add(strict).CompareTo(inputConstant) != 0 ||
                negativeConstant.Add(strict.Negate()).CompareTo(inputConstant) != 0) {
                return Invalid("等式约束的端点或系数不可精确表示。 / The equality constraint endpoints or coefficients are not exactly representable.");
            }
        }
        if (boundDependencies.Any(dependencies => !dependencies.IsCurrent())) {
            return Invalid("输入范围已变更或无法跟踪。 / Input bounds changed or cannot be tracked.");
        }
        return IndicatorSupport.AddConstraints(
            model: model,
            constraints: constraints,
            afterWrite: () => {
                if (boundDependencies.Any(dependencies => !dependencies.IsCurrent())) {
                    return Invalid("输入范围已变更或无法跟踪。 / Input bounds changed or cannot be tracked.");
                }
                if (model is BasicMechanismModel<Flt64> mechanism) {
                    foreach (BoundDependencySnapshot<Flt64> dependencies in boundDependencies) {
                        mechanism.TrackBoundDependencies(dependencies);
                    }
                }
                return Results.OkInstance;
            }
        );
    }

    private bool? IsSatisfied(Comparison comparison, Flt64 difference) => comparison switch {
        Comparison.LE => difference.Leq(Flt64.Zero) ? true : difference.Geq(Epsilon) ? false : null,
        Comparison.GE => difference.Geq(Flt64.Zero) ? true : difference.Leq(Epsilon.Negate()) ? false : null,
        Comparison.EQ when BigMValues.IsUsable(StrictBoundary) && StrictBoundary.Gr(Epsilon) =>
            difference.Abs().Leq(Epsilon) ? true : difference.Abs().Geq(StrictBoundary) ? false : null,
        Comparison.EQ => null,
        _ => null,
    };

    internal static bool NeedsResidual(LinearInequality<Flt64> inequality) =>
        inequality.Comparison is Comparison.LE or Comparison.GE or Comparison.EQ &&
        (!inequality.Lhs.Constant.Eq(inequality.Rhs.Constant) ||
            inequality.Lhs.Monomials.Concat(inequality.Rhs.Monomials)
                .Any(term => term.Symbol is not IVariableItem));

    internal static RealVar CreateResidual(string name) {
        var residual = new RealVar(name);
        residual.Range.SetRange(ValueRange<Flt64>.Full());
        return residual;
    }

    internal static bool MatchesCoefficients(
        SymbolFlatten.LinearFlattenData<Flt64> input,
        SymbolFlatten.LinearFlattenData<Flt64> row,
        IVariableItem helper,
        bool reversed,
        IVariableItem? otherHelper = null) {
        var differences = new Dictionary<VariableItemKey, ExactBoundValue>();
        foreach (LinearMonomial<Flt64> term in input.Monomials) {
            if (term.Symbol is not IVariableItem variable || !ExactBoundValue.TryRead(term.Coefficient, out ExactBoundValue value)) {
                return false;
            }
            differences[variable.Key] = (differences.TryGetValue(variable.Key, out ExactBoundValue current)
                ? current : ExactBoundValue.Zero).Add(value);
        }
        foreach (LinearMonomial<Flt64> term in row.Monomials) {
            if (term.Symbol is not IVariableItem variable || !ExactBoundValue.TryRead(term.Coefficient, out ExactBoundValue value)) {
                return false;
            }
            if (variable.Key == helper.Key || variable.Key == otherHelper?.Key) {
                continue;
            }
            differences[variable.Key] = (differences.TryGetValue(variable.Key, out ExactBoundValue current)
                ? current : ExactBoundValue.Zero).Add(reversed ? value : value.Negate());
        }
        return differences.Values.All(value => value.Sign == 0);
    }

    /// <summary>把无符号计数提升为 solver 值 / Lift an unsigned count to a solver value.</summary>
    internal static Flt64 RepeatOne(UInt64 count) => new(count.ToULong());

    private static bool IsUsable(LinearPolynomial<Flt64> polynomial) =>
        BigMValues.IsUsable(polynomial.Constant) &&
        polynomial.Monomials.All(monomial => BigMValues.IsUsable(monomial.Coefficient));

    internal static bool HasCoefficient(
        SymbolFlatten.LinearFlattenData<Flt64> row,
        IVariableItem variable,
        Flt64 coefficient) => row.Monomials.Any(term =>
            term.Symbol is IVariableItem actual && actual.Key == variable.Key && term.Coefficient.Eq(coefficient));

    internal static LinearPolynomial<Flt64> Negate(LinearPolynomial<Flt64> polynomial) =>
        new(
            polynomial.Monomials
                .Select(monomial => new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol))
                .ToArray(),
            polynomial.Constant.Negate()
        );

    private static IReadOnlyList<LinearMonomial<Flt64>> Shift(
        LinearPolynomial<Flt64> polynomial,
        params LinearMonomial<Flt64>[] indicatorMonomials) {
        var monomials = new List<LinearMonomial<Flt64>>(polynomial.Monomials.Count + indicatorMonomials.Length);
        monomials.AddRange(polynomial.Monomials);
        monomials.AddRange(indicatorMonomials);
        return monomials;
    }

    private static Try Invalid(string message) =>
        Results.Failed<Success>(
            new Err<ErrorCode>(ErrorCode.IllegalArgument, message)
        );
}
