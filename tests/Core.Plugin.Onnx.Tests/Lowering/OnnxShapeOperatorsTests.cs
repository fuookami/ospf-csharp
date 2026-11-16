#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **六个形状算子：错一位就静默** / Six shape operators: one off is silent.
/// </summary>
/// <remarks>
/// 。**这些算子只算形状、不实现元素，所以不会抛异常——** **只会在下游让一个约束绑错变量。**
///
/// Syncs. **These operators compute shapes without moving elements, so nothing throws — downstream a constraint simply binds the wrong
/// variable.**
/// </remarks>
public class OnnxShapeOperatorsTests
{
    /// <summary>**`-1` 由元素数推断** / `-1` is inferred from the element count.</summary>
    [Fact]
    public void Reshape_ShouldInferTheMinusOne() {
        OnnxShapeOperators.Reshape(new[] { 2, 3, 4 }, new long[] { 6, -1 }, out string? failure).Should().Equal(new[] { 6, 4 });
        failure.Should().BeNull();
        OnnxShapeOperators.Reshape(new[] { 2, 3, 4 }, new long[] { -1 }, out _).Should().Equal(new[] { 24 });
    }

    /// <summary>**`0` 是沿用同位置的源维度，不是空维度** / `0` reuses the source dimension; it is not an empty dimension.</summary>
    /// <remarks>
    /// **把 `0` 当成"这个维度是空的"会让元素数为 0，** **而后续所有校验都退化通过**——**因为 0 乘任何数都是 0，
    /// "元素数与源不符"这条永远成立不了。**
    ///
    /// **Reading `0` as "this dimension is empty" makes the element count zero, and every later check degenerates into a pass** — **since zero times anything is zero,
    /// "the element count disagrees with the source" can never fire.**
    /// </remarks>
    [Fact]
    public void Reshape_ShouldReuseTheSourceDimensionForZero() {
        OnnxShapeOperators.Reshape(new[] { 2, 3, 4 }, new long[] { 0, 12 }, out string? failure2).Should().Equal(new[] { 2, 12 },
            "a 0 entry copies the source dimension at that position; the other entry carries the rest of the elements");
        failure2.Should().BeNull();
        OnnxShapeOperators.Reshape(new[] { 2, 3, 4 }, new long[] { 0, 4 }, out string? failure).Should().BeNull(
            "2 x 4 is eight elements, not the twenty-four the source holds");
        failure.Should().NotBeNull("and that mismatch has to be reported, not silently accepted");

        OnnxShapeOperators.Reshape(new[] { 2, 3, 4 }, new long[] { 0, 0, 0 }, out _).Should().Equal(new[] { 2, 3, 4 },
            "all zeros means the shape is unchanged");

        OnnxShapeOperators.Reshape(new[] { 2, 3 }, new long[] { 0, 0, 0 }, out string? tooFar).Should().BeNull(
            "a 0 past the source rank has nothing to copy");
        tooFar.Should().NotBeNull();
    }

    /// <summary>**多个 `-1`、非正维度、元素数不符都被拒绝** / Repeated -1, non-positive dims and count mismatches are refused.</summary>
    [Fact]
    public void Reshape_ShouldRefuseWhatCannotBeResolved() {
        OnnxShapeOperators.Reshape(new[] { 2, 3 }, new long[] { -1, -1 }, out string? repeated).Should().BeNull("two unknowns cannot both be inferred");
        repeated.Should().NotBeNull();

        OnnxShapeOperators.Reshape(new[] { 2, 3 }, new long[] { 5, 1 }, out string? mismatch).Should().BeNull("6 elements do not fit into 5");
        mismatch.Should().NotBeNull();

        OnnxShapeOperators.Reshape(new[] { 2, 3 }, new long[] { -2, 3 }, out _).Should().BeNull("a negative dimension other than -1 is not allowed");

        OnnxShapeOperators.Reshape(new[] { 0 }, new long[] { -1 }, out string? zeroSource).Should().BeNull(
            "with zero source elements the -1 cannot be inferred without producing a non-positive dimension");
        zeroSource.Should().NotBeNull();
    }

    /// <summary>**`Flatten` 的 `axis = rank` 合法** / `Flatten` allows `axis = rank`.</summary>
    /// <remarks>
    /// **别的算子在 `axis >= rank` 时越界，`Flatten` 的合法区间多一个端点：** **`axis = 3` 对 3 阶张量是合法的，含义是"左半边什么也不留"，**
    /// **结果是 `[1, 24]`。** **按别的算子那样拒绝，会把这个用例整批挡在门外。**
    ///
    /// **Other operators are out of range at `axis >= rank`, but Flatten's range has one more endpoint: `axis = 3` is legal for a rank-3 tensor and means "the left
    /// half is empty", giving `[1, 24]`. Judging it like the others would turn that whole case away.**
    /// </remarks>
    [Fact]
    public void Flatten_ShouldAllowTheExtraAxis() {
        OnnxShapeOperators.Flatten(new[] { 2, 3, 4 }, 0, out _).Should().Equal(new[] { 1, 24 });
        OnnxShapeOperators.Flatten(new[] { 2, 3, 4 }, 1, out _).Should().Equal(new[] { 2, 12 });
        OnnxShapeOperators.Flatten(new[] { 2, 3, 4 }, 3, out string? failure).Should().Equal(new[] { 24, 1 },
            "axis = rank leaves nothing on the left and is legal");
        failure.Should().BeNull();
        OnnxShapeOperators.Flatten(new[] { 2, 3, 4 }, -1, out _).Should().Equal(new[] { 6, 4 }, "negative counts from the end");

        OnnxShapeOperators.Flatten(new[] { 2, 3, 4 }, 4, out string? outOfRange).Should().BeNull("one past rank is still illegal");
        outOfRange.Should().NotBeNull();
    }

