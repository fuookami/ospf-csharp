#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Error;
using Fuookami.Ospf.Utils.Error;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Error;

/// <summary>
/// **错误必须带得上定位信息** / An error has to carry enough to locate the fault.
/// </summary>
/// <remarks>
/// （13 个变体）。**判据钉两件：**
/// **① 上下文只在存在时出现（`node=null` 这种占位比不写更糟，它看起来像线索却什么也没说）；**
/// **② 每个变体携带自己的错误码，调用方既能按类别分派、也能取回结构化对象。**
///
/// Syncs (13 variants). **Two things are pinned: context appears only when present (a `node=null` placeholder reads like a lead while saying
/// nothing), and every variant carries its own error code so a caller can dispatch by class and still recover the structured object.**
/// </remarks>
public class OnnxErrorTests
{
    /// <summary>**没有上下文时不拼括号** / With no context there are no parentheses.</summary>
    [Fact]
    public void WithoutContext_TheMessageShouldStayClean() {
        var error = new OnnxUnsupportedOperator("LeakyRelu");

        error.FullMessage().Should().Be(error.Message, "appending an empty parenthesis block would look like a bug in the message itself");
        error.FullMessage().Should().NotContain("()");
    }

    /// <summary>**有上下文时逐项拼上** / With context every provided field appears.</summary>
    [Fact]
    public void WithContext_EveryFieldShouldAppear() {
        var error = new OnnxShapeMismatch(
            expected: new long?[] { 1, 3, 224, 224 },
            actual: new long?[] { 1, 3, 112, null },
            nodeName: "conv1",
            tensorName: "input");

        string message = error.FullMessage();
        message.Should().Contain("node=conv1");
        message.Should().Contain("tensor=input");
        message.Should().Contain("shape=[1, 3, 112, ?]", "an unknown dimension renders as ?, not as a zero that looks measured");
        message.Should().NotContain("node=null");
    }

    /// <summary>**未知维度渲染成 `?`，不是 0** / Unknown dimensions render as ?, not as zero.</summary>
    /// <remarks>
    /// **0 是一个合法维度**（空张量），**用它表示"未知"会让两者再也分不开**——而"未知"与"空"在形状推演里走完全不同的分支。
    ///
    /// **Zero is a legitimate dimension** (an empty tensor), **so using it for "unknown" makes the two indistinguishable** — and unknown and empty take
    /// completely different branches in shape inference.
    /// </remarks>
    [Fact]
    public void AnUnknownDimension_ShouldNotLookLikeZero() {
        var error = new OnnxDynamicShape(
            "axis 2 is not fixed",
            tensorName: "hidden",
            shape: new long?[] { 1, null, 0 });

        string message = error.FullMessage();
        message.Should().Contain("shape=[1, ?, 0]", "unknown stays unknown while a real zero stays a zero");
    }

    /// <summary>**每个变体带自己的错误码** / Every variant carries its own code.</summary>
    [Fact]
    public void EachVariant_ShouldCarryItsOwnCode() {
        new OnnxLoadFailed("no such file").ErrorCode.Should().Be(ErrorCode.DeserializationFailed, "a load failure is a deserialisation problem");
        new OnnxInferenceFailed("ort returned 1").ErrorCode.Should().Be(ErrorCode.ApplicationFailed, "an inference failure is an application failure");
        new OnnxResourceClosed("after dispose").ErrorCode.Should().Be(ErrorCode.ApplicationFailed);

        foreach (OnnxError error in new OnnxError[] {
                     new OnnxInvalidModel("cycle in graph"),
                     new OnnxUnsupportedOpset(23, new long[] { 13, 17 }),
                     new OnnxUnsupportedOperator("NonMaxSuppression"),
                     new OnnxUnsupportedDType(14),
                     new OnnxDynamicShape("symbolic"),
                     new OnnxShapeMismatch(new long?[] { 2 }, new long?[] { 3 }),
                     new OnnxBindingMismatch("input arity"),
                     new OnnxOutputSelection("unknown output"),
                     new OnnxUnsupportedAttribute("dilations"),
                     new OnnxDegreeOverflow("degree 4"),
                     new OnnxMissingBound("lower bound absent"),
                 }) {
            error.ErrorCode.Should().NotBe(ErrorCode.None, error.GetType().Name + " must carry a real error code");
            error.FullMessage().Should().NotBeEmpty(error.GetType().Name + " must say something");
        }
    }

    /// <summary>**不支持 opset 的消息里带上实际版本与支持范围** / The opset message carries the actual version and the supported range.</summary>
    [Fact]
    public void TheOpsetError_ShouldNameBothSides() {
        var error = new OnnxUnsupportedOpset(23, new long[] { 13, 17, 21 });

        error.FullMessage().Should().Contain("23", "the version actually found");
        error.FullMessage().Should().Contain("13, 17, 21", "what this plugin does support, so the reader knows what to downgrade to");
        error.Supports.Should().HaveCount(3);
    }
}