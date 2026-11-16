#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **只改形状的那组算子：数值一个都不动，所以错全在形状上** / The shape-only group: no value moves, so every mistake lives in the shape.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxShapeOperators` 在逐节点求值里的那一半。**正因为"数值不动"，它们的错最容易漏：**
/// **元素个数对不上时不会报错，只是后面某个算子按错的维度去接，而错位之后每个维度都跟着错。**
///
/// Syncs the node-by-node half of `OnnxShapeOperators`. **Precisely because no value moves, their mistakes slip through most easily: a mismatched element count raises
/// nothing, a later operator simply wires to the wrong rank, and every dimension is off after that.**
/// </remarks>
public class OnnxShapeTransformTests
{
    /// <summary>**`Reshape` 保持表达式、换掉形状** / Reshape keeps the expression and swaps the shape.</summary>
    [Fact]
    public void Reshape_ShouldSwapTheShape() {
        OnnxShapeTransform.Result result = OnnxShapeTransform.Compute("Reshape", new[] { 2, 3 }, new[] { 6 });

        result.IsOk.Should().BeTrue(result.Failure);
        result.Shape.Should().Equal(new[] { 6 });
    }

    /// <summary>**元素个数不守恒被拒绝** / A reshape that changes the element count is refused.</summary>
    /// <remarks>
    /// **这是这一组唯一必须在导入期挡住的东西：** **`[2,3]` 变 `[4]` 不会在任何一次求解里暴露，** **只是后面按 4 去接而实际有 6 个——**
    /// **而模型照常求解。**
    ///
    /// **This is the one thing the group has to stop at import time: `[2,3]` becoming `[4]` never shows up in a solve, a later operator simply wires to 4 while six exist —
    /// and the model still solves.**
    /// </remarks>
    [Fact]
    public void AReshapeThatChangesTheCount_ShouldBeRefused() {
        OnnxShapeTransform.Result result = OnnxShapeTransform.Compute("Reshape", new[] { 2, 3 }, new[] { 4 });

        result.IsOk.Should().BeFalse();
        result.Failure.Should().Contain("6").And.Contain("4", "both counts have to appear, or the reader cannot tell which side is wrong");
    }

    /// <summary>**`-1` 推断出正确的那一维** / `-1` infers the right dimension.</summary>
    [Fact]
    public void TheInferredDimension_ShouldBeResolved() {
        OnnxShapeTransform.Compute("Reshape", new[] { 2, 3 }, new[] { -1, 3 }).Shape.Should().Equal(new[] { 2, 3 });
        OnnxShapeTransform.Compute("Reshape", new[] { 4, 5 }, new[] { 2, -1 }).Shape.Should().Equal(new[] { 2, 10 });
        OnnxShapeTransform.Compute("Reshape", new[] { 4, 5 }, new[] { -1 }).Shape.Should().Equal(new[] { 20 });
    }

    /// <summary>**两个 `-1` 或不能整除时被拒绝** / Two `-1`s or a non-dividing count is refused.</summary>
    [Fact]
    public void AmbiguousInference_ShouldBeRefused() {
        OnnxShapeTransform.Compute("Reshape", new[] { 2, 3 }, new[] { -1, -1 }).IsOk.Should().BeFalse();
        OnnxShapeTransform.Compute("Reshape", new[] { 2, 3 }, new[] { -1, 4 }).IsOk.Should().BeFalse(
            "six does not divide into four, so the inferred dimension would not be an integer");
        OnnxShapeTransform.Compute("Reshape", new[] { 2, 3 }, null).IsOk.Should().BeFalse(
            "a target that is not constant leaves the import time with nothing to reshape into");
    }

    /// <summary>**`Transpose` 缺省是逆序，给定则按置换** / Transpose reverses by default and permutes when asked.</summary>
    /// <remarks>
    /// **缺省逆序不是"随便选一个"，是 ONNX 的规定：** **`[2,3,4]` 缺省得到 `[4,3,2]`。** '
    /// **若实现缺省返回原形状，模型会静默地少一次转置——而它照常求解。**
    ///
    /// **The default reversal is not "pick one", it is what ONNX specifies: `[2,3,4]` yields `[4,3,2]`.** **An implementation returning the original shape quietly drops the
    /// transpose — and the model still solves.**
    /// </remarks>
    [Fact]
    public void Transpose_ShouldReverseByDefault() {
        OnnxShapeTransform.Compute("Transpose", new[] { 2, 3, 4 }).Shape.Should().Equal(new[] { 4, 3, 2 });
        OnnxShapeTransform.Compute("Transpose", new[] { 2, 3, 4 }, axes: new[] { 0, 2, 1 }).Shape.Should().Equal(new[] { 2, 4, 3, });
        OnnxShapeTransform.Compute("Transpose", new[] { 2, 3 }, axes: new[] { 0, 0 }).IsOk.Should().BeFalse(
            "a repeated axis is not a permutation");
        OnnxShapeTransform.Compute("Transpose", new[] { 2, 3 }, axes: new[] { 0 }).IsOk.Should().BeFalse(
            "the permutation length has to match the rank");
    }

    /// <summary>**`Flatten` 把轴两侧各自乘成一个维度** / Flatten multiplies each side of the axis into one dimension.</summary>
    [Fact]
    public void Flatten_ShouldCollapseAroundTheAxis() {
        OnnxShapeTransform.Compute("Flatten", new[] { 2, 3, 4 }).Shape.Should().Equal(new[] { 2, 12 });
        OnnxShapeTransform.Compute("Flatten", new[] { 2, 3, 4 }, axes: new[] { 2 }).Shape.Should().Equal(new[] { 6, 4 });
        OnnxShapeTransform.Compute("Flatten", new[] { 7 }, axes: new[] { 0 }).Shape.Should().Equal(new[] { 1, 7 });
    }

