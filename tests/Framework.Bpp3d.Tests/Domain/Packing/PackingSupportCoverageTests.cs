// 水平圆柱支撑覆盖测试 / Tests for the horizontal cylinder support coverage.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

using InfraItem = Fuookami.Ospf.Framework.Bpp3d.Infra.Item;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain.Packing;

/// <summary>
/// 水平圆柱支撑覆盖 / Horizontal cylinder support coverage.
/// </summary>
/// <remarks>
/// 夹具约定：半径 1、轴向 X、底面 y=2（离地）、轴线沿 x 跨 0..4、径向中心 z=1。
/// 支撑体一律"顶面与圆柱底面同高"（y 上界 2）、径向覆盖 z ∈ [0, 2]。
///
/// Fixture convention: radius 1, X axis, bottom face at y=2 (off the floor), axis spanning x 0..4, radial centre z=0. Supports are flush with the
/// cylinder's bottom face (y upper bound 2) and cover z ∈ [-1, 1] radially.
/// </remarks>
public sealed class PackingSupportCoverageTests {
    private static Quantity<FltX> M(double value) =>
        new(new FltX(value), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);

    private static PackingBin Bin(params PackingItem[] items) => new(items) {
        Name = "B1",
        Type = new BinType<FltX>("B1", M(10), M(10), M(10), M(1000)),
    };

    private static PackingItem Cylinder() => new(
        new InfraItem("C1", "Cylinder"),
        Amount: 1,
        Geometry: new PackingItemGeometry(
            PackingShapeType.Cylinder, Axis3.X, Radius: 1.0,
            MinX: 0.0, MinY: 2.0, MinZ: 0.0,
            MaxX: 4.0, MaxY: 4.0, MaxZ: 2.0));

    private static PackingItem Support(string id, double minX, double maxX) => new(
        new InfraItem(id, id),
        Amount: 1,
        Geometry: new PackingItemGeometry(
            PackingShapeType.Cuboid, Axis: null, Radius: 0.0,
            MinX: minX, MinY: 0.0, MinZ: -1.0,
            MaxX: maxX, MaxY: 2.0, MaxZ: 1.0));

    /// <summary>**两块各盖一半的支撑合起来算支撑充足** / Two supports covering one half each together count as sufficient.</summary>
    /// <remarks>
    /// 这一条钉的是"求并集、不是只看一块"：任何"只要有任意一块支撑就算"和"必须有单块完整覆盖"的实现都会在这里出错，
    /// 前者对后者给出相反结论。
    ///
    /// This pins "union, not one support": both "any single support counts" and "one support must cover everything" get this case wrong, and they
    /// disagree with each other about it.
    /// </remarks>
    [Fact]
    public void TwoHalves_ShouldCountAsSufficientSupport() {
        PackingBin bin = Bin(Cylinder(), Support("S1", 0.0, 2.0), Support("S2", 2.0, 4.0));

        PackingGeometryGuard.RequireHorizontalCylinderSupport(bin, "test").IsOk.Should().BeTrue();
    }

    /// <summary>**只托住中间一段的支撑不算支撑充足** / A single support under the middle segment is not sufficient.</summary>
    /// <remarks>
    /// 这是与旧实现分开的关键样本：旧实现"任意一块支撑顶面不高于圆柱底面就算有支撑"，
    /// **只托中间一段会通过**，而圆柱两端仍然悬空。
    ///
    /// This is the sample that separates this implementation from the old one: the old rule ("any support whose top is no higher than the cylinder's
    /// bottom counts") **accepts a narrow contact under the middle**, leaving both ends of the cylinder hanging.
    /// </remarks>
    [Fact]
    public void NarrowMiddleSupport_ShouldBeInsufficient() {
        PackingBin bin = Bin(Cylinder(), Support("S1", 1.0, 3.0));

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            PackingGeometryGuard.RequireHorizontalCylinderSupport(bin, "test");

        result.IsOk.Should().BeFalse();
        Failed<Success, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("type=horizontal_support");
        failed.Message.Should().Contain("item[0]");
    }

    /// <summary>**完全悬空的圆柱不算支撑充足** / A hanging cylinder is not supported.</summary>
    [Fact]
    public void HangingCylinder_ShouldBeInsufficient() {
        PackingBin bin = Bin(Cylinder());

        PackingGeometryGuard.RequireHorizontalCylinderSupport(bin, "test").IsOk.Should().BeFalse();
    }

    /// <summary>**顶面与圆柱底面之间有间隙的支撑不算支撑** / A support with a gap below the cylinder's bottom face does not count.</summary>
    /// <remarks>
    /// 基线的判据要求两者**同高**（`abs(support.maxY - cylinder.minY) <= tolerance`）。旧实现只要求"支撑顶面不高于圆柱底面"，
    /// 于是**中间空出 0.5 的悬空支撑也算数**。
    ///
    /// The baseline requires the two to be **flush**. The old rule only required the support top to be no higher, so **a support floating half a unit
    /// below still counted**.
    /// </remarks>
    [Fact]
    public void SupportWithAGap_ShouldNotCount() {
        var dropped = new PackingItem(
            new InfraItem("S1", "S1"),
            Amount: 1,
            Geometry: new PackingItemGeometry(
                PackingShapeType.Cuboid, Axis: null, Radius: 0.0,
                MinX: 0.0, MinY: 0.0, MinZ: 0.0,
                MaxX: 4.0, MaxY: 1.5, MaxZ: 2.0));

        PackingBin bin = Bin(Cylinder(), dropped);

        PackingGeometryGuard.RequireHorizontalCylinderSupport(bin, "test").IsOk.Should().BeFalse();
    }

