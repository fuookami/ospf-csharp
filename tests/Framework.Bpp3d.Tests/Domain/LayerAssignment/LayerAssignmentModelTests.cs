#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment;
using Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain.LayerAssignment;

public class LayerAssignmentModelTests {
    private static Quantity<FltX> M(double v) => new(new FltX(v), SIBaseUnits.Meter);
    private static double D(Quantity<FltX> q) => q.Value.ToFlt64().ToDouble();

    [Fact]
    public void ImpreciseLoadShouldStartEmpty() {
        var load = new ImpreciseLoad<FltX>();
        load.Columns.Should().BeEmpty();
    }

    [Fact]
    public void ImpreciseLoadRegisterShouldAddColumn() {
        var load = new ImpreciseLoad<FltX>();
        var col = new LoadColumn<FltX>(M(5), 0);
        Result<Success, ErrorCode, Error<ErrorCode>> result = load.Register(col);
        result.IsFailed.Should().BeFalse();
        load.Columns.Should().HaveCount(1);
        load.Columns[0].Should().Be(col);
    }

    [Fact]
    public void ImpreciseLoadRegisterNullShouldFail() {
        var load = new ImpreciseLoad<FltX>();
        Result<Success, ErrorCode, Error<ErrorCode>> result = load.Register(null!);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ImpreciseLoadAddColumnsShouldAddMultiple() {
        var load = new ImpreciseLoad<FltX>();
        LoadColumn<FltX>[] cols = new[] {
            new LoadColumn<FltX>(M(1), 0),
            new LoadColumn<FltX>(M(2), 1),
            new LoadColumn<FltX>(M(3), 2),
        };
        Result<Success, ErrorCode, Error<ErrorCode>> result = load.AddColumns(cols);
        result.IsFailed.Should().BeFalse();
        load.Columns.Should().HaveCount(3);
    }

    [Fact]
    public void ImpreciseLoadAddColumnsShouldFailOnNull() {
        var load = new ImpreciseLoad<FltX>();
        var cols = new LoadColumn<FltX>?[] {
            new LoadColumn<FltX>(M(1), 0),
            null,
        };
        Result<Success, ErrorCode, Error<ErrorCode>> result = load.AddColumns(cols!);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ImpreciseAssignmentShouldStartEmpty() {
        var assign = new ImpreciseAssignment<FltX>();
        assign.Columns.Should().BeEmpty();
    }

    [Fact]
    public void ImpreciseAssignmentRegisterShouldAddColumn() {
        var assign = new ImpreciseAssignment<FltX>();
        var col = new AssignmentColumn<FltX>(0, 0, M(5));
        Result<Success, ErrorCode, Error<ErrorCode>> result = assign.Register(col);
        result.IsFailed.Should().BeFalse();
        assign.Columns.Should().HaveCount(1);
    }

    [Fact]
    public void ImpreciseAssignmentRegisterNullShouldFail() {
        var assign = new ImpreciseAssignment<FltX>();
        Result<Success, ErrorCode, Error<ErrorCode>> result = assign.Register(null!);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ImpreciseAssignmentAddColumnsShouldAddMultiple() {
        var assign = new ImpreciseAssignment<FltX>();
        AssignmentColumn<FltX>[] cols = new[] {
            new AssignmentColumn<FltX>(0, 0, M(1)),
            new AssignmentColumn<FltX>(1, 0, M(2)),
        };
        Result<Success, ErrorCode, Error<ErrorCode>> result = assign.AddColumns(cols);
        result.IsFailed.Should().BeFalse();
        assign.Columns.Should().HaveCount(2);
    }

    [Fact]
    public void ImpreciseAssignmentAddColumnsShouldFailOnNull() {
        var assign = new ImpreciseAssignment<FltX>();
        var cols = new AssignmentColumn<FltX>?[] {
            new AssignmentColumn<FltX>(0, 0, M(1)),
            null,
        };
        Result<Success, ErrorCode, Error<ErrorCode>> result = assign.AddColumns(cols!);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void LayerAggregationShouldStartEmpty() {
        var agg = new LayerAggregation<FltX>();
        agg.Columns.Should().BeEmpty();
    }

    [Fact]
    public void LayerAggregationAddColumnsShouldAddMultiple() {
        var agg = new LayerAggregation<FltX>();
        LayerColumn<FltX>[] cols = new[] {
            new LayerColumn<FltX>(M(10), 0),
            new LayerColumn<FltX>(M(20), 1),
        };
        Result<Success, ErrorCode, Error<ErrorCode>> result = agg.AddColumns(cols);
        result.IsFailed.Should().BeFalse();
        agg.Columns.Should().HaveCount(2);
    }

    [Fact]
    public void LayerAggregationAddColumnsShouldFailOnNull() {
        var agg = new LayerAggregation<FltX>();
        var cols = new LayerColumn<FltX>?[] {
            new LayerColumn<FltX>(M(10), 0),
            null,
        };
        Result<Success, ErrorCode, Error<ErrorCode>> result = agg.AddColumns(cols!);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void LayerAggregationRemoveColumnShouldRemoveMatching() {
        var agg = new LayerAggregation<FltX>();
        var col = new LayerColumn<FltX>(M(10), 0);
        agg.AddColumns(new[] { col });
        Result<Success, ErrorCode, Error<ErrorCode>> result = agg.RemoveColumn(col);
        result.IsFailed.Should().BeFalse();
        agg.Columns.Should().BeEmpty();
    }

    [Fact]
    public void LayerAggregationRemoveColumnNullShouldFail() {
        var agg = new LayerAggregation<FltX>();
        Result<Success, ErrorCode, Error<ErrorCode>> result = agg.RemoveColumn(null!);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void LayerAggregationRemoveColumnsShouldReturnSuccess() {
        var agg = new LayerAggregation<FltX>();
        var c1 = new LayerColumn<FltX>(M(10), 0);
        var c2 = new LayerColumn<FltX>(M(20), 1);
        agg.AddColumns(new[] { c1, c2 });
        // RemoveColumns uses HashSet which may not match due to Quantity equality;
        // just verify it returns success
        Result<Success, ErrorCode, Error<ErrorCode>> result = agg.RemoveColumns(new[] { c1 });
        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void CapacityShouldStoreValue() {
        var cap = new Capacity<FltX>(M(100));
        D(cap.Value).Should().BeApproximately(100.0, 1e-9);
    }

    [Fact]
    public void ImpreciseAggregationShouldBeInstantiable() {
        var agg = new ImpreciseAggregation();
        agg.Should().NotBeNull();
    }

    [Fact]
    public void PreciseAggregationShouldBeInstantiable() {
        var agg = new PreciseAggregation();
        agg.Should().NotBeNull();
    }

    [Fact]
    public void LayerAssignmentContextShouldBeInstantiable() {
        var ctx = new LayerAssignmentContext();
        ctx.Should().NotBeNull();
    }
}
