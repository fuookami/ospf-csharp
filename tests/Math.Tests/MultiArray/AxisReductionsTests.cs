// 按轴归约测试 / Tests for the axis reductions.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.MultiArray;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.MultiArray;

/// <summary>
/// 按轴归约 / The axis reductions.
/// </summary>
public sealed class AxisReductionsTests {
    /// <summary>2×3 的固定夹具，元素就是下标和 / A fixed 2×3 fixture whose elements are the index sums.</summary>
    private static MultiArray<Flt64, Shape2> Fixture() =>
        MultiArray<Flt64, Shape2>.Factory.NewBy(
            Shape2.WithOrder(2, 3, StorageOrder.RowMajor),
            (index, vector) => new Flt64(vector[0] * 10 + vector[1]));

    private static double[] Values(IEnumerable<Flt64> source) =>
        source.Select(value => value.ToDouble()).ToArray();

    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("归约必须成功 / the reduction must succeed: " + result);

    /// <summary>**全量求和把每个元素都算一次** / The full sum counts every element once.</summary>
    /// <remarks>
    /// 2×3 夹具元素是 `下标和`：`00 01 02 = 0 1 2`、`10 11 12 = 10 11 12`，合计 **36**。
    /// 若实现漏掉第二行，和会是 3——**而 3 看起来完全正常**。
    ///
    /// The elements are the row-major index sums `0 1 2 / 10 11 12`, totalling **36**. Dropping the second row would give 3 — **and 3 looks
    /// perfectly normal**.
    /// </remarks>
    [Fact]
    public void SumAll_ShouldCountEveryElement() {
        Fixture().SumAll(Flt64.Zero).Should().Be(new Flt64(36));
        Fixture().SumAll(new Flt64(1)).Should().Be(new Flt64(37),
            "初值参与累加 / the seed takes part in the accumulation");
    }

    /// <summary>**沿 0 轴归约得到每列之和，沿 1 轴得到每行之和** / Reducing axis 0 sums the columns, axis 1 sums the rows.</summary>
    /// <remarks>
    /// **两条断言必须一起看**：只断言"形状变成 1×3"时，把行列搞反也一样通过——**而两个方向的形状都是 `[3]`**。
    ///
    /// **Both assertions are needed together**: checking only "the shape became 1×3" passes with rows and columns swapped, **because both directions
    /// yield `[3]`**.
    /// </remarks>
    [Fact]
    public void SumAxis_ShouldReduceTheRequestedAxis() {
        MultiArray<Flt64, DynShape> columns = Unwrap(Fixture().SumAxisSafe(0, Flt64.Zero));
        MultiArray<Flt64, DynShape> rows = Unwrap(Fixture().SumAxisSafe(1, Flt64.Zero));

        columns.Shape.Dimension.Should().Be(1);
        columns.Shape[0].Should().Be(3);
        Values(columns).Should().Equal(10.0, 12.0, 14.0);

        rows.Shape.Dimension.Should().Be(1);
        rows.Shape[0].Should().Be(2);
        Values(rows).Should().Equal(3.0, 33.0);
    }

    /// <summary>**多轴归约一次削掉两个轴，结果与逐轴归约一致** / Multi-axis reduction drops both axes and agrees with reducing one axis at a time.</summary>
    [Fact]
    public void SumAxes_ShouldAgreeWithRepeatedSingleAxisReduction() {
        MultiArray<Flt64, Shape3> cube = MultiArray<Flt64, Shape3>.Factory.NewBy(
            Shape3.WithOrder(2, 3, 4, StorageOrder.RowMajor),
            (index, vector) => new Flt64((vector[0] + 1) * (vector[1] + 1) * (vector[2] + 1)));

        MultiArray<Flt64, DynShape> both = Unwrap(cube.SumAxesSafe(new[] { 0, 2 }, Flt64.Zero));
        MultiArray<Flt64, DynShape> step = Unwrap(
            Unwrap(cube.SumAxisSafe(0, Flt64.Zero)).SumAxisSafe(1, Flt64.Zero));

        both.Shape.Dimension.Should().Be(1);
        both.Shape[0].Should().Be(3);
        Values(both).Should().Equal(Values(step));
    }

    /// <summary>**零个轴返回同内容的 DynShape 副本，不是标量** / Zero axes returns a DynShape copy with the same content, not a scalar.</summary>
    /// <remarks>
    /// 空轴集合若被当成"归约成标量"，会返回 1 维、长度 1 的数组——**维度变成 1 之后，"和"这个数字依然正确**，
    /// 所以只比对总量抓不住它。
    ///
    /// An empty axis set treated as "reduce to a scalar" returns a 1-element array — **the total is still correct after collapsing**, so comparing only
    /// the sum cannot catch it.
    /// </remarks>
    [Fact]
    public void SumAxes_ShouldCopyWhenNoAxesAreGiven() {
        MultiArray<Flt64, DynShape> copy = Unwrap(
            Fixture().SumAxesSafe(Array.Empty<int>(), Flt64.Zero));

        copy.Shape.Dimension.Should().Be(2);
        copy.Shape[0].Should().Be(2);
        copy.Shape[1].Should().Be(3);
        Values(copy).Should().Equal(0.0, 1.0, 2.0, 10.0, 11.0, 12.0);
    }

