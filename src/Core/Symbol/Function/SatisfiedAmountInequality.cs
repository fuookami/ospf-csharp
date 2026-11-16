#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
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
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 约束满足数量函数 / Satisfied amount over constraint inputs.
/// <para>对应 Kotlin <c>SatisfiedAmountInequalityFunction&lt;V&gt;</c>：每个输入约束一个二值标志 <c>u[i]</c>；
/// 无 <c>amount</c> 时结果为 <c>sum(u)</c>，给定 <c>amount</c> 区间时结果为二值 <c>y</c>，
/// 由两行把 <c>y</c> 与满足数量区间关联。归一化左侧范围不含 0 时标志按平凡真值固定。</para>
/// <para>Mirrors Kotlin <c>SatisfiedAmountInequalityFunction&lt;V&gt;</c>: one binary flag per input
/// constraint; the result is <c>sum(u)</c> without <c>amount</c>, otherwise the binary <c>y</c> linked to
/// the satisfied-count range by two rows. Flags are fixed trivially when the normalized lhs range
/// excludes zero.</para>
/// <para>注意：注册使用零带蕴含，而求值按比较符号计数；二者不是双向等价。
/// Note: baseline registration uses a zero-band implication, while evaluation counts by comparison;
/// these are not equivalent. The count-range output also only implies membership when y=1.</para>
/// </summary>
public class SatisfiedAmountInequalityFunction :
    IMathFunctionSymbol<Flt64>,
    IHasResultPolynomial<Flt64> {
    /// <summary>Kotlin 基线默认容差 / Default epsilon of the Kotlin baseline.</summary>
    internal static readonly Flt64 DefaultEpsilon = new(1e-6);

    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    public string Name { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<LinearConstraintInput<Flt64>> Inputs { get; }
    public Flt64 Epsilon { get; }
    public virtual ValueRange<UInt64>? Amount { get; }
    public IReadOnlyList<BinVar> FlagVars { get; }
    public BinVar? AmountFlagVar { get; }
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Inputs);

    /// <summary>结果多项式：存在数量区间时为 y，否则为标志之和。
    /// Result polynomial: the indicator when a count range exists, otherwise the flag sum.</summary>
    public LinearPolynomial<Flt64> ResultPolynomial => Amount is not null
        ? new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, AmountFlagVar!) },
            Flt64.Zero)
        : new LinearPolynomial<Flt64>(
            FlagVars.Select(flag => new LinearMonomial<Flt64>(Flt64.One, flag)).ToArray(),
            Flt64.Zero);

    public SatisfiedAmountInequalityFunction(
        IReadOnlyList<LinearConstraintInput<Flt64>> inputs,
        Flt64? epsilon = null,
        ValueRange<UInt64>? amount = null,
        string name = "satisfied_amount",
        string? displayName = null) {
        if (inputs is null) {
            throw new ArgumentNullException(nameof(inputs));
        }
        if (inputs.Any(input => input is null)) {
            throw new ArgumentException(
                "约束输入不可为 null。 / Constraint inputs must not be null.",
                nameof(inputs)
            );
        }
        Inputs = inputs.ToArray();
        Epsilon = epsilon ?? DefaultEpsilon;
        Amount = amount;
        Name = name;
        DisplayName = displayName;
        FlagVars = Enumerable.Range(0, Inputs.Count)
            .Select(index => new BinVar($"{name}_u_{index}"))
            .ToArray();
        AmountFlagVar = amount is null ? null : new BinVar($"{name}_y");
        _helperVariables = AmountFlagVar is { } indicator
            ? FlagVars.Cast<IVariableItem>().Append(indicator).ToArray()
            : FlagVars.Cast<IVariableItem>().ToArray();
    }

    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
        int count = 0;
        foreach (LinearConstraintInput<Flt64> input in Inputs) {
            bool? satisfied = IsSatisfied(input, values);
            if (satisfied is null) {
                return null;
            }
            if (satisfied.Value) {
                ++count;
            }
        }
        return Amount is { } range
            ? range.Contains(new UInt64((ulong)count)) ? Flt64.One : Flt64.Zero
            : new Flt64(count);
    }

    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        if (!BigMValues.IsUsable(Epsilon) || !Epsilon.Geq(Flt64.Zero)) {
            return Invalid("epsilon 必须为有限且非负。 / epsilon must be finite and non-negative.");
        }

        var constraints = new List<LinearInequality<Flt64>>();
        for (int index = 0; index < Inputs.Count; ++index) {
            LinearConstraintInput<Flt64> input = Inputs[index];
            Flt64? lower = Finite(input.LhsRange.LowerBound.Value);
            Flt64? upper = Finite(input.LhsRange.UpperBound.Value);
            if (lower is null || upper is null) {
                // 范围无界时不写行，标志保持自由变量，与 Kotlin 一致。
                // No row for unbounded ranges; the flag stays free, matching Kotlin.
                continue;
            }

            Flt64 zero = Flt64.Zero;
            if (lower.Value.Leq(zero) && upper.Value.Geq(zero)) {
                Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> rows =
                    IndicatorRows(
                        input: input,
                        index: index,
                        lower: lower.Value,
                        upper: upper.Value
                    );
                if (rows is Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> failed) {
                    return Results.Failed<Success>(failed.Error);
                }
                constraints.AddRange(
                    ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)rows).Value);
                continue;
            }

            bool triviallySatisfied = input.Sign switch {
                Comparison.LE or Comparison.LT => upper.Value.Ls(zero),
                Comparison.GE or Comparison.GT => lower.Value.Gr(zero),
                Comparison.EQ => false,
                _ => true,
            };
            constraints.Add(IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<Flt64>(Flt64.One, FlagVars[index]) },
                constant: Flt64.Zero,
                comparison: Comparison.EQ,
                rhs: triviallySatisfied ? Flt64.One : Flt64.Zero,
                name: $"{Name}_i{index}_flag"
            ));
        }

        if (Amount is { } amountRange && AmountFlagVar is { } indicator) {
            IReadOnlyList<LinearMonomial<Flt64>> sumMonomials = FlagVars
                .Select(flag => new LinearMonomial<Flt64>(Flt64.One, flag))
                .ToArray();
            UInt64? lowerCount = amountRange.LowerBound.Value.UnwrapOrNull();
            UInt64? upperCount = amountRange.UpperBound.Value.UnwrapOrNull();
            if (lowerCount is null || upperCount is null || lowerCount.Value.Gr(upperCount.Value)) {
                return Invalid("数量区间需要有限且有序的边界。 / The count range requires finite ordered bounds.");
            }
            Flt64 total = SatisfiedAmountFunction.RepeatOne(new UInt64(Inputs.Count));
            Flt64 amountLower = SatisfiedAmountFunction.RepeatOne(lowerCount.Value);
            Flt64 amountUpper = SatisfiedAmountFunction.RepeatOne(upperCount.Value);

            constraints.Add(IndicatorSupport.Row(
                monomials: Append(sumMonomials, new LinearMonomial<Flt64>(amountLower.Negate(), indicator)),
                constant: Flt64.Zero,
                comparison: Comparison.GE,
                rhs: Flt64.Zero,
                name: $"{Name}_amount_lb"
            ));
            constraints.Add(IndicatorSupport.Row(
                monomials: Append(sumMonomials, new LinearMonomial<Flt64>(total.Minus(amountUpper), indicator)),
                constant: Flt64.Zero,
                comparison: Comparison.LE,
                rhs: total,
                name: $"{Name}_amount_ub"
            ));
        }

        if (constraints.Any(constraint => !IsUsable(constraint.Lhs) || !IsUsable(constraint.Rhs))) {
            return Invalid("生成的约束含非有限或不可编码系数。 / The generated constraints contain non-finite or unrepresentable coefficients.");
        }
        return IndicatorSupport.AddConstraints(model, constraints);
    }

    /// <summary>按生成零带蕴含行，不按 sign 改写 / Emit baseline zero-band implication rows without rewriting by sign.</summary>
    private Result<
        IReadOnlyList<LinearInequality<Flt64>>,
        ErrorCode,
        Error<ErrorCode>> IndicatorRows(
        LinearConstraintInput<Flt64> input,
        int index,
        Flt64 lower,
        Flt64 upper) {
        LinearPolynomial<Flt64> polynomial = Polynomial(input.FlattenData);
        Flt64 magnitude = lower.Abs().Geq(upper.Abs()) ? lower.Abs() : upper.Abs();
        Flt64 bigM = BigMValues.EnsurePositiveBigM(magnitude, IIntoValue<Flt64>.Identity);
        Try valid = IndicatorSupport.Validate(
            polynomial: polynomial,
            bigM: bigM,
            tolerance: Epsilon
        );
        if (!valid.IsOk) {
            return valid.Map<IReadOnlyList<LinearInequality<Flt64>>>(_ => Array.Empty<LinearInequality<Flt64>>());
        }
        return Results.Ok<IReadOnlyList<LinearInequality<Flt64>>>(new[] {
            IndicatorSupport.Row(
                monomials: Append(polynomial.Monomials, new LinearMonomial<Flt64>(bigM, FlagVars[index])),
                constant: polynomial.Constant,
                comparison: Comparison.LE,
                rhs: bigM.Plus(Epsilon),
                name: $"{Name}_i{index}_upper"
            ),
            IndicatorSupport.Row(
                monomials: Append(polynomial.Monomials, new LinearMonomial<Flt64>(bigM.Negate(), FlagVars[index])),
                constant: polynomial.Constant,
                comparison: Comparison.GE,
                rhs: bigM.Negate().Minus(Epsilon),
                name: $"{Name}_i{index}_lower"
            ),
        });
    }

    /// <summary>按符号判断归一化左侧是否满足 / Decide satisfaction of the normalized lhs by sign.</summary>
    private static bool? IsSatisfied(
        LinearConstraintInput<Flt64> input,
        IReadOnlyDictionary<ISymbol, Flt64> values) {
        Flt64 lhs = input.FlattenData.Constant;
        foreach (LinearMonomial<Flt64> monomial in input.FlattenData.Monomials) {
            if (!values.TryGetValue(monomial.Symbol, out Flt64 value)) {
                return null;
            }
            lhs = lhs.Plus(monomial.Coefficient.Times(value));
        }
        return input.Sign switch {
            Comparison.LT => lhs.Ls(Flt64.Zero),
            Comparison.LE => lhs.Leq(Flt64.Zero),
            Comparison.GT => lhs.Gr(Flt64.Zero),
            Comparison.GE => lhs.Geq(Flt64.Zero),
            Comparison.EQ => lhs.Eq(Flt64.Zero),
            _ => !lhs.Eq(Flt64.Zero),
        };
    }

    /// <summary>读取有限边界；无界或非有限视为缺失 / Read a finite bound; unbounded or non-finite reads as absent.</summary>
    private static Flt64? Finite(ValueWrapper<Flt64> wrapper) {
        if (wrapper.IsInfinityOrNegativeInfinity) {
            return null;
        }
        Flt64 value = wrapper.Unwrap();
        return BigMValues.IsUsable(value) ? value : null;
    }

    private static LinearPolynomial<Flt64> Polynomial(SymbolFlatten.LinearFlattenData<Flt64> flattenData) =>
        new(flattenData.Monomials, flattenData.Constant);

    private static bool IsUsable(LinearPolynomial<Flt64> polynomial) =>
        BigMValues.IsUsable(polynomial.Constant) &&
        polynomial.Monomials.All(monomial => BigMValues.IsUsable(monomial.Coefficient));

    private static IReadOnlyList<LinearMonomial<Flt64>> Append(
        IReadOnlyList<LinearMonomial<Flt64>> monomials,
        LinearMonomial<Flt64> additional) {
        var result = new List<LinearMonomial<Flt64>>(monomials.Count + 1);
        result.AddRange(monomials);
        result.Add(additional);
        return result;
    }

    private static Try Invalid(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}

