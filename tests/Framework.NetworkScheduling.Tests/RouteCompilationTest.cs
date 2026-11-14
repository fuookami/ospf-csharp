// 路线编译影子价格与列池测试 / Tests for route-compilation shadow prices and the column pool.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 路线编译影子价格与列池 / Route-compilation shadow prices and the column pool.
/// </summary>
public sealed class RouteCompilationTest {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>车队键按车辆类型区分 / Fleet keys distinguish vehicle types.</summary>
    [Fact]
    public void FleetKey_ShouldDistinguishVehicleTypes() =>
        new FleetSizeShadowPriceKey(new VehicleTypeId("a"))
            .Should().NotBe(new FleetSizeShadowPriceKey(new VehicleTypeId("b")));

    /// <summary>车队键同类型相等 / Fleet keys of the same type are equal.</summary>
    [Fact]
    public void FleetKey_ShouldEquateSameType() =>
        new FleetSizeShadowPriceKey(new VehicleTypeId("a"))
            .Should().Be(new FleetSizeShadowPriceKey(new VehicleTypeId("a")));

    /// <summary>客户覆盖键按客户区分 / Coverage keys distinguish customers.</summary>
    [Fact]
    public void CoverageKey_ShouldDistinguishCustomers() =>
        new CustomerCoverageShadowPriceKey(new CustomerId("a"))
            .Should().NotBe(new CustomerCoverageShadowPriceKey(new CustomerId("b")));

    /// <summary>
    /// **两类键即使标识相同也不相等**：车队键与客户覆盖键承载不同的对偶，混淆会让 reduced cost 扣错行。
    /// **The two key kinds differ even with the same identifier**: fleet and coverage keys carry different duals, and conflating
    /// them would subtract the wrong row when pricing.
    /// </summary>
    [Fact]
    public void Keys_ShouldNotCollideAcrossKinds() {
        var fleet = new FleetSizeShadowPriceKey(new VehicleTypeId("x"));
        var coverage = new CustomerCoverageShadowPriceKey(new CustomerId("x"));

        fleet.Should().NotBe(coverage);
    }

    /// <summary>缺失的客户对偶读作零 / A missing customer dual reads as zero.</summary>
    [Fact]
    public void Map_ShouldReturnZeroForMissingCustomerDual() =>
        new VrpShadowPriceMap().CustomerDual(new CustomerId("ghost")).ToDouble().Should().Be(0.0);

    /// <summary>缺失的车队对偶读作零 / A missing fleet dual reads as zero.</summary>
    [Fact]
    public void Map_ShouldReturnZeroForMissingFleetDual() =>
        new VrpShadowPriceMap().FleetDual(new VehicleTypeId("ghost")).ToDouble().Should().Be(0.0);

    /// <summary>写入后可读回客户对偶 / A stored customer dual reads back.</summary>
    [Fact]
    public void Map_ShouldStoreCustomerDual() {
        var map = new VrpShadowPriceMap();
        var customerId = new CustomerId("c1");
        map.Put(new ShadowPrice(new CustomerCoverageShadowPriceKey(customerId), new Flt64(3.5)));

        map.CustomerDual(customerId).ToDouble().Should().BeApproximately(3.5, 1e-9);
    }

    /// <summary>
    /// **车队对偶保持 solver 的原始符号**：取绝对值或改符号会让每条定价的 reduced cost 系统性偏移。
    /// **The fleet dual keeps the solver's sign**: taking absolute values or flipping it would shift every priced reduced cost
    /// systematically.
    /// </summary>
    [Fact]
    public void Map_ShouldPreserveFleetDualSign() {
        var map = new VrpShadowPriceMap();
        var vehicleTypeId = new VehicleTypeId("truck");
        map.Put(new ShadowPrice(new FleetSizeShadowPriceKey(vehicleTypeId), new Flt64(-2.0)));

        map.FleetDual(vehicleTypeId).ToDouble().Should().BeApproximately(-2.0, 1e-9);
    }

    /// <summary>定价快照按业务含义分栏 / The pricing snapshot splits duals by business meaning.</summary>
    [Fact]
    public void Map_ShouldProducePricingDuals() {
        var map = new VrpShadowPriceMap();
        map.Put(new ShadowPrice(new CustomerCoverageShadowPriceKey(new CustomerId("c1")), new Flt64(1.0)));
        map.Put(new ShadowPrice(new FleetSizeShadowPriceKey(new VehicleTypeId("truck")), new Flt64(-4.0)));

        var duals = map.ToPricingDuals(PricingPhase.PhaseTwo);

        duals.Phase.Should().Be(PricingPhase.PhaseTwo);
        duals.Customer[new CustomerId("c1")].ToDouble().Should().BeApproximately(1.0, 1e-9);
        duals.Fleet[new VehicleTypeId("truck")].ToDouble().Should().BeApproximately(-4.0, 1e-9);
    }

    /// <summary>扩展行对偶随快照输出 / Extra-row duals travel with the snapshot.</summary>
    [Fact]
    public void Map_ShouldCarryAdditionalDuals() {
        var map = new VrpShadowPriceMap();
        map.PutAdditionalDual("row", new Flt64(7.0));

        var duals = map.ToPricingDuals(PricingPhase.PhaseOne);

        duals.Additional["row"].ToDouble().Should().BeApproximately(7.0, 1e-9);
        map.AdditionalDuals["row"].ToDouble().Should().BeApproximately(7.0, 1e-9);
    }

