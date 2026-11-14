#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Core.Solver.Value;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>条件的三值结果 / Three-valued condition result.</summary>
public enum TruthValue {
    /// <summary>满足真分支 / The true branch is satisfied.</summary>
    True,
    /// <summary>满足假分支 / The false branch is satisfied.</summary>
    False,
    /// <summary>处于分支间隔 / The value lies in the branch gap.</summary>
    Undefined,
}

/// <summary>按业务间隔判定条件差值 / Classify a condition difference with a business gap.</summary>
/// <remarks>
/// 支持 Flt32、Flt64、FltX、RtnX 及内建整数；按原始值比较，不保证转换为 Flt64 后仍精确。
/// Supports Flt32, Flt64, FltX, RtnX and built-in integers; native comparison does not guarantee exact Flt64 conversion.
/// </remarks>
public static class ConditionClassification {
    /// <summary>校验离散步长与严格间隔 / Validate the discrete step and strict gap.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="delta">正离散步长 / Positive discrete step.</param>
    /// <param name="strictBoundary">正严格间隔，不大于步长 / Positive strict gap, no larger than the step.</param>
    /// <returns>校验结果 / Validation result.</returns>
    public static Try ValidateDiscreteParameters<V>(V delta, V strictBoundary)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (!TryRead(delta, out ExactBoundValue step) ||
            !TryRead(strictBoundary, out ExactBoundValue gap) ||
            step.Sign <= 0 || gap.Sign <= 0 || gap.CompareTo(step) > 0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "步长和严格间隔必须有限且满足 0 < strictBoundary <= delta。 / The step and strict gap must be finite and satisfy 0 < strictBoundary <= delta."
            ));
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>按关系与严格间隔判定差值 / Classify a difference by relation and strict gap.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="difference">条件差值 lhs-rhs / Condition difference lhs-rhs.</param>
    /// <param name="relation">GT、GE、LT 或 LE / GT, GE, LT or LE.</param>
    /// <param name="strictBoundary">真假分支之间的正间隔 / Positive gap between true and false branches.</param>
    /// <returns>三值结果，或参数无效时返回失败 / Three-valued result, or failure for invalid input.</returns>
    public static Result<TruthValue, ErrorCode, Error<ErrorCode>> Classify<V>(
        V difference,
        Comparison relation,
        V strictBoundary)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (relation is not (Comparison.GT or Comparison.GE or Comparison.LT or Comparison.LE) ||
            !TryRead(difference, out ExactBoundValue value) ||
            !TryRead(strictBoundary, out ExactBoundValue gap) || gap.Sign <= 0) {
            return Results.Failed<TruthValue>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "条件关系、差值或严格间隔无效。 / Invalid condition relation, difference or strict gap."
            ));
        }
        return Results.Ok(ClassifyExact(
            value: value,
            relation: relation,
            gap: gap
        ));
    }

    private static TruthValue ClassifyExact(ExactBoundValue value, Comparison relation, ExactBoundValue gap) {
        int zero = value.Sign;
        int positive = value.CompareTo(gap);
        int negative = value.CompareTo(gap.Negate());
        return relation switch {
            Comparison.GT => positive >= 0 ? TruthValue.True : zero <= 0 ? TruthValue.False : TruthValue.Undefined,
            Comparison.GE => zero >= 0 ? TruthValue.True : negative <= 0 ? TruthValue.False : TruthValue.Undefined,
            Comparison.LT => negative <= 0 ? TruthValue.True : zero >= 0 ? TruthValue.False : TruthValue.Undefined,
            _ => zero <= 0 ? TruthValue.True : positive >= 0 ? TruthValue.False : TruthValue.Undefined,
        };
    }

    /// <summary>校验步长后判定差值 / Classify a difference after validating the discrete step.</summary>
    /// <remarks>标量判定不证明多项式格点或求解器降阶安全。 / Scalar classification does not prove a polynomial lattice or safe solver lowering.</remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="difference">条件差值 lhs-rhs / Condition difference lhs-rhs.</param>
    /// <param name="relation">GT、GE、LT 或 LE / GT, GE, LT or LE.</param>
    /// <param name="delta">正离散步长 / Positive discrete step.</param>
    /// <param name="strictBoundary">正严格间隔，不大于步长 / Positive strict gap, no larger than the step.</param>
    /// <returns>三值结果，或参数无效时返回失败 / Three-valued result, or failure for invalid input.</returns>
    public static Result<TruthValue, ErrorCode, Error<ErrorCode>> ClassifyDiscrete<V>(
        V difference,
        Comparison relation,
        V delta,
        V strictBoundary)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Try validation = ValidateDiscreteParameters(delta, strictBoundary);
        if (validation is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<TruthValue>(failed.Error);
        }
        if (validation is Fatal<Success, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<TruthValue, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        return Classify(
            difference: difference,
            relation: relation,
            strictBoundary: strictBoundary
        );
    }

    internal static Result<TruthValue, ErrorCode, Error<ErrorCode>> ClassifyChecked<V>(
        V difference,
        ExactBoundValue exact,
        ExactBoundValue encoded,
        Comparison relation,
        V delta,
        V strictBoundary,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<TruthValue, ErrorCode, Error<ErrorCode>> result = ClassifyDiscrete(
            difference: difference,
            relation: relation,
            delta: delta,
            strictBoundary: strictBoundary
        );
        if (!result.IsOk) {
            return result;
        }
        return VerifyExpanded(
            expected: result.Value,
            exact: exact,
            encoded: encoded,
            relation: relation,
            strictBoundary: strictBoundary,
            converter: converter
        );
    }

    internal static Result<TruthValue, ErrorCode, Error<ErrorCode>> VerifyExpanded<V>(
        TruthValue expected,
        ExactBoundValue exact,
        ExactBoundValue encoded,
        Comparison relation,
        V strictBoundary,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            if (FunctionEvaluation.TryReadConditionValue(
                    value: strictBoundary,
                    converter: converter,
                    exact: out ExactBoundValue gap,
                    encoded: out ExactBoundValue encodedGap
                ) && gap.CompareTo(encodedGap) == 0 &&
                expected == ClassifyExact(
                    value: exact,
                    relation: relation,
                    gap: gap
                ) && expected == ClassifyExact(
                    value: encoded,
                    relation: relation,
                    gap: encodedGap
                )) {
                return Results.Ok(expected);
            }
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
        return Results.Failed<TruthValue>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "条件求值与模型分支不一致或数值转换无效。 / Condition evaluation disagrees with the model branch or has an invalid numeric conversion."
        ));
    }

    private static bool TryRead<V>(V value, out ExactBoundValue exact)
        where V : struct, IRealNumber<V>, INumberField<V> {
        exact = default;
        try {
            if (!ExactBoundValue.TryRead(value, out exact) || !FunctionEvaluation.IsUsable(value)) {
                return false;
            }
            Flt64 solverValue = value.ToFlt64();
            return BigMValues.IsUsable(solverValue) && (exact.Sign == 0 || solverValue.ToDouble() != 0);
        }
        catch (ArithmeticException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }
}
