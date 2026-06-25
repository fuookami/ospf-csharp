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

    [Fact]
    public async Task BinCapacityConstraintRefreshShouldSucceed() {
        var constraint = new BinCapacityConstraint<FltX>();
        var map = new TestShadowPriceMap();

        Result<Success, ErrorCode, Error<ErrorCode>> result = await constraint.RefreshAsync(map);

        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void BinCapacityConstraintRightHandSideShouldReturnEmpty() {
        var constraint = new BinCapacityConstraint<FltX>();

        Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<FltX>>, ErrorCode, Error<ErrorCode>> result = constraint.RightHandSide();

        result.IsFailed.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    // ===== BinDepthConstraint =====

    [Fact]
    public async Task BinDepthConstraintRefreshShouldSucceed() {
        var constraint = new BinDepthConstraint<FltX>();
        var map = new TestShadowPriceMap();

        Result<Success, ErrorCode, Error<ErrorCode>> result = await constraint.RefreshAsync(map);

        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void BinDepthConstraintRightHandSideShouldReturnEmpty() {
        var constraint = new BinDepthConstraint<FltX>();

        Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<FltX>>, ErrorCode, Error<ErrorCode>> result = constraint.RightHandSide();

        result.IsFailed.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    // ===== BinLoadingOrderConstraint =====

    [Fact]
    public async Task BinLoadingOrderConstraintRefreshShouldSucceed() {
        var constraint = new BinLoadingOrderConstraint<FltX>();
        var map = new TestShadowPriceMap();

        Result<Success, ErrorCode, Error<ErrorCode>> result = await constraint.RefreshAsync(map);

        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void BinLoadingOrderConstraintRightHandSideShouldReturnEmpty() {
        var constraint = new BinLoadingOrderConstraint<FltX>();

        Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<FltX>>, ErrorCode, Error<ErrorCode>> result = constraint.RightHandSide();

        result.IsFailed.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    // ===== TailBinAssignmentConstraint =====

    [Fact]
    public async Task TailBinAssignmentConstraintRefreshShouldSucceed() {
        var constraint = new TailBinAssignmentConstraint<FltX>();
        var map = new TestShadowPriceMap();

        Result<Success, ErrorCode, Error<ErrorCode>> result = await constraint.RefreshAsync(map);

        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void TailBinAssignmentConstraintRightHandSideShouldReturnEmpty() {
        var constraint = new TailBinAssignmentConstraint<FltX>();

        Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<FltX>>, ErrorCode, Error<ErrorCode>> result = constraint.RightHandSide();

        result.IsFailed.Should().BeFalse();
        result.Value.Should().BeEmpty();
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
