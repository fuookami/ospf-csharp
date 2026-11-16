#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **逐元素表示：重排与切片算得对** / The per-element representation: the permutation and the slice are computed right.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxTensorExpr`。**判据的落点很直接：** **重排之后"某个位置上是原来哪一个元素"，**
/// **用元素自身的编号来认——** **而不是用形状（形状对了而元素乱序，正是这一层要防的错）。**
///
/// Syncs `OnnxTensorExpr`. **The criteria land on something direct: after a permutation, "which original element sits at this position", identified by the element's own label — not by the
/// shape, because a correct shape with scrambled elements is exactly the mistake this layer exists to prevent.**
/// </remarks>
public class OnnxElementListTests
{
    /// <summary>**元素个数与形状不符时构造失败** / A count that disagrees with the shape fails to build.</summary>
    [Fact]
    public void AMismatchedCount_ShouldFailToBuild() {
        OnnxElementList.Result built = OnnxElementList.Create(new[] { 2, 3 }, Labels(4));

        built.IsOk.Should().BeFalse("six positions cannot hold four expressions");
        built.Failure.Should().Contain("6").And.Contain("4");
        OnnxElementList.Create(new[] { 2, 3 }, Labels(6)).Value!.Count.Should().Be(6);
    }

    /// <summary>**二维转置把元素按坐标换位** / A 2-D transpose moves elements by coordinate.</summary>
    /// <remarks>
    /// **`[2,3]` 换成 `[3,2]` 之后，新位置 `(r,c)` 上是原来的 `(c,r)`。** **只看形状会以为成功了，而元素可能整片错位——**
    /// **所以判据认的是元素自己的编号。**
    ///
    /// **After [2,3] becomes [3,2], the new position (r,c) holds the old (c,r).** **Checking only the shape would look like success while the elements are entirely scrambled — so the
    /// criterion identifies elements by their own label.**
    /// </remarks>
    [Fact]
    public void ATranspose_ShouldMoveElementsByCoordinate() {
        OnnxElementList built = OnnxElementList.Create(new[] { 2, 3 }, Labels(6)).Value!;

        OnnxElementList transposed = built.Transpose(new[] { 1, 0 }).Value!;

        transposed.Shape.Should().Equal(new[] { 3, 2 });
        // 源 (r,c) 的编号是 3r + c；新位置 (r,c) 取源 (c,r) = 3c + r。
        // Source (r,c) is labelled 3r + c; the new (r,c) takes source (c,r) = 3c + r.
        LabelAt(transposed, 0, 0).Should().Be("e0");
        LabelAt(transposed, 0, 1).Should().Be("e3");
        LabelAt(transposed, 1, 0).Should().Be("e1");
        LabelAt(transposed, 1, 1).Should().Be("e4");
        LabelAt(transposed, 2, 0).Should().Be("e2");
        LabelAt(transposed, 2, 1).Should().Be("e5");
    }

    /// <summary>**三维转置也按行主序换位** / A 3-D transpose moves by row-major order as well.</summary>
    [Fact]
    public void AThreeDimensionalTranspose_ShouldFollowRowMajor() {
        OnnxElementList built = OnnxElementList.Create(new[] { 2, 3, 4 }, Labels(24)).Value!;

        OnnxElementList transposed = built.Transpose(new[] { 2, 0, 1 }).Value!;

        transposed.Shape.Should().Equal(new[] { 4, 2, 3 });
        // 源 (0,0,1) 的编号是 4·0 + 1·4 + 0 = 4；新位置 (0,0,1) 取源 (1,0,0)……这里直接用一条可手算的：
        // 新位置 (3,1,2) 取源 (1,2,3) = 1·12 + 2·4 + 3 = 23。
        // Source (0,0,1) is labelled 4; the new (3,1,2) reads source (1,2,3) = 1·12 + 2·4 + 3 = 23.
        transposed.Elements[3 * 6 + 1 * 3 + 2].Monomials[0].Symbol.Name.Should().Be("e23");
        transposed.Elements[0].Monomials[0].Symbol.Name.Should().Be("e0", "the corner element stays where it was");
    }

