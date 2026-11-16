// 影子价格映射转换的判据 / Criteria for the shadow-price map conversion.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Domain.Material.Model;

/// <summary>
/// **`ToShadowPriceMap` 必须保留所有 CSP1D 键，并转换值** / `ToShadowPriceMap` must keep every CSP1D key and convert the value.
/// </summary>
/// <remarks>
/// **记的缺口**："`ShadowPriceMap&lt;V&gt;` 与 `Csp1dDefaultShadowPriceMap` 的转换点无判据"。
///
/// 转换点（`Csp1dProduceContext.cs:614` → `ToShadowPriceMap`）做两件事：
///
/// | 步骤 | 写错的后果 |
/// | --- | --- |
/// | 键过滤 `key is Csp1dShadowPriceKey` | **非 CSP1D 键被静默丢弃** |
/// | 值转换 `converter(price.Price)` | **对偶值没换算** |
///
/// 两个错法都**不报错**：定价阶段查不到某个键时，只会当作"没有对偶价格"，
/// **那个产品的 reduced cost 就悄悄算错了**。
///
/// **the earlier gap**: "the conversion point between `ShadowPriceMap&lt;V&gt;` and `Csp1dDefaultShadowPriceMap` has no criteria". The conversion does two things (table above) and
/// **both failure modes are silent**: a key that goes missing during pricing just reads as "no dual price", and **that product's reduced cost is quietly wrong**.
/// </remarks>
public class ShadowPriceMapConversionTests {
    /// <summary>**多个键必须全部保留，值必须经过转换函数** / Every key must survive and every value must pass through the converter.</summary>
    /// <remarks>
    /// 放三个键、三个不同的对偶值，转换函数是 `× 2`。
    ///
    /// 断言**键集合 + 每个键的值**：
    /// - 只断言数量抓不住"键被换成别的"；
    /// - 只断言一个键抓不住"其余被丢"。
    ///
    /// Three keys with three distinct duals, converter `× 2`. The assertions cover **the key set and every value**: a count alone cannot catch keys being swapped, and a single
    /// key cannot catch the rest being dropped.
    /// </remarks>
    [Fact]
    public void ToShadowPriceMap_ShouldKeepEveryKeyAndConvertEveryValue() {
        var source = new Csp1dDefaultShadowPriceMap();
        source.Put(new ShadowPrice(new ProductDemandShadowPriceKey("A", "mm"), new Flt64(1.5)));
        source.Put(new ShadowPrice(new MaterialUsageShadowPriceKey("M1"), new Flt64(2.5)));
        source.Put(new ShadowPrice(new MachineCapacityShadowPriceKey("K1"), new Flt64(3.5)));

        var converted = source.ToShadowPriceMap(v => new Flt64(v.ToDouble() * 2.0));

        converted[new ProductDemandShadowPriceKey("A", "mm")]!.Value.ToDouble().Should().BeApproximately(3.0, 1e-9);
        converted[new MaterialUsageShadowPriceKey("M1")]!.Value.ToDouble().Should().BeApproximately(5.0, 1e-9);
        converted[new MachineCapacityShadowPriceKey("K1")]!.Value.ToDouble().Should().BeApproximately(7.0, 1e-9);
    }

    /// <summary>**空映射转换后仍是空映射**——反向对照 / An empty map stays empty — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"**凭空造出键**"的实现也可能蒙混过关。
    ///
    /// Without this, an implementation that **invents keys out of thin air** could slip through.
    /// </remarks>
    [Fact]
    public void ToShadowPriceMap_ShouldStayEmptyForAnEmptySource() {
        var source = new Csp1dDefaultShadowPriceMap();

        var converted = source.ToShadowPriceMap(v => v);

        converted[new ProductDemandShadowPriceKey("A", "mm")].Should().BeNull(
            "an empty source must not produce any key");
    }
}
