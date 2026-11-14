// 需求量统计（B636）的判据 / Criteria for the demand statistics (B636).

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Demand;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.Demand;

/// <summary>
/// **需求量统计（B636）** / The demand statistics (B636).
/// </summary>
/// <remarks>
/// 判据的重点：**同类才合并**（`Amount` 与 `Area` 跨类型必须失败）、**缩放按类型分别处理**，以及**默认量纲域是离散**。
///
/// The criteria focus on **merging only like values** (`Amount` crossed with `Area` must fail), **scaling per type**, and **the default quantity domain being discrete**.
/// </remarks>
public class DemandStatisticsTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Bpp2dDemandValue<FltX> Amount(ulong value) => new Bpp2dDemandValue<FltX>.Amount<FltX>(value);

    private static Bpp2dDemandValue<FltX> Area(double value) => new Bpp2dDemandValue<FltX>.Area<FltX>(Q(value));

    private static double D(Bpp2dDemandValue<FltX> value) => value switch {
        Bpp2dDemandValue<FltX>.Area<FltX> area => area.Value.Value.ToFlt64().ToDouble(),
        _ => throw new ArgumentException("not an area", nameof(value)),
    };

    /// <summary>
    /// **同类合并，跨类型失败** / Like values merge, cross-type values fail.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两个 `Amount` 相加 → 件数相加。
    /// 2. 两个 `Area` 相加 → 面积相加。
    /// 3. **`Amount` 与 `Area` 合并必须失败**，错误码是 `IllegalArgument`——静默换算会凭空多出一个换算率。
    /// 4. 失败值原样带出，不是被吞掉。
    ///
    /// Four paired points: two `Amount`s add their counts; two `Area`s add their areas; **merging `Amount` with `Area` must fail** with `IllegalArgument`, since a silent conversion
    /// would invent a conversion rate; and the failure is passed back rather than swallowed.
    /// </remarks>
    [Fact]
    public void MergeDemandValue_ShouldRejectCrossTypeMerging() {
        Result<Bpp2dDemandValue<FltX>, ErrorCode, Error<ErrorCode>> amounts =
            Bpp2dDemandValue<FltX>.MergeDemandValue(Amount(3UL), Amount(4UL));
        amounts.IsOk.Should().BeTrue();
        Result<Bpp2dDemandValue<FltX>, ErrorCode, Error<ErrorCode>> amountsValue = amounts;
        var merged = (Bpp2dDemandValue<FltX>.Amount<FltX>)((Ok<Bpp2dDemandValue<FltX>, ErrorCode, Error<ErrorCode>>)amountsValue).Value;
        merged.Value.Should().Be(7UL);

        Result<Bpp2dDemandValue<FltX>, ErrorCode, Error<ErrorCode>> areasResult =
            Bpp2dDemandValue<FltX>.MergeDemandValue(Area(2.5), Area(1.5));
        areasResult.IsOk.Should().BeTrue();
        D(((Ok<Bpp2dDemandValue<FltX>, ErrorCode, Error<ErrorCode>>)areasResult).Value).Should().Be(4.0);

        Result<Bpp2dDemandValue<FltX>, ErrorCode, Error<ErrorCode>> crossed =
            Bpp2dDemandValue<FltX>.MergeDemandValue(Amount(3UL), Area(1.0));
        crossed.IsOk.Should().BeFalse("counts and areas are different dimensions");
        var failed = (Failed<Bpp2dDemandValue<FltX>, ErrorCode, Error<ErrorCode>>)crossed;
        failed.Error.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>
    /// **缩放按类型分别处理** / Scaling is handled per type.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `Amount` 乘倍数 → 件数相乘。
    /// 2. `Area` 乘倍数 → 面积相乘（这里用累加实现，`O(n)`）。
    /// 3. **倍数 1 是恒等**，倍数 0 归零——两个边界都要对，否则累加实现容易差一次。
    ///
    /// Three paired points: scaling an `Amount` multiplies the count; scaling an `Area` multiplies the area (accumulated here, `O(n)`); and **a multiplier of 1 is the identity while 0
    /// gives zero**, both boundaries needing to hold or an accumulation is off by one.
    /// </remarks>
    [Fact]
    public void ScaleDemandValue_ShouldScaleBothKinds() {
        var amount = (Bpp2dDemandValue<FltX>.Amount<FltX>)
            Bpp2dDemandValue<FltX>.ScaleDemandValue(Amount(3UL), 4UL);
        amount.Value.Should().Be(12UL);

        D(Bpp2dDemandValue<FltX>.ScaleDemandValue(Area(2.5), 4UL)).Should().Be(10.0);
        D(Bpp2dDemandValue<FltX>.ScaleDemandValue(Area(2.5), 1UL)).Should().Be(2.5, "multiplying by one is the identity");
        D(Bpp2dDemandValue<FltX>.ScaleDemandValue(Area(2.5), 0UL)).Should().Be(0.0, "multiplying by zero gives zero");

        var scaledOnce = (Bpp2dDemandValue<FltX>.Amount<FltX>)
            Bpp2dDemandValue<FltX>.ScaleDemandValue(Amount(3UL), 1UL);
        scaledOnce.Value.Should().Be(3UL);
    }

    /// <summary>
    /// **条目默认量纲域是离散** / An entry defaults to the discrete quantity domain.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 不传量纲域 → **`Discrete`**，不是 `Continuous`。
    /// 2. 显式传 `Continuous` → 保持。
    /// 3. 四种载体（模式、键、需求、量纲域）都真的存下来。
    ///
    /// Three paired points: omitting the domain yields **`Discrete`** rather than `Continuous`; passing `Continuous` explicitly keeps it; and all four carriers (mode, key, demand,
    /// domain) really persist.
    /// </remarks>
    [Fact]
    public void DemandEntry_ShouldDefaultToDiscrete() {
        var material = new MaterialKey(new MaterialNo("M1"), MaterialType.RawMaterial);
        var mode = new Bpp2dDemandMode<FltX>.Material<FltX>(material);
        var key = new Bpp2dDemandKey<FltX>.Material<FltX>(material);

        var entry = new Bpp2dDemandEntry<FltX>(mode, key, Amount(5UL));
        entry.QuantityDomain.Should().Be(Bpp2dDemandDomain.Discrete, "discrete is the default domain");
        entry.Mode.Should().Be(mode);
        entry.Key.Should().Be(key);
        entry.Demand.Should().Be(Amount(5UL));

        Bpp2dDemandEntry<FltX> continuous = entry with { QuantityDomain = Bpp2dDemandDomain.Continuous };
        continuous.QuantityDomain.Should().Be(Bpp2dDemandDomain.Continuous);
    }
}
