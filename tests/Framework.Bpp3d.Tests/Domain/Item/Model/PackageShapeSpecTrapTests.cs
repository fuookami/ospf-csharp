// `PackageShape.ShapeSpec` 的陷阱判据 / A criterion pinning the `PackageShape.ShapeSpec` trap.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Application;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// **`PackageShape.ShapeSpec` 只写不读——设置它不会产生任何效果** / `PackageShape.ShapeSpec` is written but never read, so setting it has no effect.
/// </summary>
/// <remarks>
/// **这里推翻了 一直沿用的一条记录**：计划文档里长期写着"`PackageShapeSpec` 仍是死代码"。
/// 查证后发现**说反了一半**：
///
/// | 项 | 实际情况 |
/// | --- | --- |
/// | `PackageShapeSpec` 类型 | 存在（`Cuboid` / `VerticalCylinder`） |
/// | `PackageShape.ShapeSpec` 字段 | **存在，且零读取方**（`grep '\.ShapeSpec' src` 零命中） |
/// | **连续圆柱半径能力** | **已实现**，但走 **`CylinderPackingShape3`**（`RadiusWeightFunctionKey` 门控） |
///
/// **所以这不是"功能没做"，而是"同一个概念被建了两套类型，只有一套接线"。**
/// 这正是反复记录的那个缺陷形态（"同一份投影写了两遍，只有一份被接上"）。
///
/// **危险之处**：调用方写 `new PackageShape(..., ShapeSpec: VerticalCylinder(...))`
/// 时**以为自己启用了圆柱半径求解**，实际**什么都不会发生**——能力静默失效。
///
/// 这条判据钉住**现状**：把 `ShapeSpec` 设成竖直圆柱，与不设，对原型提取的结果**毫无差别**。
/// 若将来有人把 `ShapeSpec` 接上，判据会红，提醒同步更新文档与迁移说明。
///
/// ** overturns a record carried from to **: the plan long said "`PackageShapeSpec` is still dead code", but the truth is **half the opposite**
/// (table above). **So this is not "the feature was never built" but "one concept was modelled twice with only one copy wired"** — exactly the defect shape this
/// session keeps recording. **The danger**: a caller writing `new PackageShape(..., ShapeSpec: VerticalCylinder(...))` **believes cylinder radius solving is enabled**
/// while **nothing happens** — the capability fails silently. The criterion pins the **status quo**: setting `ShapeSpec` to a vertical cylinder changes prototype
/// extraction **not at all**. If someone wires `ShapeSpec` later, it goes red and forces a doc and migration-note update.
/// </remarks>
public class PackageShapeSpecTrapTests {
    /// <summary>**带竖直圆柱规格时，体积仍按长方体计算** / A vertical-cylinder spec still yields cuboid volume.</summary>
    /// <remarks>
    /// 这是**可直接观测的静默失效**：调用方给出圆柱半径 `5`、高 `10`，
    /// 圆柱体积应为 `π·5²·10 ≈ 785`，而 `PackageShape.Volume` 返回 `W·H·D` 的长方体体积。
    ///
    /// | 规格 | 期望体积 |
    /// | --- | --- |
    /// | `Cuboid` | `W·H·D`（正确） |
    /// | `VerticalCylinder(radius 5)` | 仍为 `W·H·D`，**半径被完全忽略** |
    ///
    /// **判据钉的是"半径不参与体积"这个事实本身**，而不是"算错了"——
    /// 因为 C# 的 `PackageShape` 是**另一套模型**，真实管线走 `IPackingShape3`。
    /// 若将来把 `PackageShape` 接上并让体积尊重圆柱规格，判据会红，提醒更新文档。
    ///
    /// **This is directly observable silent failure**: with radius `5` and height `10` the cylinder volume should be `π·5²·10 ≈ 785`, yet `PackageShape.Volume` returns
    /// the cuboid `W·H·D` (table above). **The criterion pins the fact "radius plays no part in volume"** rather than "the maths is wrong", because C#'s `PackageShape`
    /// is **a separate model** and the real pipeline uses `IPackingShape3`. If `PackageShape` is ever wired up and volume starts respecting the cylinder, it goes red and
    /// forces a doc update.
    /// </remarks>
    [Fact]
    public void Volume_ShouldIgnoreTheVerticalCylinderRadius() {
        PackageShape cuboid = MakeShape(spec: null);
        PackageShape cylinder = MakeShape(spec: new PackageShapeSpec.VerticalCylinder(
            Radius: new Quantity<FltX>(new FltX(5.0), LengthUnits.Millimeter)));

        FltX cuboidVolume = cuboid.Volume.Value;
        FltX cylinderVolume = cylinder.Volume.Value;

        cuboidVolume.Should().Be(
            new FltX(6000.0),
            "10 × 20 × 30; the three dimensions are deliberately unequal so that any transposition changes the product");
        cylinderVolume.Should().Be(
            cuboidVolume,
            "the radius of 5 would give π·5²·10 ≈ 785, but the spec is never read, so the cuboid volume comes back unchanged");
    }

