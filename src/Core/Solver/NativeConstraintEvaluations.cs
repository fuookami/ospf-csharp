#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// <summary>
/// native 结构的约束求值 / Constraint evaluation for the native structures.
/// </summary>
/// <remarks>
/// 对齐基线 `NativePiecewiseReport.kt` 的 `absEvaluations` 与 `maxEvaluations`。
///
/// **两个求值器之间有两处不对称，都照抄基线并在判据里钉住。**
///
/// **第一处：容差的来源不同。**
///
/// | | 容差 |
/// | --- | --- |
/// | **ABS** | **`finiteSolverDouble(PIECEWISE_TOLERANCE, ...)` 转换之后的值** |
/// | **MAX** | **字面量 `1e-6`** |
///
/// **数值上相同，结构上不同：`PIECEWISE_TOLERANCE` 一旦改动，ABS 跟着走，MAX 不跟。**
///
/// **第二处，也是更要紧的一处：`maxEvaluations` <b>忽略 `structure.minimum`</b>。**
///
/// **它永远取 `inputs.maxOrNull()`，所以一个 `minimum = true` 的结构会被拿去和<b>最大值</b>比。**
///
/// **这不是本仓库的偏差，是<b>基线的缺陷</b>：** **`NativeMaxData` 带着 `minimum` 标志，
/// `restoreValues` 也按它取极值（`if (structure.minimum) minOrNull else maxOrNull`），
/// 而 `maxEvaluations` 却没看这个标志。**
/// **同一份数据在两条路径上被解释成两种含义。**
///
/// **本批<b>照抄</b>以保持与基线的可比性，并把这一点写成判据，
/// 让"最小值的结构被当成最大值来评"成为一个被记录的事实而不是一个潜伏的错。**
///
/// **Two asymmetries between the evaluators, both copied from the baseline and pinned by criteria.**
///
/// **First, the tolerance source differs**: **ABS converts `PIECEWISE_TOLERANCE` through the helper, MAX uses the literal
/// `1e-6`.** **Numerically identical, structurally different: changing the constant moves ABS and not MAX.**
///
/// **Second and more importantly, `maxEvaluations` <b>ignores `structure.minimum`</b>.**
///
/// **It always takes `maxOrNull()`, so a `minimum = true` structure is compared against the <b>maximum</b>.**
///
/// **This is not a deviation of this repository but a <b>baseline defect</b>**: **`NativeMaxData` carries the `minimum`
/// flag and `restoreValues` honours it (`minOrNull`/`maxOrNull`), while `maxEvaluations` does not — the same data is
/// interpreted two ways on two paths.**
///
/// **This batch <b>copies</b> it to stay comparable with the baseline and turns it into a criterion, making "a minimum
/// structure evaluated as a maximum" a recorded fact rather than a latent bug.**
/// </remarks>
public static class NativeConstraintEvaluations
{
    /// <summary>求值 ABS 结构 / Evaluate the ABS structures.</summary>
    public static Result<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>
        Abs(
            IReadOnlyList<Flt64> values,
            IReadOnlyDictionary<VariableItemKey, int> columns,
            IReadOnlyList<NormalizedAbs> structures,
            string backendName) {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(structures);

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>
            projected = NativeSolutionProjection.ValuesByKey(values, columns);
        if (projected is Failed<
                IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> bad) {
            return Fail(bad.Error.Message);
        }

        IReadOnlyDictionary<VariableItemKey, Flt64> nativeValues =
            ((Ok<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)projected)
            .Value;

        var evaluations = new List<ConstraintEvaluation<Flt64>>(structures.Count);

        // **ABS 走转换后的容差。**
        // **ABS takes the converted tolerance.**
        double tolerance =
            NativePiecewiseReport.PiecewiseTolerance;

        foreach (NormalizedAbs structure in structures) {
            if (!nativeValues.TryGetValue(structure.InputKey, out Flt64 input)) {
                return Fail("ABS input token is missing");
            }

            if (!nativeValues.TryGetValue(structure.ResultKey, out Flt64 result)) {
                return Fail("ABS result token is missing");
            }

            Flt64 expected = input.CompareTo(Flt64.Zero) < 0 ? input.Negate() : input;
            double difference = expected.Minus(result).ToSolverDouble();

            if (!double.IsFinite(difference)) {
                return Fail("numeric value must be finite: native.abs.violation");
            }

            double violationValue = global::System.Math.Abs(difference);

            evaluations.Add(new ConstraintEvaluation<Flt64>(
                ConstraintId: new ConstraintId(
                    $"{backendName}-abs:{NativeCombinedKeyValidator.KeyText(structure.ResultKey)}"),
                Lhs: expected,
                Rhs: result,
                Relation: SolveConstraintRelation.Equal,
                Slack: result.Minus(expected),
                Violation: new Flt64(violationValue),
                Tolerance: new Flt64(tolerance),
                Satisfied: violationValue <= tolerance));
        }

        return new Ok<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>(
            evaluations);
    }

    /// <summary>求值 MAX 结构 / Evaluate the MAX structures.</summary>
    public static Result<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>
        Max(
            IReadOnlyList<Flt64> values,
            IReadOnlyDictionary<VariableItemKey, int> columns,
            IReadOnlyList<NativeMaxData> structures,
            string backendName) {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(structures);

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>
            projected = NativeSolutionProjection.ValuesByKey(values, columns);
        if (projected is Failed<
                IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> bad) {
            return Fail(bad.Error.Message);
        }

        IReadOnlyDictionary<VariableItemKey, Flt64> nativeValues =
            ((Ok<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)projected)
            .Value;

        var evaluations = new List<ConstraintEvaluation<Flt64>>(structures.Count);

        foreach (NativeMaxData structure in structures) {
            Result<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>> computed =
                NativeValueRestoration.MaxInputValues(nativeValues, structure);
            if (computed is Failed<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>> badInputs) {
                return Fail(badInputs.Error.Message);
            }

            IReadOnlyList<double> inputs =
                ((Ok<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>>)computed).Value;

            if (!nativeValues.TryGetValue(structure.ResultKey, out Flt64 actual)) {
                return Fail("MAX result token is missing");
            }

            // **基线在这里<b>不</b>看 `minimum`，永远取最大。照抄。**
            // **The baseline does <b>not</b> consult `minimum` here and always takes the maximum. Copied.**
            double extreme = double.NegativeInfinity;
            foreach (double input in inputs) {
                if (input > extreme) {
                    extreme = input;
                }
            }

            var expected = new Flt64(extreme);
            double difference = expected.Minus(actual).ToSolverDouble();

            if (!double.IsFinite(difference)) {
                return Fail("numeric value must be finite: native.max.violation");
            }

            double violation = global::System.Math.Abs(difference);

            evaluations.Add(new ConstraintEvaluation<Flt64>(
                ConstraintId: new ConstraintId(
                    $"{backendName}-max:{NativeCombinedKeyValidator.KeyText(structure.ResultKey)}"),
                Lhs: expected,
                Rhs: actual,
                Relation: SolveConstraintRelation.Equal,
                Slack: actual.Minus(expected),
                Violation: new Flt64(violation),
                Tolerance: new Flt64(1e-6),
                Satisfied: violation <= 1e-6));
        }

        return new Ok<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>(
            evaluations);
    }

    private static Result<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
