// 运输分配上下文测试 / Tests for the transport-allocation context.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Allocation;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 运输分配上下文 / The transport-allocation context.
/// </summary>
public sealed class AllocationContextTest {
    private static readonly NetworkNodeId Origin = new("plant");
    private static readonly NetworkNodeId Destination = new("store");
    private static readonly NetworkArcId Service = new("arc:plant-store");

    /// <summary>**主路径：单地点对、单服务、单请求可构造** / The main path constructs.</summary>
    [Fact]
    public void Create_ShouldSucceedOnMainPath() =>
        AllocationContext<Flt64>.Create(Problem(), Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeTrue();

    /// <summary>上下文持有业务问题与底层建设上下文 / The context holds the problem and the design context.</summary>
    [Fact]
    public void Create_ShouldExposeProblemAndDesign() {
        AllocationContext<Flt64> context =
            AllocationContext<Flt64>.Create(Problem(), Flt64NetworkSchedulingSolverValueAdapter.Instance).Value;

        context.Problem.Should().NotBeNull();
        context.Design.Should().NotBeNull();
        context.Name.Should().Be("network_allocation");
    }

    /// <summary>**空白模型名被拒** / A blank model name is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectBlankName() =>
        AllocationContext<Flt64>.Create(
            Problem(), Flt64NetworkSchedulingSolverValueAdapter.Instance, "  ").IsOk.Should().BeFalse();

    /// <summary>**重复地点被拒** / Duplicate locations are rejected.</summary>
    [Fact]
    public void Create_ShouldRejectDuplicateLocations() {
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(Origin),
                new AllocationLocation<Flt64>(Origin),
            },
            new[] { ServiceOf() },
            new[] { RequestOf() },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

        AllocationContext<Flt64>.Create(problem, Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeFalse();
    }

    /// <summary>**服务引用未知地点被拒** / A service referencing an unknown location is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectUnknownServiceEndpoint() {
        var problem = new AllocationProblem<Flt64>(
            new[] { new AllocationLocation<Flt64>(Origin) },
            new[] {
                new AllocationService<Flt64>(
                    Service, Origin, new NetworkNodeId("ghost"), Meters(1.0), Bounds()),
            },
            new[] { RequestOf() },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

        AllocationContext<Flt64>.Create(problem, Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeFalse();
    }

    /// <summary>**服务方向违反层级顺序被拒** / A service going upstream is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectUpstreamService() {
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(Origin, layer: 2),
                new AllocationLocation<Flt64>(Destination, layer: 1),
            },
            new[] {
                new AllocationService<Flt64>(Service, Origin, Destination, Meters(1.0), Bounds()),
            },
            new[] { RequestOf() },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

        AllocationContext<Flt64>.Create(problem, Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeFalse();
    }

    /// <summary>**层级相同或递增方向的服务合法** / Equal or increasing layers are legitimate.</summary>
    [Fact]
    public void Create_ShouldAcceptIncreasingLayers() {
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(Origin, layer: 1),
                new AllocationLocation<Flt64>(Destination, layer: 3),
            },
            new[] {
                new AllocationService<Flt64>(Service, Origin, Destination, Meters(1.0), Bounds()),
            },
            new[] { RequestOf() },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

        AllocationContext<Flt64>.Create(problem, Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeTrue("跨层直达合法 / cross-layer direct is legitimate");
    }

    /// <summary>**请求起止相同被拒** / A request with equal endpoints is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectSameEndpointRequest() {
        var request = new AllocationRequest<Flt64>(
            "req", Origin, Origin, Meters(5.0));
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(Origin),
                new AllocationLocation<Flt64>(Destination),
            },
            new[] { ServiceOf() },
            new[] { request },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

        AllocationContext<Flt64>.Create(problem, Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeFalse();
    }

