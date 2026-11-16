// 余料统计与解决方案适配的判据 / Criteria for waste statistics and the solution adapter.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing;
using Fuookami.Ospf.Framework.Bpp3d.Tests.Application;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain;

/// <summary>
/// **余料不为负、缺省价不为零、子节点先合并** / Waste is never negative, an unconfigured price is not zero, and children merge first.
/// </summary>
/// <remarks>
/// 三处都是"静默做错"的类型：负余料会抵消别的箱子的浪费，缺省零价会让求解器偏爱没配价的箱型，
/// 先合并自己的物料会让子节点里更大的数量被丢掉。
///
/// All three fail silently: negative waste cancels another bin's waste, a zero default makes the solver prefer unpriced types, and merging this node's materials first
/// drops the larger amounts held by children.
/// </remarks>
public class MixedBinObjectivesTests {
    /// **余料 = 容器体积减去已装体积** / Waste is the container volume minus what is loaded.
    [Fact]
    public void WasteVolume_ShouldBeTheUnusedContainerVolume() {
        BinWaste.Volume(Value(10.0), new List<Quantity<FltX>> { Cubic(3.0), Cubic(4.0) })
            .ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-12);
    }

    /// <summary>
    /// **超出容量时余料为零，不是负数** / Over-capacity waste is zero, not negative.
    /// </summary>
    /// <remarks>
    /// 这条单独写：如果实现返回 `-5`，它会在"总浪费"里**减掉**别的箱子的余料，而总数看起来依然合理。
    ///
    /// Written separately because an implementation returning `-5` would **subtract** from another bin's waste in a total, and the total still looks plausible.
    /// </remarks>
    [Fact]
    public void WasteVolume_ShouldNeverGoNegative() {
        BinWaste.Volume(Value(10.0), new List<Quantity<FltX>> { Cubic(15.0) })
            .ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-12);
    }

    /// **余料率落在 [0,1]** / The waste ratio stays within [0,1].
    [Fact]
    public void WasteRatio_ShouldStayWithinTheUnitInterval() {
        BinWaste.Ratio(Value(10.0), new List<Quantity<FltX>> { Cubic(2.5) })
            .ToFlt64().ToDouble().Should().BeApproximately(0.75, 1e-12);
        BinWaste.Ratio(Value(10.0), new List<Quantity<FltX>> { Cubic(20.0) })
            .ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-12, "clamped at zero waste");
        BinWaste.Ratio(Value(10.0), new List<Quantity<FltX>>())
            .ToFlt64().ToDouble().Should().BeApproximately(1.0, 1e-12, "an empty bin is fully wasted");
    }

    /// **零体积容器不产生 NaN** / A zero-volume container yields no NaN.
    [Fact]
    public void WasteRatio_ShouldGuardAgainstDivisionByZero() {
        double ratio = BinWaste.Ratio(Value(0.0), new List<Quantity<FltX>> { Cubic(1.0) })
            .ToFlt64().ToDouble();

        ratio.Should().BeApproximately(0.0, 1e-12);
        double.IsNaN(ratio).Should().BeFalse("NaN would make every downstream comparison false");
    }

    /// <summary>
    /// **未配价的箱型取缺省价，不是零** / An unpriced bin type takes the default, not zero.
    /// </summary>
    /// <remarks>
    /// 这条用得是"配了 A 没配 B"的输入：若实现返回零，求解器会**偏爱** B——因为它在成本上免费，
    /// 而这正是"漏配一条价目"最容易造成的错误决策。
    ///
    /// The input prices A but not B: an implementation returning zero makes the solver **prefer** B because it is free, which is exactly the bad decision a missing price
    /// entry produces.
    /// </remarks>
    [Fact]
    public void PerTypeBinCost_ShouldFallBackToTheDefaultNotZero() {
        var priced = new Dictionary<string, FltX> { ["A"] = new(5.0) };
        System.Func<string, FltX> cost = BinCost.PerType(priced, defaultCost: new FltX(9.0));

        cost("A").ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-12);
        cost("B").ToFlt64().ToDouble().Should().BeApproximately(9.0, 1e-12,
            "an unpriced type must not look free");

        System.Func<string, FltX> noDefault = BinCost.PerType(priced);
        noDefault("B").ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-12,
            "the explicit default of zero is honoured when the caller asks for it");
    }

    /// **子节点的物料先合并，再合并本节点的** / Children's materials merge before this node's.
    [Fact]
    public void ToPackingProgram_ShouldMergeChildrenBeforeOwnMaterials() {
        var child = new PackageSolutionLikeNode(
            Shape(),
            MaterialItems: new List<PackageSolutionLikeMaterialItem> {
                new("M1", new PackageSolutionLikeQuantity.Amount(7UL)),
            });
        var parent = new PackageSolutionLikeNode(
            Shape(),
            MaterialItems: new List<PackageSolutionLikeMaterialItem> {
                new("M1", new PackageSolutionLikeQuantity.Amount(5UL)),
            },
            Children: new List<PackageSolutionLikeNode> { child });

        PackingProgram program = RetAssertions.Unwrap(parent.ToPackingProgram());

        program.Materials!["M1"].Amount.Should().Be(12UL,
            "7 from the child plus 5 from the parent; a parent-first merge would keep 5");
        program.Packages.Should().HaveCount(1);
    }

    /// **叶子节点没有子包装，数量口径按类型分别落地** / A leaf has no children, and each quantity variant lands separately.
    [Fact]
    public void ToPackingProgram_ShouldMapEachQuantityVariant() {
        var leaf = new PackageSolutionLikeNode(
            Shape(),
            MaterialItems: new List<PackageSolutionLikeMaterialItem> {
                new("BY_COUNT", new PackageSolutionLikeQuantity.Amount(3UL)),
                new("BY_WEIGHT", new PackageSolutionLikeQuantity.Weight(Kilograms(2.0))),
                new("BOTH", new PackageSolutionLikeQuantity.AmountAndWeight(4UL, Kilograms(1.5))),
            });

        PackingProgram program = RetAssertions.Unwrap(leaf.ToPackingProgram());

        program.Packages.Should().BeNull("a leaf has no children");
        program.Materials!["BY_COUNT"].Amount.Should().Be(3UL);
        program.Materials["BY_COUNT"].Weight.Should().BeNull("only the count was known");
        program.Materials["BY_WEIGHT"].Amount.Should().BeNull("only the weight was known");
        program.Materials["BY_WEIGHT"].Weight!.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-12);
        program.Materials["BOTH"].Amount.Should().Be(4UL);
        program.Materials["BOTH"].Weight!.Value.ToFlt64().ToDouble().Should().BeApproximately(1.5, 1e-12);
    }

    /// <summary>取裸体积数值 / The bare volume number.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>裸数值 / The bare number.</returns>
    /// <remarks>
    /// 余料函数收的是 `volume.value`（裸数值）而不是带单位的量：这里保持同样口径，
    /// 因为余料的输入本来就来自同一箱型，混单位没有意义，加一层 `Quantity` 只会掩盖"两个不同箱型相减"这种真错误。
    /// The code's waste functions take `volume.value` (a bare number) rather than a quantity: the same convention is kept here, because the inputs come from one bin
    /// type by construction, so mixing units is meaningless and wrapping them in `Quantity` would only hide the real error of subtracting two different bin types.
    /// </remarks>
    private static FltX Value(double value) => new(value);

    /// <summary>构造立方体积 / Build a cubic volume.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>体积量 / The volume quantity.</returns>
    private static Quantity<FltX> Cubic(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>构造质量 / Build a mass.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>质量 / The mass.</returns>
    private static Quantity<FltX> Kilograms(double value) => new(new FltX(value), SIBaseUnits.Kilogram);

    /// <summary>构造包装形状 / Build a package shape.</summary>
    /// <returns>形状 / The shape.</returns>
    private static PackageShape Shape() => new(
        Cubic(1.0), Cubic(1.0), Cubic(1.0), Kilograms(1.0), PackageType.Box);
}
