// 产能调度聚合类补齐总产能/ The capacity scheduling aggregation gains its total capacity.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.CapacityScheduling;

/// <summary>
/// **产能调度聚合类补齐总产能** / The capacity scheduling aggregation gains its total capacity.
/// </summary>
/// <remarks>
/// `CapacitySchedulingAggregation` 早就在（按文件名扫也扫得出来），但它**只有计数和索引，缺基线的两个总产能成员**：
/// `totalCapacityValue` 与 `totalCapacityQuantity`。这是"同名不等于能力完整"的又一例——清点表按 stem 扫，扫不出缺半边。
///
/// `CapacitySchedulingAggregation` already existed (and name-based scanning finds it), but it **had only the counters and indices, missing the baseline's two capacity members**:
/// `totalCapacityValue` and `totalCapacityQuantity`. Another case of "same name is not the same capability" — scanning by stem cannot see a half-filled class.
/// </remarks>
public class CapacitySchedulingAggregationCapacityTests {
    private sealed record TestAggregationAction(string Id, Executor Executor, ulong Bound, double UnitCapacityValue) : IProductionAction {
        public string Name => Id;

        public bool Discrete => true;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => Flt64.Zero;

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => Bound;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => new(UnitCapacityValue);
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() {
        new TimeRange(Origin, Origin.AddHours(8)),
        new TimeRange(Origin.AddHours(8), Origin.AddHours(16)),
    };

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static List<TestAggregationAction> Actions() {
        var first = new TestExecutor("E1", "E1");
        var second = new TestExecutor("E2", "E2");
        return new List<TestAggregationAction> {
            new("A1", first, 5UL, 2.0),
            new("A2", first, 5UL, 3.0),
            new("A3", second, 1UL, 4.0),
        };
    }

    private static CapacitySchedulingAggregation<TestAggregationAction> Aggregation() =>
        new(Actions(), Slots(), Window());

    /// <summary>
    /// **总产能 = Σ 单位产能 × 上界，逐格累加** / Total capacity is the sum of unit capacity x upper bound over every cell.
    /// </summary>
    /// <remarks>
    /// 三点成对：`2×5 + 3×5 + 4×1 = 29`，两个时隙 **58** / 每格都算，不是"每行只算一次"（那样会得 29）/ 加一个动作或时隙结果就变，
    /// **说明它真的在遍历两维**。
    ///
    /// Three paired points: `2x5 + 3x5 + 4x1 = 29`, two slots make 58; every cell counts, not one per row (which would give 29); and adding an action or a slot changes the result, showing it
    /// really walks both dimensions.
    /// </remarks>
    [Fact]
    public void TotalCapacityValue_ShouldSumEveryCell() {
        CapacitySchedulingAggregation<TestAggregationAction> aggregation = Aggregation();

        // 每格是 `单位产能 × 上界`：`2×5 + 3×5 + 4×1 = 29`，两个时隙共 **58**。
        // Each cell is `unit capacity x upper bound`: 2x5 + 3x5 + 4x1 = 29, and two slots make 58.
        aggregation.TotalCapacityValue().ToDouble().Should().Be(58.0);

        var extraSlot = new List<TimeRange>(Slots()) { new TimeRange(Origin.AddHours(16), Origin.AddHours(24)) };
        new CapacitySchedulingAggregation<TestAggregationAction>(Actions(), extraSlot, Window())
            .TotalCapacityValue().ToDouble().Should().Be(87.0, "one more slot adds 29");
    }

    /// <summary>
    /// **物理量只是裸值加单位** / The quantity is just the bare value plus a unit.
    /// </summary>
    /// <remarks>
    /// 三点成对：不给单位就是无量纲 / **给 `Hour` 就真的是 `Hour`**（用一个和 `NoneUnit` 不同的单位才能证明没有把它丢掉）/ 值不变。**值不随单位改变**——这里不做换算，换算是另一层的事。
    ///
    /// Three paired points: without a unit it is dimensionless; **passing `Hour` really yields `Hour`** (a unit distinct from `NoneUnit` is what proves it is not dropped); and the
    /// value is unchanged. **The value does not move with the unit** — no conversion happens here, that is
    /// another layer's job.
    /// </remarks>
    [Fact]
    public void TotalCapacityQuantity_ShouldCarryTheValueAndTheUnit() {
        CapacitySchedulingAggregation<TestAggregationAction> aggregation = Aggregation();

        aggregation.TotalCapacityQuantity().Value.ToDouble().Should().Be(58.0);
        aggregation.TotalCapacityQuantity().Unit.Should().BeSameAs(NoneUnit.Instance);
        aggregation.TotalCapacityQuantity(TimeUnits.Hour).Unit.Should().BeSameAs(TimeUnits.Hour,
            "an explicit unit must be carried through, not dropped");
        aggregation.TotalCapacityQuantity(TimeUnits.Hour).Value.ToDouble().Should().Be(58.0);
    }

}