    /// <summary>**`Squeeze` 只删长度为 1 的轴** / Squeeze only drops axes of length one.</summary>
    /// <remarks>
    /// **删掉一个长度为 2 的轴会丢掉元素，而模型照常继续——只是每一条后续索引都错位。**
    ///
    /// **Dropping an axis of length 2 loses elements and the model carries on — every later index is simply off by one.**
    /// </remarks>
    [Fact]
    public void Squeeze_ShouldOnlyDropLengthOne() {
        OnnxShapeTransform.Compute("Squeeze", new[] { 1, 3, 1 }).Shape.Should().Equal(new[] { 3 });
        OnnxShapeTransform.Compute("Squeeze", new[] { 1, 3, 1 }, squeezeAxes: new[] { 0 }).Shape.Should().Equal(new[] { 3, 1 });
        OnnxShapeTransform.Compute("Squeeze", new[] { 2, 3 }, squeezeAxes: new[] { 0 }).IsOk.Should().BeFalse(
            "the axis is not of length one, so elements would be lost");
    }

    /// <summary>**`Unsqueeze` 必须给轴，负轴按新秩换算** / Unsqueeze needs axes, and negative axes use the new rank.</summary>
    /// <remarks>
    /// **负轴加的是<b>秩</b>，不是 `秩 + 1`：** **`-1` 在秩为 3 时指第 2 位，而 `3 + 1 - 1 = 3` 会指到界外。**
    ///
    /// **A negative axis adds the rank, not rank + 1: -1 with rank 3 means position 2, while 3 + 1 - 1 = 3 points outside.**
    /// </remarks>
    [Fact]
    public void Unsqueeze_ShouldInsertAxes() {
        OnnxShapeTransform.Compute("Unsqueeze", new[] { 3, 4 }, squeezeAxes: new[] { 0 }).Shape.Should().Equal(new[] { 1, 3, 4 });
        OnnxShapeTransform.Compute("Unsqueeze", new[] { 3, 4 }, squeezeAxes: new[] { -1 }).Shape.Should().Equal(new[] { 3, 4, 1 },
            "a negative axis counts from the new rank, so -1 is the last position");
        OnnxShapeTransform.Compute("Unsqueeze", new[] { 3, 4 }).IsOk.Should().BeFalse(
            "there is no sensible default for where to insert");
        OnnxShapeTransform.Compute("Unsqueeze", new[] { 3, 4 }, squeezeAxes: new[] { 0, 0 }).IsOk.Should().BeFalse();
    }

    /// <summary>**会重排元素的算子在多元素张量上被拒绝** / An order-changing operator on a multi-element tensor is refused.</summary>
    /// <remarks>
    /// **基线里 `transpose` 会按坐标重排 `input.elements`，而这一层每个绑定只有一个标量表达式加一个形状。** '
    /// **形状换了、表达式没换——** **下游若按位置去接，拿到的就是错位的元素，而模型照常求解。** '
    /// **所以这里不是"还没做"，是"这一层表达不了"。**
    ///
    /// **In the baseline transpose permutes input.elements by coordinate, while each binding here holds one scalar expression plus a shape.** **The shape changes and the
    /// expression does not, and a positional downstream then reads misaligned elements while the model still solves.** **So this is not "not done yet" — this layer cannot
    /// express it.**
    /// </remarks>
    [Fact]
    public void AnOrderChangingOperator_ShouldBeRefusedOnMultiElement() {
        OnnxShapeTransform.CanApply("Transpose", new[] { 2, 3 }, out string? failure).Should().BeFalse();
        failure.Should().Contain("重排").And.Contain("元素");

        OnnxShapeTransform.CanApply("Reshape", new[] { 2, 3 }, out _).Should().BeTrue("a reshape keeps the element order");
        OnnxShapeTransform.CanApply("Flatten", new[] { 2, 3 }, out _).Should().BeTrue();
        OnnxShapeTransform.CanApply("Squeeze", new[] { 1, 3 }, out _).Should().BeTrue();
        OnnxShapeTransform.CanApply("Unsqueeze", new[] { 3 }, out _).Should().BeTrue();
    }

    /// <summary>**单元素张量上重排没有意义，所以放过** / On a one-element tensor the order means nothing, so it passes.</summary>
    /// <remarks>
    /// **放过它是因为它真的无害，不是因为"反正没人检查"。**
    ///
    /// **It passes because it is genuinely harmless, not because nobody checks.**
    /// </remarks>
    [Fact]
    public void AnOrderChangingOperator_ShouldPassOnASingleElement() {
        OnnxShapeTransform.CanApply("Transpose", new[] { 1 }, out string? failure).Should().BeTrue(failure);
        OnnxShapeTransform.CanApply("Transpose", new[] { 1, 1, 1 }, out _).Should().BeTrue();
    }

    /// <summary>**不认识的算子被拒绝** / An operator outside the group is refused.</summary>
    [Fact]
    public void AnOperatorOutsideTheGroup_ShouldBeRefused() {
        OnnxShapeTransform.Compute("Concat", new[] { 2, 3 }).IsOk.Should().BeFalse(
            "Concat needs a per-element representation, which this layer does not have");
    }
}