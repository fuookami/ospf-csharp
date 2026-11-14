// ESPPRC 标签、位集与支配策略测试 / Tests for ESPPRC labels, bitsets and dominance.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// ESPPRC 标签、位集与支配策略 / ESPPRC labels, bitsets and dominance.
/// </summary>
public sealed class EspprcLabelTest {
    /// <summary>空位集不含任何客户 / An empty bitset contains no customer.</summary>
    [Fact]
    public void Visited_ShouldStartEmpty() {
        var visited = VisitedCustomers.Empty(10);

        visited.IsEmpty().Should().BeTrue();
        visited.Contains(0).Should().BeFalse();
        visited.Size.Should().Be(10);
    }

    /// <summary>添加客户后可查到 / An added customer is found.</summary>
    [Fact]
    public void Visited_ShouldContainAdded() {
        VisitedCustomers visited = VisitedCustomers.Empty(10).Add(3);

        visited.Contains(3).Should().BeTrue();
        visited.Count().Should().Be(1);
    }

    /// <summary>
    /// **位集不可变**：添加返回新副本，原对象不受影响。可变位集会让一个标签的扩展悄悄改掉另一个标签。
    /// **The bitset is immutable**: adding returns a copy and leaves the original untouched.
    /// </summary>
    [Fact]
    public void Visited_ShouldBeImmutable() {
        var original = VisitedCustomers.Empty(10);
        VisitedCustomers extended = original.Add(5);

        original.Contains(5).Should().BeFalse("原对象未被修改 / the original is untouched");
        extended.Contains(5).Should().BeTrue();
    }

    /// <summary>重复添加幂等 / Adding twice is idempotent.</summary>
    [Fact]
    public void Visited_ShouldBeIdempotent() {
        VisitedCustomers once = VisitedCustomers.Empty(10).Add(2);
        VisitedCustomers twice = once.Add(2);

        twice.Count().Should().Be(1);
        twice.Should().BeSameAs(once, "已包含时直接返回自身 / an already-present customer returns the same instance");
    }

    /// <summary>跨字边界的位正确 / Bits across word boundaries are correct.</summary>
    [Fact]
    public void Visited_ShouldHandleWordBoundary() {
        VisitedCustomers visited = VisitedCustomers.Empty(200).Add(63).Add(64).Add(199);

        visited.Contains(63).Should().BeTrue();
        visited.Contains(64).Should().BeTrue();
        visited.Contains(199).Should().BeTrue();
        visited.Contains(62).Should().BeFalse();
        visited.Count().Should().Be(3);
    }

    /// <summary>访问全部客户后为满 / The bitset is full once every customer is visited.</summary>
    [Fact]
    public void Visited_ShouldReportFull() {
        var visited = VisitedCustomers.Empty(3);
        for (int index = 0; index < 3; index++) {
            visited = visited.Add(index);
        }

        visited.IsFull().Should().BeTrue();
    }

    /// <summary>从已访问集合构造不可达集合 / A forbidden set derives from a visited set.</summary>
    [Fact]
    public void Forbidden_ShouldDeriveFromVisited() {
        VisitedCustomers visited = VisitedCustomers.Empty(10).Add(1).Add(7);
        var forbidden = ForbiddenCustomers.FromVisited(visited, 10);

        forbidden.Contains(1).Should().BeTrue();
        forbidden.Contains(7).Should().BeTrue();
        forbidden.Contains(2).Should().BeFalse();
    }

    /// <summary>空集是任何集合的子集 / The empty set is a subset of any set.</summary>
    [Fact]
    public void Forbidden_EmptyIsSubsetOfAnything() {
        var empty = ForbiddenCustomers.Empty(10);
        ForbiddenCustomers some = ForbiddenCustomers.Empty(10).Add(4);

        empty.IsSubsetOf(some).Should().BeTrue();
        some.IsSubsetOf(empty).Should().BeFalse();
    }

    /// <summary>自反子集成立 / A set is a subset of itself.</summary>
    [Fact]
    public void Forbidden_ShouldBeSubsetOfItself() {
        ForbiddenCustomers set = ForbiddenCustomers.Empty(10).Add(4);

        set.IsSubsetOf(set).Should().BeTrue();
    }

