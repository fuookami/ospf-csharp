// 需求项构造（B664）的判据 / Criteria for demand-entry construction (B664).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **需求项构造（B664）** / Demand-entry construction (B664).
/// </summary>
/// <remarks>
/// 判据的重点：**件数需求总是存在**（面积需求是额外的）、**面积用包围面积而不是面积**、
/// **每个物品"先件数后面积"**（顺序决定需求约束的索引）。
///
/// The criteria focus on **the amount demand always being present** (the area one being extra), on **the area using the bounding area rather than the area**, and on **each item contributing
/// "amount then area"**, since that order fixes the demand constraint's index.
/// </remarks>
public class DemandEntriesTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static ActualItem<FltX> Rectangle(string id, double width, double height, ulong amount = 1UL) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Q(width), Q(height)),
            AllowRotate: true, Weight: Q(1.0));

    private static ActualItem<FltX> Circle(string id, double diameter) =>
        new(new ItemId(id), id, new CirclePackingShape2<FltX>(Q(diameter)),
            AllowRotate: false, Weight: Q(1.0));

    /// <summary>
    /// **件数需求总在，面积需求是额外的一条，用包围面积** / The amount demand is always there, the area demand is extra, and it uses the bounding area.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 不要求面积 → 每个物品**一条件数需求**。
    /// 2. 要求面积 → 每个物品**两条**，第一条件数、第二条面积。
    /// 3. 面积总量 = **包围面积 × 件数**：`20×10` 的件、4 件 → `800`。
    /// 4. **圆形件用包围面积**，不是圆面积：半径 `10` 的件外接矩形是 `20×20 = 400`，3 件 → `1200`；
    ///    若用圆面积会得 `314.16 × 3 ≈ 942.5`——排版占的是外接矩形。
    ///
    /// Four paired points: without the area demand each item yields **one amount entry**; with it each item yields **two**, amount first and area second; the area total is **bounding area times
    /// the count**, so a `20×10` item at four units gives `800`; and **a circular item uses its bounding area** (`diameter × diameter`) rather than the circle's area — a `10` diameter item at three
    /// units gives `1200` where the circle's own area would give about `942.5`, since what occupies a sheet is the bounding box.
    /// </remarks>
    [Fact]
    public void Build_ShouldAlwaysAddTheAmountAndOptionallyTheArea() {
        var items = new Dictionary<ActualItem<FltX>, ulong>
        {
            [Rectangle("I1", 20.0, 10.0)] = 4UL,
        };

        IReadOnlyList<Bpp2dAssignmentDemandEntry> amounts = DemandEntries.Build(items);
        amounts.Should().HaveCount(1);
        amounts[0].Mode.Should().Be(Bpp2dAssignmentDemandMode.ItemAmount);
        amounts[0].ItemId.Value.Should().Be("I1");
        ((Bpp2dAssignmentDemandValue.Amount)amounts[0].Demand).Value.Should().Be(4UL);

        IReadOnlyList<Bpp2dAssignmentDemandEntry> both = DemandEntries.Build(items, withAreaDemand: true);
        both.Should().HaveCount(2, "the area demand is extra, not a replacement");
        both[0].Mode.Should().Be(Bpp2dAssignmentDemandMode.ItemAmount, "the amount entry comes first");
        both[1].Mode.Should().Be(Bpp2dAssignmentDemandMode.ItemArea);
        ((Bpp2dAssignmentDemandValue.Area)both[1].Demand).Value.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(800.0, 1e-6, "20×10 per piece at four units");

        var circles = new Dictionary<ActualItem<FltX>, ulong>
        {
            [Circle("C1", 10.0)] = 3UL,
        };
        IReadOnlyList<Bpp2dAssignmentDemandEntry> circleEntries = DemandEntries.Build(circles, withAreaDemand: true);
        // **半径 10 的圆，外接矩形 20×20 = 400**，3 件 → 1200；圆面积约 314.16。
        // **A radius-10 circle has a 20×20 bounding box = 400**, so three units give 1200; its own area is about 314.16.
        ((Bpp2dAssignmentDemandValue.Area)circleEntries[1].Demand).Value.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(1200.0, 1e-6, "the bounding box, not the circle's own area");
    }

    /// <summary>
    /// **每个物品两条，顺序与物品顺序一致** / Two entries per item, in item order.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 两个物品 → **四条**，每两条一组。
    /// 2. **顺序是"物品1件数、物品1面积、物品2件数、物品2面积"**：需求约束的索引就按这个顺序，
    ///    打乱会让影子价格安到另一条需求上（B662 的教训）。
    /// 3. 空清单 → 空结果，不抛。
    ///
    /// Three paired points: two items yield **four entries** in two groups; the order is **item1-amount, item1-area, item2-amount, item2-area**, which is exactly the order the demand
    /// constraints take their indices from, and shuffling it would attach shadow prices to the wrong demand (the B662 lesson); and an empty list yields an empty result rather than a throw.
    /// </remarks>
    [Fact]
    public void Build_ShouldKeepTheItemOrder() {
        var items = new Dictionary<ActualItem<FltX>, ulong>
        {
            [Rectangle("I1", 20.0, 10.0)] = 4UL,
            [Rectangle("I2", 30.0, 10.0)] = 2UL,
        };

        IReadOnlyList<Bpp2dAssignmentDemandEntry> entries = DemandEntries.Build(items, withAreaDemand: true);
        entries.Should().HaveCount(4);
        entries[0].Mode.Should().Be(Bpp2dAssignmentDemandMode.ItemAmount);
        entries[0].ItemId.Value.Should().Be("I1");
        entries[1].Mode.Should().Be(Bpp2dAssignmentDemandMode.ItemArea);
        entries[1].ItemId.Value.Should().Be("I1");
        entries[2].Mode.Should().Be(Bpp2dAssignmentDemandMode.ItemAmount);
        entries[2].ItemId.Value.Should().Be("I2");
        entries[3].Mode.Should().Be(Bpp2dAssignmentDemandMode.ItemArea);
        entries[3].ItemId.Value.Should().Be("I2");

        DemandEntries.Build(new Dictionary<ActualItem<FltX>, ulong>(), withAreaDemand: true)
            .Should().BeEmpty("an empty item list is not an error");
    }
}
