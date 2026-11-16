// 二维影子价格映射的判据 / Criteria for the 2D shadow price map.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **二维影子价格映射** / The 2D shadow price map.
/// </summary>
/// <remarks>
/// 判据的重点：**模式与物品都参与键**（这里存在的理由就是共享基类把这两项折叠掉了），以及**累加、空键返回 null、提取器求和**三条语义。
///
/// The criteria focus on **both mode and item taking part in the key** — the whole reason exists is that the shared base class collapsed those two — plus three
/// semantics: accumulation, null for an unregistered key, and summing extractors.
/// </remarks>
public class Bpp2dShadowPriceMapTests {
    private static ItemId Id(string value) => new(value);

    /// <summary>
    /// **同一个物品的按件数与按面积是两条** / By-count and by-area on one item are two entries.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 累加 `ItemAmount` 与 `ItemArea` 各一条 → **映射表里两条**。
    /// 2. 分别取回，值互不影响。
    /// 3. **没登记的键取回 null**，不是零。
    ///
    /// Three paired points: one `ItemAmount` plus one `ItemArea` gives **two entries** (the shared base class collapsed this to one); each reads back independently; and **an
    /// unregistered key returns null**, not zero.
    /// </remarks>
    [Fact]
    public void Map_ShouldKeyByModeAndItem() {
        var map = new Bpp2dShadowPriceMap();
        map.Add(Bpp2dAssignmentDemandMode.ItemAmount, Id("I1"), new Flt64(1.5));
        map.Add(Bpp2dAssignmentDemandMode.ItemArea, Id("I1"), new Flt64(2.5));

        map.Map.Should().HaveCount(2, "the mode is part of the key");
        map.At(Bpp2dAssignmentDemandMode.ItemAmount, Id("I1"))!.Value.ToDouble().Should().Be(1.5);
        map.At(Bpp2dAssignmentDemandMode.ItemArea, Id("I1"))!.Value.ToDouble().Should().Be(2.5);
        map.At(Bpp2dAssignmentDemandMode.ItemAmount, Id("I2")).Should().BeNull("nothing was registered for I2");
    }

    /// <summary>
    /// **同一键累加，不同物品互不影响** / The same key accumulates; different items stay separate.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 同一键累加两次 → 值相加。
    /// 2. 条目数仍是 1。
    /// 3. **不同物品是不同键**。
    ///
    /// 另有一处：`Put` 是覆盖，`Add` 是累加——两者必须不同，否则"重置定价"没有手段。
    ///
    /// Three paired points: accumulating twice on one key adds the values; the map still holds one entry; and **different items are different keys**. One more: `Put` overwrites while `Add`
    /// accumulates, and they must differ or there is no way to reset a price.
    /// </remarks>
    [Fact]
    public void Map_ShouldAccumulateButPutShouldOverwrite() {
        var map = new Bpp2dShadowPriceMap();
        map.Add(Bpp2dAssignmentDemandMode.ItemAmount, Id("I1"), new Flt64(1.5));
        map.Add(Bpp2dAssignmentDemandMode.ItemAmount, Id("I1"), new Flt64(2.0));
        map.Add(Bpp2dAssignmentDemandMode.ItemAmount, Id("I2"), new Flt64(4.0));

        map.Map.Should().HaveCount(2);
        map.At(Bpp2dAssignmentDemandMode.ItemAmount, Id("I1"))!.Value.ToDouble().Should().Be(3.5);
        map.At(Bpp2dAssignmentDemandMode.ItemAmount, Id("I2"))!.Value.ToDouble().Should().Be(4.0);

        map.Put(Bpp2dAssignmentDemandMode.ItemAmount, Id("I1"), new Flt64(0.5));
        map.At(Bpp2dAssignmentDemandMode.ItemAmount, Id("I1"))!.Value.ToDouble().Should().Be(0.5, "Put replaces rather than accumulates");
    }

    /// <summary>
    /// **提取器求和** / Extractors sum.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 没有提取器 → `Invoke` 返回**零**。
    /// 2. 登记两个 → 结果**相加**，不是取最后一个。
    /// 3. 提取器收到的参数就是传进去的那一份。
    ///
    /// Three paired points: with no extractor `Invoke` returns **zero**; with two registered the result is their **sum** rather than the last one; and the extractor receives exactly the
    /// arguments passed in.
    /// </remarks>
    [Fact]
    public void Map_ShouldSumRegisteredExtractors() {
        var map = new Bpp2dShadowPriceMap();
        var arguments = new Bpp2dShadowPriceArguments(Bpp2dAssignmentDemandMode.ItemArea, Id("I1"));

        map.Invoke(arguments).ToDouble().Should().Be(0.0, "no extractor means no shadow price");

        Bpp2dShadowPriceArguments? seen = null;
        map.Register(received => {
            seen = received;
            return new Flt64(1.25);
        });
        map.Register(_ => new Flt64(0.75));

        map.ExtractorCount.Should().Be(2);
        map.Invoke(arguments).ToDouble().Should().Be(2.0);
        seen.Should().Be(arguments, "the extractor gets the arguments passed in");
    }
}