/// <summary>
/// 任一满足函数：至少一个约束满足（<c>amount = [1, n]</c>）。
/// Any function: at least one constraint is satisfied (<c>amount = [1, n]</c>).
/// </summary>
public sealed class AnyFunction : SatisfiedAmountInequalityFunction {
    public AnyFunction(
        IReadOnlyList<LinearConstraintInput<Flt64>> inputs,
        Flt64? epsilon = null,
        string name = "any",
        string? displayName = null)
        : base(inputs, epsilon, SatisfiedAmountRange.CountRange(1, inputs.Count), name, displayName) { }
}

/// <summary>
/// 全部满足函数：所有约束必须满足（<c>amount = [n, n]</c>）。
/// All function: every constraint must be satisfied (<c>amount = [n, n]</c>).
/// </summary>
public sealed class AllFunction : SatisfiedAmountInequalityFunction {
    public AllFunction(
        IReadOnlyList<LinearConstraintInput<Flt64>> inputs,
        Flt64? epsilon = null,
        string name = "all",
        string? displayName = null)
        : base(inputs, epsilon, SatisfiedAmountRange.CountRange(inputs.Count, inputs.Count), name, displayName) { }
}

/// <summary>
/// 至少满足函数：至少 k 个约束满足（<c>amount = [k, n]</c>）。
/// At-least function: at least k constraints are satisfied (<c>amount = [k, n]</c>).
/// </summary>
public sealed class AtLeastInequalityFunction : SatisfiedAmountInequalityFunction {
    /// <summary>最小满足数量 / Minimum satisfied count.</summary>
    public UInt64 K { get; }

