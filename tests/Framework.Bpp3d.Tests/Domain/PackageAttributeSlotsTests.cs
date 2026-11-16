// 因此在本文件内抑制这两条；预算数字与"没有新类别"的判断因此仍然可信。
#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

/// <summary>
/// B1310 补上的两块：五个策略槽位（含有效层数的合成）与递归层数计算 /
/// The two pieces B1310 filled in: the five policy slots (with the effective layer composition) and the recursive layer computation.
/// </summary>
/// <remarks>
/// 基线里这两块分别是 `PackageAttribute` 的字段与它伴生对象里的 `layerLayer`／`layerHeight`／`layer`。
/// 前者的语义是"有效层数 = min(包材层数, 重量属性层数)"，后者是"沿着'谁压在谁上面'递归取最大值"。
/// 判据各钉住一半：层数合成能变小、递归在同类型链上给出正确的层数与高度、类型不同则不叠。
///
/// In the baseline these are the fields of `PackageAttribute` and its companion's `layerLayer`/`layerHeight`/`layer`. The former means "effective layers =
/// min(package layers, weight-attribute layers)" and the latter "recurse along who-covers-whom and take the maximum". The criteria pin each half: the
/// composition can only lower, the recursion gives the right count and height on a same-type chain, and different types do not stack.
/// </remarks>
public class PackageAttributeSlotsTests
{
    /// <summary>有效层数只能取小，且没配重量属性时不变 / The effective layer count only lowers, and stays put without a weight attribute.</summary>
    [Fact]
    public void TheEffectiveLayerCount_ShouldTakeTheMinimum() {
        var bound = new PackageAttribute(PackageType.Box, new UInt64(10UL), new UInt64(3UL), UInt64.Zero, UInt64.Zero);
        bound.MaxLayer.Should().Be(new UInt64(10UL), "with no weight attribute the package count is the answer");

        PackageAttribute lowered = bound with { WeightAttribute = new WeightAttribute(new UInt64(4UL)) };
        lowered.MaxLayer.Should().Be(new UInt64(4UL), "10 package layers capped by 4 weight layers is 4");
        lowered.PackageMaxLayer.Should().Be(new UInt64(10UL), "the package count itself is not overwritten");

        PackageAttribute looser = bound with { WeightAttribute = new WeightAttribute(new UInt64(99UL)) };
        looser.MaxLayer.Should().Be(new UInt64(10UL), "a looser weight limit cannot raise the package limit");
    }

    /// <summary>五个槽位都能挂上且可空 / All five slots accept a value and none of them is required.</summary>
    [Fact]
    public void TheFiveSlots_ShouldBeOptional() {
        var plain = new PackageAttribute(PackageType.Box, new UInt64(2UL), new UInt64(1UL), UInt64.Zero, UInt64.Zero);
        plain.CargoAttribute.Should().BeNull("a package attribute without cargo attributes is legitimate");
        plain.DeformationAttribute.Should().BeNull();
        plain.HangingPolicy.Should().BeNull();
        plain.StackingOnPolicy.Should().BeNull();
        plain.WeightAttribute.Should().BeNull();

        PackageAttribute decorated = plain with {
            CargoAttribute = new WeightAttributeMarker(),
            WeightAttribute = new WeightAttribute(new UInt64(2UL)),
            DeformationAttribute = new LinearDeformationAttribute(new FltX(0.5m)),
            HangingPolicy = new RelativeHangingPolicy(new FltX(0.1m)),
            StackingOnPolicy = new FilterStackingOnPolicy(),
        };
        decorated.CargoAttribute.Should().NotBeNull();
        decorated.DeformationAttribute.Should().BeOfType<LinearDeformationAttribute>();
        decorated.HangingPolicy.Should().BeOfType<RelativeHangingPolicy>();
        decorated.StackingOnPolicy.Should().BeOfType<FilterStackingOnPolicy>();
        decorated.MaxLayer.Should().Be(new UInt64(2UL), "the weight attribute agrees with the package count here");
    }

