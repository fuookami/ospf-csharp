// demo5 应用层测试 / Tests for the demo5 application layer.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo5;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// demo5 应用层 / The demo5 application layer.
/// </summary>
public sealed class Demo5ApplicationTests {
    /// <summary>**未知成本策略被拒，而不是静默退回 demo17** / An unknown cost policy is rejected instead of silently falling back to demo17.</summary>
    /// <remarks>
    /// 静默退回意味着**一次求解用的是完全另一套距离与成本语义，而结果看上去仍然"正常"**。
    ///
    /// A silent fallback means **a solve ran under an entirely different distance and cost semantics while the results still look "normal"**.
    /// </remarks>
    [Fact]
    public void BuildPolicies_ShouldRejectUnknownCostPolicy() {
        Result<BranchAndPriceAlgorithm<Flt64>.Policy, ErrorCode, Error<ErrorCode>> built =
            Application.BuildPolicies(new SemanticParameter(CostPolicy: "gurobi"));

        built.Should().BeOfType<
            Failed<BranchAndPriceAlgorithm<Flt64>.Policy, ErrorCode, Error<ErrorCode>>>();
        ((Failed<BranchAndPriceAlgorithm<Flt64>.Policy, ErrorCode, Error<ErrorCode>>)built)
            .Error.Message.Should().Contain("gurobi",
                "错误必须指名那个取值 / the error must name the offending value");
    }

    /// <summary>**两条已知策略各自装上对应的距离与成本实现** / The two known policies install their own distance and cost implementations.</summary>
    [Fact]
    public void BuildPolicies_ShouldInstallTheMatchingImplementations() {
        Result<BranchAndPriceAlgorithm<Flt64>.Policy, ErrorCode, Error<ErrorCode>> solomon =
            Application.BuildPolicies(new SemanticParameter(CostPolicy: "solomon"));
        Result<BranchAndPriceAlgorithm<Flt64>.Policy, ErrorCode, Error<ErrorCode>> demo17 =
            Application.BuildPolicies(new SemanticParameter(CostPolicy: "demo17"));

        solomon.IsOk.Should().BeTrue(solomon.ToString());
        demo17.IsOk.Should().BeTrue(demo17.ToString());
        solomon.Value.DistanceCalculator.Should().BeOfType<SolomonDistancePolicy<Flt64>>();
        solomon.Value.RouteCostPolicy.Should().BeOfType<FixedPlusArcCostPolicy<Flt64>>();
        demo17.Value.DistanceCalculator.Should().BeOfType<EuclideanDistanceCalculator<Flt64>>();
        demo17.Value.RouteCostPolicy.Should().BeOfType<Demo17CostPolicy<Flt64>>();
    }

    /// <summary>**路线装载取最后一个停靠点的累计值** / The route load comes from the last stop's accumulated value.</summary>
    /// <remarks>
    /// 取第一个会在"只有一个客户"的短线上给出 0，而**0 装载正是"这条路线什么也没送"的样子**。
    ///
    /// Taking the first would yield 0 on a one-customer route, and **a zero load is exactly what "this route delivered nothing" looks like**.
    /// </remarks>
    [Fact]
    public void ToOutput_ShouldReportTheLastAccumulatedLoad() {
        VrptwSolveResult<Flt64> result = Result(Route(
            Stop("customer-1", "1", 4.0),
            Stop("customer-2", "2", 9.0)));

        var output = Application.ToOutput(result);

        output.Routes.Should().ContainSingle();
        output.Routes[0].Load.Should().Be(9.0);
        output.Routes[0].Stops.Should().HaveCount(2);
        output.Routes[0].Stops[0].CustomerId.Should().Be("1");
        output.Routes[0].Stops[0].NodeId.Should().Be("customer-1");
    }

