#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// PWL 求值：插值在夹紧之后的位置，以及 ABS 列级校验 / Interpolation after clamping, and ABS column checks.
/// </summary>
/// <remarks>
/// 对齐基线 `NativePiecewiseReport.kt` 的 `piecewiseEvaluations` 与 `validateNativeAbsColumns`。
///
/// Adds </remarks>
public class NativePiecewiseEvaluationTests
{
    /// <summary>
    /// **段内插值给出期望值** / Interpolation inside a segment gives the expected value.
    /// </summary>
    /// <remarks>
    /// **断点 `[0, 5, 10]`、值 `[0, 5, 5]`（第一段斜率 1，第二段常数 5）。**
    /// **输入 `7` 落第二段，插值是 `5`。**
    ///
    /// **第二段是常数，所以这个算例<b>不检验</b>插值公式——它只检验"下标选对了"。**
    /// **下面那条用非平凡斜率检验公式本身。**
    ///
    /// **Breakpoints `[0, 5, 10]`, values `[0, 5, 5]`.** **Input `7` lands in the second segment and interpolates to `5`.**
    ///
    /// **That second segment is constant, so this case does <b>not</b> exercise the interpolation formula — only the
    /// index choice.** **The next criterion uses a non-trivial slope to test the formula itself.**
    /// </remarks>
    [Fact]
    public void AnInputInsideASegment_ShouldInterpolate() {
        ConstraintEvaluation<Flt64> evaluation = Single(
            input: 7.0, result: 5.0,
            breakpoints: new[] { 0.0, 5.0, 10.0 }, values: new[] { 0.0, 5.0, 5.0 });

        evaluation.Lhs.ToDouble().Should().Be(5.0);
        evaluation.Satisfied.Should().BeTrue();
        evaluation.Relation.Should().Be(SolveConstraintRelation.Equal);
    }

    /// <summary>
    /// **插值公式本身：中点必须给出两端的平均值** / The formula itself: a midpoint must give the average of the ends.
    /// </summary>
    /// <remarks>
    /// **单段 `[0, 10]`、值 `[2, 8]`。输入 `5` 的中点是 `2 + 0.5 × 6 = 5`。**
    ///
    /// **若实现误写成 `lowerValue + fraction × upperValue`（漏了减去 lowerValue），
    /// 中点会得到 `2 + 0.5 × 8 = 6`。**
    ///
    /// **两个数（`5` 与 `6`）不同，所以这条判据能区分"插值"与"加权和"。**
    ///
    /// **A single segment `[0, 10]` with values `[2, 8]`: at the midpoint `5` the interpolation is `2 + 0.5 * 6 = 5`.**
    ///
    /// **An implementation that wrote `lowerValue + fraction * upperValue` (forgetting to subtract `lowerValue`) would give
    /// `2 + 0.5 * 8 = 6` at the midpoint.**
    ///
    /// **The two numbers differ, so the criterion distinguishes interpolation from a weighted sum.**
    /// </remarks>
    [Fact]
    public void TheInterpolation_ShouldUseTheDifference() {
        ConstraintEvaluation<Flt64> evaluation = Single(
            input: 5.0, result: 5.0,
            breakpoints: new[] { 0.0, 10.0 }, values: new[] { 2.0, 8.0 });

        evaluation.Lhs.ToDouble().Should().Be(
            5.0, "two plus half of (eight minus two), not two plus half of eight");
    }

