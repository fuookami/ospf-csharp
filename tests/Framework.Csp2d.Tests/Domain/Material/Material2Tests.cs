// 物料/设备/副产物判据 / Criteria for materials, machines and by-products.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Domain.Material;

/// <summary>
/// **物料形态、余料与设备限制** / Material kind, remnants and machine limits.
/// </summary>
/// <remarks>
/// 对应里除几何（`region` / `sheetArea`）与校验（`validate`）之外的部分。
/// **判据不需要求解器**：四条都是纯判断。
///
/// Mirrors the parts of other than geometry (`region` / `sheetArea`) and validation (`validate`). **No criterion needs a solver**: all four are pure
/// predicates.
/// </remarks>
public class Material2Tests {
    /// <summary>**`height` 为空就是卷材** / A null `height` is the coil mode.</summary>
    [Fact]
    public void Kind_ShouldBeDerivedFromTheHeightAlone() {
        Material2<Flt64> coil = Material2Of(height: null);
        Material2<Flt64> sheet = Material2Of(height: Metres(2.0));

        coil.Kind.Should().Be(Material2Kind.Coil);
        coil.IsCoil.Should().BeTrue();
        sheet.Kind.Should().Be(Material2Kind.Sheet);
        sheet.IsCoil.Should().BeFalse();
    }

    /// <summary>**余料可用性：无阈值时要求为正，有阈值时要求不小于阈值** / Remnant usability: strictly positive without a threshold, at least the threshold with one.</summary>
    [Fact]
    public void IsUsableRemnant_ShouldRespectTheThreshold() {
        Material2<Flt64> plain = Material2Of(height: Metres(2.0));

        plain.IsUsableRemnant(SquareMetres(0.5)).Should().BeTrue();
        plain.IsUsableRemnant(SquareMetres(0.0)).Should().BeFalse(
            "a zero-area remnant is scrap, not a remnant");

        Material2<Flt64> limited = Material2Of(height: Metres(2.0), minRemnantArea: SquareMetres(0.5));
        limited.IsUsableRemnant(SquareMetres(0.5)).Should().BeTrue("the threshold is inclusive");
        limited.IsUsableRemnant(SquareMetres(0.49)).Should().BeFalse();
    }

    /// <summary>**单位不可比时不得判为可用** / An incomparable unit must not count as usable.</summary>
    /// <remarks>
    /// 把不可比当成"够大"会**白白留下不可用的余料**，而它在后续排样里又装不进任何东西。
    ///
    /// Treating incomparability as "big enough" **keeps unusable remnants alive**, and they then fit into nothing during nesting.
    /// </remarks>
    [Fact]
    public void IsUsableRemnant_ShouldRejectIncomparableUnits() {
        Material2<Flt64> limited = Material2Of(height: Metres(2.0), minRemnantArea: Kilograms(1.0));

        limited.IsUsableRemnant(SquareMetres(10.0)).Should().BeFalse("area and mass are not comparable");
    }

    /// <summary>**设备没有幅宽上限时一律可加工，有上限时按上限判断** / A machine without a width limit processes anything; with one the limit decides.</summary>
    [Fact]
    public void EnabledBy_ShouldUseTheMachineWidthLimit() {
        Material2<Flt64> material = Material2Of(height: Metres(2.0), width: Metres(3.0));

        new Machine2<Flt64>(Csp2dIds.MachineId2Of("M1"), "unlimited").Let(
            machine => material.EnabledBy(machine).Should().BeTrue("no limit means anything fits"));

        new Machine2<Flt64>(Csp2dIds.MachineId2Of("M2"), "wide", MaxWidth: Metres(4.0)).Let(
            machine => material.EnabledBy(machine).Should().BeTrue());

        new Machine2<Flt64>(Csp2dIds.MachineId2Of("M3"), "narrow", MaxWidth: Metres(2.5)).Let(
            machine => material.EnabledBy(machine).Should().BeFalse("three metres do not fit a 2.5 metre machine"));

        new Machine2<Flt64>(Csp2dIds.MachineId2Of("M4"), "wrong unit", MaxWidth: Kilograms(4.0)).Let(
            machine => material.EnabledBy(machine).Should().BeFalse("an incomparable limit cannot be judged"));
    }

    /// <summary>**不允许旋转的件只有一个方向** / A non-rotatable piece has exactly one orientation.</summary>
    [Fact]
    public void Orientations_ShouldCollapseWhenRotationIsForbidden() {
        Costar2<Flt64> rotatable = Costar2Of(allowRotate: true);
        Costar2<Flt64> fixedPiece = Costar2Of(allowRotate: false);

        rotatable.Orientations().Should().HaveCount(2);
        rotatable.Orientations().Should().Contain(new List<bool> { false, true });
        fixedPiece.Orientations().Should().ContainSingle(
            "an extra orientation doubles the search space for nothing");
    }

    /// <summary>构造物料 / Build a material.</summary>
    /// <param name="height">板高，空即卷材 / The sheet height; null means a coil.</param>
    /// <param name="width">幅宽 / The width.</param>
    /// <param name="minRemnantArea">最小可用余料面积 / The minimum usable remnant area.</param>
    /// <returns>物料 / The material.</returns>
    private static Material2<Flt64> Material2Of(
        Quantity<Flt64>? height,
        Quantity<Flt64>? width = null,
        Quantity<Flt64>? minRemnantArea = null) =>
        new(
            Id: Csp2dIds.MaterialId2Of("MAT1"),
            Name: "steel",
            Width: width ?? Metres(3.0),
            Height: height,
            MinRemnantArea: minRemnantArea);

    /// <summary>构造副产物 / Build a by-product.</summary>
    /// <param name="allowRotate">是否允许旋转 / Whether rotation is allowed.</param>
    /// <returns>副产物 / The by-product.</returns>
    private static Costar2<Flt64> Costar2Of(bool allowRotate) =>
        new(
            Id: Csp2dIds.CostarId2Of("C1"),
            Name: "filler",
            Width: Metres(1.0),
            Height: Metres(1.0),
            AllowRotate: allowRotate);

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造平方米量 / Build an area in square metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> SquareMetres(double value) =>
        new(new Flt64(value), AreaUnits.SquareMeter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);
}

/// <summary>测试用的小工具 / A small test helper.</summary>
internal static class LetExtension {
    /// <summary>在值上执行一个断言 / Run one assertion on the value.</summary>
    /// <typeparam name="T">类型 / Type.</typeparam>
    /// <param name="value">值 / The value.</param>
    /// <param name="action">断言 / The assertion.</param>
    public static void Let<T>(this T value, System.Action<T> action) => action(value);
}
