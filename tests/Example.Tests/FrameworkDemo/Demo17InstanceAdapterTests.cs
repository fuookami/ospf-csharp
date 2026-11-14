// Demo17 → VRPTW 适配测试 / Tests for the Demo17 instance adapter.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.CoreDemo.Demo17;
using Fuookami.Ospf.Example.FrameworkDemo.Demo5;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Demo17 → VRPTW 适配 / The Demo17 VRPTW adapter.
/// </summary>
public sealed class Demo17InstanceAdapterTests {
    /// <summary>**前缀客户集大小正确且客户按原始顺序** / The prefix has the right size and keeps the source order.</summary>
    [Fact]
    public void Create_ShouldTakeAPrefixOfTheDemandNodes() {
        Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> created =
            Demo17InstanceAdapter.Create(3, Application.DefaultSchedulingWindow());

        created.IsOk.Should().BeTrue(created.ToString());
        VrptwInstance<Flt64> instance = created.Value;
        instance.Name.Should().Be("demo17-3");
        instance.Customers.Should().HaveCount(3);
        instance.Customers.Select(customer => customer.Id.Value)
            .Should().Equal("demo17-customer-1", "demo17-customer-2", "demo17-customer-3");
        // 坐标必须来自源节点的前三个客户，而不是随便三个。
        // The coordinates must come from the first three source customers, not from any three.
        instance.Customers[0].Node.Attributes["x"].Value.ToDouble().Should()
            .Be(VrptwDemo.CustomerNodes[0].PosX.ToDouble());
    }

    /// <summary>**两条仓库的节点 id 不同** / The two depots have different node ids.</summary>
    /// <remarks>
    /// 共用同一个节点会把"回到仓库"与"离开仓库"压成同一条弧。
    ///
    /// Sharing one node collapses "return to depot" and "leave depot" into the same arc.
    /// </remarks>
    [Fact]
    public void Create_ShouldGiveTheTwoDepotsDistinctNodes() {
        Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> created =
            Demo17InstanceAdapter.Create(1, Application.DefaultSchedulingWindow());

        created.IsOk.Should().BeTrue(created.ToString());
        created.Value.StartDepot.Node.Id.Value.Should().Be("demo17-start");
        created.Value.EndDepot.Node.Id.Value.Should().Be("demo17-end");
        created.Value.StartDepot.Node.Id.Should().NotBe(created.Value.EndDepot.Node.Id);
    }

    /// <summary>**同规格车辆归组成一个车辆类型** / Identically specified vehicles collapse into one vehicle type.</summary>
    /// <remarks>
    /// 不归组会让每种车各占一个类型，而**车辆类型数量会直接乘进分支维度**。
    ///
    /// Not grouping gives each vehicle its own type, and **the vehicle-type count multiplies straight into the branching dimension**.
    /// </remarks>
    [Fact]
    public void Create_ShouldGroupIdenticalVehicles() {
        Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> created =
            Demo17InstanceAdapter.Create(1, Application.DefaultSchedulingWindow());

        created.IsOk.Should().BeTrue(created.ToString());
        created.Value.VehicleTypes.Should().ContainSingle();
        created.Value.VehicleTypes[0].Amount.Should().Be(VrptwDemo.VehicleList.Count);
        created.Value.VehicleTypes[0].Capacity.Value.ToDouble().Should()
            .Be(VrptwDemo.VehicleList[0].Capacity);
        created.Value.VehicleTypes[0].FixedCost.Value.ToDouble().Should()
            .Be(VrptwDemo.VehicleList[0].FixedCost.ToDouble());
    }

    /// <summary>**越界客户数量被拒，而不是截断** / An out-of-range count is rejected instead of truncated.</summary>
    /// <remarks>
    /// 截断会让"要 100 个客户"悄悄变成"5 个客户"，**而实例名仍然写着 `demo17-100`**。
    ///
    /// Truncation turns "give me 100 customers" into "5 customers" silently, **while the instance name still says `demo17-100`**.
    /// </remarks>
    [Fact]
    public void Create_ShouldRejectOutOfRangeCustomerCounts() {
        int available = VrptwDemo.CustomerNodes.Count;

        Demo17InstanceAdapter.Create(0, Application.DefaultSchedulingWindow())
            .Should().BeOfType<Failed<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>>>();
        Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> tooMany =
            Demo17InstanceAdapter.Create(available + 1, Application.DefaultSchedulingWindow());
        tooMany.Should().BeOfType<Failed<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>>)tooMany).Error.Message
            .Should().Contain((available + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "错误必须指名请求的数量 / the error must name the requested count");
    }

    /// <summary>**时间窗按业务时间轴映射成绝对时刻** / The time windows map onto the business timeline.</summary>
    [Fact]
    public void Create_ShouldMapTimeWindowsOntoTheBusinessTimeline() {
        TimeWindow<Flt64> window = Application.DefaultSchedulingWindow();

        Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> created =
            Demo17InstanceAdapter.Create(1, window);

        created.IsOk.Should().BeTrue(created.ToString());
        Customer<Flt64> customer = created.Value.Customers[0];
        customer.TimeWindow.ReadyTime.Should().Be(
            window.InstantOf(VrptwDemo.CustomerNodes[0].TimeWindowStart));
        customer.TimeWindow.DueTime.Should().Be(
            window.InstantOf(VrptwDemo.CustomerNodes[0].TimeWindowEnd));
        customer.TimeWindow.ReadyTime.Should().BeBefore(customer.TimeWindow.DueTime);
    }

    /// <summary>**每个需求都被搬到客户上** / Every demand is carried onto the customer.</summary>
    [Fact]
    public void Create_ShouldCarryEveryDemand() {
        Result<VrptwInstance<Flt64>, ErrorCode, Error<ErrorCode>> created =
            Demo17InstanceAdapter.Create(
                VrptwDemo.CustomerNodes.Count,
                Application.DefaultSchedulingWindow());

        created.IsOk.Should().BeTrue(created.ToString());
        IReadOnlyList<Customer<Flt64>> customers = created.Value.Customers;
        customers.Should().HaveCount(VrptwDemo.CustomerNodes.Count);
        for (int index = 0; index < customers.Count; ++index) {
            customers[index].Demand.Value.ToDouble().Should()
                .Be(VrptwDemo.CustomerNodes[index].Demand);
            customers[index].Demand.Unit.Should().Be(SIBaseUnits.Kilogram);
        }
    }
}