    /// <summary>并集包含两者成员 / The union contains both sides.</summary>
    [Fact]
    public void Forbidden_UnionShouldMerge() {
        ForbiddenCustomers left = ForbiddenCustomers.Empty(10).Add(1);
        ForbiddenCustomers right = ForbiddenCustomers.Empty(10).Add(2);

        ForbiddenCustomers union = left.Union(right);

        union.Contains(1).Should().BeTrue();
        union.Contains(2).Should().BeTrue();
    }

    /// <summary>
    /// **并集不修改任一输入**：不可变性在支配判定里是正确性前提。
    /// **The union mutates neither input**: immutability is a correctness precondition for dominance.
    /// </summary>
    [Fact]
    public void Forbidden_UnionShouldNotMutateInputs() {
        ForbiddenCustomers left = ForbiddenCustomers.Empty(10).Add(1);
        ForbiddenCustomers right = ForbiddenCustomers.Empty(10).Add(2);

        left.Union(right);

        left.Contains(2).Should().BeFalse();
        right.Contains(1).Should().BeFalse();
    }

    /// <summary>
    /// **支配要求至少一个严格不等**：完全相同（各资源相等）的两个标签不算互相支配，否则两者都会被剪掉、路径凭空消失。
    /// **Dominance requires at least one strict inequality**: identical labels do not dominate each other, or both would be
    /// pruned and routes would vanish.
    /// </summary>
    [Fact]
    public void Dominance_ShouldRejectIdenticalLabels() {
        EspprcLabel<FltX> label = Label(1.0, 1.0, 1.0);

        DefaultLabelDominancePolicy.Instance.Dominates(label, label).Should().BeFalse();
    }

    /// <summary>更低的 reduced cost 支配 / A lower reduced cost dominates.</summary>
    [Fact]
    public void Dominance_ShouldAcceptLowerCost() =>
        DefaultLabelDominancePolicy.Instance
            .Dominates(Label(1.0, 5.0, 5.0), Label(2.0, 5.0, 5.0)).Should().BeTrue();

    /// <summary>更短的支配 / A shorter time dominates.</summary>
    [Fact]
    public void Dominance_ShouldAcceptShorterTime() =>
        DefaultLabelDominancePolicy.Instance
            .Dominates(Label(5.0, 1.0, 5.0), Label(5.0, 2.0, 5.0)).Should().BeTrue();

    /// <summary>更轻的负载支配 / A lighter load dominates.</summary>
    [Fact]
    public void Dominance_ShouldAcceptLighterLoad() =>
        DefaultLabelDominancePolicy.Instance
            .Dominates(Label(5.0, 5.0, 1.0), Label(5.0, 5.0, 2.0)).Should().BeTrue();

    /// <summary>任一资源更差则不支配 / A worse resource breaks dominance.</summary>
    [Fact]
    public void Dominance_ShouldRejectWorseResource() =>
        DefaultLabelDominancePolicy.Instance
            .Dominates(Label(1.0, 5.0, 9.0), Label(2.0, 5.0, 5.0)).Should().BeFalse();

    /// <summary>不同节点之间不比较 / Labels at different nodes are not compared.</summary>
    [Fact]
    public void Dominance_ShouldRejectDifferentNodes() =>
        DefaultLabelDominancePolicy.Instance
            .Dominates(Label(1.0, 1.0, 1.0, "a"), Label(2.0, 2.0, 2.0, "b")).Should().BeFalse();

    /// <summary>
    /// **已访问集合更小才可能支配**：访问了额外客户的标签扩展性更差。
    /// **A smaller visited set is required**: a label with extra customers is less extendable.
    /// </summary>
    [Fact]
    public void Dominance_ShouldRejectVisitedSuperset() {
        EspprcLabel<FltX> narrow = Label(1.0, 1.0, 1.0);
        EspprcLabel<FltX> wide = Label(2.0, 1.0, 1.0) with { Visited = narrow.Visited.Add(3) };

        DefaultLabelDominancePolicy.Instance.Dominates(wide, narrow).Should().BeFalse();
        DefaultLabelDominancePolicy.Instance.Dominates(narrow, wide)
            .Should().BeTrue("更便宜且访问更少 / cheaper and visits fewer");
    }

    /// <summary>
    /// **不可达集合更小才可能支配**：更受限的标签扩展性更差。
    /// **A smaller forbidden set is required**: a more restricted label is less extendable.
    /// </summary>
    [Fact]
    public void Dominance_ShouldRejectForbiddenSuperset() {
        EspprcLabel<FltX> open = Label(1.0, 1.0, 1.0);
        EspprcLabel<FltX> restricted = open with { Forbidden = open.Forbidden.Add(4) };

        DefaultLabelDominancePolicy.Instance.Dominates(restricted, open).Should().BeFalse();
    }

