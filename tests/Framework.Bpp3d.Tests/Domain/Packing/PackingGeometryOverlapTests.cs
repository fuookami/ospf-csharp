// 装箱几何重叠检测测试 / Tests for the packing geometry overlap detection.

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
/// 装箱几何重叠检测 / The packing geometry overlap detection.
/// </summary>
public sealed class PackingGeometryOverlapTests {
    /// <summary>构造一个长方体几何快照 / Build a cuboid geometry snapshot.</summary>
    private static PackingItemGeometry Box(double x, double y, double z, double size = 1.0) =>
        new(PackingShapeType.Cuboid, Axis: null, Radius: 0.0,
            MinX: x, MinY: y, MinZ: z,
            MaxX: x + size, MaxY: y + size, MaxZ: z + size);

    /// <summary>构造一个圆柱几何快照 / Build a cylinder geometry snapshot.</summary>
    private static PackingItemGeometry Cylinder(
        Axis3 axis,
        double radius,
        double x,
        double y,
        double z,
        double extent = 1.0) =>
        new(PackingShapeType.Cylinder, axis, radius,
            MinX: x, MinY: y, MinZ: z,
            MaxX: x + (axis == Axis3.X ? extent : radius * 2),
            MaxY: y + (axis == Axis3.Y ? extent : radius * 2),
            MaxZ: z + (axis == Axis3.Z ? extent : radius * 2));

    private static PackingItem Item(PackingItemGeometry? geometry, ulong order = 0) =>
        new(new InfraItem("I1", "Item 1"), 1, geometry, order);

    private static Quantity<FltX> M(double value) =>
        new(new FltX(value), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);

    /// <summary>构造一个 10×10×10 的箱子 / Build a 10×10×10 bin.</summary>
    private static PackingBin Bin(params PackingItem[] items) => new(items) {
        Name = "B1",
        Type = new BinType<FltX>("B1", M(10), M(10), M(10), M(1000)),
    };

    /// <summary>**真正相交的两个长方体被判为重叠** / Two genuinely intersecting boxes are reported as overlapping.</summary>
    /// <remarks>
    /// 这条断言是这里存在的原因：修复前的 `ItemsOverlap` **恒返回 `false`**，守卫永远说"没有重叠"，
    /// 而"全部通过"看起来和"数据真的合法"一模一样。
    ///
    /// This is why the batch exists: the pre-fix `ItemsOverlap` **always returned `false`**, so the guard never reported an overlap, and "everything
    /// passed" looked exactly like "the data really is valid".
    /// </remarks>
    [Fact]
    public void BoxesOverlap_ShouldBeReported() {
        PackingGeometryGuard.Overlaps(Box(0, 0, 0), Box(0.5, 0.5, 0.5)).Should().BeTrue();
        PackingGeometryGuard.Overlaps(Box(0, 0, 0), Box(0, 0, 0)).Should().BeTrue();
    }

    /// <summary>**只在一个轴上相接不算重叠** / Touching on a single axis is not an overlap.</summary>
    /// <remarks>
    /// 只断言"完全不接触的不重叠"是不够的：**把边界相接也算成重叠**是最常见的边界写错方式，
    /// 而它在只有分离样本的测试里完全看不出来。
    ///
    /// Asserting only that fully separated boxes do not overlap is not enough: **counting boundary contact as an overlap** is the most common
    /// off-by-one in this code, and it is invisible in a test that only has separated samples.
    /// </remarks>
    [Fact]
    public void BoxesTouchingOrSeparated_ShouldNotBeReported() {
        PackingGeometryGuard.Overlaps(Box(0, 0, 0), Box(1, 0, 0)).Should().BeFalse();
        PackingGeometryGuard.Overlaps(Box(0, 0, 0), Box(0, 1, 0)).Should().BeFalse();
        PackingGeometryGuard.Overlaps(Box(0, 0, 0), Box(0, 0, 1)).Should().BeFalse();
        PackingGeometryGuard.Overlaps(Box(0, 0, 0), Box(5, 5, 5)).Should().BeFalse();
    }

