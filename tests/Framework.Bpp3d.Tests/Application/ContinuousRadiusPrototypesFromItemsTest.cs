#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Framework.Bpp3d.Application;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Application;

/// <summary>
/// 连续半径原型抽取必须**真的检查货物形状**，而不是恒定返回空表。
/// Continuous-radius prototype extraction must **actually inspect item shapes** rather than always returning an empty list.
/// </summary>
/// <remarks>
/// 之前的方法是：
///
/// ```csharp
/// var prototypes = new Dictionary...;
/// // Stub: in full implementation, would inspect item shapes
/// return prototypes.Values.ToList;
/// ```
///
/// **它建了一个字典，从不填充，然后返回它。** 而 `ColumnGenerationAlgorithm` 在真实求解时**每一轮都调用它**
/// 并把结果放进 `ColumnGenerationState`——所以生产中列生成**永远拿到零个原型**，
/// 连续半径这一整族列**从来没有被生成过**。
///
/// 既有唯一测试是 `PrototypesFromItemsShouldReturnEmptyForEmptyList`，**空壳自然满足它**。
///
/// The method used to build a dictionary, never fill it and return it. And `ColumnGenerationAlgorithm` **calls it every iteration** during a real solve,
/// putting the result into `ColumnGenerationState` — so in production column generation **always received zero prototypes** and the whole continuous-radius
/// column family **was never generated**.
///
/// The only existing test asserted empty output for empty input, **which the shell satisfied trivially**.
/// </remarks>
public class ContinuousRadiusPrototypesFromItemsTest {
    private static Quantity<FltX> M(double value) => new(new FltX(value), SIBaseUnits.Meter);

    private static Item CylinderItem(string id, Axis3 axis, string? key = "radius-weight") =>
        new(
            id,
            id,
            new CylinderPackingShape3(
                Cylinder: new QuantityCylinder3<FltX>(M(0.5), M(1.0), axis),
                RadiusWeightFunctionKey: key)
        );

    private static Item CuboidItem(string id) =>
        new(
            id,
            id,
            new CuboidPackingShape3<FltX>(new QuantityCuboid3<FltX>(M(1.0), M(1.0), M(1.0)))
        );

    /// <summary>**竖直圆柱必须产出原型** / A vertical cylinder must yield a prototype.</summary>
    /// <remarks>
    /// **这是整批的核心判据**：空壳对任何输入都返回空表，这条会立刻失败。
    ///
    /// **This is the batch's core criterion**: the shell returned an empty list for every input, so this fails immediately on it.
    /// </remarks>
    [Fact]
    public void VerticalCylinder_ShouldYieldAPrototype() {
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes =
            ContinuousRadiusSolverHelpers.PrototypesFromItems(new[] { CylinderItem("c1", Axis3.Y) });

        prototypes.Should().ContainSingle();
        prototypes[0].Axis.Should().Be(Axis3.Y);
        prototypes[0].VariableName.Should().NotBeNullOrWhiteSpace();
        prototypes[0].Source.Should().Contain("c1");
    }

    /// <summary>**长方体不得产出原型** / A cuboid must not yield a prototype.</summary>
    [Fact]
    public void Cuboid_ShouldYieldNoPrototype() {
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes =
            ContinuousRadiusSolverHelpers.PrototypesFromItems(new[] { CuboidItem("b1") });

        prototypes.Should().BeEmpty();
    }

    /// <summary>**横置圆柱不得产出原型** / A horizontal cylinder must not yield a prototype.</summary>
    /// <remarks>
    /// 基线只取 `PackageShapeSpec.VerticalCylinder`。**若实现只判"是不是圆柱"而不判轴向，
    /// 横置圆柱会混进来**——而原型数量仍然"看起来合理"。
    ///
    /// The baseline takes only `PackageShapeSpec.VerticalCylinder`. **An implementation that only asks "is it a cylinder" without checking the axis lets
    /// horizontal ones through** while the prototype count still looks plausible.
    /// </remarks>
    [Fact]
    public void HorizontalCylinder_ShouldYieldNoPrototype() {
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes =
            ContinuousRadiusSolverHelpers.PrototypesFromItems(new[] { CylinderItem("h1", Axis3.X) });

        prototypes.Should().BeEmpty();
    }

    /// <summary>**多个竖直圆柱各自产出，且保持输入顺序** / Several vertical cylinders each yield one, in input order.</summary>
    [Fact]
    public void SeveralCylinders_ShouldPreserveInputOrder() {
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes =
            ContinuousRadiusSolverHelpers.PrototypesFromItems(new[] {
                CylinderItem("first", Axis3.Y),
                CuboidItem("skip"),
                CylinderItem("second", Axis3.Y),
            });

        prototypes.Should().HaveCount(2);
        prototypes[0].Source.Should().Contain("first");
        prototypes[1].Source.Should().Contain("second");
    }