    /// <summary>**请求数量非正被拒** / A non-positive request amount is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectNonPositiveQuantity() {
        var request = new AllocationRequest<Flt64>("req", Origin, Destination, Meters(0.0));
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(Origin),
                new AllocationLocation<Flt64>(Destination),
            },
            new[] { ServiceOf() },
            new[] { request },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

        AllocationContext<Flt64>.Create(problem, Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeFalse();
    }

    /// <summary>**整数步长非正被拒** / A non-positive integer step is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectNonPositiveIntegerStep() {
        var request = new AllocationRequest<Flt64>(
            "req", Origin, Destination, Meters(5.0), integerStep: Meters(0.0));
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(Origin),
                new AllocationLocation<Flt64>(Destination),
            },
            new[] { ServiceOf() },
            new[] { request },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

        AllocationContext<Flt64>.Create(problem, Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeFalse();
    }

    /// <summary>**枢纽引用未知配额被拒** / A hub referencing an unknown quota is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectUnknownHubQuota() {
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(Origin),
                new AllocationLocation<Flt64>(Destination),
            },
            new[] { ServiceOf() },
            new[] { RequestOf() },
            new Flt64(0.0),
            hubs: new[] {
                new AllocationHub<Flt64>(Origin, Meters(2.0), quotaIds: new HashSet<string> { "ghost" }),
            },
            costUnit: SIBaseUnits.Meter);

        AllocationContext<Flt64>.Create(problem, Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeFalse();
    }

    /// <summary>**服务容量消耗引用未知服务被拒** / Service-usage referencing an unknown service is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectUnknownUsageService() {
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(Origin),
                new AllocationLocation<Flt64>(Destination),
            },
            new[] { ServiceOf() },
            new[] { RequestOf() },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter,
            serviceUsage: new Dictionary<AllocationFlowKey, Quantity<Flt64>> {
                [new AllocationFlowKey("req", new NetworkArcId("arc:ghost"))] = Meters(1.0),
            });

        AllocationContext<Flt64>.Create(problem, Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeFalse();
    }

    /// <summary>**请求不允许的服务出现在消耗里被拒** / A usage on a disallowed service is rejected.</summary>
    [Fact]
    public void Create_ShouldRejectUsageOnDisallowedService() {
        var request = new AllocationRequest<Flt64>(
            "req", Origin, Destination, Meters(5.0),
            allowedServices: new HashSet<NetworkArcId> { new("arc:other") });
        var problem = new AllocationProblem<Flt64>(
            new[] {
                new AllocationLocation<Flt64>(Origin),
                new AllocationLocation<Flt64>(Destination),
            },
            new[] { ServiceOf() },
            new[] { request },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter,
            serviceUsage: new Dictionary<AllocationFlowKey, Quantity<Flt64>> {
                [new AllocationFlowKey("req", Service)] = Meters(1.0),
            });

        AllocationContext<Flt64>.Create(problem, Flt64NetworkSchedulingSolverValueAdapter.Instance)
            .IsOk.Should().BeFalse();
    }

    /// <summary>**注册在未求解模型上成功** / Registration succeeds on an unsolved model.</summary>
    [Fact]
    public void Register_ShouldSucceed() {
        AllocationContext<Flt64> context =
            AllocationContext<Flt64>.Create(Problem(), Flt64NetworkSchedulingSolverValueAdapter.Instance).Value;
        LinearMetaModel<Flt64> model = Model();

        context.Register(model).IsOk.Should().BeTrue();
    }

    /// <summary>**未求解时提取失败** / Extraction on an unsolved model fails.</summary>
    [Fact]
    public void ExtractSolution_ShouldFailWhenUnsolved() {
        AllocationContext<Flt64> context =
            AllocationContext<Flt64>.Create(Problem(), Flt64NetworkSchedulingSolverValueAdapter.Instance).Value;
        LinearMetaModel<Flt64> model = Model();
        context.Register(model);

        context.ExtractSolution(model).IsOk.Should().BeFalse();
    }

    /// <summary>构造问题 / Build the problem.</summary>
    private static AllocationProblem<Flt64> Problem() =>
        new(
            new[] {
                new AllocationLocation<Flt64>(Origin, layer: 1),
                new AllocationLocation<Flt64>(Destination, layer: 2),
            },
            new[] { ServiceOf() },
            new[] { RequestOf() },
            new Flt64(0.0),
            costUnit: SIBaseUnits.Meter);

    /// <summary>构造服务 / Build a service.</summary>
    private static AllocationService<Flt64> ServiceOf() =>
        new(Service, Origin, Destination, Meters(1.0), Bounds());

    /// <summary>构造请求 / Build a request.</summary>
    private static AllocationRequest<Flt64> RequestOf() =>
        new("req", Origin, Destination, Meters(5.0));

    /// <summary>构造容量上下界 / Build capacity bounds.</summary>
    private static CapacityBounds<Flt64> Bounds() =>
        new(Meters(0.0), Meters(10.0));

    /// <summary>构造模型 / Build a model.</summary>
    private static LinearMetaModel<Flt64> Model() => new("allocation");

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<Flt64> Meters(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
