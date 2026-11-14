// 分支节点求解器测试 / Tests for the branch-node solver.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 分支节点求解器 / The branch-node solver.
/// </summary>
public sealed class BranchNodeSolverTest {
    private static readonly NetworkNodeId StartDepot = new("depot");
    private static readonly NetworkNodeId EndDepot = new("depot_end");
    private static readonly NetworkNodeId Customer = new("c1");
    private static readonly VehicleTypeId Truck = new("truck");

    /// <summary>**遮罩兼容的列被保留** / A mask-compatible column is kept.</summary>
    [Fact]
    public void Filter_ShouldKeepCompatibleColumn() {
        BranchMask<VehicleTypeId> mask = Mask();

        BranchNodeSolver<Flt64>.FilterRouteColumnsForBranch(mask, new[] { Route(Truck, Customer) })
            .Should().ContainSingle();
    }

    /// <summary>**被禁节点的列被过滤** / A column over a forbidden node is filtered out.</summary>
    [Fact]
    public void Filter_ShouldDropForbiddenNode() {
        BranchMask<VehicleTypeId> mask = Mask(forbiddenNodes: new HashSet<NetworkNodeId> { Customer });

        BranchNodeSolver<Flt64>.FilterRouteColumnsForBranch(mask, new[] { Route(Truck, Customer) })
            .Should().BeEmpty();
    }

    /// <summary>**被禁弧的列被过滤** / A column using a forbidden arc is filtered out.</summary>
    [Fact]
    public void Filter_ShouldDropForbiddenArc() {
        var forbidden = new HashSet<ResourceArc<VehicleTypeId>> {
            new(Truck, StartDepot, Customer, new NetworkArcId("arc:a")),
        };
        BranchMask<VehicleTypeId> mask = Mask(forbiddenArcs: forbidden);

        BranchNodeSolver<Flt64>.FilterRouteColumnsForBranch(mask, new[] { Route(Truck, Customer) })
            .Should().BeEmpty();
    }

    /// <summary>**资源图里不存在的弧也过滤掉** / An arc absent from the resource graph is dropped too.</summary>
    [Fact]
    public void Filter_ShouldDropArcMissingFromResourceGraph() {
        ResourceGraph<VehicleTypeId> graph = ResourceGraph<VehicleTypeId>.Invoke(
            new[] { StartDepot, Customer, EndDepot },
            new[] {
                new ResourceArc<VehicleTypeId>(Truck, StartDepot, Customer, new NetworkArcId("arc:a")),
            }).Value;

        // 第二段弧不在资源图里。 / The second arc is absent from the resource graph.
        BranchNodeSolver<Flt64>.FilterRouteColumnsForBranch(
                Mask(), new[] { Route(Truck, Customer) }, graph)
            .Should().BeEmpty();
    }

    /// <summary>**两端弧都在资源图里时保留** / Kept when both arcs exist in the resource graph.</summary>
    [Fact]
    public void Filter_ShouldKeepWhenResourceGraphCoversAllArcs() {
        ResourceGraph<VehicleTypeId> graph = ResourceGraph<VehicleTypeId>.Invoke(
            new[] { StartDepot, Customer, EndDepot },
            new[] {
                new ResourceArc<VehicleTypeId>(Truck, StartDepot, Customer, new NetworkArcId("arc:a")),
                new ResourceArc<VehicleTypeId>(Truck, Customer, EndDepot, new NetworkArcId("arc:b")),
            }).Value;

        BranchNodeSolver<Flt64>.FilterRouteColumnsForBranch(
                Mask(), new[] { Route(Truck, Customer) }, graph)
            .Should().ContainSingle();
    }

    /// <summary>**没有遮罩检查时不能只信资源图**：两条弧都在图里但节点被禁 / Both arcs exist but the node is forbidden.</summary>
    [Fact]
    public void Filter_ShouldPreferMaskOverResourceGraph() {
        ResourceGraph<VehicleTypeId> graph = ResourceGraph<VehicleTypeId>.Invoke(
            new[] { StartDepot, Customer, EndDepot },
            new[] {
                new ResourceArc<VehicleTypeId>(Truck, StartDepot, Customer, new NetworkArcId("arc:a")),
                new ResourceArc<VehicleTypeId>(Truck, Customer, EndDepot, new NetworkArcId("arc:b")),
            }).Value;

        BranchNodeSolver<Flt64>.FilterRouteColumnsForBranch(
                Mask(forbiddenNodes: new HashSet<NetworkNodeId> { Customer }),
                new[] { Route(Truck, Customer) },
                graph)
            .Should().BeEmpty();
    }

    /// <summary>**构造要求实例与服务均非空** / Construction requires a non-null instance and solver.</summary>
    [Fact]
    public void Construct_ShouldRejectNulls() =>
        ((System.Func<BranchNodeSolver<Flt64>>)(() => new BranchNodeSolver<Flt64>(
            null!, null!, null!, null!, null!, null!, null!)))
            .Should().Throw<System.ArgumentNullException>();

    /// <summary>构造遮罩 / Build a branch mask.</summary>
    private static BranchMask<VehicleTypeId> Mask(
        IReadOnlySet<NetworkNodeId>? forbiddenNodes = null,
        IReadOnlySet<ResourceArc<VehicleTypeId>>? forbiddenArcs = null) =>
        BranchMask<VehicleTypeId>.Invoke(
            StartDepot,
            EndDepot,
            forbiddenNodes is null
                ? null
                : new Dictionary<VehicleTypeId, IReadOnlySet<NetworkNodeId>> {
                    [Truck] = forbiddenNodes,
                },
            null,
            forbiddenArcs).Value;

    /// <summary>构造一条含单个客户的路线 / Build a single-customer route.</summary>
    private static Route<Flt64> Route(VehicleTypeId vehicleTypeId, NetworkNodeId customer) {
        var origin = new System.DateTimeOffset(2024, 1, 1, 0, 0, 0, System.TimeSpan.Zero);
        var stops = new List<RouteStop<Flt64>> {
            new(StartDepot, null, origin, origin, origin, Meters(0.0)),
            new(customer, new CustomerId(customer.Value), origin, origin, origin, Meters(1.0)),
            new(EndDepot, null, origin, origin, origin, Meters(1.0)),
        };
        return Route<Flt64>.Invoke(
            vehicleTypeId,
            stops,
            Meters(0.0),
            Meters(10.0),
            new[] { new NetworkArcId("arc:a"), new NetworkArcId("arc:b") }).Value;
    }

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Fuookami.Ospf.Quantities.Quantity.Quantity<Flt64> Meters(double value) =>
        new(new Flt64(value), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);
}
