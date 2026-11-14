// 求解器半径回写测试 / Tests for the solver-selected radius writeback.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

using InfraItem = Fuookami.Ospf.Framework.Bpp3d.Infra.Item;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain.Packing;

/// <summary>
/// 求解器选出的圆柱半径回写 / Writing back the solver-selected cylinder radius.
/// </summary>
/// <remarks>
/// 连续半径求解器把半径作为**变量**交给 MILP，选中的值只存在于解向量里。不回写进渲染 DTO，
/// 渲染出的圆柱就仍是**标称半径**，与实际装箱的体积、装载率都不一致——而两者在类型上完全相同。
///
/// The continuous-radius solver hands the radius to the MILP **as a variable**, so the selected value only exists in the solution vector. Without
/// writing it back into the rendering DTO, the rendered cylinder keeps its **nominal radius** and disagrees with the packed volume and loading rate —
/// while both look identical in the types.
/// </remarks>
public sealed class PackingRadiusWritebackTests {
    private static Quantity<FltX> M(double value) => new(new FltX(value), SIBaseUnits.Meter);

    private static PackingBin CylinderBin(string itemId, double nominalRadius) {
        var geometry = new PackingItemGeometry(
            PackingShapeType.Cylinder, Axis3.X, nominalRadius,
            MinX: 0.0, MinY: 0.0, MinZ: 0.0,
            MaxX: 4.0, MaxY: nominalRadius * 2, MaxZ: nominalRadius * 2);
        return new PackingBin(new[] {
            new PackingItem(new InfraItem(itemId, itemId), Amount: 1, Geometry: geometry, LoadingOrder: 1),
        }) {
            Name = "B1",
            Type = new BinType<FltX>("B1", M(10), M(10), M(10), M(1000)),
        };
    }

    private static PackingResult Result(PackingBin bin) =>
        new(new PackingAggregationNonGeneric(new[] { bin }), new List<MaterialSummaryEntry>());

    /// <summary>**求解器选出的半径覆盖标称半径** / A solver-selected radius overrides the nominal one.</summary>
    [Fact]
    public void ToSchema_ShouldWriteBackTheSelectedRadius() {
        var adapter = new PackingRendererAdapter();
        var selections = new Dictionary<string, double> { ["cr_item-C1"] = 0.5 };

        RenderLoadingPlanItemDto item = adapter
            .ToSchema(Result(CylinderBin("C1", 1.0)), selections)
            .Value.LoadingPlans![0].Items[0];

        item.Radius.Should().Be(0.5);
        item.Diameter.Should().Be(1.0);
    }

    /// <summary>**体积跟着回写后的半径走** / The volume follows the written-back radius.</summary>
    /// <remarks>
    /// 只断言 `Radius` 会放过一个"半径回写了、体积却仍按标称半径算"的实现——那正是装载率算错的那种 bug。
    /// 夹具取轴 X、高 4、半径 0.5：体积应为 π × 0.5² × 4 = π，标称半径 1.0 会给出 4π，两者相差 4 倍。
    ///
    /// Asserting only `Radius` would let an implementation that **writes the radius back but still computes the volume from the nominal one** pass —
    /// exactly the kind of bug that makes the loading rate wrong. The fixture is axis X, height 4, radius 0.5: the volume must be π × 0.5² × 4 = π, while
    /// the nominal radius 1.0 gives 4π — a factor of four apart.
    /// </remarks>
    [Fact]
    public void ToSchema_ShouldComputeVolumeFromTheSelectedRadius() {
        var adapter = new PackingRendererAdapter();
        var selections = new Dictionary<string, double> { ["cr_item-C1"] = 0.5 };

        RenderLoadingPlanItemDto item = adapter
            .ToSchema(Result(CylinderBin("C1", 1.0)), selections)
            .Value.LoadingPlans![0].Items[0];

        item.ActualVolume!.Value.Should().BeApproximately(global::System.Math.PI, 1e-9);
    }

