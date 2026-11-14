#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain.LayerAssignment;

public class ConstraintTests {
    private static Quantity<FltX> M(double v) => new(new FltX(v), SIBaseUnits.Meter);

    private sealed class TestShadowPriceMap : AbstractBpp3dShadowPriceMap { }

    // ===== BinCapacityConstraint =====

    private static Quantity<FltX> Kg(double v) => new(new FltX(v), SIBaseUnits.Kilogram);

    [Fact]
    public async Task BinCapacityConstraintRefreshShouldSucceed() {
        var constraint = new BinCapacityConstraint<FltX>(new[] { Kg(100) });
        var map = new TestShadowPriceMap();

        Result<Success, ErrorCode, Error<ErrorCode>> result = await constraint.RefreshAsync(map);

        result.IsFailed.Should().BeFalse();
    }

    /// <summary>**每只箱子一条右端项，数值就是它的载重** / One right-hand side per bin, carrying its weight capacity.</summary>
    /// <remarks>
    /// 修复前 `RightHandSide()` **恒返回空字典**，测试还把"返回空"写成预期。两条箱子 100 kg / 50 kg 的夹具
    /// 同时钉住"有条目""数值对""条数对"——只断言"非空"会放过"两条都填第一只箱子的载重"。
    ///
    /// Before the fix `RightHandSide()` **always returned an empty dictionary**, and the test asserted the empty result as expected. A two-bin fixture
    /// of 100 kg / 50 kg pins "there are entries", "the values are right" and "the count is right" — asserting only "not empty" would let an
    /// implementation that fills both entries with the first bin's capacity pass.
    /// </remarks>
    [Fact]
    public void BinCapacityConstraintRightHandSideShouldCarryOneEntryPerBin() {
        var constraint = new BinCapacityConstraint<FltX>(new[] { Kg(100), Kg(50) });

        Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<FltX>>, ErrorCode, Error<ErrorCode>> result =
            constraint.RightHandSide();

        result.IsFailed.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value[new DemandShadowPriceKey.ByMaterial("bin_0")].ToDouble().Should().Be(100.0);
        result.Value[new DemandShadowPriceKey.ByMaterial("bin_1")].ToDouble().Should().Be(50.0);
    }

    [Fact]
    public void BinCapacityConstraintCountShouldMatchTheBins() =>
        new BinCapacityConstraint<FltX>(new[] { Kg(1), Kg(2), Kg(3) }).Count.Should().Be(3);

    /// <summary>**剩余载重判据在边界上通过、越界时拒绝** / The remaining-capacity check passes at the boundary and rejects beyond it.</summary>
    [Fact]
    public void BinCapacityConstraintShouldCheckRemainingCapacity() {
        var constraint = new BinCapacityConstraint<FltX>(new[] { Kg(100) });

        // 60 + 40 = 100，正好装满 → 装得下 / 60 + 40 = 100, exactly full → fits.
        constraint.CanHold(0, Kg(60), Kg(40)).Value.Should().BeTrue();
        // 60 + 41 = 101 → 装不下 / 60 + 41 = 101 → does not fit.
        constraint.CanHold(0, Kg(60), Kg(41)).Value.Should().BeFalse();
    }

    /// <summary>**单位不符必须失败，不能当成"装得下"** / A unit mismatch must fail rather than count as "it fits".</summary>
    [Fact]
    public void BinCapacityConstraintShouldRejectUnitMismatch() {
        var constraint = new BinCapacityConstraint<FltX>(new[] { Kg(100) });

        constraint.CanHold(0, M(1), M(1)).IsOk.Should().BeFalse();
    }

    /// <summary>**箱序号越界必须失败** / An out-of-range bin index must fail.</summary>
    [Fact]
    public void BinCapacityConstraintShouldRejectOutOfRangeIndex() {
        var constraint = new BinCapacityConstraint<FltX>(new[] { Kg(100) });

        constraint.CanHold(1, Kg(1), Kg(1)).IsOk.Should().BeFalse();
        constraint.CanHold(-1, Kg(1), Kg(1)).IsOk.Should().BeFalse();
    }

    // ===== BinDepthConstraint =====

    [Fact]
    public async Task BinDepthConstraintRefreshShouldSucceed() {
        var constraint = new BinDepthConstraint<FltX>(new[] { M(10) });
        var map = new TestShadowPriceMap();

        Result<Success, ErrorCode, Error<ErrorCode>> result = await constraint.RefreshAsync(map);

        result.IsFailed.Should().BeFalse();
    }