    public AtLeastInequalityFunction(
        IReadOnlyList<LinearConstraintInput<Flt64>> inputs,
        UInt64 k,
        Flt64? epsilon = null,
        string name = "at_least",
        string? displayName = null)
        : base(inputs, epsilon, RangeFor(k, inputs.Count), name, displayName) => K = k;

    private static ValueRange<UInt64> RangeFor(UInt64 k, int count) {
        if (!k.Gr(UInt64.Zero)) {
            throw new ArgumentException(
                "至少满足阈值必须为正。 / The at-least threshold must be positive.",
                nameof(k)
            );
        }
        int lower = SatisfiedAmountRange.ToInt(k);
        if (lower > count) {
            throw new ArgumentException(
                "至少满足阈值不能超过约束数量。 / The at-least threshold cannot exceed the constraint count.",
                nameof(k)
            );
        }
        return SatisfiedAmountRange.CountRange(lower, count);
    }
}

/// <summary>
/// 非全满足函数：不能同时满足所有约束（<c>amount = [1, n-1]</c>）；单约束时无区间，
/// 结果退化为满足计数，与 Kotlin 一致。
/// Not-all function: not every constraint can be satisfied (<c>amount = [1, n-1]</c>); a single input
/// drops the range and the symbol falls back to raw counting, matching Kotlin.
/// </summary>
public sealed class NotAllFunction : SatisfiedAmountInequalityFunction {
    public NotAllFunction(
        IReadOnlyList<LinearConstraintInput<Flt64>> inputs,
        Flt64? epsilon = null,
        string name = "not_all",
        string? displayName = null)
        : base(inputs, epsilon, inputs.Count > 1
            ? SatisfiedAmountRange.CountRange(1, inputs.Count - 1)
            : null, name, displayName) { }
}

