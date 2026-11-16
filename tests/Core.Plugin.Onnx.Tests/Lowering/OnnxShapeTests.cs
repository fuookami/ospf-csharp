#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **形状算术：底座错一处，七个算子在错** / Shape arithmetic: one fault here faults seven operators.
/// </summary>
/// <remarks>
/// 里 `OnnxShape`。**`Transpose`／`Reshape`／`Flatten`／`Concat`／`Split`／`Gather`／
/// `ReduceSum` 共用它，而它的错法都属于"不抛异常但算错"**——**步长错只是取到别的元素，广播错只是形状多一维。**
///
/// Syncs `OnnxShape`. **Seven operators share it, and its mistakes are all of the "wrong answer, no exception" kind — a wrong stride fetches another element, a
/// wrong broadcast adds a dimension.**
/// </remarks>
public class OnnxShapeTests
{
    /// <summary>**行主序步长从最后一维往前累乘** / Row-major strides multiply from the last dimension backwards.</summary>
    /// <remarks>
    /// **写成 `strides[i] = shape[i] * strides[i-1]` 会得到一组"看起来合理"的数组，** **而归一化后最末维必须恒为 1——**
    /// **任何末维不为 1 的步长表都必然错。** 判据两条一起钉：具体数值与末维恒 1。
    ///
    /// **Writing `strides[i] = shape[i] * strides[i-1]` yields a plausible-looking array, while the last stride of a row-major layout must always be 1 — any stride
    /// table whose last entry is not 1 is necessarily wrong.** Both are pinned.
    /// </remarks>
    [Fact]
    public void Strides_ShouldBeRowMajor() {
        OnnxShape.Strides(new[] { 2, 3, 4 }).Should().Equal(new[] { 12, 4, 1 });
        OnnxShape.Strides(new[] { 5 }).Should().Equal(new[] { 1 }, "a rank-1 tensor has a single stride of 1");
        OnnxShape.Strides(System.Array.Empty<int>()).Should().BeEmpty("a scalar has no strides");

        foreach (int[] shape in new[] { new[] { 2, 3, 4 }, new[] { 7, 1, 8 }, new[] { 1, 1, 1, 1 }, new[] { 9, 2 } }) {
            int[] strides = OnnxShape.Strides(shape);
            strides[^1].Should().Be(1, "a row-major layout always has a last stride of 1, whatever the shape");
            (strides[0] * shape[0]).Should().Be(
                OnnxShape.ElementCount(shape),
                "the first stride times the first dimension is the whole tensor");
        }
    }

    /// <summary>**坐标与展平索引互为逆运算** / Coordinates and flatten are inverses.</summary>
    /// <remarks>
    /// **遍历全部索引做往返，而不是抽查几个点：** **错位往往只在某几维上出现，抽查容易正好避开。**
    ///
    /// **The round trip walks every index rather than sampling a few: a shift often affects only certain dimensions, and sampling easily misses it.**
    /// </remarks>
    [Fact]
    public void CoordinatesAndFlatten_ShouldRoundTripEveryIndex() {
        int[] shape = { 2, 3, 4 };
        int[] strides = OnnxShape.Strides(shape);
        int total = OnnxShape.ElementCount(shape);

        for (int index = 0; index < total; ++index) {
            int[] coordinates = OnnxShape.Coordinates(index, strides);
            coordinates.Should().HaveCount(3);
            for (int dimension = 0; dimension < 3; ++dimension) {
                coordinates[dimension].Should().BeInRange(0, shape[dimension] - 1, "every coordinate has to stay inside its dimension");
            }
            OnnxShape.Flatten(coordinates, strides).Should().Be(index, "the round trip must return exactly where it started");
        }
    }

    /// <summary>**`[2, 3]` 与 `[3]` 广播成 `[2, 3]`：从末尾对齐** / Broadcast aligns from the end.</summary>
    /// <remarks>
    /// **从开头对齐会把 `[3]` 贴到第一维上，** **于是得到 `[6]` 或直接拒绝——** **而正确的答案是 `[2, 3]`，最后一维拉伸。**
    ///
    /// **Aligning from the start would attach `[3]` to the first dimension, yielding `[6]` or an outright refusal, while the right answer is `[2, 3]` with the
    /// last dimension stretched.**
    /// </remarks>
    [Fact]
    public void Broadcast_ShouldAlignFromTheEnd() {
        OnnxShape.BroadcastShape(new[] { 2, 3 }, new[] { 3 }, out string? failure).Should().Equal(new[] { 2, 3 });
        failure.Should().BeNull();

        OnnxShape.BroadcastShape(new[] { 5, 1, 4 }, new[] { 3, 1 }, out _).Should().Equal(new[] { 5, 3, 4 },
            "a dimension of 1 stretches; it does not participate in the comparison");

        OnnxShape.BroadcastShape(new[] { 2, 3 }, new[] { 4 }, out string? bad).Should().BeNull("3 and 4 cannot broadcast");
        bad.Should().NotBeNull();
    }