    /// <summary>**置换不是排列时被拒绝** / A non-permutation is refused.</summary>
    [Fact]
    public void ANonPermutation_ShouldBeRefused() {
        OnnxElementList built = OnnxElementList.Create(new[] { 2, 3 }, Labels(6)).Value!;

        built.Transpose(new[] { 0, 0 }).IsOk.Should().BeFalse("a repeated axis is not a permutation");
        built.Transpose(new[] { 0 }).IsOk.Should().BeFalse("the length has to match the rank");
        built.Transpose(new[] { 0, 2 }).IsOk.Should().BeFalse("an out-of-range axis has no meaning");
    }

    /// <summary>**沿首维切片取的是连续的一段** / Slicing along the leading axis takes a contiguous run.</summary>
    /// <remarks>
    /// **这就是 `Scan` 运行期切片要的那一刀，与基线 `sliceLeading` 同一个形状：** **首维是轮数，第 `i` 片是连续的一段元素。**
    ///
    /// **This is the cut Scan runtime slicing needs, shaped like the baseline's sliceLeading: the leading dimension is the round count and slice i is a contiguous run.**
    /// </remarks>
    [Fact]
    public void SliceLeading_ShouldTakeAContiguousRun() {
        OnnxElementList built = OnnxElementList.Create(new[] { 3, 2 }, Labels(6)).Value!;

        OnnxElementList second = built.SliceLeading(1).Value!;

        second.Shape.Should().Equal(new[] { 2 });
        second.Count.Should().Be(2);
        second.Elements[0].Monomials[0].Symbol.Name.Should().Be("e2");
        second.Elements[1].Monomials[0].Symbol.Name.Should().Be("e3");
        built.SliceLeading(0).Value!.Elements[0].Monomials[0].Symbol.Name.Should().Be("e0");
    }

    /// <summary>**切片越界与标量切片被拒绝** / An out-of-range slice and slicing a scalar are refused.</summary>
    [Fact]
    public void AnOutOfRangeSlice_ShouldBeRefused() {
        OnnxElementList built = OnnxElementList.Create(new[] { 3 }, Labels(3)).Value!;

        built.SliceLeading(3).IsOk.Should().BeFalse("there are three slices, numbered 0 to 2");
        built.SliceLeading(-1).IsOk.Should().BeFalse();
        OnnxElementList.Create(Array.Empty<int>(), Labels(1)).Value!.SliceLeading(0).IsOk.Should().BeFalse(
            "a scalar has no leading axis");
    }

    /// <summary>**沿 0 轴拼接：两段依次接上** / Concatenating on axis 0 appends the second run after the first.</summary>
    [Fact]
    public void ConcatOnAxisZero_ShouldAppend() {
        OnnxElementList left = OnnxElementList.Create(new[] { 2, 3 }, Labels(6)).Value!;
        OnnxElementList right = OnnxElementList.Create(new[] { 2, 3 }, Labels(6)).Value!;

        OnnxElementList joined = OnnxElementList.Concat(new[] { left, right }, 0).Value!;

        joined.Shape.Should().Equal(new[] { 4, 3 });
        joined.Count.Should().Be(12);
        joined.Elements[0].Monomials[0].Symbol.Name.Should().Be("e0");
        joined.Elements[5].Monomials[0].Symbol.Name.Should().Be("e5");
        joined.Elements[6].Monomials[0].Symbol.Name.Should().Be("e0", "the second input starts again from its first element");
    }