    /// <summary>**`Concat` 的非拼接轴必须一致** / Non-concatenated axes must match.</summary>
    [Fact]
    public void Concat_ShouldRefuseMismatchedOtherAxes() {
        OnnxShapeOperators.Concat(new IReadOnlyList<int>[] { new[] { 2, 3 }, new[] { 4, 3 } }, 0, out string? failure)
            .Should().Equal(new[] { 6, 3 });
        failure.Should().BeNull();

        OnnxShapeOperators.Concat(new IReadOnlyList<int>[] { new[] { 2, 3 }, new[] { 2, 4 } }, 0, out string? bad)
            .Should().BeNull("the second axis disagrees, so the four elements of the second input cannot fit");
        bad.Should().NotBeNull();

        OnnxShapeOperators.Concat(new IReadOnlyList<int>[] { new[] { 2, 3 }, new[] { 2 } }, 0, out _)
            .Should().BeNull("ranks differ and cannot be concatenated as-is");
    }

    /// <summary>**`Split` 的段长必须加得起来** / Split lengths must add up.</summary>
    [Fact]
    public void Split_ShouldRespectTheLengths() {
        IReadOnlyList<int[]>? parts = OnnxShapeOperators.Split(new[] { 6, 4 }, 0, new long[] { 2, 4 }, out string? failure);
        parts.Should().HaveCount(2);
        parts![0].Should().Equal(new[] { 2, 4 });
        parts[1].Should().Equal(new[] { 4, 4 });
        failure.Should().BeNull();

        OnnxShapeOperators.Split(new[] { 6, 4 }, 0, new long[] { 2, 3 }, out string? shortSum).Should().BeNull(
            "5 is not 6: a segment would be silently truncated");
        shortSum.Should().NotBeNull();

        OnnxShapeOperators.Split(new[] { 6, 4 }, 0, new long[] { 2, -4 }, out _).Should().BeNull("a negative length is not a length");
    }

    /// <summary>**`Gather` 的秩是"数据秩 - 1 + 索引秩"** / Gather's rank is data - 1 + indices.</summary>
    /// <remarks>
    /// **当成替换会让 `[5]` 配 `[3]` 得到 `[]`（0 阶），而正确结果是 `[3]`（1 阶）。** **秩差一之后，下游每个维度都错开一位。**
    ///
    /// **A replacement semantics would give `[5]` with `[3]` a result of `[]` (rank 0) while the right answer is `[3]` (rank 1). Being off by one in rank shifts
    /// every downstream dimension.**
    /// </remarks>
    [Fact]
    public void Gather_ShouldSpliceTheIndicesShape() {
        OnnxShapeOperators.Gather(new[] { 5 }, new[] { 3 }, 0, out _).Should().Equal(new[] { 3 },
            "rank-1 data with rank-1 indices stays rank-1; a replacement would give rank 0");
        OnnxShapeOperators.Gather(new[] { 4, 5, 6 }, new[] { 2, 3 }, 1, out string? failure).Should().Equal(new[] { 4, 2, 3, 6 });
        failure.Should().BeNull();
        OnnxShapeOperators.Gather(new[] { 4, 5 }, System.Array.Empty<int>(), 0, out _).Should().Equal(new[] { 5 },
            "scalar indices remove the gathered axis itself, leaving the others");
    }

    /// <summary>**`keepDims` 只影响秩** / `keepDims` changes only the rank.</summary>
    /// <remarks>
    /// **两种写法的元素数都是 1，所以"元素数对不对"完全测不出 `keepDims` 是否生效。** **判据必须单独打秩。**
    ///
    /// **Both forms hold one element, so the element count cannot detect a wrong `keepDims` at all. The rank has to be asserted on its own.**
    /// </remarks>
    [Fact]
    public void ReduceSum_ShouldChangeOnlyTheRank() {
        OnnxShapeOperators.ReduceSum(new[] { 2, 3, 4 }, new long[] { 1 }, keepDims: true, out _).Should().Equal(new[] { 2, 1, 4 });
        OnnxShapeOperators.ReduceSum(new[] { 2, 3, 4 }, new long[] { 1 }, keepDims: false, out _).Should().Equal(new[] { 2, 4 });

        OnnxShape.ElementCount(OnnxShapeOperators.ReduceSum(new[] { 2, 3, 4 }, new long[] { 0, 1, 2 }, true, out _)!)
            .Should().Be(1, "reducing everything leaves one element with dims kept");
        OnnxShape.ElementCount(OnnxShapeOperators.ReduceSum(new[] { 2, 3, 4 }, new long[] { 0, 1, 2 }, false, out _)!)
            .Should().Be(1, "and one element without them, so the count cannot tell the two apart");

        OnnxShapeOperators.ReduceSum(new[] { 2, 3 }, null, keepDims: false, out _).Should().BeEmpty(
            "no axes means reduce all, giving a scalar");
        OnnxShapeOperators.ReduceSum(new[] { 2, 3 }, null, keepDims: true, out _).Should().Equal(new[] { 1, 1 });
        OnnxShapeOperators.ReduceSum(new[] { 2, 3 }, new long[] { 2 }, false, out string? bad).Should().BeNull("axis 2 is out of range for rank 2");
        bad.Should().NotBeNull();
    }
}