    /// <summary>**圆柱与长方体在径向相交才算重叠** / A cylinder overlaps a box only when it intersects radially.</summary>
    [Fact]
    public void CylinderAndBox_ShouldUseRadialDistance() {
        // 半径 0.6、轴心 (0.5, ·, 0.5) 与 (0,0,0)–(1,1,1) 的盒子在径向相交。
        // Radius 0.6 centred at (0.5, ·, 0.5) intersects the (0,0,0)–(1,1,1) box radially.
        PackingGeometryGuard.Overlaps(Cylinder(Axis3.Y, 0.6, 0, 0, 0), Box(0, 0, 0))
            .Should().BeTrue();

        // 同一圆柱挪到 x=10 处，径向距离远大于半径。
        // The same cylinder moved to x=10 is radially far outside the radius.
        PackingGeometryGuard.Overlaps(Cylinder(Axis3.Y, 0.6, 10, 0, 0), Box(0, 0, 0))
            .Should().BeFalse();

        // 径向相交但轴向完全错开，仍然不重叠。
        // Radially intersecting but fully separated along the axis means no overlap.
        PackingGeometryGuard.Overlaps(Cylinder(Axis3.Y, 0.6, 0, 5, 0), Box(0, 0, 0))
            .Should().BeFalse();

        // **包围盒相交但径向不相交**：细圆柱压在盒子的角外沿上。包围盒判据会说"重叠"，
        // 真实几何是"没碰到"——这一条才是"径向判据真的在用"的证据。
        // **Bounding boxes intersect but the discs do not**: a thin cylinder sits just outside the box's corner. A bounding-box test would say
        // "overlap"; the real geometry says "no contact" — this is the evidence that the radial rule is actually used.
        PackingGeometryGuard.Overlaps(Cylinder(Axis3.Y, 0.1, 1.05, 0, 1.05), Box(0, 0, 0))
            .Should().BeFalse();

        // **包围盒真的相交、圆盘却不接触**：半径 0.5 的圆盘中心在 (1.45, ·, 1.45)，
        // 它在 X/Z 上各伸出 0.45 之外的 0.05 压在盒子边沿内，但到盒子角点 (1,1) 的距离是 0.636 > 0.5。
        // 只用包围盒会把这一对该判成重叠。
        // **The bounding boxes genuinely intersect while the discs do not**: a radius-0.5 disc centred at (1.45, ·, 1.45) pokes 0.05 past the box edge
        // in X and Z, yet sits 0.636 from the corner (1,1), i.e. beyond its radius. A bounding-box-only test would report this pair as overlapping.
        PackingGeometryGuard.Overlaps(Cylinder(Axis3.Y, 0.5, 0.95, 0, 0.95), Box(0, 0, 0))
            .Should().BeFalse();
    }

    /// <summary>**同轴圆柱按半径和判交，异轴圆柱按相贯判交** / Same-axis cylinders compare radii, different-axis ones compare their shared span.</summary>
    /// <remarks>
    /// **两条分支必须分别钉住**：只测同轴时，把异轴也走半径和会让"并排但不接触的两个异轴圆柱"被判成重叠。
    ///
    /// **Both branches need their own assertion**: testing only the same-axis case lets a different-axis pair fall through to the radius-sum rule, which
    /// reports two side-by-side but disjoint cylinders as overlapping.
    /// </remarks>
    [Fact]
    public void Cylinders_ShouldDistinguishSameAxisFromDifferentAxis() {
        // 同轴（Y），径向距离 0.5，半径和 1.0 → 重叠。
        // Same axis (Y), radial distance 0.5, radius sum 1.0 → overlap.
        PackingGeometryGuard.Overlaps(
            Cylinder(Axis3.Y, 0.5, 0, 0, 0),
            Cylinder(Axis3.Y, 0.5, 0.5, 0, 0)).Should().BeTrue();

        // 同轴（Y），径向距离 2.0 > 半径和 1.0 → 不重叠。
        // Same axis (Y), radial distance 2.0 > radius sum 1.0 → no overlap.
        PackingGeometryGuard.Overlaps(
            Cylinder(Axis3.Y, 0.5, 0, 0, 0),
            Cylinder(Axis3.Y, 0.5, 2, 0, 0)).Should().BeFalse();

        // 异轴：X 轴与 Y 轴圆柱在 (0.5, 0.5) 附近真正相贯。
        // Different axes: an X-axis and a Y-axis cylinder genuinely intersect near (0.5, 0.5).
        PackingGeometryGuard.Overlaps(
            Cylinder(Axis3.X, 0.5, 0, 0.5, 0.5),
            Cylinder(Axis3.Y, 0.5, 0.5, 0, 0.5)).Should().BeTrue();

        // 异轴但相距极远 → 不重叠。
        // Different axes but far apart → no overlap.
        PackingGeometryGuard.Overlaps(
            Cylinder(Axis3.X, 0.5, 0, 0.5, 0.5),
            Cylinder(Axis3.Y, 0.5, 40, 0, 40)).Should().BeFalse();

        // **异轴相交，且两端中心在彼此径向轴上相距很远**：细长 Y 圆柱贯穿短 X 圆柱。
        // 这里"半径和"判据会说"不重叠"（中心距 5.0 > 半径和 1.0），真实几何是"相贯"——
        // 这一条才证明异轴分支真的在用相贯判据。
        // **Different axes, intersecting, with the centres far apart on each other's radial axes**: a long Y cylinder pierces a short X cylinder.
        // The radius-sum rule would say "no overlap" (centre distance 5.0 > radius sum 1.0); the real geometry says "they intersect" — this is the
        // evidence that the different-axis branch really uses the intersection rule.
        PackingGeometryGuard.Overlaps(
            Cylinder(Axis3.X, 0.5, 0, 0, 0, extent: 1.0),
            Cylinder(Axis3.Y, 0.5, 0, 0.5, 0, extent: 10.0)).Should().BeTrue();

        // **异轴，且只有一个半径项真的起作用**：Y 圆柱的半径与其到 X 圆柱轴线的距离几乎相等，
        // 相贯后的共享半径只剩一小段，够不到 X 圆柱的共享跨度。忽略那一个半径项会把它误判成重叠。
        // **Different axes where exactly one radius term matters**: the Y cylinder's radius nearly equals its distance to the X cylinder's axis, leaving
        // only a sliver of shared radius that does not reach the X cylinder's shared span. Dropping that radius term misreports it as overlapping.
        PackingGeometryGuard.Overlaps(
            Cylinder(Axis3.X, 1.0, 0, 0, -0.5, extent: 1.0),
            Cylinder(Axis3.Y, 1.0, 0.995, 2.89, 0, extent: 0.2)).Should().BeFalse();
    }

