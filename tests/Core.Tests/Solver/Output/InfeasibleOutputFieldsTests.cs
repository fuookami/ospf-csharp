#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Output;

public class InfeasibleOutputFieldsTests {
    [Fact]
    public void InfeasibleUnifiedFields_CanBeCreated() {
        var fields = new InfeasibleUnifiedFields(
            Iterations: 100,
            NodeCount: 500,
            BestBound: new Flt64(42.0),
            MipGap: new Flt64(0.01),
            SolveTime: TimeSpan.FromSeconds(5));

        fields.Iterations.Should().Be(100);
        fields.NodeCount.Should().Be(500);
        fields.BestBound.Should().NotBeNull();
        fields.BestBound!.Value.ToDouble().Should().Be(42.0);
        fields.MipGap.Should().NotBeNull();
        fields.MipGap!.Value.ToDouble().Should().Be(0.01);
        fields.SolveTime.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void InfeasibleUnifiedFields_NullOptionalFields() {
        var fields = new InfeasibleUnifiedFields(
            Iterations: null,
            NodeCount: null,
            BestBound: null,
            MipGap: null,
            SolveTime: TimeSpan.Zero);

        fields.Iterations.Should().BeNull();
        fields.NodeCount.Should().BeNull();
        fields.BestBound.Should().BeNull();
        fields.MipGap.Should().BeNull();
    }

    [Fact]
    public void Resolve_WithNullStatus_UsesFallbackSolveTime() {
        var fallbackTime = TimeSpan.FromSeconds(10);

        InfeasibleUnifiedFields result = InfeasibleOutputResolver.Resolve(null, fallbackTime);

        result.Iterations.Should().BeNull();
        result.NodeCount.Should().BeNull();
        result.BestBound.Should().BeNull();
        result.MipGap.Should().BeNull();
        result.SolveTime.Should().Be(fallbackTime);
    }

    [Fact]
    public void Resolve_WithStatus_ExtractsFields() {
        var status = new SolvingStatus(
            Solver: "test",
            SolverIndex: 0,
            SolverConfig: null!,
            Iterations: 200,
            NodeCount: 1000,
            BestBound: new Flt64(99.5),
            MipGap: new Flt64(0.05),
            SolveTime: TimeSpan.FromSeconds(15));

        InfeasibleUnifiedFields result = InfeasibleOutputResolver.Resolve(
            status, TimeSpan.FromSeconds(1));

        result.Iterations.Should().Be(200);
        result.NodeCount.Should().Be(1000);
        result.BestBound.Should().NotBeNull();
        result.BestBound!.Value.ToDouble().Should().Be(99.5);
        result.MipGap.Should().NotBeNull();
        result.MipGap!.Value.ToDouble().Should().Be(0.05);
        result.SolveTime.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Resolve_WithPartialStatus_UsesAvailableFields() {
        var status = new SolvingStatus(
            Solver: "test",
            SolverIndex: 0,
            SolverConfig: null!,
            Iterations: 50,
            SolveTime: TimeSpan.FromSeconds(3));

        InfeasibleUnifiedFields result = InfeasibleOutputResolver.Resolve(
            status, TimeSpan.FromSeconds(99));

        result.Iterations.Should().Be(50);
        result.NodeCount.Should().BeNull();
        result.BestBound.Should().BeNull();
        result.SolveTime.Should().Be(TimeSpan.FromSeconds(3));
    }
}