    /// <summary>**底面面积也不看 `ShapeSpec`** / The bottom area ignores `ShapeSpec` too.</summary>
    /// <remarks>
    /// 体积之外的第二条派生量。圆柱的底面积应是 `π·r²`，而这里返回矩形面积 `宽·深`——
    /// **同一条"规格不参与几何"的结论，多一个观测点**。
    ///
    /// A second derived quantity besides volume. A cylinder's base area would be `π·r²`, yet this returns the rectangle `width·depth` — **the same "the spec plays no part in
    /// the geometry" conclusion, observed at one more point**.
    /// </remarks>
    [Fact]
    public void BottomArea_ShouldIgnoreTheVerticalCylinderRadius() {
        PackageShape cylinder = MakeShape(spec: new PackageShapeSpec.VerticalCylinder(
            Radius: new Quantity<FltX>(new FltX(5.0), LengthUnits.Millimeter)));

        cylinder.BottomShape.Area.Value.Should().Be(
            new FltX(300.0),
            "10 × 30; a cylinder of radius 5 would give π·25 ≈ 78.5, proving the spec is not consulted");
    }

    /// <summary>**`ShapeSpec` 的值被原样保留**——反向对照 / The `ShapeSpec` value round-trips — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"`ShapeSpec` 被悄悄丢弃"的实现也能满足上一条。
    /// 判据钉住：**存进去了，只是没人读**——这才叫"只写不读"，而不是"根本没存"。
    ///
    /// Without this, an implementation that **silently drops** `ShapeSpec` would also satisfy the previous criterion. This pins that it **is** stored and merely never
    /// read — which is what "write-only" means, as opposed to "not stored at all".
    /// </remarks>
    [Fact]
    public void ShapeSpec_ShouldStillRoundTripOnTheShape() {
        var spec = new PackageShapeSpec.VerticalCylinder(
            Radius: new Quantity<FltX>(new FltX(5.0), LengthUnits.Millimeter));
        PackageShape shape = MakeShape(spec);

        shape.ShapeSpec.Should().BeSameAs(spec, "the spec is stored faithfully; nothing reads it, but it is not discarded");
    }

    /// <summary>构造包装形状 / Build a package shape.</summary>
    /// <remarks>
    /// **三个维度刻意不等（10 / 20 / 30）。** 第一版用的是 `10 / 10 / 10`，
    /// 于是"宽换成深"的变异（`D·H·D`）**存活**——对称夹具让任何换位都算不出差别。
    /// 这是"夹具太简单"的第六种变体：**不是元素太少，而是元素恰好相等**。
    ///
    /// **The three dimensions are deliberately unequal (10 / 20 / 30).** The first version used `10 / 10 / 10`, so the "swap width for depth" mutant (`D·H·D`) **survived** —
    /// a symmetric fixture hides every transposition. That is the sixth variant of "fixture too simple": **not too few elements, but elements that happen to be equal**.
    /// </remarks>
    /// <param name="spec">包装形状规格 / Package shape specification.</param>
    /// <returns>包装形状 / The package shape.</returns>
    private static PackageShape MakeShape(PackageShapeSpec? spec) => new(
        Width: new Quantity<FltX>(new FltX(10.0), LengthUnits.Millimeter),
        Height: new Quantity<FltX>(new FltX(20.0), LengthUnits.Millimeter),
        Depth: new Quantity<FltX>(new FltX(30.0), LengthUnits.Millimeter),
        Weight: new Quantity<FltX>(new FltX(1.0), MassUnits.Gram),
        PackageType: PackageType.Box,
        ShapeSpec: spec);
}
