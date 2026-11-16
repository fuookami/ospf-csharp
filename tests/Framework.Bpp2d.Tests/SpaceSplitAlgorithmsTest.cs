// 空间切分搜索与块装载上下文测试 / Tests for space-splitting search and the block-loading context.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 空间切分搜索与块装载上下文 / Space-splitting search and the block-loading context.
/// </summary>
public sealed class SpaceSplitAlgorithmsTest {
    /// <summary>DFS 在空区域里不放置任何件 / DFS places nothing in an empty region.</summary>
    [Fact]
    public void DepthFirst_ShouldHandleDegenerateSpace() =>
        DepthFirstSearchAlgorithm2D
            .Invoke(Space(0, 0, 0, 0), new[] { (Item("a", 2, 2, allowRotate: false), 5) })
            .Value.Placements.Should().BeEmpty();

    /// <summary>DFS 把件数放满后停止 / DFS stops once the requested amount is placed.</summary>
    [Fact]
    public void DepthFirst_ShouldRespectAmounts() {
        SpaceSplit2Result<FltX> result = DepthFirstSearchAlgorithm2D
            .Invoke(Space(0, 0, 20, 20), new[] { (Item("a", 2, 2, allowRotate: false), 3) })
            .Value;

        result.Placements.Should().HaveCount(3);
        result.PlacedAmounts["a"].Should().Be(3);
        result.RestAmounts.Should().NotContainKey("a", "已放满 / fully placed");
    }

    /// <summary>件数超出容量时剩余件数被如实报出 / Leftovers are reported honestly when capacity runs out.</summary>
    [Fact]
    public void DepthFirst_ShouldReportRestAmounts() {
        SpaceSplit2Result<FltX> result = DepthFirstSearchAlgorithm2D
            .Invoke(Space(0, 0, 4, 4), new[] { (Item("a", 2, 2, allowRotate: false), 10) })
            .Value;

        result.Placements.Count.Should().BeLessThan(10);
        result.RestAmounts["a"].Should().Be(10 - result.Placements.Count);
        result.PlacedAmounts["a"].Should().Be(result.Placements.Count);
    }

    /// <summary>已放置件数与剩余件数之和等于需求件数 / Placed plus rest equals the demanded amount.</summary>
    [Fact]
    public void DepthFirst_ShouldBalanceAmounts() {
        SpaceSplit2Result<FltX> result = DepthFirstSearchAlgorithm2D
            .Invoke(Space(0, 0, 5, 5), new[] { (Item("a", 2, 2, allowRotate: false), 7) })
            .Value;

        int placed = result.PlacedAmounts.TryGetValue("a", out int placedAmount) ? placedAmount : 0;
        int rest = result.RestAmounts.TryGetValue("a", out int restAmount) ? restAmount : 0;
        (placed + rest).Should().Be(7);
    }

    /// <summary>DFS 的放置都位于区域内且互不重叠 / DFS placements stay inside and stay disjoint.</summary>
    [Fact]
    public void DepthFirst_ShouldProduceValidPlacements() {
        Space2<FltX> space = Space(0, 0, 10, 10);
        SpaceSplit2Result<FltX> result = DepthFirstSearchAlgorithm2D
            .Invoke(space, new[] { (Item("a", 2, 2, allowRotate: false), 20) })
            .Value;

        var sheet = new Sheet2Shape<FltX>(space.Width, space.Height);
        foreach (Placement2<FltX> placement in result.Placements) {
            placement.InsideSheet(sheet).Value.Should().BeTrue();
        }
        for (int i = 0; i < result.Placements.Count; i++) {
            for (int j = i + 1; j < result.Placements.Count; j++) {
                result.Placements[i].Overlapped(result.Placements[j]).Value.Should().BeFalse();
            }
        }
    }