/// <summary>
/// 可计数满足函数：满足数量必须落在给定区间。
/// Numerable function: the satisfied count must fall inside the given range.
/// </summary>
public sealed class NumerableFunction : SatisfiedAmountInequalityFunction {
    public NumerableFunction(
        IReadOnlyList<LinearConstraintInput<Flt64>> inputs,
        ValueRange<UInt64> amount,
        Flt64? epsilon = null,
        string name = "numerable",
        string? displayName = null)
        : base(inputs, epsilon, amount, name, displayName) { }
}

/// <summary>满足数量区间助手 / Satisfied-count range helpers.</summary>
internal static class SatisfiedAmountRange {
    /// <summary>构造满足数量闭区间 / Build a closed satisfied-count range.</summary>
    internal static ValueRange<UInt64> CountRange(int lower, int upper) {
        if (lower < 0 || upper < lower) {
            throw new ArgumentException(
                "满足数量区间要求 0 <= lower <= upper。 / The satisfied-count range requires 0 <= lower <= upper.",
                nameof(lower)
            );
        }
        Result<ValueRange<UInt64>, ErrorCode, Error<ErrorCode>> range =
            ValueRange<UInt64>.Of(new UInt64((ulong)lower), new UInt64((ulong)upper));
        if (range is Failed<ValueRange<UInt64>, ErrorCode, Error<ErrorCode>> failed) {
            throw new ArgumentException(
                $"满足数量区间非法。 / The satisfied-count range is invalid: {failed.Message}",
                nameof(lower)
            );
        }
        return ((Ok<ValueRange<UInt64>, ErrorCode, Error<ErrorCode>>)range).Value;
    }

    /// <summary>把无符号阈值缩窄为 int / Narrow an unsigned threshold to int.</summary>
    internal static int ToInt(UInt64 value) => checked((int)value.ToULong());
}