    /// <summary>
    /// **插值用的是<b>夹紧之后</b>的输入** / The interpolation uses the <b>clamped</b> input.
    /// </summary>
    /// <remarks>
    /// **这是这里最有内容的一条。**
    ///
    /// **`selectSegment` 在端点容差内会把输入夹到边界。** **一个比下界小 `1e-9` 的输入，
    /// 插值是在<b>边界上</b>做的，而不是在它自己的位置上。**
    ///
    /// **这与"报告值就是求解器值"一致：** **求解器在边界上满足了约束，报告就该在边界上评它。
    /// 拿原始输入去插值，会得到一个求解器从未见过的点。**
    ///
    /// **判据用一个斜率非零的算例让差别显形：** **段 `[0, 10]`、值 `[0, 20]`（斜率 2）。
    /// 输入 `-5e-7`（在 `1e-6` 容差内，会被夹到 `0`）——**
    ///
    /// | 用哪个输入 | 期望值 |
    /// | --- | --- |
    /// | **夹紧后（`0`）** | **`0`** |
    /// | **原始（`-5e-7`）** | **`-1e-6`** |
    ///
    /// **两个值差 `1e-6`，在容差量级上——所以判据必须直接比较 `Lhs`，而不是靠 `Satisfied`。**
    ///
    /// **The batch's most substantive criterion.**
    ///
    /// **`selectSegment` clamps into the endpoint tolerance, so an input `1e-9` below the domain is interpolated <b>at the
    /// bound</b> rather than at its own position.**
    ///
    /// **This agrees with "the reported value is the solver's value": the solver satisfied the constraint at the bound.
    /// Interpolating at the raw input yields a point the solver never saw.**
    ///
    /// **The criterion uses a nonzero slope so the difference shows: segment `[0, 10]`, values `[0, 20]` (slope 2), input
    /// `-5e-7` (inside the `1e-6` tolerance, so it clamps to `0`).**
    ///
    /// | Which input | Expected |
    /// | --- | --- |
    /// | **clamped (`0`)** | **`0`** |
    /// | **raw (`-5e-7`)** | **`-1e-6`** |
    ///
    /// **The two differ by `1e-6`, on the order of the tolerance — so the criterion must compare `Lhs` directly rather than
    /// rely on `Satisfied`.**
    /// </remarks>
    [Fact]
    public void TheInterpolation_ShouldUseTheClampedInput() {
        ConstraintEvaluation<Flt64> evaluation = Single(
            input: -5e-7, result: 0.0,
            breakpoints: new[] { 0.0, 10.0 }, values: new[] { 0.0, 20.0 });

        evaluation.Lhs.ToDouble().Should().Be(
            0.0, "the clamped input is zero, so the interpolation sits exactly on the boundary");
        evaluation.Lhs.ToDouble().Should().NotBe(
            -1e-6, "interpolating at the raw input would land outside the domain the solver saw");
    }

