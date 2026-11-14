// 渲染适配器装载计划测试 / Tests for the renderer adapter's loading plans.

using System;
using System.Collections.Generic;
using System.Linq;
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
/// 渲染适配器的装载计划产出 / The renderer adapter's loading plans.
/// </summary>
public sealed class PackingRendererPlanTests {
    private static Quantity<FltX> M(double value) =>
        new(new FltX(value), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);

    private static PackingBin Bin(IReadOnlyList<PackingItem> items, IReadOnlyList<string>? group = null) =>
        new(items) {
            Name = "B1",
            Type = new BinType<FltX>("B1", M(10), M(10), M(10), M(1000)),
            Group = group,
        };

    private static PackingItem Cuboid(string name, ulong order, double x, double y, double z, double size) => new(
        new InfraItem(name, name),
        Amount: 1,
        Geometry: new PackingItemGeometry(
            PackingShapeType.Cuboid, Axis: null, Radius: 0.0,
            MinX: x, MinY: y, MinZ: z,
            MaxX: x + size, MaxY: y + size, MaxZ: z + size),
        LoadingOrder: order);

    private static PackingItem Cylinder(string name, ulong order) => new(
        new InfraItem(name, name),
        Amount: 1,
        Geometry: new PackingItemGeometry(
            PackingShapeType.Cylinder, Axis3.X, Radius: 1.0,
            MinX: 0.0, MinY: 0.0, MinZ: 0.0,
            MaxX: 4.0, MaxY: 2.0, MaxZ: 2.0),
        LoadingOrder: order);

    private static PackingResult Result(params PackingBin[] bins) =>
        new(new PackingAggregationNonGeneric(bins), new List<MaterialSummaryEntry>());