    /// <summary>**轴越界返回 null，不取模折回** / An out-of-range axis returns null rather than wrapping.</summary>
    /// <remarks>
    /// **`axis % rank` 会把 `axis = rank` 静默折回 0：** **而 `axis = rank` 是明显写错的模型，** **折回之后它在第一维上悄悄算完，谁也不会发现。**
    ///
    /// **`axis % rank` folds `axis = rank` back to 0, while `axis = rank` is a plainly wrong model — folding lets it quietly finish on the first dimension and
    /// nobody notices.**
    /// </remarks>
    [Fact]
    public void NormalizeAxis_ShouldRefuseInsteadOfWrapping() {
        OnnxShape.NormalizeAxis(0, 3).Should().Be(0);
        OnnxShape.NormalizeAxis(2, 3).Should().Be(2);
        OnnxShape.NormalizeAxis(-1, 3).Should().Be(2, "a negative axis counts from the end");
        OnnxShape.NormalizeAxis(-3, 3).Should().Be(0);

        OnnxShape.NormalizeAxis(3, 3).Should().BeNull("rank is one past the last axis and must not wrap to 0");
        OnnxShape.NormalizeAxis(-4, 3).Should().BeNull();
        OnnxShape.NormalizeAxis(0, 0).Should().BeNull("a scalar has no axes at all");
    }

    /// <summary>**空 `perm` 是反转各维，不是不变** / An empty `perm` reverses the dimensions.</summary>
    /// <remarks>
    /// **把空 `perm` 当成"不变"会让 3 阶张量整体不转置，而下游仍然能算完——** **只是每个元素都来自另一个位置，** **而模型给出的解看起来完全正常。**
    ///
    /// **Treating an empty `perm` as identity leaves a rank-3 tensor untransposed while the downstream still completes — every element simply comes from another
    /// position, and the model's answer looks perfectly normal.**
    /// </remarks>
    [Fact]
    public void Transpose_ShouldReverseWhenPermIsEmpty() {
        OnnxShape.TransposeShape(new[] { 2, 3, 4 }, System.Array.Empty<long>(), out string? failure)
            .Should().Equal(new[] { 4, 3, 2 }, "an empty permutation reverses every dimension");
        failure.Should().BeNull();
    }

    /// <summary>**显式 `perm` 按置换取维，且必须是排列** / An explicit permutation is applied, and must be a permutation.</summary>
    [Fact]
    public void Transpose_ShouldApplyThePermutationAndRefuseNonPermutations() {
        OnnxShape.TransposeShape(new[] { 2, 3, 4 }, new long[] { 1, 0, 2 }, out _).Should().Equal(new[] { 3, 2, 4 });
        OnnxShape.TransposeShape(new[] { 2, 3, 4 }, new long[] { -1, 0, 1 }, out _).Should().Equal(new[] { 4, 2, 3 },
            "negative entries count from the end");

        OnnxShape.TransposeShape(new[] { 2, 3, 4 }, new long[] { 0, 0, 1 }, out string? repeated).Should().BeNull(
            "a repeated axis is not a permutation and would drop a dimension");
        repeated.Should().NotBeNull();

        OnnxShape.TransposeShape(new[] { 2, 3, 4 }, new long[] { 0, 1 }, out string? wrongLength).Should().BeNull(
            "a short permutation would leave a dimension unplaced");
        wrongLength.Should().NotBeNull();

        OnnxShape.TransposeShape(new[] { 2, 3, 4 }, new long[] { 0, 1, 5 }, out _).Should().BeNull("an axis past the rank is not a permutation either");
    }

    /// <summary>**转置后的元素计数不变** / A transpose preserves the element count.</summary>
    [Fact]
    public void Transpose_ShouldPreserveTheElementCount() {
        int[] shape = { 2, 3, 4 };
        int[] target = OnnxShape.TransposeShape(shape, new long[] { 2, 1, 0 }, out _)!;

        OnnxShape.ElementCount(target).Should().Be(OnnxShape.ElementCount(shape),
            "a transpose moves elements, it does not create or destroy them");
    }

    /// <summary>**标量的元素个数是 1** / A scalar has one element.</summary>
    [Fact]
    public void AScalar_ShouldHaveOneElement() {
        OnnxShape.ElementCount(System.Array.Empty<int>()).Should().Be(1, "zero dimensions means exactly one element");
        OnnxShape.ElementCount(new[] { 0 }).Should().Be(0, "an empty dimension means no elements, which is a different thing");
    }
}