    /// <summary>
    /// 深度用尽即停止，不会无限递归。**必须选一件第一块就装不满全板的物品**：2×2 在 20×20 上第一块就能放满 100 件，
    /// 那样深度对结果没有影响，测不出任何东西。
    /// The depth cap stops recursion. The item **must not fit the whole sheet in the first block**: 2×2 pieces fill all 100
    /// slots of a 20×20 sheet immediately, so depth would not affect the result and the test would prove nothing.
    /// </summary>
    /// <remarks>
    /// **必须用异质物品**：单一物品类型时，首块按 7×7 铺满角落后，两条余下条带都窄于该物品，递归必然放不下任何东西，
    /// 深度对结果完全没有影响。这个性质在曾先用 2×2、再用 3×3 各验证了一次，最后才定位到原因——它不是缺陷，
    /// 而是 DFS"只沿块边界切分"的直接后果。
    ///
    /// **Heterogeneous items are required**: with a single item type the first block fills the corner and both leftover
    /// strips are narrower than the item, so recursion can never place anything and depth has no effect. This session
    /// verified that property with 2×2 and then 3×3 before locating the cause — not a defect, but a direct consequence of
    /// DFS splitting only along block boundaries.
    /// </remarks>
    [Fact]
    public void DepthFirst_ShouldRespectMaxDepth() {
        (ActualItem<FltX> Item, int Amount)[] items = new[] {
            (Item("big", 7, 7, allowRotate: false), 4),
            (Item("small", 2, 2, allowRotate: false), 100),
        };

        int shallow = DepthFirstSearchAlgorithm2D
            .Invoke(Space(0, 0, 20, 20), items, maxDepth: 1)
            .Value.Placements.Count;
        int deep = DepthFirstSearchAlgorithm2D
            .Invoke(Space(0, 0, 20, 20), items, maxDepth: 6)
            .Value.Placements.Count;

        shallow.Should().Be(4, "深度 1 只放掉首块的大件 / depth 1 only places the first big block");
        deep.Should().BeGreaterThan(shallow, "更深的递归能在条带里继续放小件 / deeper recursion fills the strips");
    }

    /// <summary>圆形件不进入 DFS / Circles are excluded from DFS.</summary>
    [Fact]
    public void DepthFirst_ShouldSkipCircles() =>
        DepthFirstSearchAlgorithm2D
            .Invoke(Space(0, 0, 10, 10), new[] { (CircleItem("c", 2), 3) })
            .Value.Placements.Should().BeEmpty();

    /// <summary>多行启发式按行推进，放满后停止 / The multi-row heuristic advances row by row and stops when full.</summary>
    [Fact]
    public void MultiRow_ShouldFillRows() {
        SpaceSplit2Result<FltX> result = MultiRowHeuristicAlgorithm2D
            .Invoke(Space(0, 0, 10, 10), new[] { (Item("a", 2, 2, allowRotate: false), 25) })
            .Value;

        result.Placements.Should().HaveCount(25);
        result.Source.Should().Be("multi-row");
    }

    /// <summary>多行启发式在件数用尽时停止 / The multi-row heuristic stops when amounts run out.</summary>
    [Fact]
    public void MultiRow_ShouldRespectAmounts() {
        SpaceSplit2Result<FltX> result = MultiRowHeuristicAlgorithm2D
            .Invoke(Space(0, 0, 10, 10), new[] { (Item("a", 2, 2, allowRotate: false), 3) })
            .Value;

        result.Placements.Should().HaveCount(3);
        result.RestAmounts.Should().NotContainKey("a");
    }

    /// <summary>
    /// **多行启发式每行选能放最多件数的物品**：这是它与 DFS 的关键差别，选错了行密度就会下降。
    /// **Each row picks the item fitting the most copies**: that is the key difference from DFS, and picking otherwise
    /// lowers row density.
    /// </summary>
    [Fact]
    public void MultiRow_ShouldPickItemWithMostCopies() {
        SpaceSplit2Result<FltX> result = MultiRowHeuristicAlgorithm2D
            .Invoke(
                Space(0, 0, 10, 4),
                new[] {
                    (Item("wide", 5, 2, allowRotate: false), 2),
                    (Item("narrow", 2, 2, allowRotate: false), 10),
                })
            .Value;

        // 10 宽里 narrow 能放 5 件、wide 只能放 2 件，第一行应全为 narrow。
        // In width 10 the narrow item fits 5 copies while the wide one fits 2, so the first row is all narrow.
        result.Placements.Take(5).Should().OnlyContain(placement => placement.ItemId == "narrow");
    }