    /// <summary>**沿 1 轴拼接：按行交错，而不是整块接上** / Concatenating on axis 1 interleaves by row instead of appending whole blocks.</summary>
    /// <remarks>
    /// **这是拼接最容易写错的一处：** **把第二段整块接在末尾，形状对得上、元素个数也对得上，** **而每一行的内容其实全部错位了。**
    /// **所以判据看的是行内的元素，不是个数。**
    ///
    /// **This is where concatenation is easiest to get wrong: appending the second input wholesale at the end matches the shape and the element count while every row's content has
    /// shifted.** **The criterion therefore reads the elements inside each row, not the count.**
    /// </remarks>
    [Fact]
    public void ConcatOnAxisOne_ShouldInterleaveByRow() {
        OnnxElementList left = OnnxElementList.Create(new[] { 2, 2 }, Labels(4)).Value!;
        OnnxElementList right = OnnxElementList.Create(new[] { 2, 1 }, Labels(2, "f0", "f1")).Value!;

        OnnxElementList joined = OnnxElementList.Concat(new[] { left, right }, 1).Value!;

        joined.Shape.Should().Equal(new[] { 2, 3 });
        Labels(joined).Should().Equal(new[] { "e0", "e1", "f0", "e2", "e3", "f1" });
    }

    /// <summary>**负数轴按秩归一** / A negative axis is normalised by the rank.</summary>
    [Fact]
    public void ANegativeAxis_ShouldNormalise() {
        OnnxElementList left = OnnxElementList.Create(new[] { 2, 2 }, Labels(4)).Value!;
        OnnxElementList right = OnnxElementList.Create(new[] { 2, 1 }, Labels(2, "f0", "f1")).Value!;

        OnnxElementList joined = OnnxElementList.Concat(new[] { left, right }, -1).Value!;

        joined.Shape.Should().Equal(new[] { 2, 3 }, "-1 on a rank-2 tensor is axis 1");
    }

    /// <summary>**非拼接维不一致时被拒绝** / A mismatch on another dimension is refused.</summary>
    /// <remarks>
    /// **不一致意味着每组的段长不同，** **而按下标去取会读到另一个输入的尾巴——** **数值上看不出异常，只是那一段整体挪了位。**
    ///
    /// **A mismatch means the runs differ in size, so indexing by offset reads another input's tail — invisible numerically, the run has simply moved.**
    /// </remarks>
    [Fact]
    public void AMismatchOnAnotherDimension_ShouldBeRefused() {
        OnnxElementList left = OnnxElementList.Create(new[] { 2, 3 }, Labels(6)).Value!;
        OnnxElementList right = OnnxElementList.Create(new[] { 3, 3 }, Labels(9)).Value!;

        OnnxElementList.Result joined = OnnxElementList.Concat(new[] { left, right }, 1);

        joined.IsOk.Should().BeFalse("dimension 0 differs while the axis is 1");
        joined.Failure.Should().Contain("维度 0");
    }

    /// <summary>**秩不一致、轴越界、空输入都被拒绝** / Different ranks, an out-of-range axis and an empty list are refused.</summary>
    [Fact]
    public void BadConcatenations_ShouldBeRefused() {
        OnnxElementList twoDimensional = OnnxElementList.Create(new[] { 2, 2 }, Labels(4)).Value!;
        OnnxElementList oneDimensional = OnnxElementList.Create(new[] { 2 }, Labels(2)).Value!;

        OnnxElementList.Concat(new[] { twoDimensional, oneDimensional }, 0).IsOk.Should().BeFalse("the ranks differ");
        OnnxElementList.Concat(new[] { twoDimensional }, 2).IsOk.Should().BeFalse("the axis is out of range");
        OnnxElementList.Concat(Array.Empty<OnnxElementList>(), 0).IsOk.Should().BeFalse("nothing to concatenate");
    }

    /// <summary>**均分把一段切成连续的小段** / An even split cuts one run into consecutive pieces.</summary>
    [Fact]
    public void SplitEvenly_ShouldCutConsecutiveRuns() {
        OnnxElementList built = OnnxElementList.Create(new[] { 4 }, Labels(4)).Value!;

        IReadOnlyList<OnnxElementList> pieces = built.Split(0, null, 2).Values!;

        pieces.Should().HaveCount(2);
        Labels(pieces[0]).Should().Equal(new[] { "e0", "e1" });
        Labels(pieces[1]).Should().Equal(new[] { "e2", "e3" });
    }

