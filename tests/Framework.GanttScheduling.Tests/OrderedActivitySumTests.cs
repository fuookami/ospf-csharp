// 日期偏移与按原序求和的判据 / Criteria for the date offset and the ordered sum.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests;

/// <summary>
/// **班次归属按偏移量切，正好等于偏移量的时刻算当天** / Shift attribution follows the offset, and an instant exactly at the offset belongs to that day.
/// </summary>
/// <remarks>
/// 夜班从 `22:00` 开始时，`22:30` 归当天、`02:30` 归前一天。若按自然日切，同一个夜班被拦腰切断，
/// "今天这个班做了多少"永远算不对。而"正好等于偏移量"的那一秒正是班次开始时刻，划到前一天等于把班次开头的一批任务记到上一班。
///
/// With a night shift starting at `22:00`, `22:30` belongs to that day and `02:30` to the previous one. Splitting by the natural day cuts the same night shift in half, so
/// "how much did today's shift produce" can never be right. And the exact second of the offset is the shift start — moving it back books the opening tasks onto the
/// previous shift.
/// </remarks>
public class LocalDateOffsetTests {
    /// **早于偏移量归前一天** / An instant earlier than the offset belongs to the previous day.
    [Fact]
    public void Date_ShouldFallBackBeforeTheOffset() {
        var offset = new LocalDateOffset(new TimeOnly(22, 0));

        offset.Date(new DateTime(2026, 3, 10, 2, 30, 0)).Should().Be(new DateOnly(2026, 3, 9));
    }

    /// <summary>
    /// **正好等于偏移量的时刻算当天** / An instant exactly at the offset belongs to that day.
    /// </summary>
    /// <remarks>
    /// 这条防的是把比较写成 `<=`：那样夜班开始的第一秒会被记到上一班，而这一秒恰恰是班次最开始的时刻。
    ///
    /// This guards against writing the comparison as `<=`: the first second of the night shift would then be booked onto the previous shift, and that second is exactly
    /// when the shift begins.
    /// </remarks>
    [Fact]
    public void Date_ShouldKeepTheSameDayAtTheOffsetExactly() {
        var offset = new LocalDateOffset(new TimeOnly(22, 0));

        offset.Date(new DateTime(2026, 3, 10, 22, 0, 0)).Should().Be(new DateOnly(2026, 3, 10));
        offset.Date(new DateTime(2026, 3, 10, 23, 30, 0)).Should().Be(new DateOnly(2026, 3, 10));
    }

    /// **跨月边界的回退** / Falling back across a month boundary.
    [Fact]
    public void Date_ShouldFallBackAcrossAMonthBoundary() {
        var offset = new LocalDateOffset(new TimeOnly(6, 0));

        offset.Date(new DateTime(2026, 3, 1, 4, 0, 0)).Should().Be(new DateOnly(2026, 2, 28));
    }

    /// **零偏移量时不回退** / A zero offset never falls back.
    [Fact]
    public void Date_ShouldNotFallBackWithAZeroOffset() {
        var offset = new LocalDateOffset(TimeOnly.MinValue);

        offset.Date(new DateTime(2026, 3, 10, 0, 0, 0)).Should().Be(new DateOnly(2026, 3, 10));
    }
}

/// <summary>
/// **不做减法撤销旧值：同一组项经不同更新路径必须得到同一个和** / No subtractive undo: the same set of terms must yield the same sum regardless of the update path.
/// </summary>
/// <remarks>
/// 浮点减法与加法的消去/溢出行为不对称，`a + b - b` 并不总等于 `a`。若用减法撤销，总和会依赖**更新历史**，
/// 于是"同一组项、不同的增删顺序"给出不同的和——而调度里同一个聚合会被反复增量更新。
///
/// Floating-point subtraction's cancellation and overflow behaviour is not symmetric with addition, so `a + b - b` is not always `a`. Undoing by subtraction makes the total
/// depend on the **update history**, so "the same set of terms with a different add/remove order" yields a different sum — and in scheduling the same aggregate is updated
/// incrementally over and over.
/// </remarks>
public class OrderedActivitySumTests {
    /// **只追加时和等于各项之和** / With only appends the sum equals the sum of the terms.
    [Fact]
    public void Update_ShouldAccumulateAppendedTerms() {
        var sum = new OrderedActivitySum(new Flt64(10.0));
        sum.Update(new List<Tuple<int, Flt64>> {
            Tuple.Create(0, new Flt64(1.0)),
            Tuple.Create(1, new Flt64(2.0)),
        });

        sum.Value.ToDouble().Should().BeApproximately(13.0, 1e-12);
    }

    /// **乱序追加仍按原序累加，和与顺序无关** / Out-of-order appends are summed in original order and the result is order-independent.
    [Fact]
    public void Update_ShouldRespectTheOriginalOccurrenceOrder() {
        var sum = new OrderedActivitySum(new Flt64(0.0));
        sum.Update(new List<Tuple<int, Flt64>> {
            Tuple.Create(2, new Flt64(3.0)),
            Tuple.Create(0, new Flt64(1.0)),
            Tuple.Create(1, new Flt64(2.0)),
        });

        sum.Value.ToDouble().Should().BeApproximately(6.0, 1e-12, "the indices are reordered, not the values swapped");
    }

