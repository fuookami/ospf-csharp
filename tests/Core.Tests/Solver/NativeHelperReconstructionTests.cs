#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 辅助列重建：零点约定与"解里不该有它" / The zero convention and "the solution must not contain it".
/// </summary>
/// <remarks>
/// B1209 按基线 `NativePiecewiseReport.kt` 的 `selectorValues` 与 `absHelperValues` 补。
///
/// B1209 adds the baseline's `selectorValues` and `absHelperValues`.
/// </remarks>
public class NativeHelperReconstructionTests
{
    /// <summary>
    /// **选中的那一段的选择列是 `1`，其余是 `0`** / The selected segment's selector is one, the rest zero.
    /// </summary>
    [Fact]
    public void TheSelectedSegment_ShouldHaveOne() {
        var values = new Dictionary<VariableItemKey, Flt64> {
            [Input.Key] = new Flt64(7.0),
            [Result.Key] = new Flt64(5.0),
        };

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> result =
            NativeHelperReconstruction.SelectorValues(
                values,
                new[] { Piecewise(new[] { 0.0, 5.0, 10.0 }, Selector1, Selector2) });

        result.Should().BeOfType<
            Ok<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>>();
        result.Value[Selector1.Key].ToDouble().Should().Be(0.0);
        result.Value[Selector2.Key].ToDouble().Should().Be(1.0, "seven lies in the second segment");
    }

