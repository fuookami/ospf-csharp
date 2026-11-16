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
/// native 求值：两处沿用下来的不对称 / Two asymmetries copied from the code.
/// </summary>
/// <remarks>
/// 对齐的 `absEvaluations` 与 `maxEvaluations`。
///
/// Adds </remarks>
public class NativeConstraintEvaluationsTests
{
    /// <summary>
    /// **ABS 的求值形状：`lhs = |input|`、`rhs = result`、`slack = result − lhs`** /
    /// The ABS shape: `lhs = |input|`, `rhs = result`, `slack = result - lhs`.
    /// </summary>
    [Fact]
    public void AbsEvaluations_ShouldHaveTheExpectedShape() {
        Result<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            NativeConstraintEvaluations.Abs(
                values: new[] { new Flt64(-4.0), new Flt64(4.0) },
                columns: new Dictionary<VariableItemKey, int> {
                    [Input.Key] = 0,
                    [AbsResult.Key] = 1,
                },
                structures: new[] { Abs() },
                backendName: "gurobi");

        result.Value.Should().HaveCount(1);
        ConstraintEvaluation<Flt64> evaluation = result.Value[0];
        evaluation.Lhs.ToDouble().Should().Be(4.0, "the expected value is the absolute value");
        evaluation.Rhs.ToDouble().Should().Be(4.0);
        evaluation.Relation.Should().Be(SolveConstraintRelation.Equal);
        evaluation.Slack.ToDouble().Should().Be(0.0);
        evaluation.Violation.ToDouble().Should().Be(0.0);
        evaluation.Satisfied.Should().BeTrue();
        evaluation.ConstraintId.Value.Should().Be(
            $"gurobi-abs:{NativeCombinedKeyValidator.KeyText(AbsResult.Key)}");
    }

    /// <summary>
    /// **容差边界：恰好等于容差算满足，稍微超过就算不满足** /
    /// The tolerance boundary: exactly the tolerance is satisfied, slightly over is not.
    /// </summary>
    /// <remarks>
    /// **`violationValue <= tolerance` 而不是 `<`。**
    ///
    /// **判据用 `violation` 恰为 `1e-6` 与 `1e-6 + 1e-9` 这一对——
    /// 只测 0 与一个大值的话，`<=` 与 `<` 区分不开。**
    ///
    /// **`violationValue <= tolerance`, not `<`.** **The criterion uses the pair "violation exactly `1e-6`" and "`1e-6 +
    /// 1e-9`" — with only zero and a large value, `<=` and `<` are indistinguishable.**
    /// </remarks>
    [Fact]
    public void TheToleranceBoundary_ShouldBeInclusive() {
        // **`input = 0`、`result = 1e-6`：期望值是 0，违反量恰为 1e-6。**
        // **input = 0, result = 1e-6: the expected value is zero and the violation is exactly 1e-6.**
        ConstraintEvaluation<Flt64> at = Single(
            new[] { new Flt64(0.0), new Flt64(1e-6) },
            new Dictionary<VariableItemKey, int> { [Input.Key] = 0, [AbsResult.Key] = 1 },
            Abs());
        at.Violation.ToDouble().Should().Be(1e-6);
        at.Satisfied.Should().BeTrue("the comparison is <=");

        ConstraintEvaluation<Flt64> over = Single(
            new[] { new Flt64(0.0), new Flt64(1e-6 + 1e-9) },
            new Dictionary<VariableItemKey, int> { [Input.Key] = 0, [AbsResult.Key] = 1 },
            Abs());
        over.Satisfied.Should().BeFalse();
    }

    /// <summary>
    /// **MAX 求值<b>忽略</b> `minimum` 标志** / MAX evaluation <b>ignores</b> the `minimum` flag.
    /// </summary>
    /// <remarks>
    /// **这是这里最要紧的一条，它记录的是一处<b>基线缺陷</b>。**
    ///
    /// **`NativeMaxData` 带着 `minimum`，`restoreValues` 按它取极值，
    /// 而 `maxEvaluations` <b>没看这个标志</b>——永远取最大值。**
    ///
    /// **所以一个 `minimum = true` 的结构会被拿去和<b>最大值</b>比：** **输入是 `10` 与 `3`，
    /// 结果正确地取了 `3`（最小值），而求值器期望 `10`，于是违反量是 `7`、判为不满足。**
    ///
    /// **这里<b>沿用</b>以保持与可比性，并把这一点写成判据。**
    /// **一个被记录下来的缺陷是"已知债"；一个没人测过的缺陷是"潜伏的错"。**
    ///
    /// **The batch's key criterion, and it records a <b>baseline defect</b>.**
    ///
    /// **`NativeMaxData` carries `minimum`, `restoreValues` honours it, and `maxEvaluations` <b>does not</b> — it always
    /// takes the maximum.**
    ///
    /// **So a `minimum = true` structure is compared against the <b>maximum</b>: inputs `10` and `3`, the result correctly
    /// takes `3` (the minimum), the evaluator expects `10`, the violation is `7` and the evaluation is deemed
    /// unsatisfied.**
    ///
 /// ** <b>copies</b> it to stay comparable with the code and turns it into a criterion.** **A recorded
    /// defect is known debt; an untested one is a latent bug.**
    /// </remarks>
    [Fact]
    public void MaxEvaluation_ShouldIgnoreTheMinimumFlag() {
        ConstraintEvaluation<Flt64> evaluation = Single(
            new[] { new Flt64(10.0), new Flt64(3.0), new Flt64(3.0) },
            new Dictionary<VariableItemKey, int> {
                [A.Key] = 0,
                [B.Key] = 1,
                [MaxResult.Key] = 2,
            },
            MaxOf(minimum: true),
            isMax: true);

        evaluation.Lhs.ToDouble().Should().Be(
            10.0, "the evaluator takes the maximum even though the structure computes a minimum");
        evaluation.Rhs.ToDouble().Should().Be(3.0);
        evaluation.Violation.ToDouble().Should().Be(7.0);
        evaluation.Satisfied.Should().BeFalse(
            "a minimum structure is therefore always reported as violated when it is correct");
    }