    /// <summary>**按给的长度分片时，每处理完外层一个坐标就接着切** / With explicit sizes the cursor follows each outer coordinate.</summary>
    /// <remarks>
    /// **这是分片最容易写错的一处：** **按"整段偏移"去切，第一行的两段都对，第二行就整体挪了位——** '
    /// **形状与元素个数全都正确，只有内容不对。** **所以判据读的是每一行里的元素。**
    ///
    /// **This is where splitting is easiest to get wrong: cutting by a whole-block offset gets the first row right and shifts the second row entirely — the shape and the counts all agree,
    /// only the content is wrong.** **So the criterion reads the elements inside each row.**
    /// </remarks>
    [Fact]
    public void SplitWithSizes_ShouldAdvancePerOuterCoordinate() {
        OnnxElementList built = OnnxElementList.Create(new[] { 2, 4 }, Labels(8)).Value!;

        IReadOnlyList<OnnxElementList> pieces = built.Split(1, new[] { 3, 1 }, 2).Values!;

        pieces[0].Shape.Should().Equal(new[] { 2, 3 });
        Labels(pieces[0]).Should().Equal(new[] { "e0", "e1", "e2", "e4", "e5", "e6" });
        pieces[1].Shape.Should().Equal(new[] { 2, 1 });
        Labels(pieces[1]).Should().Equal(new[] { "e3", "e7" });
    }

    /// <summary>**除不尽时拒绝，而不是向下取整** / A split that does not divide is refused instead of rounding down.</summary>
    /// <remarks>
    /// **向下取整会让最后一段短一截，而形状上谁也看不出来——只有那段少了一个元素。**
    ///
    /// **Rounding down leaves the last piece short, and no shape reveals it — only that piece is missing an element.**
    /// </remarks>
    [Fact]
    public void AnUnevenSplit_ShouldBeRefused() {
        OnnxElementList built = OnnxElementList.Create(new[] { 5 }, Labels(5)).Value!;

        OnnxElementList.SplitResult split = built.Split(0, null, 2);

        split.IsOk.Should().BeFalse("five cannot be split evenly into two");
        split.Failure.Should().Contain("5").And.Contain("2");
    }

    /// <summary>**段长之和、非正段长、轴越界、负数轴** / Sum, non-positive sizes, out-of-range axis and a negative axis.</summary>
    [Fact]
    public void BadSplits_ShouldBeRefused() {
        OnnxElementList built = OnnxElementList.Create(new[] { 2, 4 }, Labels(8)).Value!;

        built.Split(1, new[] { 2, 1 }, 2).IsOk.Should().BeFalse("2 + 1 is not 4");
        built.Split(1, new[] { 0, 4 }, 2).IsOk.Should().BeFalse("a zero-length piece is not a piece");
        built.Split(1, new[] { 4 }, 1).Values![0].Shape.Should().Equal(new[] { 2, 4 }, "a single output is the whole tensor");
        built.Split(2, null, 1).IsOk.Should().BeFalse("the axis is out of range for rank 2");
        built.Split(-1, new[] { 3, 1 }, 2).Values.Should().NotBeNull("−1 on a rank-2 tensor is axis 1");
        built.Split(-1, new[] { 3, 1 }, 2).Values!.Should().HaveCount(2);
    }

    /// <summary>**索引顺序就是结果顺序** / The index order is the result order.</summary>
    /// <remarks>
    /// **排序会静默改掉语义，而形状与元素个数完全一样。** **所以判据读的是"取出来的顺序"，不是"取出了哪几个"。**
    ///
    /// **Sorting would silently change the meaning while the shape and the count stay identical, so the criterion reads the order, not the set.**
    /// </remarks>
    [Fact]
    public void Gather_ShouldKeepTheIndexOrder() {
        OnnxElementList built = OnnxElementList.Create(new[] { 4 }, Labels(4)).Value!;

        OnnxElementList gathered = built.Gather(new[] { 2, 0 }, 0).Value!;

        Labels(gathered).Should().Equal(new[] { "e2", "e0" });
    }