    /// <summary>
    /// **解里含选择列时被拒** / A solution containing selectors is rejected.
    /// </summary>
    /// <remarks>
    /// **选择列在建模时被消掉了，所以一个"含选择列的解"意味着调用方给的是<b>消元之前</b>的向量。**
    ///
    /// **那时的选择列值与这里重建出来的可能不一致，
    /// 而同一个量有两个来源是最难查的一类错。**
    ///
    /// **A selector is eliminated at model build time, so a solution containing one means the caller passed the
    /// <b>pre-elimination</b> vector.**
    ///
    /// **Its selector values may disagree with what is reconstructed here, and one quantity with two sources is the
    /// hardest kind of mistake to find.**
    /// </remarks>
    [Fact]
    public void ASolutionContainingSelectors_ShouldBeRejected() {
        var values = new Dictionary<VariableItemKey, Flt64> {
            [Input.Key] = new Flt64(7.0),
            [Result.Key] = new Flt64(5.0),
            [Selector1.Key] = new Flt64(1.0),
        };

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> result =
            NativeHelperReconstruction.SelectorValues(
                values,
                new[] { Piecewise(new[] { 0.0, 5.0, 10.0 }, Selector1, Selector2) });

        ((Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("must omit selectors");
    }

    /// <summary>
    /// **缺输入或结果 token 时给出区分得开的消息** / Missing input and result tokens say different things.
    /// </summary>
    /// <remarks>
    /// **两条消息不同很重要：** **缺输入说明"这个结构根本没有可用的自变量"，
    /// 缺结果说明"结构参与了但输出没回来"。** **修法完全不同。**
    ///
    /// **The two messages must differ: a missing input means "this structure has no independent variable at all", while a
    /// missing result means "it participated but produced no output". The fixes are entirely different.**
    /// </remarks>
    [Fact]
    public void MissingTokens_ShouldBeReportedSeparately() {
        var onlyResult = new Dictionary<VariableItemKey, Flt64> {
            [Result.Key] = new Flt64(5.0),
        };
        ((Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)
            NativeHelperReconstruction.SelectorValues(
                onlyResult,
                new[] { Piecewise(new[] { 0.0, 5.0, 10.0 }, Selector1, Selector2) }))
            .Error.Message.Should().Contain("input token is missing");

        var onlyInput = new Dictionary<VariableItemKey, Flt64> {
            [Input.Key] = new Flt64(7.0),
        };
        ((Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)
            NativeHelperReconstruction.SelectorValues(
                onlyInput,
                new[] { Piecewise(new[] { 0.0, 5.0, 10.0 }, Selector1, Selector2) }))
            .Error.Message.Should().Contain("result token is missing");
    }

    /// <summary>
    /// **两个结构的同一选择键给不同值时报冲突** / Two structures disagreeing on one selector key conflict.
    /// </summary>
    /// <remarks>
    /// **这个分支在正常流水线里触发不到：** **跨结构的选择键唯一性已由
    /// `NativeCombinedKeyValidator` 拒掉了。**
    ///
    /// **它是一道<b>防御性</b>检查，只有在直接调用这个函数时才可能命中。**
    /// **判据直接构造那个情形——两个结构共用一个选择键、而它们选中的段不同。**
    ///
    /// **"在正常流水线里触发不到"不等于"不用测"：** **这个函数是一个公开入口，
    /// 而它的契约是"相同的键给相同的值"。**
    ///
    /// **This branch cannot fire on the normal pipeline: cross-structure selector-key uniqueness is already rejected by
    /// `NativeCombinedKeyValidator`.**
    ///
    /// **It is a <b>defensive</b> check, reachable only when the function is called directly.** **The criterion builds
    /// exactly that case — two structures sharing a selector key but selecting different segments.**
    ///
    /// **"Cannot fire on the normal pipeline" is not "need not be tested": this is a public entry point and its contract
    /// is "the same key yields the same value".**
    /// </remarks>
    [Fact]
    public void ConflictingSelectorKeys_ShouldBeReported() {
        // **第一个结构 `[0,5,10]` 在 7 处选第二段；第二个结构 `[0,20]` 在 7 处选第一段。
        // 两者共用 `Selector1`，于是它被要求既是 0 又是 1。**
        //
        // **The first structure `[0,5,10]` selects segment two at 7; the second `[0,20]` selects segment one there.
        // Both share `Selector1`, so it is asked to be both zero and one.**
        var values = new Dictionary<VariableItemKey, Flt64> {
            [Input.Key] = new Flt64(7.0),
            [Result.Key] = new Flt64(5.0),
        };

        var first = new NormalizedPiecewise(
            InputKey: Input.Key, ResultKey: Result.Key,
            SelectorKeys: new[] { Selector1.Key, Selector2.Key },
            Breakpoints: new[] { new Flt64(0.0), new Flt64(5.0), new Flt64(10.0) },
            Values: new[] { new Flt64(0.0), new Flt64(5.0), new Flt64(5.0) });

        var second = new NormalizedPiecewise(
            InputKey: Input.Key, ResultKey: Result2.Key,
            SelectorKeys: new[] { Selector1.Key },
            Breakpoints: new[] { new Flt64(0.0), new Flt64(20.0) },
            Values: new[] { new Flt64(0.0), new Flt64(20.0) });

        var withSecondResult = new Dictionary<VariableItemKey, Flt64>(values) {
            [Result2.Key] = new Flt64(7.0),
        };

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> result =
            NativeHelperReconstruction.SelectorValues(withSecondResult, new[] { first, second });

        result.Should().BeOfType<
            Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("conflicts");
    }

    /// <summary>
    /// **ABS 正部、负部与符号列** / ABS positive, negative and sign.
    /// </summary>
    [Fact]
    public void AbsHelpers_ShouldSplitBySign() {
        AssertHelpers(input: 5.0, positive: 5.0, negative: 0.0, sign: 1.0);
        AssertHelpers(input: -3.0, positive: 0.0, negative: 3.0, sign: 0.0);
    }

    /// <summary>
    /// **零点落在非负那一侧：符号是 `1`，正部是 `0`** /
    /// Zero falls on the nonnegative side: sign is `1`, the positive part is `0`.
    /// </summary>
    /// <remarks>
    /// **这是本批最有内容的一条，因为零点是唯一一个"看起来两可"的点。**
    ///
    /// **`input < 0` 取 `0`，否则取 `1`，所以 `input == 0` 时符号是 `1`。**
    /// **而正部是 `0`（因为 `input > 0` 才取 `input`）。**
    ///
    /// **所以零点处：`positive = 0`、`negative = 0`、`sign = 1`。**
    /// **三者满足 `input = positive - negative` 与 `|input| = positive + negative`，彼此自洽。**
    ///
    /// **如果符号在零点取 `0`，这套关系<b>仍然</b>成立——
    /// 所以它不是靠等式推出来的，而是<b>约定的</b>。**
    /// **约定必须与建模侧的 `sign` 列一致，否则求解器的 `sign` 与报告重建的 `sign` 会差一。**
    ///
    /// **The batch's most substantive criterion, because zero is the one point that looks ambiguous.**
    ///
    /// **`input < 0` gives `0`, otherwise `1`, so `input == 0` signs as `1`, while the positive part is `0` (since only
    /// `input > 0` yields `input`).**
    ///
    /// **At zero: `positive = 0`, `negative = 0`, `sign = 1`.** **All three satisfy `input = positive - negative` and
    /// `|input| = positive + negative`, consistently.**
    ///
    /// **Signing `0` at zero would satisfy the relations <b>just as well</b> — so the choice is not derived from them, it
    /// is a <b>convention</b>.** **It must agree with the modelled `sign` column, otherwise the solver's sign and the
    /// reconstructed one differ by one.**
    /// </remarks>
    [Fact]
    public void Zero_ShouldSignAsOne() =>
        AssertHelpers(input: 0.0, positive: 0.0, negative: 0.0, sign: 1.0);

    /// <summary>
    /// **ABS 不检查"解里含有辅助列"，与选择列的严格程度不同** /
    /// ABS does not reject a solution containing helpers, unlike selectors.
    /// </summary>
    /// <remarks>
    /// **这是一处<b>已知的不对称</b>，本批照抄基线不改。**
    /// **ABS 的辅助列同样是消掉的，但基线没写这条检查；
    /// 单方面加上会让两侧行为分岔。**
    ///
    /// **A <b>known asymmetry</b>, copied from the baseline.** **ABS helpers are eliminated too, but the baseline has no
    /// such check; adding one unilaterally would fork the two sides' behaviour.**
    /// </remarks>
    [Fact]
    public void AbsHelpers_ShouldNotRejectASolutionContainingThem() {
        var values = new Dictionary<VariableItemKey, Flt64> {
            [Input.Key] = new Flt64(5.0),
            [Result.Key] = new Flt64(5.0),
            [Positive.Key] = new Flt64(999.0),
        };

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> result =
            NativeHelperReconstruction.AbsHelperValues(
                values, new[] { Abs(Positive, Negative, Sign) });

        result.Should().BeOfType<
            Ok<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>>(
                "the baseline has no such check for ABS, and this batch copies the baseline");
        result.Value[Positive.Key].ToDouble().Should().Be(
            5.0, "the stale value in the solution is overwritten, not trusted");
    }

    /// <summary>
    /// **ABS 缺 token 时也分开报** / ABS also reports missing tokens separately.
    /// </summary>
    [Fact]
    public void AbsMissingTokens_ShouldBeReportedSeparately() {
        ((Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)
            NativeHelperReconstruction.AbsHelperValues(
                new Dictionary<VariableItemKey, Flt64> { [Result.Key] = new Flt64(1.0) },
                new[] { Abs(Positive, Negative, Sign) }))
            .Error.Message.Should().Contain("ABS input token is missing");

        ((Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)
            NativeHelperReconstruction.AbsHelperValues(
                new Dictionary<VariableItemKey, Flt64> { [Input.Key] = new Flt64(1.0) },
                new[] { Abs(Positive, Negative, Sign) }))
            .Error.Message.Should().Contain("ABS result token is missing");
    }

    /// <summary>
    /// **空结构列表给出空结果** / An empty structure list gives an empty result.
    /// </summary>
    [Fact]
    public void EmptyStructures_ShouldGiveEmptyResults() {
        NativeHelperReconstruction.SelectorValues(
                new Dictionary<VariableItemKey, Flt64>(),
                Array.Empty<NormalizedPiecewise>())
            .Value.Should().BeEmpty();

        NativeHelperReconstruction.AbsHelperValues(
                new Dictionary<VariableItemKey, Flt64>(),
                Array.Empty<NormalizedAbs>())
            .Value.Should().BeEmpty();
    }

    private static void AssertHelpers(double input, double positive, double negative, double sign) {
        var values = new Dictionary<VariableItemKey, Flt64> {
            [Input.Key] = new Flt64(input),
            [Result.Key] = new Flt64(global::System.Math.Abs(input)),
        };

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> result =
            NativeHelperReconstruction.AbsHelperValues(
                values, new[] { Abs(Positive, Negative, Sign) });

        result.Should().BeOfType<
            Ok<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>>();
        result.Value[Positive.Key].ToDouble().Should().Be(positive);
        result.Value[Negative.Key].ToDouble().Should().Be(negative);
        result.Value[Sign.Key].ToDouble().Should().Be(sign);
    }

    private static NormalizedPiecewise Piecewise(
        double[] breakpoints, params IVariableItem[] selectors) {
        var keys = new VariableItemKey[selectors.Length];
        for (int index = 0; index < selectors.Length; ++index) {
            keys[index] = selectors[index].Key;
        }

        var points = new Flt64[breakpoints.Length];
        for (int index = 0; index < breakpoints.Length; ++index) {
            points[index] = new Flt64(breakpoints[index]);
        }

        return new NormalizedPiecewise(
            InputKey: Input.Key, ResultKey: Result.Key, SelectorKeys: keys,
            Breakpoints: points, Values: points);
    }

    private static NormalizedAbs Abs(
        IVariableItem positive, IVariableItem negative, IVariableItem sign) =>
        new(InputKey: Input.Key, ResultKey: Result.Key, PositiveKey: positive.Key,
            NegativeKey: negative.Key, SignKey: sign.Key);

    private static readonly RealVar Input = new("hr-in");

    private static readonly RealVar Result = new("hr-r");

    private static readonly RealVar Result2 = new("hr-r2");

    private static readonly RealVar Positive = new("hr-p");

    private static readonly RealVar Negative = new("hr-n");

    private static readonly BinVar Sign = new("hr-s");

    private static readonly BinVar Selector1 = new("hr-sel1");

    private static readonly BinVar Selector2 = new("hr-sel2");
}
