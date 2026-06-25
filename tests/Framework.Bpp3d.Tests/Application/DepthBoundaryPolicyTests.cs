#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Application;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Application;

public class DepthBoundaryPolicyTests {
    private static Quantity<FltX> M(double v) => new(new FltX(v), SIBaseUnits.Meter);

    [Fact]
    public void PolicyWithNoConstraintsShouldNotBeEnabled() {
        var policy = new DepthBoundaryLayerOrientationPolicy();
        policy.Enabled.Should().BeFalse();
    }

    [Fact]
    public void PolicyWithFirstLayerCylinderAxesShouldBeEnabled() {
        var policy = new DepthBoundaryLayerOrientationPolicy(
            FirstLayerAllowedCylinderAxes: new HashSet<Axis3> { Axis3.Y });
        policy.Enabled.Should().BeTrue();
    }

    [Fact]
    public void PolicyWithLastLayerCylinderAxesShouldBeEnabled() {
        var policy = new DepthBoundaryLayerOrientationPolicy(
            LastLayerAllowedCylinderAxes: new HashSet<Axis3> { Axis3.Y });
        policy.Enabled.Should().BeTrue();
    }

    [Fact]
    public void PolicyWithFirstLayerCuboidOrientationsShouldBeEnabled() {
        var policy = new DepthBoundaryLayerOrientationPolicy(
            FirstLayerAllowedCuboidOrientations: new HashSet<Orientation> { Orientation.Upright });
        policy.Enabled.Should().BeTrue();
    }

    [Fact]
    public void PolicyWithLastLayerCuboidOrientationsShouldBeEnabled() {
        var policy = new DepthBoundaryLayerOrientationPolicy(
            LastLayerAllowedCuboidOrientations: new HashSet<Orientation> { Orientation.Upright });
        policy.Enabled.Should().BeTrue();
    }

    [Fact]
    public void DisabledPolicyShouldPassWithEmptyBins() {
        var policy = new DepthBoundaryLayerOrientationPolicy();
        Result<Success, ErrorCode, Error<ErrorCode>> result = policy.EnsureSatisfied(Array.Empty<Bin<BinLayer, FltX>>());
        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void EnabledPolicyShouldPassWithEmptyBins() {
        var policy = new DepthBoundaryLayerOrientationPolicy(
            FirstLayerAllowedCuboidOrientations: new HashSet<Orientation> { Orientation.Upright });
        Result<Success, ErrorCode, Error<ErrorCode>> result = policy.EnsureSatisfied(Array.Empty<Bin<BinLayer, FltX>>());
        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void EnabledPolicyShouldPassWhenBinHasNoUnits() {
        var policy = new DepthBoundaryLayerOrientationPolicy(
            FirstLayerAllowedCuboidOrientations: new HashSet<Orientation> { Orientation.Upright });
        var bin = new Bin<BinLayer, FltX>(
            new Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model.BinType<FltX>("B1", M(3), M(3), M(3), new Quantity<FltX>(FltX.One, SIBaseUnits.Kilogram)),
            Array.Empty<QuantityPlacement3<BinLayer, FltX>>());
        Result<Success, ErrorCode, Error<ErrorCode>> result = policy.EnsureSatisfied(new[] { bin });
        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void EnabledPolicyShouldPassWhenLayerUnitsOrientationIsAllowed() {
        var item = new Item("I1", "Item 1");
        var layer = new BinLayer {
            Shape = new Container3Shape<FltX>(M(3), M(3), M(3)),
            Units = new[] {
                new QuantityPlacement3<Item, FltX>(item, new QuantityPoint3<FltX>(M(0), M(0), M(0)), Orientation.Upright)
            }
        };
        var bin = new Bin<BinLayer, FltX>(
            new Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model.BinType<FltX>("B1", M(3), M(3), M(3), new Quantity<FltX>(FltX.One, SIBaseUnits.Kilogram)),
            new[] {
                new QuantityPlacement3<BinLayer, FltX>(layer, new QuantityPoint3<FltX>(M(0), M(0), M(0)))
            });

        var policy = new DepthBoundaryLayerOrientationPolicy(
            FirstLayerAllowedCuboidOrientations: new HashSet<Orientation> { Orientation.Upright });

        Result<Success, ErrorCode, Error<ErrorCode>> result = policy.EnsureSatisfied(new[] { bin });
        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void EnabledPolicyShouldFailWhenLayerUnitsOrientationIsNotAllowed() {
        var item = new Item("I1", "Item 1");
        var layer = new BinLayer {
            Shape = new Container3Shape<FltX>(M(3), M(3), M(3)),
            Units = new[] {
                new QuantityPlacement3<Item, FltX>(item, new QuantityPoint3<FltX>(M(0), M(0), M(0)), Orientation.Lie)
            }
        };
        var bin = new Bin<BinLayer, FltX>(
            new Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model.BinType<FltX>("B1", M(3), M(3), M(3), new Quantity<FltX>(FltX.One, SIBaseUnits.Kilogram)),
            new[] {
                new QuantityPlacement3<BinLayer, FltX>(layer, new QuantityPoint3<FltX>(M(0), M(0), M(0)))
            });

        var policy = new DepthBoundaryLayerOrientationPolicy(
            FirstLayerAllowedCuboidOrientations: new HashSet<Orientation> { Orientation.Upright });

        Result<Success, ErrorCode, Error<ErrorCode>> result = policy.EnsureSatisfied(new[] { bin });
        result.IsFailed.Should().BeTrue();
    }

    // ===== MaterialPackingMixedDemandPolicy =====

    [Fact]
    public void MaterialPackingMixedDemandPolicyShouldHaveExpectedValues() {
        MaterialPackingMixedDemandPolicy[] values = Enum.GetValues<MaterialPackingMixedDemandPolicy>();
        values.Should().Contain(MaterialPackingMixedDemandPolicy.Reject);
        values.Should().Contain(MaterialPackingMixedDemandPolicy.PreferItem);
        values.Should().Contain(MaterialPackingMixedDemandPolicy.PreferMaterial);
    }
}
