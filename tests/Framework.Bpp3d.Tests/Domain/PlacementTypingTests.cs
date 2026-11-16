// 因此在本文件内抑制这两条；预算数字与"没有新类别"的判断因此仍然可信。
#nullable enable

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

/// <summary>
/// 真实几何版的顶层/底层与底面重叠过滤，以及把它接到递归上的适配器 /
/// The real-geometry top/bottom placement helpers, the bottom-overlap filter, and the adapter into the recursion.
/// </summary>
/// <remarks>
/// **此前这里有一个语义分歧：** **旧的 `TopItemPlacements` 返回"全局最高一层的所有放置"，
/// 而基线返回"没有任何底面重叠且更高的放置压在其上的放置"。** **一高一低并列时两者答案不同：** **基线两件都算顶层，旧实现只返回高的那件。**
/// **旧实现全仓零调用方，所以替换。** **第一条判据就是那个让两者分道的场景。**
///
/// **There used to be a semantic divergence: the old `TopItemPlacements` returned "every placement on the globally highest layer" while the code
/// returns "every placement that no overlapping higher placement sits above".** **With a tall and a short item side by side the two disagree: the code
/// counts both, the old one returned only the tall one.** **The old implementation had zero callers, so it is replaced by its. The first criterion
/// is exactly the shape that separates them.**
/// </remarks>
public class PlacementTypingTests
{
    /// <summary>**一高一低并列时两件都算顶层** / With a tall and a short item side by side, both are top.</summary>
    /// <remarks>
    /// 高箱 (0..2) 与矮箱 (0..1) 并排立在同一底座上，谁也不压谁。**旧实现会按"全局最高 Y"只挑出高箱**——这正是被替换掉的那个语义。
    ///
    /// A tall box (0..2) and a short box (0..1) stand side by side on the same base, covering neither. **The old implementation picked only the tall one by
    /// "globally highest Y", which is exactly the semantics that was replaced.**
    /// </remarks>
    [Fact]
    public void TopPlacements_ShouldKeepBothOfAnUnevenPair() {
        QuantityPlacement3<ActualItem, FltX> baseItem = Box("base", height: 1.0, x: 0.0, z: 0.0);
        QuantityPlacement3<ActualItem, FltX> tall = Box("tall", height: 2.0, x: 0.0, z: 0.0, y: 1.0);
        QuantityPlacement3<ActualItem, FltX> shortItem = Box("short", height: 1.0, x: 5.0, z: 0.0, y: 1.0);

        var placements = new List<QuantityPlacement3<ActualItem, FltX>> { baseItem, tall, shortItem };

IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> tops = PlacementTyping.TopItemPlacements(placements);
        tops.Should().HaveCount(2, "the tall and the short item both have nothing on top of them");
        tops.Should().Contain(tall);
        tops.Should().Contain(shortItem);
        tops.Should().NotContain(baseItem, "the base is covered by the tall box standing on it");

        // 基线比的是放置的下沿（`.y`）：矮箱在 x=5 处与底座不重叠，所以它下面确实没有东西，也算底面放置。
        // The code compares bottom edges (`.y`): the short box at x=5 does not overlap the base, so nothing is under it and it is a bottom
        // placement too.
IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> bottoms = PlacementTyping.BottomItemPlacements(placements);
        bottoms.Should().Contain(baseItem, "nothing is under the base");
        bottoms.Should().NotContain(tall, "the tall box stands on the base");
        bottoms.Should().Contain(shortItem, "the short box is off to the side, over nothing");
    }

    /// <summary>底面重叠只认投影，不认高度 / Bottom overlap is about the footprint, not the height.</summary>
    [Fact]
    public void FilterBottomOverlapped_ShouldUseTheFootprint() {
        QuantityPlacement3<ActualItem, FltX> target = Box("base", height: 1.0, x: 0.0, z: 0.0);
        QuantityPlacement3<ActualItem, FltX> above = Box("above", height: 1.0, x: 0.0, z: 0.0, y: 1.0);
QuantityPlacement3<ActualItem, FltX> aside = Box("aside", height: 1.0, x: 5.0, z: 0.0);

IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> overlapping = PlacementTyping.FilterBottomOverlapped(new[] { above, aside }, target);
        overlapping.Should().ContainSingle("only the box directly above shares the footprint");
        overlapping[0].Should().Be(above);
    }