    /// <summary>**沿轴 1 采集时按外层坐标逐组取** / Gathering on axis 1 takes one group per outer coordinate.</summary>
    [Fact]
    public void GatherOnAxisOne_ShouldWorkPerRow() {
        OnnxElementList built = OnnxElementList.Create(new[] { 2, 3 }, Labels(6)).Value!;

        OnnxElementList gathered = built.Gather(new[] { 2, 0 }, 1).Value!;

        gathered.Shape.Should().Equal(new[] { 2, 2 });
        Labels(gathered).Should().Equal(new[] { "e2", "e0", "e5", "e3" });
    }

    /// <summary>**负索引归一到与正索引相同的元素** / A negative index normalises to the same element as its positive twin.</summary>
    /// <remarks>
    /// **归一站错会取到对称位置，** **而形状与个数都对——** **所以判据比的是"两个索引取出来的是不是同一个元素"。**
    ///
    /// **A wrong normalisation picks the mirrored position while the shape and the count agree, so the criterion compares whether both indices yield the same element.**
    /// </remarks>
    [Fact]
    public void ANegativeIndex_ShouldMatchItsPositiveTwin() {
        OnnxElementList built = OnnxElementList.Create(new[] { 4 }, Labels(4)).Value!;

        Labels(built.Gather(new[] { -1 }, 0).Value!).Should().Equal(new[] { "e3" });
        Labels(built.Gather(new[] { 3 }, 0).Value!).Should().Equal(new[] { "e3" }, "−1 and 3 name the same element");
        Labels(built.Gather(new[] { -4 }, 0).Value!).Should().Equal(new[] { "e0" });
    }

    /// <summary>**越界索引与越界轴被拒绝** / An out-of-range index and an out-of-range axis are refused.</summary>
    [Fact]
    public void OutOfRangeGathers_ShouldBeRefused() {
        OnnxElementList built = OnnxElementList.Create(new[] { 4 }, Labels(4)).Value!;

        OnnxElementList.Result beyond = built.Gather(new[] { 4 }, 0);
        beyond.IsOk.Should().BeFalse("indices run 0..3 and −4..−1");
        beyond.Failure.Should().Contain("4").And.Contain("轴长度");
        built.Gather(new[] { -5 }, 0).IsOk.Should().BeFalse();
        built.Gather(new[] { 0 }, 1).IsOk.Should().BeFalse("the axis is out of range for rank 1");
    }

    private static string LabelAt(OnnxElementList list, int row, int column) {
        int index = row * list.Shape[1] + column;
        return list.Elements[index].Monomials[0].Symbol.Name;
    }

    private static IReadOnlyList<LinearPolynomial<Flt64>> Labels(int count, params string[] named) {
        if (named.Length > 0) {
            var custom = new List<LinearPolynomial<Flt64>>(named.Length);
            foreach (string name in named) {
                custom.Add(new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, new Fuookami.Ospf.Core.Variable.RealVar(name)) },
                    Flt64.Zero));
            }
            return custom;
        }
        return Labels(count);
    }

    private static IEnumerable<string> Labels(OnnxElementList list) {
        foreach (LinearPolynomial<Flt64> element in list.Elements) {
            yield return element.Monomials[0].Symbol.Name;
        }
    }

    private static IReadOnlyList<LinearPolynomial<Flt64>> Labels(int count) {
        var elements = new List<LinearPolynomial<Flt64>>(count);
        for (int index = 0; index < count; ++index) {
            elements.Add(new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, new Fuookami.Ospf.Core.Variable.RealVar("e" + index)) },
                Flt64.Zero));
        }
        return elements;
    }
}