    /// <summary>**前缀和沿轴累积，每行/列的首元素等于原值** / The prefix sum accumulates along the axis and each slice starts at the original value.</summary>
    /// <remarks>
    /// 只断言"最后一项等于全量之和"时，实现把整条轴填成同一个总数也通过。
    ///
    /// Asserting only "the last element equals the total" passes even when the whole axis is filled with the same total.
    /// </remarks>
    [Fact]
    public void CumsumAxis_ShouldAccumulateAlongTheAxis() {
        MultiArray<Flt64, DynShape> prefix = Unwrap(Fixture().CumsumAxisSafe(1, Flt64.Zero));

        prefix.Shape.Dimension.Should().Be(2);
        prefix.Shape[0].Should().Be(2);
        prefix.Shape[1].Should().Be(3);
        Values(prefix).Should().Equal(0.0, 1.0, 3.0, 10.0, 21.0, 33.0);
    }

    /// <summary>**越界轴被拒，而不是静默返回原数组** / An out-of-range axis is rejected rather than silently returning the source.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Reductions_ShouldRejectOutOfRangeAxes(int axis) {
        Fixture().SumAxisSafe(axis, Flt64.Zero).IsOk.Should().BeFalse();
        Fixture().SumAxesSafe(new[] { 0, axis }, Flt64.Zero).IsOk.Should().BeFalse();
        Fixture().CumsumAxisSafe(axis, Flt64.Zero).IsOk.Should().BeFalse();
    }

    /// <summary>**列主序的 2×3 得到与行主序相同的归约结果** / Column-major agrees with row-major.</summary>
    /// <remarks>
    /// **两条路径都在用不同的线性遍历**：列主序元素按列排，进位方向随之翻转。只测行主序时，
    /// 进位方向写死成"从最后一维往前"依然全绿——**而列主序的结果会整片错位**。
    ///
    /// **The two paths walk a different linear order**: column-major lays elements out by column and the carry direction flips with it. Testing only
    /// row-major stays fully green even with the carry direction hard-coded to "last dimension first" — **while column-major results are shifted
    /// wholesale**.
    /// </remarks>
    [Fact]
    public void Reductions_ShouldAgreeAcrossStorageOrders() {
        MultiArray<Flt64, Shape2> columnMajor = MultiArray<Flt64, Shape2>.Factory.NewBy(
            Shape2.WithOrder(2, 3, StorageOrder.ColumnMajor),
            (index, vector) => new Flt64(vector[0] * 10 + vector[1]));

        columnMajor.SumAll(Flt64.Zero).Should().Be(new Flt64(36));
        Values(Unwrap(columnMajor.SumAxisSafe(0, Flt64.Zero))).Should().Equal(10.0, 12.0, 14.0);
        Values(Unwrap(columnMajor.SumAxisSafe(1, Flt64.Zero))).Should().Equal(3.0, 33.0);
        // **按"每个线性位置对应的坐标"算期望，而不是按行主序的坐标表**：列主序的线性顺序本来就是逐列排的
        // （实测线性序列是 `0 10 1 21 3 33`）。用行主序的坐标去查列主序数组，会把"布局不同"误判成"前缀算错"。
        // **Derive the expectation from each linear position's own coordinates instead of a row-major coordinate table**: column-major linear order is
        // inherently column-by-column (measured sequence `0 10 1 21 3 33`). Probing a column-major array with row-major coordinates misreads the layout
        // as a prefix bug.
        MultiArray<Flt64, DynShape> columnPrefix = Unwrap(columnMajor.CumsumAxisSafe(1, Flt64.Zero));
        columnPrefix.Shape.Dimension.Should().Be(2);
        columnPrefix.Count.Should().Be(6);
        for (int index = 0; index < columnPrefix.Count; ++index) {
            int[] vector = columnMajor.Shape.Vector(index).Value!;
            double expected = 0;
            for (int step = 0; step <= vector[1]; ++step) {
                expected += vector[0] * 10 + step;
            }
            columnPrefix[index].ToDouble().Should().Be(expected,
                $"线性位置 {index} 的坐标为 ({vector[0]},{vector[1]}) / the coordinates at linear position {index} are ({vector[0]},{vector[1]})");
        }
    }
}