    /// <summary>**没有解时输出空路线而不是空引用** / Without a solution the output carries no routes instead of a null reference.</summary>
    [Fact]
    public void ToOutput_ShouldHandleNoSolution() {
        var trace = new BranchAndPriceTrace(
            NodesExplored: 3,
            NodesPruned: 1,
            GlobalLowerBound: new Flt64(1.0),
            GlobalUpperBound: new Flt64(2.0),
            RelativeGap: new Flt64(0.5),
            TotalTime: TimeSpan.FromMilliseconds(12),
            TotalIterations: 7);
        var result = new VrptwSolveResult<Flt64>(
            BranchAndPriceStatus.Infeasible,
            Solution: null,
            LowerBound: new Flt64(1.0),
            UpperBound: new Flt64(2.0),
            RelativeGap: new Flt64(0.5),
            Trace: trace);

        var output = Application.ToOutput(result);

        output.Routes.Should().BeEmpty();
        output.VehiclesUsed.Should().Be(0);
        output.TotalCost.Should().Be(0.0);
        output.Status.Should().Be(BranchAndPriceStatus.Infeasible);
        output.Trace.NodesExplored.Should().Be(3);
        output.Trace.TotalTimeMs.Should().Be(12);
        output.Trace.TotalIterations.Should().Be(7);
    }

    /// <summary>**默认业务时间轴起点固定** / The default business timeline starts at a fixed instant.</summary>
    /// <remarks>
    /// `DefaultSchedulingWindow` 与其它 Demo5 夹具必须共享同一对常量，否则 `InstantOf` 会给出相差几分钟的绝对时刻，
    /// **而两份值都能自洽地通过各自的校验**。
    ///
    /// `DefaultSchedulingWindow` must share one pair of constants with the other Demo5 fixtures; otherwise `InstantOf` returns instants minutes apart,
    /// **and both values pass their own validation coherently**.
    /// </remarks>
    [Fact]
    public void DefaultSchedulingWindow_ShouldStartAtTheSharedInstant() {
        TimeWindow<Flt64> window = Application.DefaultSchedulingWindow();

        window.Start.Should().Be(Application.WindowStart);
        window.Duration.Should().Be(TimeSpan.FromHours(1));
        window.Interval.Should().Be(TimeSpan.FromSeconds(1));
        window.InstantOf(new Flt64(10.0)).Should().Be(Application.WindowStart.AddSeconds(10));
    }

    private static VrptwSolveResult<Flt64> Result(Route<Flt64> route) {
        var solution = new VrptwSolution<Flt64>(
            Routes: new[] { route },
            TotalDistance: new Quantity<Flt64>(new Flt64(4.0), SIBaseUnits.Meter),
            TotalCost: new Quantity<Flt64>(new Flt64(7.0), NoneUnit.Instance));
        return new VrptwSolveResult<Flt64>(
            BranchAndPriceStatus.Optimal,
            solution,
            LowerBound: new Flt64(7.0),
            UpperBound: new Flt64(7.0),
            RelativeGap: Flt64.Zero,
            Trace: new BranchAndPriceTrace(
                NodesExplored: 1,
                NodesPruned: 0,
                GlobalLowerBound: new Flt64(7.0),
                GlobalUpperBound: new Flt64(7.0),
                RelativeGap: Flt64.Zero,
                TotalTime: TimeSpan.FromMilliseconds(5),
                TotalIterations: 2));
    }

    private static Route<Flt64> Route(params RouteStop<Flt64>[] stops) {
        Result<Route<Flt64>, ErrorCode, Error<ErrorCode>> created = Route<Flt64>.Invoke(
            vehicleTypeId: new VehicleTypeId("solomon-vehicle"),
            stops: stops,
            distance: new Quantity<Flt64>(new Flt64(4.0), SIBaseUnits.Meter),
            cost: new Quantity<Flt64>(new Flt64(7.0), NoneUnit.Instance));
        created.IsOk.Should().BeTrue(created.ToString());
        return created.Value;
    }

    private static RouteStop<Flt64> Stop(string nodeId, string customerId, double load) =>
        new(
            NodeId: new NetworkNodeId(nodeId),
            CustomerId: new CustomerId(customerId),
            Arrival: Application.WindowStart,
            ServiceStart: Application.WindowStart.AddSeconds(1),
            Departure: Application.WindowStart.AddSeconds(2),
            AccumulatedLoad: new Quantity<Flt64>(
                new Flt64(load), SIBaseUnits.Kilogram));
}
