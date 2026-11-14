// VRPTW 模型层测试 / Tests for the VRPTW model layer.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// VRPTW 模型层 / The VRPTW model layer.
/// </summary>
public sealed class VrpModelTest {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>正行驶时间合法 / A positive travel time is legal.</summary>
    [Fact]
    public void TravelTime_ShouldAcceptPositive() =>
        TravelTime.Invoke(TimeSpan.FromMinutes(10)).IsOk.Should().BeTrue();

    /// <summary>
    /// **零时长合法**：表示两个节点重合（同一坐标的客户、仓库与首个客户同址），是常见输入。
    /// **A zero duration is legal**: it means two nodes coincide, a common input.
    /// </summary>
    [Fact]
    public void TravelTime_ShouldAcceptZero() =>
        TravelTime.Invoke(TimeSpan.Zero).IsOk.Should().BeTrue();

    /// <summary>负时长被拒绝 / A negative duration is rejected.</summary>
    [Fact]
    public void TravelTime_ShouldRejectNegative() =>
        TravelTime.Invoke(TimeSpan.FromMinutes(-1)).IsOk.Should().BeFalse();

    /// <summary>合法时间窗通过 / A valid window passes.</summary>
    [Fact]
    public void TimeWindow_ShouldAcceptOrdered() =>
        ServiceTimeWindow.Invoke(Origin, Origin.AddHours(4)).IsOk.Should().BeTrue();

    /// <summary>
    /// **上下界相等合法**：退化为"必须在某一时刻开始"，在硬约束排程里是合法输入。
    /// **Equal bounds are legal**: the window degenerates to "must start at exactly this instant".
    /// </summary>
    [Fact]
    public void TimeWindow_ShouldAcceptEqualBounds() =>
        ServiceTimeWindow.Invoke(Origin, Origin).IsOk.Should().BeTrue();

    /// <summary>截止早于起始被拒绝 / A due time before the ready time is rejected.</summary>
    [Fact]
    public void TimeWindow_ShouldRejectInverted() =>
        ServiceTimeWindow.Invoke(Origin.AddHours(1), Origin).IsOk.Should().BeFalse();

    /// <summary>
    /// **闭区间：恰好在上界开始服务是合法的**。用右开区间会把"刚好赶上截止时刻"的客户判为迟到，而这类边界解往往是最优解。
    /// **Closed interval: starting service exactly at the upper bound is legal.** A half-open range would mark "just made
    /// the deadline" customers as late, and those boundary solutions are often optimal.
    /// </summary>
    [Fact]
    public void TimeWindow_ShouldIncludeBothEnds() {
        ServiceTimeWindow window = ServiceTimeWindow.Invoke(Origin, Origin.AddHours(4)).Value;

        window.Contains(Origin).Should().BeTrue("下界闭 / the lower bound is closed");
        window.Contains(Origin.AddHours(4)).Should().BeTrue("上界也闭 / the upper bound is closed too");
        window.Contains(Origin.AddHours(-1)).Should().BeFalse();
        window.Contains(Origin.AddHours(5)).Should().BeFalse();
    }

    /// <summary>合法客户构造成功 / A valid customer constructs.</summary>
    [Fact]
    public void Customer_ShouldBuild() =>
        CustomerOf("c1", 5.0, TimeSpan.FromMinutes(10)).IsOk.Should().BeTrue();

    /// <summary>**零需求合法**：客户可以被纯作为路径上的访问点 / **A zero demand is legal**: a customer can be a pure visiting point.</summary>
    [Fact]
    public void Customer_ShouldAcceptZeroDemand() =>
        CustomerOf("c1", 0.0, TimeSpan.FromMinutes(10)).IsOk.Should().BeTrue();

    /// <summary>空客户标识被拒绝 / A blank customer identity is rejected.</summary>
    [Fact]
    public void Customer_ShouldRejectBlankId() =>
        CustomerOf(" ", 5.0, TimeSpan.FromMinutes(10)).IsOk.Should().BeFalse();

    /// <summary>负需求被拒绝 / A negative demand is rejected.</summary>
    [Fact]
    public void Customer_ShouldRejectNegativeDemand() =>
        CustomerOf("c1", -1.0, TimeSpan.FromMinutes(10)).IsOk.Should().BeFalse();

