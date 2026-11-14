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
/// PWL 求值与 ABS 列校验 / PWL evaluation and ABS column validation.
/// </summary>
/// <remarks>
/// B1212 按基线 `NativePiecewiseReport.kt` 的 `piecewiseEvaluations` 与 `validateNativeAbsColumns` 补。
///
/// **插值用的是<b>夹紧之后</b>的输入。**
///
/// **`selectSegment` 在端点容差内会把输入夹到边界，并把这个夹紧后的值放进 `SegmentSelection.Input`。**
/// **`piecewiseEvaluations` 用的是那个值，所以一个比定义域下界小 `1e-9` 的输入，
/// 插值是在<b>边界上</b>做的，而不是在它自己的位置上。**
///
/// **这与"报告值就是求解器值"是一致的：** **求解器在边界上满足了这个约束，
/// 报告就该在边界上评它。** **而如果拿原始输入去插值，会得到一个求解器从未见过的点。**
///
/// **容差的来源在这里是<b>第三种</b>：**
///
/// | 求值器 | 容差 |
/// | --- | --- |
/// | `absEvaluations` | **`finiteSolverDouble(PIECEWISE_TOLERANCE, …)`** |
/// | `maxEvaluations` | **字面量 `1e-6`** |
/// | `piecewiseEvaluations` | **`finiteSolverDouble(PIECEWISE_TOLERANCE, …)`** |
///
/// **所以三个求值器里两个同源、一个不同源。** **照抄，并记录这处分歧。**
///
/// **分母可以是零：** **`fraction = (input − lower) / (upper − lower)` 在 `upper == lower` 时除零。**
/// **`prepareNativePiecewise` 会拒绝非严格递增的断点，但 `NormalizedPiecewise` 可以被直接构造——
/// 那时这一层是唯一会拦的地方，而它拦的方式是"插值结果非有限"。**
///
/// **The interpolation uses the <b>clamped</b> input.**
///
/// **`selectSegment` clamps into the endpoint tolerance and puts the clamped value into `SegmentSelection.Input`.**
/// **`piecewiseEvaluations` uses that value, so an input `1e-9` below the domain is interpolated <b>at the bound</b>, not
/// at its own position.**
///
/// **That agrees with "the reported value is the solver's value": the solver satisfied the constraint at the bound, so
/// the report evaluates it at the bound.** **Interpolating at the raw input would produce a point the solver never
/// saw.**
///
/// **The tolerance source is a <b>third</b> variation**: **two of the three evaluators share `PIECEWISE_TOLERANCE` and one
/// does not.** **Copied, and the divergence recorded.**
///
/// **The denominator can be zero**: **`fraction` divides by `upper - lower`.** **`prepareNativePiecewise` rejects
/// breakpoints that are not strictly increasing, but `NormalizedPiecewise` can be constructed directly — then this layer is
/// the only guard, and it guards by "the interpolated value is not finite".**
/// </remarks>
public static class NativePiecewiseEvaluation
{
    /// <summary>求值 PWL 结构 / Evaluate the PWL structures.</summary>
    public static Result<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>
        Piecewise(
            IReadOnlyList<Flt64> values,
            IReadOnlyDictionary<VariableItemKey, int> columns,
            IReadOnlyList<NormalizedPiecewise> structures,
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
        double tolerance = NativePiecewiseReport.PiecewiseTolerance;

        foreach (NormalizedPiecewise structure in structures) {
            if (!nativeValues.TryGetValue(structure.InputKey, out Flt64 input)) {
                return Fail("PWL input token is missing");
            }

            if (!nativeValues.TryGetValue(structure.ResultKey, out Flt64 result)) {
                return Fail("PWL result token is missing");
            }

            Result<SegmentSelection, ErrorCode, Error<ErrorCode>> selected =
                NativePiecewiseReport.SelectSegment(input, structure.Breakpoints);
            if (selected is Failed<SegmentSelection, ErrorCode, Error<ErrorCode>> badSegment) {
                return Fail(badSegment.Error.Message);
            }

            SegmentSelection selection =
                ((Ok<SegmentSelection, ErrorCode, Error<ErrorCode>>)selected).Value;

            // **结构直接构造时 `Values` 可能不够长——越界比"插出个错值"更容易查。**
            // **A directly-built structure may have too few values; an out-of-range is easier to diagnose than a wrong
            // interpolated number.**
            if (selection.Index + 1 >= structure.Values.Count ||
                selection.Index + 1 >= structure.Breakpoints.Count) {
                return Fail("piecewise snapshot has fewer values than segments");
            }

            double lower = structure.Breakpoints[selection.Index].ToDouble();
            double upper = structure.Breakpoints[selection.Index + 1].ToDouble();
            double lowerValue = structure.Values[selection.Index].ToDouble();
            double upperValue = structure.Values[selection.Index + 1].ToDouble();

            double fraction = (selection.Input - lower) / (upper - lower);
            double expectedValue = lowerValue + (fraction * (upperValue - lowerValue));

            var expected = new Flt64(expectedValue);
            double difference = expected.Minus(result).ToSolverDouble();

            if (!double.IsFinite(difference)) {
                return Fail("numeric value must be finite: native.pwl.violation");
            }

            double violationValue = global::System.Math.Abs(difference);

            evaluations.Add(new ConstraintEvaluation<Flt64>(
                ConstraintId: new ConstraintId(
                    $"{backendName}-pwl:{NativeCombinedKeyValidator.KeyText(structure.ResultKey)}"),
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

    /// <summary>校验 ABS 的列：输入与结果必须在，辅助列必须不在 / Validate ABS columns.</summary>
    /// <remarks>
    /// **三条检查的顺序与"哪一侧该在"是同一个判断：**
    ///
    /// | 列 | 要求 | 理由 |
    /// | --- | --- | --- |
    /// | **输入列** | **必须在** | **原生 primitive 要读它** |
    /// | **结果列** | **必须在** | **它是被求解的量** |
    /// | **正部/负部/符号列** | **必须不在** | **它们在建模时被消掉了** |
    ///
    /// **这与 B1209 的"解里不该有选择列"是同一件事的两个层次：**
    /// **那里查的是<b>值</b>，这里查的是<b>列</b>。**
    /// **一条在解投影之后，一条在列映射阶段——所以两处都需要，不能互相替代。**
    ///
    /// **The three checks and "which side should be present" are one judgement:**
    ///
    /// | Column | Requirement | Why |
    /// | --- | --- | --- |
    /// | **input** | **must be present** | **the native primitive reads it** |
    /// | **result** | **must be present** | **it is what gets solved for** |
    /// | **positive/negative/sign** | **must be absent** | **eliminated at model build time** |
    ///
    /// **This is the same fact as B1209's "the solution must omit selectors" at a different level: that one checks
    /// <b>values</b>, this one checks <b>columns</b>.** **One runs after solution projection and one at the column mapping
    /// stage, so both are needed and neither replaces the other.**
    /// </remarks>
    public static Result<Success, ErrorCode, Error<ErrorCode>> ValidateAbsColumns(
        IReadOnlyDictionary<VariableItemKey, int> columns,
        IReadOnlyList<NormalizedAbs> structures) {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(structures);

        foreach (NormalizedAbs structure in structures) {
            if (!columns.ContainsKey(structure.InputKey)) {
                return FailColumns("ABS input column is missing");
            }

            if (!columns.ContainsKey(structure.ResultKey)) {
                return FailColumns("ABS result column is missing");
            }

            foreach (VariableItemKey key in new[] {
                structure.PositiveKey, structure.NegativeKey, structure.SignKey,
            }) {
                if (columns.ContainsKey(key)) {
                    return FailColumns("ABS helper is still present in native columns");
                }
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    private static Result<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);

    private static Result<Success, ErrorCode, Error<ErrorCode>> FailColumns(string message) =>
        new Failed<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);
}
