// 分支定价算法测试 / Tests for the branch-and-price algorithm.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 分支定价算法 / The branch-and-price algorithm.
/// </summary>
public sealed class BranchAndPriceAlgorithmTest {
    /// <summary>**互补决策成对闭合**：禁用 ↔ 要求 / Complementary decisions close in pairs.</summary>
    [Fact]
    public void Complementary_ShouldFlipForbidAndRequireVehicleType() {
        var forbid = new ForbidVehicleType(new VehicleTypeId("truck"), new CustomerId("c1"), new NetworkNodeId("n1"));

        BranchAndPriceAlgorithm<Flt64>.ComplementaryDecision(forbid).Should().Be(
            new RequireVehicleType(new VehicleTypeId("truck"), new CustomerId("c1"), new NetworkNodeId("n1")));
    }

    /// <summary>要求车型的互补是禁用车型 / The complement of requiring a vehicle type is forbidding it.</summary>
    [Fact]
    public void Complementary_ShouldFlipRequireVehicleType() {
        var require = new RequireVehicleType(new VehicleTypeId("truck"), new CustomerId("c1"), new NetworkNodeId("n1"));

        BranchAndPriceAlgorithm<Flt64>.ComplementaryDecision(require).Should().Be(
            new ForbidVehicleType(new VehicleTypeId("truck"), new CustomerId("c1"), new NetworkNodeId("n1")));
    }

    /// <summary>禁用弧的互补是要求该弧 / The complement of forbidding an arc is requiring it.</summary>
    [Fact]
    public void Complementary_ShouldFlipForbidArc() {
        var forbid = new ForbidArc(
            new VehicleTypeId("truck"), new NetworkNodeId("a"), new NetworkNodeId("b"), new NetworkArcId("arc:1"));

        BranchAndPriceAlgorithm<Flt64>.ComplementaryDecision(forbid).Should().Be(
            new RequireArc(
                new VehicleTypeId("truck"), new NetworkNodeId("a"), new NetworkNodeId("b"),
                new NetworkArcId("arc:1")));
    }

    /// <summary>**互补两次回到原决策** / Applying the complement twice returns the original decision.</summary>
    [Fact]
    public void Complementary_ShouldBeInvolution() {
        var forbid = new ForbidArc(
            new VehicleTypeId("truck"), new NetworkNodeId("a"), new NetworkNodeId("b"), new NetworkArcId("arc:1"));

        BranchAndPriceAlgorithm<Flt64>.ComplementaryDecision(
            BranchAndPriceAlgorithm<Flt64>.ComplementaryDecision(forbid)).Should().Be(forbid);
    }

    /// <summary>**下标区间越界时抛出而不是静默** / An unindexable decision kind throws rather than silently passing.</summary>
    [Fact]
    public void Complementary_ShouldRejectUnknownDecisionKind() =>
        ((Func<IBranchDecision>)(() =>
            BranchAndPriceAlgorithm<Flt64>.ComplementaryDecision(new UnknownDecision())))
            .Should().Throw<ArgumentOutOfRangeException>();

    /// <summary>**配置默认容差为 1e-4** / The default gap tolerance is 1e-4.</summary>
    [Fact]
    public void Configuration_ShouldDefaultGapTolerance() =>
        new BranchAndPriceAlgorithm<Flt64>.Configuration(TimeSpan.FromSeconds(1))
            .GapTolerance.ToDouble().Should().BeApproximately(1e-4, 1e-12);

    /// <summary>**显式容差覆盖默认值** / An explicit tolerance overrides the default.</summary>
    [Fact]
    public void Configuration_ShouldHonourExplicitGapTolerance() =>
        new BranchAndPriceAlgorithm<Flt64>.Configuration(
            TimeSpan.FromSeconds(1), RelativeGapTolerance: new Flt64(1e-9))
            .GapTolerance.ToDouble().Should().BeApproximately(1e-9, 1e-18);

    /// <summary>未实现的分支决策 / An unimplemented branch decision.</summary>
    private sealed record UnknownDecision : IBranchDecision;
}