    /// <summary>
    /// **但 `restoreValues` <b>是</b>看 `minimum` 的——同一份数据的两种解释** /
    /// `restoreValues` <b>does</b> honour `minimum` — two readings of the same data.
    /// </summary>
    /// <remarks>
    /// **把两条路径并排断言，才能证明缺陷的<b>形状</b>：** **不是"某个函数错了"，
    /// 而是"同一份数据在两条路径上被解释成两种含义"。**
    ///
    /// **恢复路径在最小值结构上选下标 1（`B = 3`）；求值路径给出 `10`。**
    ///
    /// **Asserting the two paths side by side is what proves the <b>shape</b> of the defect: not "some function is wrong"
    /// but "one datum is read two ways on two paths".**
    ///
    /// **The restore path selects index 1 (`B = 3`); the evaluation path reports `10`.**
    /// </remarks>
    [Fact]
    public void TheTwoPaths_ShouldReadTheMinimumFlagDifferently() {
        NativeMaxData minimum = MaxOf(minimum: true);
        var values = new Dictionary<VariableItemKey, int> {
            [A.Key] = 0,
            [B.Key] = 1,
        };

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> restored =
            NativeValueRestoration.Restore(
                values: new[] { new Flt64(10.0), new Flt64(3.0) },
                columns: values,
                originalTokens: new[] { TokenOf(MaxSel1, 2), TokenOf(MaxSel2, 3) },
                structures: Array.Empty<NormalizedPiecewise>(),
                absStructures: Array.Empty<NormalizedAbs>(),
                maxStructures: new[] { minimum },
                semiStructures: Array.Empty<Fuookami.Ospf.Core.Model.Intermediate.SemiStructure>());

        restored.Value[0].ToDouble().Should().Be(
            0.0, "the restore path honours the flag and selects the smaller input");
        restored.Value[1].ToDouble().Should().Be(1.0);

        ConstraintEvaluation<Flt64> evaluation = Single(
            new[] { new Flt64(10.0), new Flt64(3.0), new Flt64(3.0) },
            new Dictionary<VariableItemKey, int> {
                [A.Key] = 0,
                [B.Key] = 1,
                [MaxResult.Key] = 2,
            },
            minimum,
            isMax: true);

        evaluation.Lhs.ToDouble().Should().Be(
            10.0, "the evaluation path ignores the flag, so the same structure reads two ways");
    }

    /// <summary>
    /// **MAX 的容差是字面量 `1e-6`，与 ABS 的来源不同** /
    /// MAX's tolerance is the literal `1e-6`, from a different source than ABS's.
    /// </summary>
    /// <remarks>
    /// **数值上相同（因为 `PIECEWISE_TOLERANCE` 目前就是 `1e-6`），结构上不同。**
    ///
    /// **判据断言两者<b>当前</b>相等，并在注释里写清"这是巧合而不是同源"——
    /// 因为一条测不出差别的判据，至少可以把这个巧合记录成<b>有意为之的巧合</b>。**
    ///
    /// **Numerically identical (since `PIECEWISE_TOLERANCE` is `1e-6` today), structurally different.**
    ///
    /// **The criterion asserts they are <b>currently</b> equal and states in a comment that this is coincidence rather than
    /// shared provenance — because a criterion that cannot see a difference can at least record the coincidence as a
    /// <b>deliberate</b> one.**
    /// </remarks>
    [Fact]
    public void TheTwoTolerances_ShouldBeEqualByCoincidence() {
        ConstraintEvaluation<Flt64> absEvaluation = Single(
            new[] { new Flt64(0.0), new Flt64(0.0) },
            new Dictionary<VariableItemKey, int> { [Input.Key] = 0, [AbsResult.Key] = 1 },
            Abs());

        ConstraintEvaluation<Flt64> maxEvaluation = Single(
            new[] { new Flt64(1.0), new Flt64(1.0), new Flt64(1.0) },
            new Dictionary<VariableItemKey, int> {
                [A.Key] = 0,
                [B.Key] = 1,
                [MaxResult.Key] = 2,
            },
            MaxOf(minimum: false),
            isMax: true);

        absEvaluation.Tolerance.ToDouble().Should().Be(
            NativePiecewiseReport.PiecewiseTolerance,
            "ABS converts the shared constant");
        maxEvaluation.Tolerance.ToDouble().Should().Be(1e-6, "MAX hardcodes the literal");

        absEvaluation.Tolerance.ToDouble().Should().Be(
            maxEvaluation.Tolerance.ToDouble(),
            "equal today only because the constant happens to be 1e-6");
    }