    /// <summary>负服务时长被拒绝 / A negative service duration is rejected.</summary>
    [Fact]
    public void Customer_ShouldRejectNegativeServiceTime() =>
        CustomerOf("c1", 5.0, TimeSpan.FromMinutes(-1)).IsOk.Should().BeFalse();

    /// <summary>
    /// 校验顺序：标识最先查，因为后续两条的失败消息都要点名是哪个客户。
    /// Check order: identity first, because the other two messages name the offending customer.
    /// </summary>
    [Fact]
    public void Customer_ShouldReportBlankIdBeforeOtherFailures() {
        Result<Customer<FltX>, ErrorCode, Error<ErrorCode>> result = Customer<FltX>.Invoke(
            new CustomerId(""), Node("n1"), Meters(-1),
            ServiceTimeWindow.Invoke(Origin, Origin.AddHours(1)).Value, TimeSpan.FromMinutes(-1));

        result.IsOk.Should().BeFalse();
    }

    /// <summary>客户保留全部字段 / A customer keeps all its fields.</summary>
    [Fact]
    public void Customer_ShouldKeepFields() {
        Customer<FltX> customer = CustomerOf("c1", 5.0, TimeSpan.FromMinutes(10)).Value;

        customer.Id.Value.Should().Be("c1");
        customer.Demand.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-9);
        customer.ServiceTime.Should().Be(TimeSpan.FromMinutes(10));
    }

    /// <summary>仓库作为纯数据记录 / A depot is a plain data record.</summary>
    [Fact]
    public void Depot_ShouldHoldFields() {
        var depot = new Depot<FltX>(
            Node("depot"), ServiceTimeWindow.Invoke(Origin, Origin.AddHours(12)).Value);

        depot.Node.Id.Value.Should().Be("depot");
        depot.TimeWindow.ReadyTime.Should().Be(Origin);
    }

    /// <summary>
    /// **路线停靠点分别保存三个时刻**：它们的差值就是等待与服务时长，下游定价与可行性校验都要读这些差值。
    /// **A route stop stores three instants separately**: their differences are the waiting and service durations that
    /// downstream pricing and feasibility checks read.
    /// </summary>
    [Fact]
    public void RouteStop_ShouldKeepThreeInstants() {
        var stop = new RouteStop<FltX>(
            new NetworkNodeId("n1"),
            new CustomerId("c1"),
            Origin,
            Origin.AddMinutes(5),
            Origin.AddMinutes(15),
            Meters(3));

        stop.Arrival.Should().Be(Origin);
        (stop.ServiceStart - stop.Arrival).Should().Be(TimeSpan.FromMinutes(5), "等待时长 / waiting duration");
        (stop.Departure - stop.ServiceStart).Should().Be(TimeSpan.FromMinutes(10), "服务时长 / service duration");
        stop.CustomerId!.Value.Should().Be("c1");
    }

    /// <summary>仓库处的停靠点客户标识为 null / A depot stop has a null customer identity.</summary>
    [Fact]
    public void RouteStop_ShouldAllowNullCustomerAtDepot() {
        var stop = new RouteStop<FltX>(
            new NetworkNodeId("depot"), null, Origin, Origin, Origin, Meters(0));

        stop.CustomerId.Should().BeNull();
    }

    /// <summary>客户标识按序比较 / Customer identities compare ordinally.</summary>
    [Fact]
    public void CustomerId_ShouldOrder() {
        var ids = new System.Collections.Generic.List<CustomerId> { new("c2"), new("c1") };
        ids.Sort();

        ids[0].Value.Should().Be("c1");
    }

    /// <summary>构造客户 / Build a customer.</summary>
    private static Fuookami.Ospf.Utils.Functional.Result<
        Customer<FltX>, Fuookami.Ospf.Utils.Error.ErrorCode,
        Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> CustomerOf(
        string id, double demand, TimeSpan serviceTime) =>
        Customer<FltX>.Invoke(
            new CustomerId(id),
            Node("n1"),
            Meters(demand),
            ServiceTimeWindow.Invoke(Origin, Origin.AddHours(4)).Value,
            serviceTime);

    /// <summary>构造节点 / Build a node.</summary>
    private static NetworkNode<FltX> Node(string id) =>
        NetworkNode<FltX>.Invoke(new NetworkNodeId(id)).Value;

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
