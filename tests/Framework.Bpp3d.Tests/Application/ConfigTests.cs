#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Application;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Application;

public class ConfigTests {
    private static Quantity<FltX> M(double v) => new(new FltX(v), SIBaseUnits.Meter);

    [Fact]
    public void DefaultConfigShouldHaveExpectedValues() {
        ColumnGenerationConfig config = ColumnGenerationConfig.Default;
        config.IterationLimit.Should().Be(128);
        config.MaxColumnsPerIteration.Should().Be(64);
        config.FinalMilpEnabled.Should().BeTrue();
        config.TimeLimit.Should().BeNull();
    }

    [Fact]
    public void ConfigShouldSupportWithExpression() {
        ColumnGenerationConfig config = ColumnGenerationConfig.Default with { IterationLimit = 10, FinalMilpEnabled = false };
        config.IterationLimit.Should().Be(10);
        config.FinalMilpEnabled.Should().BeFalse();
    }

    [Fact]
    public void InfiniteTimeLimitShouldBeTimeoutInfinite() => ColumnGenerationConfig.InfiniteTimeLimit.Should().Be(Timeout.InfiniteTimeSpan);

    [Fact]
    public void StandardExecutorConfigDefaultShouldHaveExpectedValues() {
        ColumnGenerationStandardExecutorConfig config = ColumnGenerationStandardExecutorConfig.Default;
        config.RmpSolveNamePrefix.Should().Be("bpp3d-rmp");
        config.FinalSolveNamePrefix.Should().Be("bpp3d-final");
        config.RmpToLogModel.Should().BeFalse();
        config.FinalToLogModel.Should().BeFalse();
        config.EnableFinalBinDepthConstraint.Should().BeTrue();
        config.EnableFinalBinCapacityConstraint.Should().BeTrue();
        config.EnableShadowPriceAwareRequestScore.Should().BeTrue();
    }

    [Fact]
    public void StandardExecutorConfigShouldSupportWithExpression() {
        ColumnGenerationStandardExecutorConfig config = ColumnGenerationStandardExecutorConfig.Default with {
            RmpSolveNamePrefix = "custom-rmp",
            RmpToLogModel = true
        };
        config.RmpSolveNamePrefix.Should().Be("custom-rmp");
        config.RmpToLogModel.Should().BeTrue();
    }

    [Fact]
    public void PrototypesFromItemsShouldReturnEmptyForEmptyList() {
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> result = ContinuousRadiusSolverHelpers.PrototypesFromItems(Array.Empty<Item>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractResultsShouldExtractMatchingKeys() {
        var info = new Dictionary<string, string> {
            ["continuous_radius_solver_selected_var1"] = "3.14",
            ["continuous_radius_solver_selected_var2"] = "2.71",
            ["other_key"] = "99.0",
            ["continuous_radius_solver_selected_bad"] = "not_a_number"
        };
        IReadOnlyDictionary<string, FltX> result = ContinuousRadiusSolverHelpers.ExtractResults(info);
        result.Should().HaveCount(2);
        result["var1"].ToFlt64().ToDouble().Should().BeApproximately(3.14, 1e-9);
        result["var2"].ToFlt64().ToDouble().Should().BeApproximately(2.71, 1e-9);
    }

    [Fact]
    public void ExtractResultsShouldReturnEmptyForNoMatchingKeys() {
        var info = new Dictionary<string, string> { ["foo"] = "bar" };
        IReadOnlyDictionary<string, FltX> result = ContinuousRadiusSolverHelpers.ExtractResults(info);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ToLayerPlacementShouldReturnPlacement() {
        var shape = new Container3Shape<FltX>(M(3), M(3), M(3));
        var layer = new BinLayer { Shape = shape };
        Result<QuantityPlacement3<BinLayer, FltX>, ErrorCode, Error<ErrorCode>> result = layer.ToLayerPlacement();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Ok<
            QuantityPlacement3<BinLayer, FltX>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
    }

    [Fact]
    public void ToKnownCoordinateLayerPlacementShouldReturnPlacement() {
        var shape = new Container3Shape<FltX>(M(3), M(3), M(3));
        var layer = new BinLayer { Shape = shape };
        var z = new Quantity<FltX>(new FltX(5), SIBaseUnits.Meter);
        QuantityPlacement3<BinLayer, FltX> placement = layer.ToKnownCoordinateLayerPlacement(z);
        placement.Position.Z.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-9);
        placement.Unit.Should().NotBeSameAs(layer);
    }

    [Fact]
    public void Bpp3dErrorsShouldHaveAllErrorCodes() {
        Type errors = typeof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors);
        errors.GetField(nameof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors.NullShadowPriceKey)).Should().NotBeNull();
        errors.GetField(nameof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors.ShadowPriceNotFound)).Should().NotBeNull();
        errors.GetField(nameof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors.NonFiniteSolverValue)).Should().NotBeNull();
        errors.GetField(nameof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors.NullLayerColumn)).Should().NotBeNull();
        errors.GetField(nameof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors.NullLoadColumn)).Should().NotBeNull();
        errors.GetField(nameof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors.NullAssignmentColumn)).Should().NotBeNull();
        errors.GetField(nameof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors.NullBlock)).Should().NotBeNull();
        errors.GetField(nameof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors.NullMaterial)).Should().NotBeNull();
        errors.GetField(nameof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors.NullItem)).Should().NotBeNull();
        errors.GetField(nameof(Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error.Bpp3dErrors.NullBinType)).Should().NotBeNull();
    }
}