    /// <summary>**没有回写时退回标称半径** / Without a writeback the nominal radius is used.</summary>
    [Fact]
    public void ToSchema_ShouldFallBackToTheNominalRadius() {
        var adapter = new PackingRendererAdapter();

        RenderLoadingPlanItemDto item = adapter.ToSchema(Result(CylinderBin("C1", 1.0))).Value.LoadingPlans![0].Items[0];

        item.Radius.Should().Be(1.0);
        item.ActualVolume!.Value.Should().BeApproximately(global::System.Math.PI * 4.0, 1e-9);
    }

    /// <summary>**货物编号也可作为键** / The item id works as a key too.</summary>
    [Fact]
    public void ToSchema_ShouldAcceptTheItemIdAsAKey() {
        var adapter = new PackingRendererAdapter();
        var selections = new Dictionary<string, double> { ["C1"] = 0.5 };

        RenderLoadingPlanItemDto item = adapter
            .ToSchema(Result(CylinderBin("C1", 1.0)), selections)
            .Value.LoadingPlans![0].Items[0];

        item.Radius.Should().Be(0.5);
    }

    /// <summary>**变量名优先于货物编号** / The variable name wins over the item id.</summary>
    /// <remarks>
    /// 两个键同时存在时，若实现取错一个，半径就悄悄变成另一个数。夹具刻意让两个键指向**不同的半径**。
    ///
    /// When both keys exist, taking the wrong one silently yields a different radius. The fixture deliberately points the two keys at **different radii**.
    /// </remarks>
    [Fact]
    public void ToSchema_ShouldPreferTheVariableName() {
        var adapter = new PackingRendererAdapter();
        var selections = new Dictionary<string, double> {
            ["cr_item-C1"] = 0.5,
            ["C1"] = 0.9,
        };

        RenderLoadingPlanItemDto item = adapter
            .ToSchema(Result(CylinderBin("C1", 1.0)), selections)
            .Value.LoadingPlans![0].Items[0];

        item.Radius.Should().Be(0.5);
    }

    /// <summary>**回写表里没有这个货物时退回标称半径** / An absent key falls back to the nominal radius.</summary>
    /// <remarks>
    /// "整张表为空"和"表里有别的货物但没有这一个"是两条不同的路径：前者在入口就返回，后者要走到查表失败。
    /// 只测前者会放过一个**查不到就编一个 0**的实现。
    ///
    /// "The table is empty" and "the table has other items but not this one" are different paths: the former returns at the entry, the latter has to
    /// reach a failed lookup. Testing only the former would let an implementation that **invents a 0** when the key is missing pass.
    /// </remarks>
    [Fact]
    public void ToSchema_ShouldFallBackWhenTheKeyIsAbsent() {
        var adapter = new PackingRendererAdapter();
        var selections = new Dictionary<string, double> { ["cr_item-OTHER"] = 0.5 };

        RenderLoadingPlanItemDto item = adapter
            .ToSchema(Result(CylinderBin("C1", 1.0)), selections)
            .Value.LoadingPlans![0].Items[0];

        item.Radius.Should().Be(1.0);
    }

    /// <summary>**长方体不受回写影响** / A cuboid is unaffected by the writeback.</summary>
    [Fact]
    public void ToSchema_ShouldNotTouchCuboids() {
        var geometry = new PackingItemGeometry(
            PackingShapeType.Cuboid, Axis: null, Radius: 0.0,
            MinX: 0.0, MinY: 0.0, MinZ: 0.0, MaxX: 2.0, MaxY: 2.0, MaxZ: 2.0);
        var bin = new PackingBin(new[] {
            new PackingItem(new InfraItem("C1", "C1"), Amount: 1, Geometry: geometry, LoadingOrder: 1),
        }) {
            Name = "B1",
            Type = new BinType<FltX>("B1", M(10), M(10), M(10), M(1000)),
        };
        var selections = new Dictionary<string, double> { ["cr_item-C1"] = 0.5 };

        RenderLoadingPlanItemDto item = new PackingRendererAdapter()
            .ToSchema(Result(bin), selections)
            .Value.LoadingPlans![0].Items[0];

        item.Radius.Should().BeNull();
        item.ActualVolume!.Value.Should().BeApproximately(8.0, 1e-9);
    }
}