    /// <summary>**混合输入只取圆柱** / A mixed input yields only the cylinders.</summary>
    [Fact]
    public void MixedItems_ShouldOnlyYieldCylinders() {
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes =
            ContinuousRadiusSolverHelpers.PrototypesFromItems(new[] {
                CuboidItem("box-a"),
                CylinderItem("tube", Axis3.Y),
                CuboidItem("box-b"),
            });

        prototypes.Should().ContainSingle();
        prototypes[0].Source.Should().Contain("tube");
    }

    /// <summary>**同一货物重复出现必须按变量名去重** / A repeated item must de-duplicate by variable name.</summary>
    /// <remarks>
    /// 基线用 `putIfAbsent`：**先到先得**。若实现改用 `[key] = value`，
    /// 内容相同的重复项会被后一个覆盖——**数量一样，但语义从"保留首个"变成"保留末个"**。
    ///
    /// The baseline uses `putIfAbsent`: **first wins**. An implementation using `[key] = value` lets a later duplicate overwrite an earlier one — **same count,
    /// but "keep the first" silently becomes "keep the last"**.
    /// </remarks>
    [Fact]
    public void DuplicateItem_ShouldBeDeduplicated() {
        Item cylinder = CylinderItem("dup", Axis3.Y);

        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes =
            ContinuousRadiusSolverHelpers.PrototypesFromItems(new[] { cylinder, cylinder });

        prototypes.Should().ContainSingle();
    }

    /// <summary>**没有半径权重函数键的圆柱不产出原型** / A cylinder without a radius weight-function key yields no prototype.</summary>
    /// <remarks>
    /// 这是 恢复的**基线门控**：基线的 `continuousRadiusSelectionResult` 在该键为 null 时返回 null，
    /// 于是无键圆柱**不进入**连续半径路径。
    ///
    /// 因为 C# 形状模型还没有这个键，只能对所有竖直圆柱产出原型，并在文档里记下偏差。
    /// **现在门控回来了，这条测试锁住它。**
    ///
    /// This is the **baseline gate** restored: the baseline's `continuousRadiusSelectionResult` returns null when the key is null, so a keyless
    /// cylinder **stays out** of the continuous-radius path.
    ///
    /// could only produce prototypes for every vertical cylinder because the C# shape model lacked the key, and it recorded that deviation. **The gate is
    /// back now, and this test pins it.**
    /// </remarks>
    [Fact]
    public void CylinderWithoutKey_ShouldYieldNoPrototype() {
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes =
            ContinuousRadiusSolverHelpers.PrototypesFromItems(new[] { CylinderItem("k", Axis3.Y, key: null) });

        prototypes.Should().BeEmpty();
    }

    /// <summary>**带键的原型必须携带键与界，且可注册 PWL** / A keyed prototype must carry its key and bounds and be PWL registerable.</summary>
    /// <remarks>
    /// 若实现只是"有键就给一个原型"但**不把键和界传下去**，下游拿到的原型**无法注册权重函数**——
    /// 而原型数量仍然正确。
    ///
    /// If the implementation merely says "has a key, so emit a prototype" without **passing the key and bounds through**, downstream receives a prototype that
    /// **cannot register a weight function** — while the prototype count is still correct.
    /// </remarks>
    [Fact]
    public void KeyedPrototype_ShouldCarryKeyBoundsAndPwlFlag() {
        var item = new Item(
            "keyed",
            "keyed",
            new CylinderPackingShape3(
                Cylinder: new QuantityCylinder3<FltX>(M(0.5), M(1.0), Axis3.Y),
                RadiusWeightFunctionKey: "w-key",
                RadiusMin: M(0.2),
                RadiusMax: M(0.8)));

        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes =
            ContinuousRadiusSolverHelpers.PrototypesFromItems(new[] { item });

        prototypes.Should().ContainSingle();
        prototypes[0].RadiusWeightFunctionKey.Should().Be("w-key");
        prototypes[0].IsPWLRegisterable.Should().BeTrue();
        prototypes[0].RadiusMin.Should().NotBeNull();
        prototypes[0].RadiusMax.Should().NotBeNull();
    }

    /// <summary>**空输入仍返回空** / Empty input still yields nothing.</summary>
    [Fact]
    public void EmptyInput_ShouldStillYieldNothing() =>
        ContinuousRadiusSolverHelpers.PrototypesFromItems(Array.Empty<Item>()).Should().BeEmpty();
}