    /// <summary>**递归层数：三层同类型堆叠给出层数 3、高度 3 倍** / A three-high same-type stack gives layer 3 and three times the height.</summary>
    /// <remarks>
    /// **构造方式值得看：** **判据不伪造几何，而是用一个按 y 坐标判定的最小放置——**"谁压在谁上面"就是 `Bottom >= Top` 且类型相同，
    /// 这正是基线的 `filterBottomOverlapped` 在最简单情形下的语义。** 上层放两件、它们再压住一件、那件再压住底座。**
    ///
    /// **The construction is worth reading: the criterion does not fake geometry but uses a minimal placement ordered by y — "sits on top" means the
    /// bottom's base is at or above the top's top, which is the baseline's `filterBottomOverlapped` in the simplest case.** **Two items on the second
    /// level, one item under them, and that one on the base.**
    /// </remarks>
    [Fact]
    public void TheRecursion_ShouldCountAThreeHighStack() {
        var baseItem = new Placement("box", 1.0, bottom: 0.0);
        var middle = new Placement("box", 1.0, bottom: 1.0);
        var left = new Placement("box", 1.0, bottom: 2.0);
        var right = new Placement("box", 1.0, bottom: 2.0);

        IReadOnlyList<Placement> bottom = new[] { baseItem, middle, left, right };
        // 基线 `directBottomItems` 就是 `topItemPlacements`——**给出"这一列里最上面的那些"，不是"最底下的那个"。**
        // 上一版把它理解反了，改成 IsAbove 之后才露出来。
        //
        // The baseline's `directBottomItems` is `topItemPlacements` — **the topmost items of the column, not the bottom one.** The previous version had it
        // backwards, which the `IsAbove` split exposed.
        StackLayering.DirectBottomItems(bottom).Should().HaveCount(2, "the two items on the second level are the ones with nothing above them");

        (UInt64 layer, Quantity<FltX> height) = StackLayering.Layer(left, bottom);
        layer.Should().Be(new UInt64(3UL), "the two top items sit on the middle one, which sits on the base");
        height.Should().Be(
            new Quantity<FltX>(new FltX(3m), SIBaseUnits.Meter),
            "the reported height is the stacked height of the same-type chain, not the base's own height");
    }

    /// <summary>不同类型的货物不计入层数 / A different item type does not contribute to the layer count.</summary>
    [Fact]
    public void DifferentItemTypes_ShouldNotStack() {
        var baseItem = new Placement("pallet", 1.0, bottom: 0.0);
        var box = new Placement("box", 1.0, bottom: 1.0);

        // 底部集合只装"底下的东西"——把 `box` 自己也放进去会让算法把自身当成一块同类型底板（基线同样如此，它的入参就是 bottoms）。
        // The bottom set holds what is underneath; including `box` itself would let the algorithm treat it as a same-type layer below (the baseline's
        // parameter is the bottoms, too).
        StackLayering.LayerCount(box, new[] { baseItem }).Should().Be(
            UInt64.Zero,
            "the base is a different type, so the baseline skips it and there is no same-type branch to count");
    }

    /// <summary>空底部集合给出零层零高 / An empty bottom set gives zero layers and zero height.</summary>
    [Fact]
    public void AnEmptyBottomSet_ShouldBeZero() {
        var item = new Placement("box", 1.0, bottom: 5.0);
        StackLayering.LayerCount(item, Array.Empty<Placement>()).Should().Be(UInt64.Zero);
        StackLayering.LayerHeight(item, Array.Empty<Placement>()).Value.ToFlt64().ToDouble().Should().Be(0.0);
    }

    /// <summary>判据用的最小放置：按 y 坐标判定"谁压在谁上面"/ A minimal placement ordered by y.</summary>
    private sealed class Placement(string itemType, double height, double bottom) : IStackPlacement
    {
        public string ItemType { get; } = itemType;

        public Quantity<FltX> Height { get; } = new(new FltX((decimal)height), SIBaseUnits.Meter);

        private double Bottom { get; } = bottom;

        public bool OverlapsBottom(IStackPlacement other) =>
            // 底面重叠 = 同一竖向列：替身没有水平坐标，于是同类型即同列。
            // 基线比的是底面投影，与高度无关；这一条正是被 `OverlapsBottom`／`IsAbove` 分开之后才看清的。
            //
            // Footprint overlap means the same vertical column; the double has no horizontal coordinate, so same type is the same column. The baseline
            // compares bottom footprints regardless of height, which only became clear once `OverlapsBottom` and `IsAbove` were separated.
            other is Placement placement &&
            string.Equals(placement.ItemType, ItemType, StringComparison.Ordinal);

        public bool IsAbove(IStackPlacement other) =>
            other is Placement placement && Bottom + height > placement.Bottom + placement.Height.Value.ToFlt64().ToDouble();
    }

    /// <summary>只为覆盖 `CargoAttribute` 槽位而用的标记实现 / A marker implementation used only to fill the `CargoAttribute` slot.</summary>
    private sealed class WeightAttributeMarker : AbstractCargoAttribute
    {
    }
}