    /// <summary>真实几何接进递归：三层同类型给出层数 3、高度 3 倍 / Real geometry feeds the recursion: three same-type layers.</summary>
    /// <remarks>
    /// **这条是"保留 `Covers` 抽象"的兑现：** **递归一行没改，真实放置通过 `AsStackPlacement` 接进去，**
    /// **于是同一段代码既能用判据里的最小替身，也能用真实几何。**
    ///
    /// **This is what keeping the `Covers` abstraction buys: the recursion is untouched, real placements plug in through `AsStackPlacement`, and the same code
    /// serves both the minimal double in the criteria and real geometry.**
    /// </remarks>
    [Fact]
    public void TheAdapter_ShouldFeedTheRecursionWithRealGeometry() {
        QuantityPlacement3<ActualItem, FltX> baseItem = Box("base", height: 1.0, x: 0.0, z: 0.0);
QuantityPlacement3<ActualItem, FltX> middle = Box("middle", height: 1.0, x: 0.0, z: 0.0, y: 1.0);
QuantityPlacement3<ActualItem, FltX> top = Box("top", height: 1.0, x: 0.0, z: 0.0, y: 2.0);
        var placements = new List<QuantityPlacement3<ActualItem, FltX>> { baseItem, middle, top };
var views = placements.Select(PlacementTyping.AsStackPlacement).ToList();
        IStackPlacement topView = PlacementTyping.AsStackPlacement(top);
        IReadOnlyList<IStackPlacement> below = new List<IStackPlacement> { views[0], views[1], views[2] };

        views.Should().HaveCount(3);
        (UInt64 layer, Quantity<FltX> height) = StackLayering.Layer(topView, below);
        layer.Should().Be(new UInt64(3UL), "the top sits on the middle, which sits on the base: three levels");
        height.Value.ToFlt64().ToDouble().Should().Be(
            3.0,
            "three one-metre boxes stacked to the top edge");
    }

    /// <summary>底面重叠只看投影，与高度无关 / Bottom overlap ignores height.</summary>
    /// <remarks>
    /// 基线递归用的是 `filterBottomOverlapped`：**只比底面投影。** 一个浮在两米多高、投影重叠的放置**仍然算"在同一个竖向通道里"**，
    /// 因为基线要的是"这一列里还有谁"，而不是"谁承着谁"。第一版按"贴着"实现，三层堆叠因此只数出两层。
    ///
    /// The code recursion uses `filterBottomOverlapped`, which compares bottom footprints only. A placement floating two metres up but sharing the
    /// footprint still counts as "in the same vertical column", because the code asks who else is in this column, not who carries whom. The first version
    /// implemented "touching" and therefore counted a three-high stack as two.
    /// </remarks>
    [Fact]
    public void OverlapsBottom_ShouldIgnoreHeight() {
        QuantityPlacement3<ActualItem, FltX> baseItem = Box("base", height: 1.0, x: 0.0, z: 0.0);
QuantityPlacement3<ActualItem, FltX> floating = Box("floating", height: 1.0, x: 0.0, z: 0.0, y: 2.5);
QuantityPlacement3<ActualItem, FltX> aside = Box("aside", height: 1.0, x: 5.0, z: 0.0);

        PlacementTyping.AsStackPlacement(baseItem).OverlapsBottom(PlacementTyping.AsStackPlacement(floating))
            .Should().BeTrue("the footprints overlap, and the vertical gap is not this predicate's business");
        PlacementTyping.AsStackPlacement(baseItem).OverlapsBottom(PlacementTyping.AsStackPlacement(aside))
            .Should().BeFalse("a box off to the side shares no footprint");
    }

    private static QuantityPlacement3<ActualItem, FltX> Box(
        string id,
        double height,
        double x,
        double z,
        double y = 0.0) =>
        new(
            new ActualItem(
                id,
                id,
                new Quantity<FltX>(new FltX(1m), SIBaseUnits.Meter),
                new Quantity<FltX>(new FltX((decimal)height), SIBaseUnits.Meter),
                new Quantity<FltX>(new FltX(1m), SIBaseUnits.Meter),
                new Quantity<FltX>(new FltX(1m), SIBaseUnits.Kilogram)),
            new QuantityPoint3<FltX>(
                new Quantity<FltX>(new FltX((decimal)x), SIBaseUnits.Meter),
                new Quantity<FltX>(new FltX((decimal)y), SIBaseUnits.Meter),
                new Quantity<FltX>(new FltX((decimal)z), SIBaseUnits.Meter)));
}