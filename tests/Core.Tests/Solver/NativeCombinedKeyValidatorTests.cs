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
/// 跨结构键检查：登记范围与引用范围不同 / The claim range and the reference range differ.
/// </summary>
/// <remarks>
/// B1206 按基线 `NativePiecewiseReport.kt` 的 `validateCombinedStructureKeys` 与 `keyText` 补。
///
/// B1206 adds the baseline's `validateCombinedStructureKeys` and `keyText`.
/// </remarks>
public class NativeCombinedKeyValidatorTests
{
    /// <summary>
    /// **三类互不相干时通过** / Disjoint kinds pass.
    /// </summary>
    [Fact]
    public void DisjointStructures_ShouldPass() {
        NativeCombinedKeyValidator.Validate(
                new[] { Piecewise(Result1, Input, Selector1, Selector2) },
                new[] { Abs(Result2, Input2, Pos1, Neg1, Sign1) },
                new[] { Max(Result3, Input3, Selector3, Selector4) })
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **结果列跨结构重复被拒** / A duplicated result column across structures is rejected.
    /// </summary>
    [Fact]
    public void ADuplicatedResultKey_ShouldBeRejected() {
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            NativeCombinedKeyValidator.Validate(
                new[] { Piecewise(Result1, Input, Selector1, Selector2) },
                new[] { Abs(Result1, Input2, Pos1, Neg1, Sign1) },
                Array.Empty<NativeMaxData>());

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should()
            .Contain("duplicated");
    }

    /// <summary>
    /// **三个结构共用同一输入列是合法的** / Three structures may share one input column.
    /// </summary>
    /// <remarks>
    /// **登记的范围是"结果列 + 辅助列"，<b>不含输入列</b>。**
    ///
    /// **所以"同一个量的三个不同函数"这个完全正常的情形必须通过。**
    /// **如果实现把输入列也登记进去，这个情形会被拒——而那会让 native 路径在多函数场景下不可用。**
    ///
    /// **The claim range is "results plus helpers" and <b>excludes input columns</b>.**
    ///
    /// **So "three different functions of one quantity" — a completely normal case — must pass.** **An implementation that
    /// also claimed input columns would reject it, making the native path unusable in multi-function scenarios.**
    /// </remarks>
    [Fact]
    public void ASharedInputColumn_ShouldBeAllowed() {
        NativeCombinedKeyValidator.Validate(
                new[] { Piecewise(Result1, Input, Selector1, Selector2) },
                new[] { Abs(Result2, Input, Pos1, Neg1, Sign1) },
                new[] { Max(Result3, Input, Selector3, Selector4) })
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
                "one quantity may feed three different functions");

        // **再加两个 PWL 共用同一输入：** **这一条是必需的——上面的算例里只有 PWL 会去登记输入列，
        // 所以"把输入列也登记进去"这个错误在那里只登记一次，撞不上。**
        // **两个 PWL 才会撞。**
        //
        // **And two PWL structures sharing one input: this case is necessary — above, only PWL would claim the input
        // column, so the error "claim inputs too" only claims it once and cannot collide. Two PWL structures collide.**
        NativeCombinedKeyValidator.Validate(
                new[] {
                    Piecewise(Result1, Input, Selector1, Selector2),
                    Piecewise(Result2, Input, Selector3, Selector4),
                },
                Array.Empty<NormalizedAbs>(),
                Array.Empty<NativeMaxData>())
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
                "two different functions of one quantity is the same normal case");
    }

    /// <summary>
    /// **拿别人的<b>结果</b>列当输入是合法的，拿别人的<b>辅助</b>列则不是** /
    /// Reading someone's <b>result</b> is legal; reading their <b>helper</b> is not.
    /// </summary>
    /// <remarks>
    /// **这是本批最有内容的一条。**
    ///
    /// **结果列是真实输出，不会被消掉，所以拿它当输入是合法的——这正是"链式函数"这个正常情形。**
    /// **辅助列在建模后会被消掉（它们的值由其他列决定），所以拿它们当输入，
    /// 会在消元之后留下一个悬空引用。**
    ///
    /// **判据把两种情形并排：`abs` 的输入读 PWL 的**结果**（必须通过）、
    /// `abs` 的输入读 PWL 的**选择列**（必须被拒）。**
    ///
    /// **The batch's most substantive criterion.**
    ///
    /// **A result column is a real output and is not eliminated, so reading it is legal — the normal case of chained
    /// functions.** **Helper columns are eliminated after modelling (their values follow from other columns), so reading
    /// one leaves a dangling reference once elimination runs.**
    ///
    /// **The criterion puts the two cases side by side: `abs` reading PWL's <b>result</b> (must pass) and `abs` reading
    /// PWL's <b>selector</b> (must be rejected).**
    /// </remarks>
    [Fact]
    public void ReadingAResultIsLegalButReadingAHelperIsNot() {
        // **读别人的结果列：合法（链式函数）。**
        // **Reading another structure's result: legal (chained functions).**
        NativeCombinedKeyValidator.Validate(
                new[] { Piecewise(Result1, Input, Selector1, Selector2) },
                new[] { Abs(Result2, Result1, Pos1, Neg1, Sign1) },
                Array.Empty<NativeMaxData>())
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        // **读别人的选择列：拒绝（那是会被消掉的辅助列）。**
        // **Reading another structure's selector: rejected (an eliminated helper).**
        Result<Success, ErrorCode, Error<ErrorCode>> readingHelper =
            NativeCombinedKeyValidator.Validate(
                new[] { Piecewise(Result1, Input, Selector1, Selector2) },
                new[] { Abs(Result2, Selector1, Pos1, Neg1, Sign1) },
                Array.Empty<NativeMaxData>());

        readingHelper.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)readingHelper).Error.Message.Should()
            .Contain("omitted helper");
    }

    /// <summary>
    /// **三类结构各自的"读辅助列"都能被报出** / Each kind's helper-reading is reported.
    /// </summary>
    /// <remarks>
    /// **三条引用检查是三个独立循环，所以三条都要测——
    /// 只测一种的话，另外两条可以整段缺失而不被发现。**
    ///
    /// **The three reference checks are three separate loops, so all three must be tested; otherwise two of them could be
    /// deleted unnoticed.**
    /// </remarks>
    [Fact]
    public void EachKind_ShouldReportHelperReading() {
        // **PWL 输入读 ABS 的正部列。**
        // **A PWL input reads ABS's positive column.**
        NativeCombinedKeyValidator.Validate(
                new[] { Piecewise(Result1, Pos1, Selector1, Selector2) },
                new[] { Abs(Result2, Input2, Pos1, Neg1, Sign1) },
                Array.Empty<NativeMaxData>())
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();

        // **ABS 输入读 MAX 的选择列。**
        // **An ABS input reads a MAX selector.**
        NativeCombinedKeyValidator.Validate(
                new[] { Piecewise(Result1, Input, Selector1, Selector2) },
                new[] { Abs(Result2, Selector3, Pos1, Neg1, Sign1) },
                new[] { Max(Result3, Input3, Selector3, Selector4) })
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();

        // **MAX 的某个输入项读了 ABS 的符号列。**
        // **One of MAX's input terms reads ABS's sign column.**
        NativeCombinedKeyValidator.Validate(
                Array.Empty<NormalizedPiecewise>(),
                new[] { Abs(Result2, Input2, Pos1, Neg1, Sign1) },
                new[] { Max(Result3, Sign1, Selector3, Selector4) })
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **同一个键在三类之间重复也被拒** / A key duplicated across kinds is rejected.
    /// </summary>
    /// <remarks>
    /// **`owners` 是<b>一个</b>字典，三类共用。**
    /// **所以"PWL 的选择列正好是 MAX 的结果列"这种跨类冲突也会被发现。**
    ///
    /// **`owners` is <b>one</b> dictionary shared by all three kinds, so a cross-kind clash — a PWL selector that is also a
    /// MAX result — is caught too.**
    /// </remarks>
    [Fact]
    public void AKeyDuplicatedAcrossKinds_ShouldBeRejected() {
        NativeCombinedKeyValidator.Validate(
                new[] { Piecewise(Result1, Input, Selector1, Selector2) },
                Array.Empty<NormalizedAbs>(),
                new[] { Max(Selector1, Input3, Selector3, Selector4) })
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "a PWL selector cannot also be a MAX result");
    }

    /// <summary>
    /// **消息只给键、不给占用者** / The message names the key but not the claimants.
    /// </summary>
    /// <remarks>
    /// **这是一个真实的诊断缺口，本批照抄基线不改。**
    ///
    /// **看到 `key is duplicated` 之后，调用方还得自己去找是哪两个结构。**
    /// **而 `owners` 字典里<b>本来就有</b>两个占用者的标签——第一个已经存在，
    /// 第二个就在手上。**
    ///
    /// **判据把这一条断言下来：消息里<b>有</b>键、<b>没有</b> `pwl[` / `abs[` / `max[` 这些标签。**
    /// **这样"消息缺什么"成为一个被记录的事实，而不是一个观感。**
    ///
    /// **A real diagnostic gap, copied from the baseline.** **After `key is duplicated` the caller still has to find the
    /// two structures, even though the `owners` dictionary <b>already holds</b> both labels — the first is stored and the
    /// second is right there.**
    ///
    /// **The criterion pins it down: the message <b>has</b> the key and <b>lacks</b> the `pwl[` / `abs[` / `max[`
    /// labels.** **That makes "what the message omits" a recorded fact rather than an impression.**
    /// </remarks>
    [Fact]
    public void TheMessage_ShouldNameTheKeyButNotTheClaimants() {
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            NativeCombinedKeyValidator.Validate(
                new[] { Piecewise(Result1, Input, Selector1, Selector2) },
                new[] { Abs(Result1, Input2, Pos1, Neg1, Sign1) },
                Array.Empty<NativeMaxData>());

        string message = ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message;

        message.Should().Contain(NativeCombinedKeyValidator.KeyText(Result1.Key));
        message.Should().Contain("duplicated");

        message.Should().NotContain("pwl[");
        message.Should().NotContain("abs[");
        message.Should().NotContain("max[");
    }

    /// <summary>
    /// **`KeyText` 两段都要** / `KeyText` needs both parts.
    /// </summary>
    /// <remarks>
    /// **只给 `identifier` 的话，一个组合变量的多个维度会打印成同一个字符串。**
    ///
    /// **With `identifier` alone, several dimensions of one combination variable print identically.**
    /// </remarks>
    [Fact]
    public void KeyText_ShouldIncludeTheDimensionIndex() {
        var first = new VariableItemKey(new global::Fuookami.Ospf.Math.Algebra.Number.UInt64(7), 0);
        var second = new VariableItemKey(new global::Fuookami.Ospf.Math.Algebra.Number.UInt64(7), 3);

        NativeCombinedKeyValidator.KeyText(first).Should().Be("7:0");
        NativeCombinedKeyValidator.KeyText(second).Should().Be("7:3");
        NativeCombinedKeyValidator.KeyText(first).Should().NotBe(
            NativeCombinedKeyValidator.KeyText(second),
            "two dimensions of one combination must not render the same");
    }

    /// <summary>
    /// **空输入通过** / Empty input passes.
    /// </summary>
    [Fact]
    public void EmptyLists_ShouldPass() {
        NativeCombinedKeyValidator.Validate(
                Array.Empty<NormalizedPiecewise>(),
                Array.Empty<NormalizedAbs>(),
                Array.Empty<NativeMaxData>())
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    private static NormalizedPiecewise Piecewise(
        IVariableItem result, IVariableItem input, params IVariableItem[] selectors) {
        var keys = new VariableItemKey[selectors.Length];
        for (int index = 0; index < selectors.Length; ++index) {
            keys[index] = selectors[index].Key;
        }

        return new NormalizedPiecewise(
            InputKey: input.Key, ResultKey: result.Key, SelectorKeys: keys,
            Breakpoints: Array.Empty<Flt64>(), Values: Array.Empty<Flt64>());
    }

    private static NormalizedAbs Abs(
        IVariableItem result, IVariableItem input, IVariableItem positive,
        IVariableItem negative, IVariableItem sign) =>
        new(InputKey: input.Key, ResultKey: result.Key, PositiveKey: positive.Key,
            NegativeKey: negative.Key, SignKey: sign.Key);

    private static NativeMaxData Max(
        IVariableItem result, IVariableItem input, params IVariableItem[] selectors) {
        var keys = new VariableItemKey[selectors.Length];
        for (int index = 0; index < selectors.Length; ++index) {
            keys[index] = selectors[index].Key;
        }

        var terms = new Dictionary<VariableItemKey, double> { [input.Key] = 1.0 };

        return new NativeMaxData(
            Inputs: new[] { new NativeMaxInputData(terms, 0.0, 0.0, 1.0) },
            ResultKey: result.Key, SelectorKeys: keys,
            BigMValues: new[] { 100.0 }, Name: "max");
    }

    private static readonly RealVar Input = new("ck-in");

    private static readonly RealVar Input2 = new("ck-in2");

    private static readonly RealVar Input3 = new("ck-in3");

    private static readonly RealVar Result1 = new("ck-r1");

    private static readonly RealVar Result2 = new("ck-r2");

    private static readonly RealVar Result3 = new("ck-r3");

    private static readonly RealVar Pos1 = new("ck-p1");

    private static readonly RealVar Neg1 = new("ck-n1");

    private static readonly BinVar Sign1 = new("ck-s1");

    private static readonly BinVar Selector1 = new("ck-sel1");

    private static readonly BinVar Selector2 = new("ck-sel2");

    private static readonly BinVar Selector3 = new("ck-sel3");

    private static readonly BinVar Selector4 = new("ck-sel4");
}