    /// <summary>路线变量求值按访问客户求和 / Route-argument evaluation sums over visited customers.</summary>
    [Fact]
    public void Map_ShouldSumCoverageDualsForRoute() {
        var map = new VrpShadowPriceMap();
        foreach (string id in new[] { "c1", "c2" }) {
            map.Put(new ShadowPrice(
                new CustomerCoverageShadowPriceKey(new CustomerId(id)),
                new Flt64(id == "c1" ? 1.0 : 2.0)));
        }
        map.Put(new ShadowPrice(new FleetSizeShadowPriceKey(new VehicleTypeId("truck")), new Flt64(-3.0)));

        var arguments = new VrpShadowPriceArguments(
            new VehicleTypeId("truck"),
            new HashSet<CustomerId> { new("c1"), new("c2") });

        map.Invoke(arguments).ToDouble().Should().BeApproximately(0.0, 1e-9, "1 + 2 - 3");
    }

    /// <summary>列池添加并返回新增 / The pool adds and returns the new routes.</summary>
    [Fact]
    public void Pool_ShouldAddAndReturnNewRoutes() {
        var pool = new RouteColumnPool<FltX>();

        IReadOnlyList<Route<FltX>> added = pool.AddColumns(new[] { RouteOf("c1", "a1", "a2") });

        added.Should().HaveCount(1);
        pool.Size.Should().Be(1);
    }

    /// <summary>
    /// **同签名路线只进一次池**：重复变量会让 LP 松弛把解拆到两个变量之间，取整得到不可行路线。
    /// **A same-signature route enters the pool once**: duplicate variables let the LP relaxation split the solution across two
    /// variables, and rounding yields an infeasible route.
    /// </summary>
    [Fact]
    public void Pool_ShouldDeduplicateBySignature() {
        var pool = new RouteColumnPool<FltX>();

        pool.AddColumns(new[] { RouteOf("c1", "a1", "a2") });
        IReadOnlyList<Route<FltX>> second = pool.AddColumns(new[] { RouteOf("c1", "a1", "a2") });

        second.Should().BeEmpty();
        pool.Size.Should().Be(1);
    }

    /// <summary>不同客户视为不同列 / Different customers are different columns.</summary>
    [Fact]
    public void Pool_ShouldKeepDistinctRoutes() {
        var pool = new RouteColumnPool<FltX>();

        pool.AddColumns(new[] { RouteOf("c1", "a1", "a2"), RouteOf("c2", "a3", "a4") });

        pool.Size.Should().Be(2);
    }

    /// <summary>签名存在性可查询 / Signature presence is queryable.</summary>
    [Fact]
    public void Pool_ShouldReportSignaturePresence() {
        var pool = new RouteColumnPool<FltX>();
        Route<FltX> route = RouteOf("c1", "a1", "a2");

        pool.AddColumns(new[] { route });

        pool.Contains(route.Signature).Should().BeTrue();
        pool.Contains("nonexistent").Should().BeFalse();
    }

    /// <summary>移除后签名同时释放 / Removing releases the signature as well.</summary>
    [Fact]
    public void Pool_ShouldReleaseSignatureOnRemove() {
        var pool = new RouteColumnPool<FltX>();
        Route<FltX> route = RouteOf("c1", "a1", "a2");
        pool.AddColumns(new[] { route });

        pool.RemoveColumns(new[] { route });

        pool.Size.Should().Be(0);
        pool.Contains(route.Signature).Should().BeFalse();
        pool.AddColumns(new[] { route }).Should().HaveCount(1, "签名已释放，可重新加入 / the signature is released and may be re-added");
    }

    /// <summary>兼容性过滤不改变池内容 / Compatibility filtering leaves the pool untouched.</summary>
    [Fact]
    public void Pool_ShouldFilterWithoutMutating() {
        var pool = new RouteColumnPool<FltX>();
        pool.AddColumns(new[] { RouteOf("c1", "a1", "a2"), RouteOf("c2", "a3", "a4") });

        IReadOnlyList<Route<FltX>> kept = pool.FilterCompatible(
            route => route.Stops.Any(stop => stop.CustomerId is { Value: "c1" }));

        kept.Should().HaveCount(1);
        pool.Size.Should().Be(2);
    }

    /// <summary>路线快照是副本 / The route snapshot is a copy.</summary>
    [Fact]
    public void Pool_ShouldSnapshotRoutes() {
        var pool = new RouteColumnPool<FltX>();
        pool.AddColumns(new[] { RouteOf("c1", "a1", "a2") });

        IReadOnlyList<Route<FltX>> snapshot = pool.Routes;
        pool.AddColumns(new[] { RouteOf("c2", "a3", "a4") });

        snapshot.Should().HaveCount(1);
        pool.Size.Should().Be(2);
    }

    /// <summary>构造一条单客户路线 / Build a one-customer route.</summary>
    private static Route<FltX> RouteOf(string customerId, string startArcId, string endArcId) {
        var stops = new List<RouteStop<FltX>> {
            new(new NetworkNodeId("depot"), null, Origin, Origin, Origin, Meters(0.0)),
            new(new NetworkNodeId($"n{customerId[1..]}"), new CustomerId(customerId), Origin, Origin, Origin, Meters(1.0)),
            new(new NetworkNodeId("depot-end"), null, Origin, Origin, Origin, Meters(1.0)),
        };
        return Route<FltX>.Invoke(
            new VehicleTypeId("truck"),
            stops,
            Meters(0.0),
            Meters(10.0),
            new[] { new NetworkArcId(startArcId), new NetworkArcId(endArcId) }).Value;
    }

    /// <summary>以米为单位的量 / A quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