    private static SchemaDTO Unwrap(Result<SchemaDTO, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<SchemaDTO, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("渲染必须成功 / the render must succeed");

    /// <summary>**每个箱子产出一条装载计划，带物品位置** / Every bin yields a loading plan carrying item positions.</summary>
    /// <remarks>
    /// 修复前 `ToSchema` **一条计划都不产出**，只有两个 KPI。**"渲染成功"在两种实现下都成立**，
    /// 所以只有检查计划条数、箱体尺寸与物品坐标才分得出来。
    ///
    /// Before the fix `ToSchema` **produced no plan at all**, only two KPIs. **"The render succeeded" holds under both implementations**, so only
    /// checking the plan count, the container extents and the item coordinates can tell them apart.
    /// </remarks>
    [Fact]
    public void ToSchema_ShouldEmitOnePlanPerBinWithPositions() {
        SchemaDTO schema = Unwrap(new PackingRendererAdapter().ToSchema(
            Result(Bin(new[] { Cuboid("I1", 0, 1, 2, 3, 2) }))));

        schema.LoadingPlans.Should().NotBeNull();
        schema.LoadingPlans!.Should().HaveCount(1);
        RenderLoadingPlanDto plan = schema.LoadingPlans![0];
        plan.Name.Should().Be("B1");
        plan.TypeCode.Should().Be("B1");
        plan.Width.Should().Be(10.0);
        plan.Items.Should().HaveCount(1);
        plan.Items[0].Name.Should().Be("I1");
        plan.Items[0].X.Should().Be(1.0);
        plan.Items[0].Y.Should().Be(2.0);
        plan.Items[0].Z.Should().Be(3.0);
        plan.Items[0].Width.Should().Be(2.0);
        plan.Items[0].ActualVolume.Should().Be(8.0);
    }

    /// <summary>**物品按装载顺序输出，而不是按输入顺序** / Items are emitted in loading order, not input order.</summary>
    /// <remarks>
    /// 输入顺序与装载顺序**在夹具里必须故意相反**：两者一致时，"排序"和"不排序"给出完全相同的输出。
    ///
    /// The input order and the loading order **have to be deliberately reversed in the fixture**: when they agree, "sorted" and "not sorted" produce
    /// byte-identical output.
    /// </remarks>
    [Fact]
    public void ToSchema_ShouldEmitItemsInLoadingOrder() {
        SchemaDTO schema = Unwrap(new PackingRendererAdapter().ToSchema(
            Result(Bin(new[] {
                // 名字的字典序与装载顺序**故意相反**：否则"按名字排"和"按顺序排"输出相同。
                // The alphabetical order of the names is **deliberately the reverse** of the loading order; otherwise "sort by name" and "sort by order"
                // produce the same output.
                Cuboid("alpha", 7, 5, 0, 0, 1),
                Cuboid("zeta", 2, 0, 0, 0, 1),
            }))));

        schema.LoadingPlans![0].Items.Select(item => item.Name)
            .Should().Equal("zeta", "alpha");
        schema.LoadingPlans[0].Items.Select(item => item.LoadingOrder)
            .Should().Equal(2, 7);
    }

    /// <summary>**圆柱带半径、直径与算法形状类型** / A cylinder carries radius, diameter and the algorithm shape type.</summary>
    [Fact]
    public void ToSchema_ShouldCarryCylinderGeometry() {
        SchemaDTO schema = Unwrap(new PackingRendererAdapter().ToSchema(
            Result(Bin(new[] { Cylinder("C1", 0) }))));

        RenderLoadingPlanItemDto item = schema.LoadingPlans![0].Items[0];
        item.RenderShapeType.Should().Be(RenderShapeType.Cylinder);
        item.AlgorithmShapeType.Should().Be(RenderAlgorithmShapeType.HorizontalCylinderX);
        item.ShapeType.Should().Be("cylinder");
        item.Radius.Should().Be(1.0);
        item.Diameter.Should().Be(2.0);
        item.Axis.Should().Be(Axis3.X);
        item.ActualVolume.Should().BeApproximately(global::System.Math.PI * 1.0 * 1.0 * 4.0, 1e-9);
    }

    /// <summary>**长方体不带半径，算法形状类型为长方体** / A cuboid carries no radius and maps to the cuboid algorithm shape.</summary>
    [Fact]
    public void ToSchema_ShouldNotMarkCuboidsAsCylinders() {
        SchemaDTO schema = Unwrap(new PackingRendererAdapter().ToSchema(
            Result(Bin(new[] { Cuboid("I1", 0, 0, 0, 0, 2) }))));

        RenderLoadingPlanItemDto item = schema.LoadingPlans![0].Items[0];
        item.RenderShapeType.Should().Be(RenderShapeType.Cuboid);
        item.AlgorithmShapeType.Should().Be(RenderAlgorithmShapeType.Cuboid);
        item.Radius.Should().BeNull();
        item.Axis.Should().BeNull();
        item.Diameter.Should().BeNull();
    }

    /// <summary>**三个方向的尺寸各自独立传出** / The three extents travel independently.</summary>
    /// <remarks>
    /// 立方体夹具（宽=高=深）**分不出"宽取宽"和"宽取深"**——九个变异里最后活下来的就是这一个。用 2×3×4 的长方体才行。
    ///
    /// A cube fixture (width = height = depth) **cannot tell "width from width" apart from "width from depth"** — that was the last surviving mutation
    /// of the nine. A 2×3×4 cuboid can.
    /// </remarks>
    [Fact]
    public void ToSchema_ShouldCarryEachExtentSeparately() {
        var item = new PackingItem(
            new InfraItem("I1", "I1"),
            Amount: 1,
            Geometry: new PackingItemGeometry(
                PackingShapeType.Cuboid, Axis: null, Radius: 0.0,
                MinX: 0.0, MinY: 0.0, MinZ: 0.0,
                MaxX: 2.0, MaxY: 3.0, MaxZ: 4.0));

        SchemaDTO schema = Unwrap(new PackingRendererAdapter().ToSchema(Result(Bin(new[] { item }))));

        RenderLoadingPlanItemDto dto = schema.LoadingPlans![0].Items[0];
        dto.Width.Should().Be(2.0);
        dto.Height.Should().Be(3.0);
        dto.Depth.Should().Be(4.0);
        dto.ActualVolume.Should().Be(24.0);
    }

    /// <summary>**逐件重量来自货物，箱级重量是它们的和** / Item weights come from the items; the bin weight is their sum.</summary>
    /// <remarks>
    /// 两件重量不相等的货物才分得清"求和"与"取第一件""取件数"：
    /// **夹具里必须避免两件重量相同**，否则三种写法输出一样。
    ///
    /// Two items of unequal weight are needed to separate "sum" from "first item" and "item count": **the fixture must not use equal weights**, or all
    /// three implementations produce the same output.
    /// </remarks>
    [Fact]
    public void ToSchema_ShouldSumItemWeightsPerBin() {
        static Quantity<FltX> Kg(double value) =>
            new(new FltX(value), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Kilogram);

        var heavy = new PackingItem(
            new InfraItem("I1", "I1", Shape: null, Weight: Kg(2.5)),
            Amount: 1,
            Geometry: new PackingItemGeometry(
                PackingShapeType.Cuboid, Axis: null, Radius: 0.0,
                MinX: 0.0, MinY: 0.0, MinZ: 0.0, MaxX: 1.0, MaxY: 1.0, MaxZ: 1.0));
        var light = new PackingItem(
            new InfraItem("I2", "I2", Shape: null, Weight: Kg(0.5)),
            Amount: 1,
            Geometry: new PackingItemGeometry(
                PackingShapeType.Cuboid, Axis: null, Radius: 0.0,
                MinX: 2.0, MinY: 0.0, MinZ: 0.0, MaxX: 3.0, MaxY: 1.0, MaxZ: 1.0),
            LoadingOrder: 1);

        SchemaDTO schema = Unwrap(new PackingRendererAdapter().ToSchema(Result(Bin(new[] { heavy, light }))));

        RenderLoadingPlanDto plan = schema.LoadingPlans![0];
        plan.Items[0].Weight.Should().Be(2.5);
        plan.Items[1].Weight.Should().Be(0.5);
        plan.Weight.Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>**没有重量信息的货物报 0，而不是崩溃** / An item without weight reports 0 instead of crashing.</summary>
    [Fact]
    public void ToSchema_ShouldReportZeroWeightWhenAbsent() {
        SchemaDTO schema = Unwrap(new PackingRendererAdapter().ToSchema(
            Result(Bin(new[] { Cuboid("I1", 0, 0, 0, 0, 1) }))));

        schema.LoadingPlans![0].Items[0].Weight.Should().Be(0.0);
        schema.LoadingPlans[0].Weight.Should().Be(0.0);
    }

    /// <summary>**装载率按体积比计算** / The loading rate is the volume ratio.</summary>
    /// <remarks>
    /// 容器 10×10×10 = 1000，物品 2×2×2 = 8 → 0.008。**只断言"非零"**会放过把填充率算成别的东西的实现。
    ///
    /// Container 10×10×10 = 1000, item 2×2×2 = 8 → 0.008. **Asserting only "non-zero"** would let an implementation that computes something else pass.
    /// </remarks>
    [Fact]
    public void ToSchema_ShouldComputeLoadingRate() {
        SchemaDTO schema = Unwrap(new PackingRendererAdapter().ToSchema(
            Result(Bin(new[] { Cuboid("I1", 0, 0, 0, 0, 2) }))));

        RenderLoadingPlanDto plan = schema.LoadingPlans![0];
        plan.Volume.Should().Be(8.0);
        plan.LoadingRate.Should().BeApproximately(0.008, 1e-12);
    }

    /// <summary>**几何重叠的箱子渲染失败，而不是画出来** / A bin with overlapping geometry fails to render rather than being drawn.</summary>
    [Fact]
    public void ToSchema_ShouldRejectOverlappingGeometry() {
        Result<SchemaDTO, ErrorCode, Error<ErrorCode>> result = new PackingRendererAdapter().ToSchema(
            Result(Bin(new[] {
                Cuboid("I1", 0, 0, 0, 0, 2),
                Cuboid("I2", 1, 1, 1, 1, 2),
            })));

        result.IsOk.Should().BeFalse();
        Failed<SchemaDTO, ErrorCode, Error<ErrorCode>> failed =
            result.Should().BeOfType<Failed<SchemaDTO, ErrorCode, Error<ErrorCode>>>().Subject;
        failed.Message.Should().Contain("type=overlap");
    }

    /// <summary>**空箱子仍然产出一条空计划** / An empty bin still yields one empty plan.</summary>
    [Fact]
    public void ToSchema_ShouldEmitAnEmptyPlanForAnEmptyBin() {
        SchemaDTO schema = Unwrap(new PackingRendererAdapter().ToSchema(
            Result(Bin(Array.Empty<PackingItem>()))));

        schema.LoadingPlans!.Should().HaveCount(1);
        schema.LoadingPlans![0].Items.Should().BeEmpty();
        schema.LoadingPlans[0].LoadingRate.Should().Be(0.0);
    }

    /// <summary>**分组信息按管道符拼接** / The group label is joined with a pipe.</summary>
    [Fact]
    public void ToSchema_ShouldJoinTheGroupLabel() {
        SchemaDTO schema = Unwrap(new PackingRendererAdapter().ToSchema(
            Result(Bin(new[] { Cuboid("I1", 0, 0, 0, 0, 1) }, new[] { "A", "B" }))));

        schema.LoadingPlans![0].Group.Should().Be("A|B");
    }
}