    /// <summary>
    /// **值列表比段数短时被拒** / Too few values for the segments is rejected.
    /// </summary>
    /// <remarks>
    /// **`prepareNativePiecewise` 会保证 `Values.Count == Breakpoints.Count`，
    /// 但 `NormalizedPiecewise` 可以被直接构造。**
    ///
    /// **那时这一层是唯一会拦的地方——而"越界"比"插出个错值"更容易查。**
    ///
    /// **`prepareNativePiecewise` guarantees the counts match, but `NormalizedPiecewise` can be built directly.**
    ///
    /// **Then this layer is the only guard, and an out-of-range is easier to diagnose than a wrong interpolated
    /// number.**
    /// </remarks>
    [Fact]
    public void TooFewValues_ShouldBeRejected() {
        Result<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            NativePiecewiseEvaluation.Piecewise(
                new[] { new Flt64(7.0), new Flt64(5.0) },
                new Dictionary<VariableItemKey, int> { [Input.Key] = 0, [Result.Key] = 1 },
                new[] { Piecewise(new[] { 0.0, 5.0, 10.0 }, new[] { 0.0 }) },
                "gurobi");

        ((Failed<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("fewer values than segments");
    }

    /// <summary>
    /// **ABS 列校验：输入与结果必须<b>在</b>，辅助列必须<b>不在** / ABS columns: input and result present, helpers absent.
    /// </summary>
    /// <remarks>
    /// **这与"解里不该有选择列"是同一件事的两个层次：那里查<b>值</b>，这里查<b>列</b>。**
    /// **一条在解投影之后，一条在列映射阶段——所以两处都需要，不能互相替代。**
    ///
    /// **This is the earlier "the solution must omit selectors" at a different level: there it checks <b>values</b>, here
    /// <b>columns</b>.** **One runs after solution projection and one at column mapping, so both are needed.**
    /// </remarks>
    [Fact]
    public void AbsColumns_ShouldBeCheckedBothWays() {
        var complete = new Dictionary<VariableItemKey, int> {
            [Input.Key] = 0,
            [AbsResult.Key] = 1,
        };
        NativePiecewiseEvaluation.ValidateAbsColumns(complete, new[] { Abs() }).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        var missingInput = new Dictionary<VariableItemKey, int> { [AbsResult.Key] = 1 };
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)
            NativePiecewiseEvaluation.ValidateAbsColumns(missingInput, new[] { Abs() }))
            .Error.Message.Should().Contain("input column is missing");

        var missingResult = new Dictionary<VariableItemKey, int> { [Input.Key] = 0 };
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)
            NativePiecewiseEvaluation.ValidateAbsColumns(missingResult, new[] { Abs() }))
            .Error.Message.Should().Contain("result column is missing");

        // **三种辅助列各测一次：只测一种的话另外两条分支可以整段缺失。**
        // **All three helper columns, once each: with one, the other two branches could be deleted unnoticed.**
        foreach (IVariableItem helper in new IVariableItem[] { Positive, Negative, Sign }) {
            var withHelper = new Dictionary<VariableItemKey, int> {
                [Input.Key] = 0,
                [AbsResult.Key] = 1,
                [helper.Key] = 2,
            };

            ((Failed<Success, ErrorCode, Error<ErrorCode>>)
                NativePiecewiseEvaluation.ValidateAbsColumns(withHelper, new[] { Abs() }))
                .Error.Message.Should().Contain("still present in native columns");
        }
    }

    /// <summary>
    /// **缺 token 时消息按类区分** / Missing tokens are named per kind.
    /// </summary>
    [Fact]
    public void MissingTokens_ShouldBeReportedPerKind() {
        ((Failed<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>)
            NativePiecewiseEvaluation.Piecewise(
                new[] { new Flt64(1.0) },
                new Dictionary<VariableItemKey, int> { [Input.Key] = 0 },
                new[] { Piecewise(new[] { 0.0, 10.0 }, new[] { 0.0, 20.0 }) }, "gurobi"))
            .Error.Message.Should().Contain("PWL result token is missing");
    }

    /// <summary>
    /// **空结构列表给出空结果** / An empty structure list gives an empty result.
    /// </summary>
    [Fact]
    public void EmptyStructures_ShouldGiveEmptyResults() {
        NativePiecewiseEvaluation.Piecewise(
                Array.Empty<Flt64>(), new Dictionary<VariableItemKey, int>(),
                Array.Empty<NormalizedPiecewise>(), "gurobi")
            .Value.Should().BeEmpty();

        NativePiecewiseEvaluation.ValidateAbsColumns(
                new Dictionary<VariableItemKey, int>(), Array.Empty<NormalizedAbs>())
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    private static ConstraintEvaluation<Flt64> Single(
        double input, double result, double[] breakpoints, double[] values) {
        Result<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>> evaluations =
            NativePiecewiseEvaluation.Piecewise(
                new[] { new Flt64(input), new Flt64(result) },
                new Dictionary<VariableItemKey, int> { [Input.Key] = 0, [Result.Key] = 1 },
                new[] { Piecewise(breakpoints, values) },
                "gurobi");

        evaluations.Should().BeOfType<
            Ok<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        return evaluations.Value[0];
    }

    private static NormalizedPiecewise Piecewise(double[] breakpoints, double[] values) {
        var points = new Flt64[breakpoints.Length];
        for (int index = 0; index < breakpoints.Length; ++index) {
            points[index] = new Flt64(breakpoints[index]);
        }

        var ys = new Flt64[values.Length];
        for (int index = 0; index < values.Length; ++index) {
            ys[index] = new Flt64(values[index]);
        }

        var selectors = new VariableItemKey[global::System.Math.Max(breakpoints.Length - 1, 0)];
        for (int index = 0; index < selectors.Length; ++index) {
            selectors[index] = index == 0 ? Selector1.Key : Selector2.Key;
        }

        return new NormalizedPiecewise(
            InputKey: Input.Key, ResultKey: Result.Key, SelectorKeys: selectors,
            Breakpoints: points, Values: ys);
    }

    private static NormalizedAbs Abs() =>
        new(InputKey: Input.Key, ResultKey: AbsResult.Key, PositiveKey: Positive.Key,
            NegativeKey: Negative.Key, SignKey: Sign.Key);

    private static readonly RealVar Input = new("pe-in");

    private static readonly RealVar Result = new("pe-r");

    private static readonly RealVar AbsResult = new("pe-absr");

    private static readonly RealVar Positive = new("pe-p");

    private static readonly RealVar Negative = new("pe-n");

    private static readonly BinVar Sign = new("pe-s");

    private static readonly BinVar Selector1 = new("pe-sel1");

    private static readonly BinVar Selector2 = new("pe-sel2");
}
