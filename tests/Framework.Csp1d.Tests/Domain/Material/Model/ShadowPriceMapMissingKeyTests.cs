#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.Domain.Material.Model;

/// <summary>
/// `ShadowPriceMap` 的索引器必须在**键不存在**时返回真正的 null。
/// `ShadowPriceMap`'s indexer must return a real null when the **key is absent**.
/// </summary>
/// <remarks>
/// **原先实现是 `TryGetValue(...) ? value : default`**。对 `struct V` 来说 `default` 转 `V?`
/// **不是 null**，而是 `HasValue == true`、值为零的可空值——**返回类型写着 `V?`，实际永远不是 null**。
///
/// 于是"查不到"与"价格为零"**无法区分**。定价代码里 `if (shadowPrice is null) continue;`
/// 因此**是死代码**（靠"零不正"挡掉，恰好正确），而任何想区分这两种情况的调用方**做不到**。
///
/// **The old implementation was `TryGetValue(...) ? value : default`.** For `struct V`, `default` converted to `V?` **is not null** — it is a nullable with
/// `HasValue == true` and value zero, so **the return type said `V?` while it could never be null**.
///
/// So "not found" and "price is zero" were **indistinguishable**. In the pricing code `if (shadowPrice is null) continue;` was therefore **dead** (a missing price
/// was stopped by "zero is not positive", which happened to be right), and any caller wanting to tell the two apart **could not**.
/// </remarks>
public class ShadowPriceMapMissingKeyTests {
    private static ProductDemandShadowPriceKey Key(string productId, string unit = "mm") =>
        new(productId, unit);

    /// <summary>**键不存在时必须返回 null** / An absent key must return null.</summary>
    [Fact]
    public void Indexer_ShouldReturnNullForAMissingKey() {
        var map = new ShadowPriceMap<Flt64>(new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [Key("A")] = new Flt64(2.0),
        });

        map[Key("B")].Should().BeNull("an absent key has no price, which is not the same as a zero price");
    }

    /// <summary>**键存在且值为零时必须不是 null** / A present zero must not be null.</summary>
    /// <remarks>
    /// **这条与上一条必须成对**：只测"缺失返回 null"时，一个"永远返回 null"的实现也会通过；
    /// 只测"存在非 null"时，旧的 `default` 实现也会通过。**两条一起才能锁定语义**。
    ///
    /// **This must be paired with the previous one**: testing only "missing returns null" lets an "always null" implementation pass, and testing only "present is non-null"
    /// lets the old `default` implementation pass. **Only together do they pin the semantics**.
    /// </remarks>
    [Fact]
    public void Indexer_ShouldReturnAZeroForAPresentZeroPrice() {
        var map = new ShadowPriceMap<Flt64>(new Dictionary<Csp1dShadowPriceKey, Flt64> {
            [Key("A")] = Flt64.Zero,
        });

        map[Key("A")].Should().NotBeNull("the key is present, so a zero price is a real value");
        map[Key("A")]!.Value.ToDouble().Should().Be(0.0);
    }
}