    /// <summary>
    /// **`IsExact` 默认假，是安全默认**：支配判定写错时定价仍会报"精确定价完成"，而解并非最优。
    /// **`IsExact` defaults to false, the safe default**: with a wrong dominance test, pricing still reports exact completion
    /// while the solution is not optimal.
    /// </summary>
    [Fact]
    public void Dominance_ShouldDefaultToUncertified() {
        ILabelDominancePolicy policy = new UncertifiedPolicy();

        policy.IsExact.Should().BeFalse();
    }

    /// <summary>默认策略认证精确性 / The default policy certifies exactness.</summary>
    [Fact]
    public void Dominance_DefaultShouldCertify() =>
        DefaultLabelDominancePolicy.Instance.IsExact.Should().BeTrue();

    /// <summary>默认列选择保留全部 / The default selector keeps every column.</summary>
    [Fact]
    public void ColumnSelector_ShouldKeepAll() {
        var columns = new List<Route<FltX>>
        {
            RouteOf("c1"), RouteOf("c2"),
        };

        KeepAllColumnSelector<FltX>.Instance.Select(columns, 1).Should().HaveCount(2);
    }

    /// <summary>
    /// **定价结果把截断与收敛分开**：截断不得报成"精确定价完成"。
    /// **The pricing result separates truncation from convergence**: truncation must not read as exact completion.
    /// </summary>
    [Fact]
    public void PricingResult_ShouldDistinguishInterruption() {
        var interrupted = new PricingResult<FltX>(Array.Empty<Route<FltX>>(), new FltX(0), false, true);
        var converged = new PricingResult<FltX>(Array.Empty<Route<FltX>>(), new FltX(0), true);

        interrupted.Interrupted.Should().BeTrue();
        interrupted.ExactPricingComplete.Should().BeFalse();
        converged.ExactPricingComplete.Should().BeTrue();
        converged.Interrupted.Should().BeFalse();
    }

    /// <summary>定价结果复制路线列表 / The pricing result copies its route list.</summary>
    [Fact]
    public void PricingResult_ShouldCopyRoutes() {
        var routes = new List<Route<FltX>> { RouteOf("c1") };
        var result = new PricingResult<FltX>(routes, new FltX(0), true);

        routes.Add(RouteOf("c2"));

        result.Routes.Should().ContainSingle();
    }

    /// <summary>默认中断检查器永不中断 / The default interruption checker never interrupts.</summary>
    [Fact]
    public void InterruptionChecker_DefaultShouldNeverStop() =>
        NeverInterruptChecker.Instance.ShouldStop().Should().BeFalse();

    /// <summary>构造标签 / Build a label.</summary>
    private static EspprcLabel<FltX> Label(
        double cost, double time, double load, string node = "n") =>
        new(
            new FltX(cost), new FltX(time), new FltX(load),
            new NetworkNodeId(node),
            VisitedCustomers.Empty(10),
            ForbiddenCustomers.Empty(10),
            -1);

    /// <summary>构造供选择器用的小路线 / Build a small route for the selector.</summary>
    private static Route<FltX> RouteOf(string customerId) {
        var origin = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        RouteStop<FltX>[] stops = new[]
        {
            new RouteStop<FltX>(new NetworkNodeId("depot"), null, origin, origin, origin, Meters(0)),
            new RouteStop<FltX>(
                new NetworkNodeId(customerId), new CustomerId(customerId),
                origin, origin, origin, Meters(1)),
        };
        return Route<FltX>.Invoke(new VehicleTypeId("truck"), stops, Meters(1), Meters(1)).Value;
    }

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Fuookami.Ospf.Quantities.Quantity.Quantity<FltX> Meters(double value) =>
        new(new FltX(value), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);

    /// <summary>未认证的支配策略替身 / An uncertified dominance policy stand-in.</summary>
    private sealed class UncertifiedPolicy : ILabelDominancePolicy {
        /// <inheritdoc/>
        public bool Dominates<V>(EspprcLabel<V> a, EspprcLabel<V> b)
            where V : struct, Fuookami.Ospf.Math.Algebra.Concept.IRealNumber<V> => false;
    }
}