    /// <summary>
    /// **缺 token 时被拒，且 ABS 与 MAX 的消息区分得开** /
    /// Missing tokens are rejected with distinct ABS and MAX messages.
    /// </summary>
    [Fact]
    public void MissingTokens_ShouldBeReportedPerKind() {
        ((Failed<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>)
            NativeConstraintEvaluations.Abs(
                new[] { new Flt64(1.0) },
                new Dictionary<VariableItemKey, int> { [Input.Key] = 0 },
                new[] { Abs() }, "gurobi"))
            .Error.Message.Should().Contain("ABS result token is missing");

        ((Failed<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>)
            NativeConstraintEvaluations.Max(
                new[] { new Flt64(1.0), new Flt64(2.0) },
                new Dictionary<VariableItemKey, int> { [A.Key] = 0, [B.Key] = 1 },
                new[] { MaxOf(minimum: false) }, "gurobi"))
            .Error.Message.Should().Contain("MAX result token is missing");
    }

    /// <summary>
    /// **空结构列表给出空结果** / An empty structure list gives an empty result.
    /// </summary>
    [Fact]
    public void EmptyStructures_ShouldGiveEmptyResults() {
        NativeConstraintEvaluations.Abs(
                Array.Empty<Flt64>(), new Dictionary<VariableItemKey, int>(),
                Array.Empty<NormalizedAbs>(), "gurobi")
            .Value.Should().BeEmpty();

        NativeConstraintEvaluations.Max(
                Array.Empty<Flt64>(), new Dictionary<VariableItemKey, int>(),
                Array.Empty<NativeMaxData>(), "gurobi")
            .Value.Should().BeEmpty();
    }

    private static ConstraintEvaluation<Flt64> Single(
        IReadOnlyList<Flt64> values,
        IReadOnlyDictionary<VariableItemKey, int> columns,
        NormalizedAbs structure) {
        Result<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            NativeConstraintEvaluations.Abs(values, columns, new[] { structure }, "gurobi");
        result.Should().BeOfType<
            Ok<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        return result.Value[0];
    }

    private static ConstraintEvaluation<Flt64> Single(
        IReadOnlyList<Flt64> values,
        IReadOnlyDictionary<VariableItemKey, int> columns,
        NativeMaxData structure,
        bool isMax) {
        _ = isMax;
        Result<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            NativeConstraintEvaluations.Max(values, columns, new[] { structure }, "gurobi");
        result.Should().BeOfType<
            Ok<IReadOnlyList<ConstraintEvaluation<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        return result.Value[0];
    }

    private static NativeMaxData MaxOf(bool minimum) =>
        new(
            Inputs: new[] {
                new NativeMaxInputData(
                    new Dictionary<VariableItemKey, double> { [A.Key] = 1.0 }, 0.0, 0.0, 10.0),
                new NativeMaxInputData(
                    new Dictionary<VariableItemKey, double> { [B.Key] = 1.0 }, 0.0, 0.0, 10.0),
            },
            ResultKey: MaxResult.Key,
            SelectorKeys: new[] { MaxSel1.Key, MaxSel2.Key },
            BigMValues: new[] { 100.0, 100.0 },
            Name: "max",
            Minimum: minimum);

    private static NormalizedAbs Abs() =>
        new(InputKey: Input.Key, ResultKey: AbsResult.Key, PositiveKey: Positive.Key,
            NegativeKey: Negative.Key, SignKey: Sign.Key);

    private static Core.Token.Token<Flt64> TokenOf(IVariableItem variable, int solverIndex) =>
        new(variable, solverIndex,
            new Dictionary<object, Action<bool>>());

    private static readonly RealVar A = new("ev-a");

    private static readonly RealVar B = new("ev-b");

    private static readonly RealVar Input = new("ev-in");

    private static readonly RealVar AbsResult = new("ev-absr");

    private static readonly RealVar Positive = new("ev-p");

    private static readonly RealVar Negative = new("ev-n");

    private static readonly BinVar Sign = new("ev-s");

    private static readonly RealVar MaxResult = new("ev-maxr");

    private static readonly BinVar MaxSel1 = new("ev-msel1");

    private static readonly BinVar MaxSel2 = new("ev-msel2");
}
