// 兼容层测试 / Tests for the compatibility layer.
// 覆盖 `Box2Need`／`Placement2Need`／`Projection2Need`／`PackingScene2`／`NeedGeometryMapping`。
// Covers `Box2Need` / `Placement2Need` / `Projection2Need` / `PackingScene2` / `NeedGeometryMapping`.
//
// 本域源码在更早的批次已存在但**零测试覆盖**，本批补齐。这不只是补数量：`Box2Need` 的重叠/相交/内含是矩形口径下
// 所有校验的基础，一条判错的边界就足以让整片正常排布被当成非法。

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 兼容层 / Compatibility layer.
/// </summary>
public sealed class CompatRectangularTest {
    /// <summary>投影面积等于宽高之积 / The projection area is width times height.</summary>
    [Fact]
    public void Projection_ShouldMultiplyArea() =>
        new Projection2Need<FltX>(Meters(3), Meters(4)).Area.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(12.0, 1e-9);

    /// <summary>放置的最大坐标是原点加投影 / A placement's max coordinates are origin plus projection.</summary>
    [Fact]
    public void Placement_ShouldComputeMax() {
        var placement = new Placement2Need<FltX>(Meters(2), Meters(5), new Projection2Need<FltX>(Meters(3), Meters(4)));

        placement.MaxX.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-9);
        placement.MaxY.Value.ToFlt64().ToDouble().Should().BeApproximately(9.0, 1e-9);
    }

    /// <summary>放置转换出的盒体覆盖原点与最大坐标 / The box derived from a placement spans origin to max.</summary>
    [Fact]
    public void Placement_ShouldConvertToBox() {
        var placement = new Placement2Need<FltX>(
            Meters(2), Meters(5), new Projection2Need<FltX>(Meters(3), Meters(4)));
        var box = placement.ToBox2Need();

        box.MinX.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
        box.MaxX.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-9);
        box.Width.Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
        box.Height.Value.ToFlt64().ToDouble().Should().BeApproximately(4.0, 1e-9);
    }

    /// <summary>盒体面积等于宽高之积 / A box's area is width times height.</summary>
    [Fact]
    public void Box_ShouldComputeArea() =>
        Box(0, 0, 3, 4).Area.Value.ToFlt64().ToDouble().Should().BeApproximately(12.0, 1e-9);

    /// <summary>两个相交的盒体判为重叠 / Two intersecting boxes overlap.</summary>
    [Fact]
    public void Box_ShouldDetectOverlap() =>
        Box(0, 0, 4, 4).Overlaps(Box(2, 2, 6, 6)).Should().BeTrue();

    /// <summary>分离的盒体不重叠 / Disjoint boxes do not overlap.</summary>
    [Fact]
    public void Box_ShouldDetectDisjoint() =>
        Box(0, 0, 4, 4).Overlaps(Box(10, 10, 14, 14)).Should().BeFalse();

    /// <summary>
    /// **相切不算重叠**：若把相切判成重叠，紧邻摆放的正常排布会被整片判成非法。
    /// **Touching is not overlap**: flagging it would condemn perfectly normal adjacent layouts.
    /// </summary>
    [Fact]
    public void Box_ShouldNotTreatTouchingAsOverlap() {
        Box(0, 0, 4, 4).Overlaps(Box(4, 0, 8, 4)).Should().BeFalse("右边界相切 / right edges touch");
        Box(0, 0, 4, 4).Overlaps(Box(0, 4, 4, 8)).Should().BeFalse("上边界相切 / top edges touch");
    }

    /// <summary>一个轴重叠而另一轴分离时不判重叠 / Overlap on one axis with separation on the other is not an overlap.</summary>
    [Fact]
    public void Box_ShouldRequireOverlapOnBothAxes() {
        Box(0, 0, 4, 4).Overlaps(Box(2, 10, 6, 14)).Should().BeFalse();
        Box(0, 0, 4, 4).Overlaps(Box(10, 2, 14, 6)).Should().BeFalse();
    }

    /// <summary>一个盒体完全包含另一个时算重叠 / A fully contained box still counts as an overlap.</summary>
    [Fact]
    public void Box_ShouldDetectContainment() =>
        Box(0, 0, 10, 10).Overlaps(Box(2, 2, 4, 4)).Should().BeTrue();

    /// <summary>相交盒体的交集是公共区域 / The intersection of overlapping boxes is the shared region.</summary>
    [Fact]
    public void Box_ShouldIntersect() {
        Box2Need<FltX>? intersection = Box(0, 0, 4, 4).Intersect(Box(2, 1, 6, 3));

        intersection.Should().NotBeNull();
        intersection!.MinX.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
        intersection.MaxX.Value.ToFlt64().ToDouble().Should().BeApproximately(4.0, 1e-9);
        intersection.MinY.Value.ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-9);
        intersection.MaxY.Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>分离盒体的交集为空 / Disjoint boxes have no intersection.</summary>
    [Fact]
    public void Box_ShouldReturnNullForDisjoint() =>
        Box(0, 0, 4, 4).Intersect(Box(10, 10, 14, 14)).Should().BeNull();

    /// <summary>相切盒体的交集为空 / Touching boxes have no intersection.</summary>
    [Fact]
    public void Box_ShouldReturnNullForTouching() =>
        Box(0, 0, 4, 4).Intersect(Box(4, 0, 8, 4)).Should().BeNull();

    /// <summary>内部盒体判为内含 / An inner box is inside the outer one.</summary>
    [Fact]
    public void Box_ShouldDetectInside() =>
        Box(1, 1, 3, 3).Inside(Box(0, 0, 10, 10)).Should().BeTrue();

    /// <summary>与边界齐平的盒体也算内含 / A box flush with the boundary still counts as inside.</summary>
    [Fact]
    public void Box_ShouldAllowFlushInside() =>
        Box(0, 0, 10, 10).Inside(Box(0, 0, 10, 10)).Should().BeTrue();

    /// <summary>越界的盒体不算内含 / An out-of-bounds box is not inside.</summary>
    [Fact]
    public void Box_ShouldRejectOutside() =>
        Box(0, 0, 12, 10).Inside(Box(0, 0, 10, 10)).Should().BeFalse();

    /// <summary>
    /// **旋转要求物品允许旋转**：`Rotated` 为真但不许旋转时按未旋转处理，否则会把不许旋转的件排成旋转态。
    /// **Rotation requires permission**: `Rotated` set on a non-rotatable item is treated as unrotated, or a non-rotatable
    /// item would be laid out rotated.
    /// </summary>
    [Fact]
    public void Planned_ShouldIgnoreRotationWithoutPermission() {
        var item = new RectangleItem2<FltX>("a", Meters(2), Meters(4), AllowRotate: false);
        var planned = new PlannedRectangle2<FltX>(item, Meters(0), Meters(0), Rotated: true);

        planned.ToPlacement2Need().Projection.Width.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(2.0, 1e-9, "未获许可时宽高不互换 / dimensions do not swap without permission");
    }

    /// <summary>允许旋转时宽高互换 / Dimensions swap when rotation is permitted.</summary>
    [Fact]
    public void Planned_ShouldSwapWithPermission() {
        var item = new RectangleItem2<FltX>("a", Meters(2), Meters(4), AllowRotate: true);
        var planned = new PlannedRectangle2<FltX>(item, Meters(0), Meters(0), Rotated: true);

        planned.ToPlacement2Need().Projection.Width.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(4.0, 1e-9);
        planned.ToPlacement2Need().Projection.Height.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>板材面积等于宽高之积 / The sheet area is width times height.</summary>
    [Fact]
    public void Scene_ShouldComputeSheetArea() =>
        Scene(10, 10).SheetArea.Value.ToFlt64().ToDouble().Should().BeApproximately(100.0, 1e-9);

    /// <summary>板材盒体从原点覆盖整个板材 / The sheet box spans the whole sheet from the origin.</summary>
    [Fact]
    public void Scene_ShouldBuildSheetBox() {
        Box2Need<FltX> box = Scene(10, 10).SheetBox2Need();

        box.MinX.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
        box.MaxX.Value.ToFlt64().ToDouble().Should().BeApproximately(10.0, 1e-9);
    }

    /// <summary>全部在板材内的场景通过校验 / A scene with everything inside passes.</summary>
    [Fact]
    public void Scene_ShouldAcceptInsidePlacements() =>
        Scene(10, 10, Planned("a", 2, 2, 0, 0), Planned("b", 2, 2, 8, 8)).AllInsideSheet().Should().BeTrue();

    /// <summary>有件越界时校验失败 / A placement out of bounds fails the check.</summary>
    [Fact]
    public void Scene_ShouldRejectOutsidePlacement() =>
        Scene(10, 10, Planned("a", 2, 2, 9, 9)).AllInsideSheet().Should().BeFalse();

    /// <summary>已用面积是各件投影面积之和 / The used area is the sum of placement areas.</summary>
    [Fact]
    public void Scene_ShouldSumUsedArea() =>
        Scene(10, 10, Planned("a", 2, 3, 0, 0), Planned("b", 4, 5, 5, 0))
            .UsedArea().Value.ToFlt64().ToDouble().Should().BeApproximately(26.0, 1e-9, "6 + 20");

    /// <summary>
    /// **重叠时已用面积仍按各件面积累加**：问的是物料本身的面积，不是包围盒面积。
    /// **With overlaps the used area still sums each item's area**: the question is the items' own area, not the bounding
    /// box.
    /// </summary>
    [Fact]
    public void Scene_ShouldSumUsedAreaEvenWhenOverlapped() =>
        Scene(10, 10, Planned("a", 2, 2, 0, 0), Planned("b", 2, 2, 1, 1))
            .UsedArea().Value.ToFlt64().ToDouble().Should().BeApproximately(8.0, 1e-9);

    /// <summary>剩余面积是板材面积减已用面积 / The remaining area is sheet minus used.</summary>
    [Fact]
    public void Scene_ShouldComputeRemainingArea() =>
        Scene(10, 10, Planned("a", 2, 3, 0, 0)).RemainingArea().Value.ToFlt64().ToDouble()
            .Should().BeApproximately(94.0, 1e-9);

    /// <summary>利用率是已用面积与板材面积之比 / The utilisation ratio is used over sheet area.</summary>
    [Fact]
    public void Scene_ShouldComputeUtilization() =>
        Scene(10, 10, Planned("a", 10, 5, 0, 0)).Utilization().Value.ToFlt64().ToDouble()
            .Should().BeApproximately(0.5, 1e-9);

    /// <summary>空场景的利用率为零 / An empty scene has zero utilisation.</summary>
    [Fact]
    public void Scene_ShouldReportZeroForEmpty() =>
        Scene(10, 10).Utilization().Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);

    /// <summary>
    /// **量纲不可换算时利用率返回失败**：猜测一个换算会让利用率看起来正常却完全错。
    /// **A dimensionally inconvertible unit fails rather than guessing**: a guessed conversion would make the ratio look
    /// plausible while being entirely wrong.
    /// </summary>
    /// <remarks>
    /// **必须用不同量纲，而不是不同单位**。本会话最初用"米板材 + 厘米物料"，以为会失败，实测**通过**——cm² 到 m² 是
    /// 合法换算。要触发这条守卫，得让面积量纲本身不同（m² 对 s²）。这条经验写在这里，免得后来者重复同一个误判。
    /// **Different dimensions are required, not merely different units.** This session first used a metre sheet with
    /// centimetre items expecting a failure and measured **success** — cm² to m² is a legal conversion. To hit this guard
    /// the area dimensions themselves must differ (m² versus s²). Recorded here so nobody repeats the same misjudgement.
    /// </remarks>
    /// <remarks>
    /// **实测结论比预期更尖锐**：量纲不匹配时**抛异常的是 `UsedArea()` 而不是 `Utilization()`**——`UsedArea` 用板材面积
    /// 的零值作为累加起点，加第一件就撞上 `L^2` 与 `T^2` 不符。因此 `Utilization()` 里那道 `ConvertTo` 守卫**实际不可达**，
    /// 它只是防御性代码。本测试断言可观察行为（抛出并带清晰消息），而不是假装守卫会返回失败结果。
    ///
    /// **The measurement is sharper than the expectation**: on a dimension mismatch the exception comes from `UsedArea()`,
    /// not `Utilization()` — `UsedArea` seeds its accumulator with the sheet area's zero and the very first addition hits
    /// `L^2` against `T^2`. The `ConvertTo` guard inside `Utilization()` is therefore **unreachable in practice**, pure
    /// defensive code. This test asserts the observable behaviour (a throw with a clear message) rather than pretending the
    /// guard returns a failure.
    /// </remarks>
    [Fact]
    public void Scene_ShouldThrowOnDimensionMismatch() {
        var sheet = new Sheet2<FltX>("s", Meters(10), Meters(10));
        var placement = new PlannedRectangle2<FltX>(
            new RectangleItem2<FltX>("a", Seconds(2), Seconds(2)),
            Seconds(0), Seconds(0));
        var scene = new PackingScene2<FltX>(sheet, new[] { placement });

        Action act = () => scene.Utilization();
        act.Should().Throw<ArgumentException>().WithMessage("*Dimension mismatch*");
    }

    /// <summary>不同但可换算的单位仍能算出利用率 / Different yet convertible units still yield a ratio.</summary>
    [Fact]
    public void Scene_ShouldConvertCompatibleUnits() {
        var sheet = new Sheet2<FltX>("s", Meters(10), Meters(10));
        var placement = new PlannedRectangle2<FltX>(
            new RectangleItem2<FltX>("a", Centimeters(100), Centimeters(100)),
            Centimeters(0), Centimeters(0));
        var scene = new PackingScene2<FltX>(sheet, new[] { placement });

        // 100cm × 100cm = 1m²，板材 100m² → 0.01。
        // 100cm by 100cm is 1m², the sheet is 100m², so 0.01.
        scene.Utilization().Value.ToFlt64().ToDouble().Should().BeApproximately(0.01, 1e-9);
    }

    /// <summary>重叠的件对按编号报出 / Overlapping pairs are reported by identifier.</summary>
    [Fact]
    public void Scene_ShouldReportOverlappedPairs() =>
        Scene(10, 10, Planned("a", 4, 4, 0, 0), Planned("b", 4, 4, 2, 2))
            .OverlappedPairs().Should().ContainSingle().Which.Should().Be(("a", "b"));

    /// <summary>紧邻但不重叠的件对不上报 / Adjacent but non-overlapping pairs are not reported.</summary>
    [Fact]
    public void Scene_ShouldNotReportAdjacentPairs() =>
        Scene(10, 10, Planned("a", 4, 4, 0, 0), Planned("b", 4, 4, 4, 0))
            .OverlappedPairs().Should().BeEmpty();

    /// <summary>非法重叠与重叠同义 / Illegal overlaps equal overlaps under the rectangle vocabulary.</summary>
    [Fact]
    public void Scene_ShouldMatchIllegalOverlaps() =>
        Scene(10, 10, Planned("a", 4, 4, 0, 0), Planned("b", 4, 4, 2, 2))
            .IllegalOverlaps().Should().HaveCount(1);

    /// <summary>投影映射到矩形几何 / A projection maps to rectangle geometry.</summary>
    [Fact]
    public void Mapping_ShouldConvertProjection() {
        IQuantityProjection2<FltX> shape =
            new Projection2Need<FltX>(Meters(3), Meters(4)).ToGeometryProjection2();

        shape.Should().BeOfType<QuantityRectangle2<FltX>>();
    }

    /// <summary>盒体映射到几何盒体并保留位置与尺寸 / A box maps to geometry preserving position and size.</summary>
    [Fact]
    public void Mapping_ShouldConvertBox() {
        QuantityBox2<FltX> box = Box(2, 5, 5, 9).ToGeometryBox2();

        box.X.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
        box.Y.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-9);
    }

    /// <summary>放置映射到几何放置并保留原点 / A placement maps to geometry keeping the origin.</summary>
    [Fact]
    public void Mapping_ShouldConvertPlacement() {
        Placement2Need<FltX> placement =
            new(Meters(2), Meters(5), new Projection2Need<FltX>(Meters(3), Meters(4)));
        QuantityPlacement2<FltX> geometry = placement.ToGeometryPlacement2();

        geometry.X.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
        geometry.Shape.Should().BeOfType<QuantityRectangle2<FltX>>();
    }

    /// <summary>构造盒体 / Build a box.</summary>
    private static Box2Need<FltX> Box(double minX, double minY, double maxX, double maxY) =>
        new(Meters(minX), Meters(minY), Meters(maxX), Meters(maxY));

    /// <summary>构造场景 / Build a scene.</summary>
    private static PackingScene2<FltX> Scene(double w, double h, params PlannedRectangle2<FltX>[] placements) =>
        new(new Sheet2<FltX>("s", Meters(w), Meters(h)), placements);

    /// <summary>构造已规划矩形 / Build a planned rectangle.</summary>
    private static PlannedRectangle2<FltX> Planned(
        string id, double w, double h, double x, double y) =>
        new(new RectangleItem2<FltX>(id, Meters(w), Meters(h)), Meters(x), Meters(y));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>构造以厘米为单位的量 / Build a quantity in centimetres.</summary>
    private static Quantity<FltX> Centimeters(double value) => new(new FltX(value), LengthUnits.Centimeter);

    /// <summary>构造以秒为单位的量（用于量纲不匹配用例）/ Build a quantity in seconds, for the dimension-mismatch case.</summary>
    private static Quantity<FltX> Seconds(double value) => new(new FltX(value), SIBaseUnits.Second);
}