    /// <summary>**守卫对相交的箱子返回失败并指名两个下标** / The guard rejects an intersecting bin and names both indices.</summary>
    [Fact]
    public void RequirePackedBinShapeGeometry_ShouldRejectOverlaps() {
        PackingBin bin = Bin(Item(Box(0, 0, 0)), Item(Box(0.2, 0.2, 0.2), order: 1));

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            PackingGeometryGuard.RequirePackedBinShapeGeometry(bin, "test");

        result.IsOk.Should().BeFalse();
        Failed<Success, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("type=overlap");
        failed.Message.Should().Contain("item[0]");
        failed.Message.Should().Contain("item[1]");
    }

    /// <summary>**互不接触的箱子通过校验** / A bin with disjoint items passes.</summary>
    [Fact]
    public void RequirePackedBinShapeGeometry_ShouldAcceptDisjointBins() {
        PackingBin bin = Bin(Item(Box(0, 0, 0)), Item(Box(2, 0, 0), order: 1));

        PackingGeometryGuard.RequirePackedBinShapeGeometry(bin, "test").IsOk.Should().BeTrue();
    }

    /// <summary>**缺几何的箱子被拒绝，而不是被当成"没重叠"** / A bin lacking geometry is rejected rather than treated as "no overlap".</summary>
    /// <remarks>
    /// 这是这里最重要的一条：**静默跳过缺几何的物品会让守卫对整条旧路径恒真**，
    /// 而"全部通过"正是最容易被当成"数据合法"的输出。
    ///
    /// This is the most important assertion of the batch: **silently skipping items without geometry makes the guard vacuously true on the whole legacy
    /// path**, and "everything passed" is exactly the output most easily mistaken for "the data is valid".
    /// </remarks>
    [Fact]
    public void RequirePackedBinShapeGeometry_ShouldRejectMissingGeometry() {
        PackingBin bin = Bin(Item(geometry: null), Item(geometry: null, order: 1));

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            PackingGeometryGuard.RequirePackedBinShapeGeometry(bin, "test");

        result.IsOk.Should().BeFalse();
        Failed<Success, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("type=missing_geometry");
        failed.Message.Should().Contain("item[0]");
    }