    /// <summary>
    /// **增删之后的和与"一次性给全"完全相同** / After add/remove the sum equals the one-shot computation exactly.
    /// </summary>
    /// <remarks>
    /// 这里用**会丢精度的值**（`0.1` 与 `1e16`）而不是整数：整数下两种实现都精确，判据无法区分。
    /// 一个用 `value - old + new` 撤销的实现会在这里给出不同的位模式。
    ///
    /// The test uses **precision-losing values** (`0.1` and `1e16`) rather than integers: with integers both implementations are exact and the criterion cannot tell them
    /// apart. An implementation undoing via `value - old + new` produces a different bit pattern here.
    /// </remarks>
    [Fact]
    public void Update_ShouldMatchTheOneShotSumAfterARemoval() {
        var terms = new List<Tuple<int, Flt64>> {
            Tuple.Create(0, new Flt64(1e16)),
            Tuple.Create(1, new Flt64(0.1)),
            Tuple.Create(2, new Flt64(0.1)),
        };

        var incremental = new OrderedActivitySum(new Flt64(0.0));
        incremental.Update(terms);
        incremental.Update(
            new List<Tuple<int, Flt64>> { Tuple.Create(2, new Flt64(0.1)) },
            new List<int> { 0 });
        incremental.Update(
            new List<Tuple<int, Flt64>> { Tuple.Create(0, new Flt64(1e16)) },
            new List<int>());

        var oneShot = new OrderedActivitySum(new Flt64(0.0));
        oneShot.Update(terms);

        // 增量路径先移除 0 号再放回，最终集合与一次性完全相同。
        // The incremental path removes index 0 and puts it back, so the final set is identical to the one-shot one.
        incremental.Value.ToDouble().Should().Be(oneShot.Value.ToDouble(),
            "the sum must be a function of the term set, not of the update history");
    }

    /// **被移除的项不再计入** / A removed term no longer counts.
    [Fact]
    public void Update_ShouldDropRemovedTerms() {
        var sum = new OrderedActivitySum(new Flt64(0.0));
        sum.Update(new List<Tuple<int, Flt64>> {
            Tuple.Create(0, new Flt64(1.0)),
            Tuple.Create(1, new Flt64(2.0)),
        });
        sum.Update(new List<Tuple<int, Flt64>>(), new List<int> { 0 });

        sum.Value.ToDouble().Should().BeApproximately(2.0, 1e-12);
    }

    /// <summary>
    /// **空更新不改变和** / An empty update leaves the sum untouched.
    /// </summary>
    /// <remarks>
    /// 判据先放进去一项再空更新：池子为空时"追加快路"的判断会因短路而通过，
    /// 守卫去掉了照样绿——**只有池子非空时，去掉守卫才会暴露**（空列表取首个元素）。
    ///
    /// The criterion seeds a term before the empty update: with an empty pool the append test short-circuits and stays green even without the guard —
    /// **only a non-empty pool exposes the missing guard** (reading the first element of an empty list).
    /// </remarks>
    [Fact]
    public void Update_ShouldIgnoreAnEmptyUpdate() {
        var sum = new OrderedActivitySum(new Flt64(5.0));
        sum.Update(new List<Tuple<int, Flt64>> { Tuple.Create(0, new Flt64(2.0)) });
        sum.Value.ToDouble().Should().BeApproximately(7.0, 1e-12);

        sum.Update(new List<Tuple<int, Flt64>>(), new List<int>());

        sum.Value.ToDouble().Should().BeApproximately(7.0, 1e-12);
    }

    /// <summary>
    /// **重复序号的覆盖不算追加** / Re-adding an existing index is an overwrite, not an append.
    /// </summary>
    /// <remarks>
    /// 追加快路的条件是"新序号**严格大于**已有最大序号"。写松成大于等于时，重放同一序号会被当成新项，
    /// 于是 `1.0` 被覆盖成 `2.0` 之后总和变成 `3.0`——**旧值和新值被加了两次**。
    ///
    /// The fast path requires the new index to be **strictly greater** than the current maximum. Relaxing it to greater-or-equal treats a replayed index as a new term, so
    /// after `1.0` is overwritten by `2.0` the total becomes `3.0` — **the old value and the new one are both counted**.
    /// </remarks>
    [Fact]
    public void Update_ShouldOverwriteRatherThanDoubleCountARepeatedIndex() {
        var sum = new OrderedActivitySum(new Flt64(0.0));
        sum.Update(new List<Tuple<int, Flt64>> { Tuple.Create(0, new Flt64(1.0)) });
        sum.Value.ToDouble().Should().BeApproximately(1.0, 1e-12);

        sum.Update(new List<Tuple<int, Flt64>> { Tuple.Create(0, new Flt64(2.0)) });

        sum.Value.ToDouble().Should().BeApproximately(2.0, 1e-12, "the second update replaces the first, it does not add to it");
    }
}