    /// <summary>多行启发式的放置都位于区域内且互不重叠 / Multi-row placements stay inside and disjoint.</summary>
    [Fact]
    public void MultiRow_ShouldProduceValidPlacements() {
        Space2<FltX> space = Space(0, 0, 10, 10);
        SpaceSplit2Result<FltX> result = MultiRowHeuristicAlgorithm2D
            .Invoke(space, new[] { (Item("a", 3, 3, allowRotate: false), 30) })
            .Value;

        var sheet = new Sheet2Shape<FltX>(space.Width, space.Height);
        foreach (Placement2<FltX> placement in result.Placements) {
            placement.InsideSheet(sheet).Value.Should().BeTrue();
        }
        for (int i = 0; i < result.Placements.Count; i++) {
            for (int j = i + 1; j < result.Placements.Count; j++) {
                result.Placements[i].Overlapped(result.Placements[j]).Value.Should().BeFalse();
            }
        }
    }

    /// <summary>行数用尽即停止 / The row cap stops the search.</summary>
    [Fact]
    public void MultiRow_ShouldRespectMaxRows() {
        int oneRow = MultiRowHeuristicAlgorithm2D
            .Invoke(Space(0, 0, 10, 10), new[] { (Item("a", 2, 2, allowRotate: false), 100) }, maxRows: 1)
            .Value.Placements.Count;

        oneRow.Should().Be(5);
    }

    /// <summary>映射推导只记录真正放置过的物品 / The amount derivation records only items actually placed.</summary>
    [Fact]
    public void PlacedAmounts_ShouldOmitUntouchedItems() {
        (ActualItem<FltX> Item, int Amount)[] items = new[] {
            (Item("a", 2, 2, false), 3),
            (Item("b", 2, 2, false), 4),
        };
        var rest = new Dictionary<string, int> { ["a"] = 1, ["b"] = 4 };

        IReadOnlyDictionary<string, int> placed = SpaceSplitSupport.PlacedAmountsOf(items, rest);

        placed.Should().ContainKey("a");
        placed["a"].Should().Be(2);
        placed.Should().NotContainKey("b", "未放置过的不记录 / untouched items are omitted");
    }

    /// <summary>块装载上下文的局部块等于逐物品简单块之并 / Local blocks equal the union of per-item simple blocks.</summary>
    [Fact]
    public void Context_LocalBlocksShouldAggregate() {
        IReadOnlyList<Block2<FltX>> blocks = new BlockLoadingContext<FltX>().LocalBlocks(
            new[] { (Item("a", 2, 2, false), 1), (Item("b", 3, 3, false), 1) },
            Space(0, 0, 10, 10)).Value;

        blocks.Should().HaveCount(2);
        blocks.Select(block => block.Placements[0].ItemId).Should().BeEquivalentTo(new[] { "a", "b" });
    }

    /// <summary>块装载上下文的全局块走复杂拼接 / Global blocks go through complex stitching.</summary>
    [Fact]
    public void Context_GlobalBlocksShouldStitch() {
        IReadOnlyList<Block2<FltX>> blocks = new BlockLoadingContext<FltX>().GlobalBlocks(
            new[] { (Item("a", 2, 2, false), 1), (Item("b", 3, 3, false), 1) },
            Space(0, 0, 10, 20)).Value;

        blocks.Should().ContainSingle();
        blocks[0].Placements.Select(placement => placement.ItemId).Distinct().Should().HaveCount(2);
    }

    /// <summary>上下文透传 DFS 与多行搜索 / The context forwards DFS and multi-row searches.</summary>
    [Fact]
    public void Context_ShouldForwardSearches() {
        var context = new BlockLoadingContext<FltX>();
        (ActualItem<FltX> Item, int Amount)[] items = new[] { (Item("a", 2, 2, false), 10) };

        context.DepthFirst(Space(0, 0, 10, 10), items).Value.Source.Should().Be("dfs");
        context.MultiRow(Space(0, 0, 10, 10), items).Value.Source.Should().Be("multi-row");
    }

    /// <summary>构造区域 / Build a region.</summary>
    private static Space2<FltX> Space(double x, double y, double w, double h) =>
        new(Meters(x), Meters(y), Meters(w), Meters(h));

    /// <summary>构造矩形件 / Build a rectangle item.</summary>
    private static ActualItem<FltX> Item(string id, double w, double h, bool allowRotate) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Meters(w), Meters(h)), allowRotate, Meters(0));

    /// <summary>构造圆形件（入参为直径，此处折半为半径）/ Build a circle item (diameter argument, halved to radius).</summary>
    private static ActualItem<FltX> CircleItem(string id, double diameter) =>
        new(new ItemId(id), id, new CirclePackingShape2<FltX>(Meters(diameter / 2.0)), true, Meters(0));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