    /// <summary>**几何快照把形状尺寸与放置位置一起冻结** / The snapshot freezes both shape extents and placement.</summary>
    [Fact]
    public void PackingItemGeometryFrom_ShouldFreezeExtentsAndPosition() {
        var shape = new CuboidPackingShape3<FltX>(
            new Fuookami.Ospf.Quantities.Geometry.QuantityCuboid3<FltX>(
                new Fuookami.Ospf.Quantities.Quantity.Quantity<FltX>(new FltX(2), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
                new Fuookami.Ospf.Quantities.Quantity.Quantity<FltX>(new FltX(3), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
                new Fuookami.Ospf.Quantities.Quantity.Quantity<FltX>(new FltX(4), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter)));
        var position = new QuantityPoint3<FltX>(
            new Fuookami.Ospf.Quantities.Quantity.Quantity<FltX>(new FltX(1), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
            new Fuookami.Ospf.Quantities.Quantity.Quantity<FltX>(new FltX(2), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
            new Fuookami.Ospf.Quantities.Quantity.Quantity<FltX>(new FltX(3), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter));

        var geometry = PackingItemGeometry.From(shape, position);

        geometry.MinX.Should().Be(1.0);
        geometry.MinY.Should().Be(2.0);
        geometry.MinZ.Should().Be(3.0);
        geometry.MaxX.Should().Be(3.0);
        geometry.MaxY.Should().Be(5.0);
        geometry.MaxZ.Should().Be(7.0);
        geometry.Width.Should().Be(2.0);
        geometry.Height.Should().Be(3.0);
        geometry.Depth.Should().Be(4.0);
        geometry.IsCylinder.Should().BeFalse();
        geometry.Axis.Should().BeNull();
    }

    /// <summary>**装不下的物品被越界检测拒绝** / An item that does not fit is rejected by the outside-bin check.</summary>
    /// <remarks>
    /// 边界用例：**贴着箱子内壁的物品必须通过**，超出 0.1 才拒绝。只测"明显在外"会漏掉把边界当越界的写法。
    ///
    /// Boundary case: **an item flush against the inner wall must pass**, and only a 0.1 overhang may be rejected. Testing only wildly-outside items
    /// misses the off-by-one that treats the boundary itself as outside.
    /// </remarks>
    [Fact]
    public void RequirePackedBinShapeGeometry_ShouldRejectItemsOutsideTheBin() {
        PackingBin flush = Bin(Item(Box(8, 0, 0, size: 2)));
        PackingGeometryGuard.RequirePackedBinShapeGeometry(flush, "test").IsOk.Should().BeTrue();

        PackingBin overhang = Bin(Item(Box(8.1, 0, 0, size: 2)));
        Result<Success, ErrorCode, Error<ErrorCode>> rejected =
            PackingGeometryGuard.RequirePackedBinShapeGeometry(overhang, "test");
        rejected.IsOk.Should().BeFalse();
        Failed<Success, ErrorCode, Error<ErrorCode>> failed =
            rejected.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("type=outside_bin");
        failed.Message.Should().Contain("item[0]");
    }

    /// <summary>**负坐标被越界检测拒绝** / Negative coordinates are rejected by the outside-bin check.</summary>
    [Fact]
    public void RequirePackedBinShapeGeometry_ShouldRejectNegativePositions() {
        // **三个轴分别钉住**：只在 X 上为负时，去掉 Y 或 Z 的下界检查仍然会失败，测不出缺口。
        // **Pin each of the three axes**: with only X negative, dropping the Y or Z lower bound still fails, hiding the gap.
        PackingGeometryGuard.RequirePackedBinShapeGeometry(Bin(Item(Box(-0.5, 0, 0))), "test")
            .IsOk.Should().BeFalse();
        PackingGeometryGuard.RequirePackedBinShapeGeometry(Bin(Item(Box(0, -0.5, 0))), "test")
            .IsOk.Should().BeFalse();
        PackingGeometryGuard.RequirePackedBinShapeGeometry(Bin(Item(Box(0, 0, -0.5))), "test")
            .IsOk.Should().BeFalse();
    }

    /// <summary>**缺箱子类型的箱子被拒绝，而不是被当成"装得下"** / A bin without a type is rejected rather than assumed to fit.</summary>
    [Fact]
    public void RequirePackedBinShapeGeometry_ShouldRejectMissingBinType() {
        PackingBin bin = new(new[] { Item(Box(0, 0, 0)) }) { Name = "B1" };

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            PackingGeometryGuard.RequirePackedBinShapeGeometry(bin, "test");

        result.IsOk.Should().BeFalse();
        Failed<Success, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("type=missing_bin_type");
    }
}