    /// <summary>**每只箱子一条深度右端项** / One depth right-hand side per bin.</summary>
    /// <remarks>
    /// 修复前 `RightHandSide()` **恒返回空字典**。两条箱深 10 / 4 的夹具同时钉住条数与数值——
    /// 只断言"非空"会放过"两条都填第一只箱子的深度"。
    ///
    /// Before the fix `RightHandSide()` **always returned an empty dictionary**. Two bins of depth 10 / 4 pin both the count and the values — asserting
    /// only "not empty" would let an implementation that fills both with the first bin's depth pass.
    /// </remarks>
    [Fact]
    public void BinDepthConstraintRightHandSideShouldCarryOneEntryPerBin() {
        var constraint = new BinDepthConstraint<FltX>(new[] { M(10), M(4) });

        Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<FltX>>, ErrorCode, Error<ErrorCode>> result =
            constraint.RightHandSide();

        result.IsFailed.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value[new DemandShadowPriceKey.ByMaterial("bin_0")].ToDouble().Should().Be(10.0);
        result.Value[new DemandShadowPriceKey.ByMaterial("bin_1")].ToDouble().Should().Be(4.0);
    }

    [Fact]
    public void BinDepthConstraintCountShouldMatchTheBins() =>
        new BinDepthConstraint<FltX>(new[] { M(1), M(2) }).Count.Should().Be(2);

    /// <summary>**层深累加在边界通过、超出一层被拒** / Layer depths pass at the boundary and fail one layer beyond.</summary>
    [Fact]
    public void BinDepthConstraintShouldCheckRemainingDepth() {
        var constraint = new BinDepthConstraint<FltX>(new[] { M(10) });

        constraint.CanHold(0, M(6), M(4)).Value.Should().BeTrue();
        constraint.CanHold(0, M(6), M(4.5)).Value.Should().BeFalse();
    }

    /// <summary>**深度单位混用必须失败** / Mixing depth units must fail.</summary>
    [Fact]
    public void BinDepthConstraintShouldRejectUnitMismatch() {
        var constraint = new BinDepthConstraint<FltX>(new[] { M(10) });

        constraint.CanHold(0, Kg(1), Kg(1)).IsOk.Should().BeFalse();
    }

    /// <summary>**箱序号越界必须失败** / An out-of-range bin index must fail.</summary>
    [Fact]
    public void BinDepthConstraintShouldRejectOutOfRangeIndex() {
        var constraint = new BinDepthConstraint<FltX>(new[] { M(10) });

        constraint.CanHold(1, M(1), M(1)).IsOk.Should().BeFalse();
        constraint.CanHold(-1, M(1), M(1)).IsOk.Should().BeFalse();
    }



    // ===== ItemDemandConstraint =====

    [Fact]
    public async Task ItemDemandConstraintRefreshShouldSucceedWhenAllPricesRegistered() {
        var key = new DemandShadowPriceKey.ByMaterial("M001");
        var demands = new Dictionary<DemandShadowPriceKey, Quantity<FltX>> {
            { key, M(10) }
        };
        var constraint = new ItemDemandConstraint<FltX>(demands);

        var map = new TestShadowPriceMap();
        map.Register(key, new Flt64(1.5));

        Result<Success, ErrorCode, Error<ErrorCode>> result = await constraint.RefreshAsync(map);

        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    public async Task ItemDemandConstraintRefreshShouldFailWhenPriceMissing() {
        var key = new DemandShadowPriceKey.ByMaterial("M001");
        var demands = new Dictionary<DemandShadowPriceKey, Quantity<FltX>> {
            { key, M(10) }
        };
        var constraint = new ItemDemandConstraint<FltX>(demands);

        var map = new TestShadowPriceMap();
        // No price registered for key

        Result<Success, ErrorCode, Error<ErrorCode>> result = await constraint.RefreshAsync(map);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ItemDemandConstraintRightHandSideShouldReturnDemands() {
        var key = new DemandShadowPriceKey.ByMaterial("M001");
        var demands = new Dictionary<DemandShadowPriceKey, Quantity<FltX>> {
            { key, M(10) }
        };
        var constraint = new ItemDemandConstraint<FltX>(demands);

        Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<FltX>>, ErrorCode, Error<ErrorCode>> result = constraint.RightHandSide();

        result.IsFailed.Should().BeFalse();
        result.Value.Should().HaveCount(1);
        result.Value.Should().ContainKey(key);
    }

    // ===== Objectives =====

    [Fact]
    public void BetterLayerMaximizationNameShouldBeCorrect() {
        var obj = new BetterLayerMaximization<FltX>();
        obj.Name.Should().Contain("BetterLayerMaximization");
    }

    [Fact]
    public void BinAmountMinimizationNameShouldBeCorrect() {
        var obj = new BinAmountMinimization<FltX>();
        obj.Name.Should().Contain("BinAmountMinimization");
    }

    [Fact]
    public void RestAmountMinimizationNameShouldBeCorrect() {
        var obj = new RestAmountMinimization<FltX>();
        obj.Name.Should().Contain("RestAmountMinimization");
    }

    [Fact]
    public void TailBinLoadingRateMinimizationNameShouldBeCorrect() {
        var obj = new TailBinLoadingRateMinimization<FltX>();
        obj.Name.Should().Contain("TailBinLoadingRateMinimization");
    }

    [Fact]
    public void VolumeMinimizationNameShouldBeCorrect() {
        var obj = new VolumeMinimization<FltX>();
        obj.Name.Should().Contain("VolumeMinimization");
    }
}