    /// <summary>**贴地或竖直轴的圆柱总是支撑充足** / A floor-standing or vertical-axis cylinder is always supported.</summary>
    [Fact]
    public void FloorAndVerticalCylinders_ShouldAlwaysBeSupported() {
        var onFloor = new PackingItem(
            new InfraItem("C1", "Cylinder"),
            Amount: 1,
            Geometry: new PackingItemGeometry(
                PackingShapeType.Cylinder, Axis3.X, Radius: 1.0,
                MinX: 0.0, MinY: 0.0, MinZ: 0.0,
                MaxX: 4.0, MaxY: 2.0, MaxZ: 2.0));
        PackingGeometryGuard.RequireHorizontalCylinderSupport(Bin(onFloor), "test").IsOk.Should().BeTrue();

        var vertical = new PackingItem(
            new InfraItem("C2", "Cylinder"),
            Amount: 1,
            Geometry: new PackingItemGeometry(
                PackingShapeType.Cylinder, Axis3.Y, Radius: 1.0,
                MinX: 0.0, MinY: 2.0, MinZ: 0.0,
                MaxX: 2.0, MaxY: 6.0, MaxZ: 2.0));
        PackingGeometryGuard.RequireHorizontalCylinderSupport(Bin(vertical), "test").IsOk.Should().BeTrue();
    }

    /// <summary>**径向错开的支撑不算支撑** / A radially offset support does not count.</summary>
    /// <remarks>
    /// 支撑体顶面同高、沿轴线也够长，但**不在圆柱轴线正下方**：圆柱的支撑线落在它的径向跨度之外。
    /// 少了这条判据，"垫在旁边而不在下面"会被当成支撑。
    ///
    /// The support is flush and long enough along the axis but **not under the cylinder's axis**: the support line falls outside its radial span.
    /// Without this criterion, a pad that sits beside rather than underneath counts as support.
    /// </remarks>
    [Fact]
    public void RadiallyOffsetSupport_ShouldNotCount() {
        var beside = new PackingItem(
            new InfraItem("S1", "S1"),
            Amount: 1,
            Geometry: new PackingItemGeometry(
                PackingShapeType.Cuboid, Axis: null, Radius: 0.0,
                MinX: 0.0, MinY: 0.0, MinZ: 5.0,
                MaxX: 4.0, MaxY: 2.0, MaxZ: 7.0));

        PackingGeometryGuard.RequireHorizontalCylinderSupport(Bin(Cylinder(), beside), "test")
            .IsOk.Should().BeFalse();
    }

    /// <summary>**主守卫也会做支撑检测（端到端）** / The main guard runs the support check too (end-to-end).</summary>
    /// <remarks>
    /// 支撑检测若只挂在 `RequireHorizontalCylinderSupport` 上、而主守卫不调用它，**整条装箱路径都不会做支撑检测**，
    /// 而"装箱成功"看起来毫无异常。
    ///
    /// If the support check hangs off `RequireHorizontalCylinderSupport` alone and the main guard never calls it, **the whole packing path skips
    /// support validation**, and "the pack succeeded" looks completely normal.
    /// </remarks>
    [Fact]
    public void MainGuard_ShouldRejectUnsupportedCylinders() {
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            PackingGeometryGuard.RequirePackedBinShapeGeometry(Bin(Cylinder()), "test");

        result.IsOk.Should().BeFalse();
        Failed<Success, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("type=horizontal_support");
    }

    /// <summary>**另一根圆柱不能充当支撑** / Another cylinder cannot act as a support.</summary>
    [Fact]
    public void CylindersCannotSupportCylinders() {
        var lower = new PackingItem(
            new InfraItem("C2", "lower"),
            Amount: 1,
            Geometry: new PackingItemGeometry(
                PackingShapeType.Cylinder, Axis3.X, Radius: 1.0,
                MinX: 0.0, MinY: 0.0, MinZ: 0.0,
                MaxX: 4.0, MaxY: 2.0, MaxZ: 2.0));

        PackingBin bin = Bin(Cylinder(), lower);

        PackingGeometryGuard.RequireHorizontalCylinderSupport(bin, "test").IsOk.Should().BeFalse();
    }

    /// <summary>**区间并集判据自身** / The interval-union criterion itself.</summary>
    [Fact]
    public void IntervalsCoverSpan_ShouldRequireAnUnbrokenUnion() {
        var halves = new List<(double Min, double Max)> { (0.0, 2.0), (2.0, 4.0) };
        HorizontalCylinderSupportCoverage.IntervalsCoverSpan(0.0, 4.0, halves).Should().BeTrue();

        var withGap = new List<(double Min, double Max)> { (0.0, 1.5), (2.5, 4.0) };
        HorizontalCylinderSupportCoverage.IntervalsCoverSpan(0.0, 4.0, withGap).Should().BeFalse();

        // **起点对上、终点对不上**：区间从 targetMin 开始却中途停下，必须判为覆盖不足。
        // 只测"中间空一段"的夹具会在循环里就返回失败，测不到那条终点判据。
        // **Starts at targetMin but stops short**: an interval that begins at targetMin yet ends early must count as uncovered. A fixture with an
        // interior gap returns false inside the loop and never exercises the end-point criterion at all.
        var stoppedShort = new List<(double Min, double Max)> { (0.0, 2.0) };
        HorizontalCylinderSupportCoverage.IntervalsCoverSpan(0.0, 4.0, stoppedShort).Should().BeFalse();

        // 乱序输入也必须按并集处理 / Out-of-order input must still be handled as a union.
        var unordered = new List<(double Min, double Max)> { (2.0, 4.0), (0.0, 2.0) };
        HorizontalCylinderSupportCoverage.IntervalsCoverSpan(0.0, 4.0, unordered).Should().BeTrue();
    }
}
