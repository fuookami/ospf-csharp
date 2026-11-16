#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.model;

/// <summary>
/// 单位键的兜底必须一致：投影器与宽度索引对"同一单位"的判定不能分叉。
/// Unit-key fallbacks must agree: the projector and the width index must not disagree about whether two units are the same.
/// </summary>
/// <remarks>
/// 记录了一处不一致：`CrossContributionProjector.Measure` 落到**空串**，
/// 而 `GenerationWidthIndex.CanonicalUnitKey` 落到 **`ToString`**。
///
/// 差别只在一个单位的 `Symbol` 与 `Name` **都为 null** 时显形。而那时后果不小：
/// `CrossContributionDominance.Dominates` 在单位键**不同**时保守返回"不支配"；
/// 若两个**不同类型**的无名单位都被压成空串，就会把"不可比"错判成"可比"，
/// **可能剪掉本来该保留的方案**。
///
/// **内置单位都同时有 `Symbol` 与 `Name`，所以这条目前不可达**——正因为如此，它不会被任何
/// 现有测试发现，只能靠**自定义单位**把它构造出来。
///
/// recorded a mismatch: `CrossContributionProjector.Measure` fell back to the **empty string** while `GenerationWidthIndex.CanonicalUnitKey` fell back to
/// **`ToString`**.
///
/// The difference only shows when a unit has **both** `Symbol` and `Name` null — and the consequence is not small: `CrossContributionDominance.Dominates`
/// conservatively returns "does not dominate" on **mismatched** unit keys, so collapsing two differently-typed nameless units to the same empty string turns
/// "incomparable" into "comparable" and **can prune a plan that should have been kept**.
///
/// **Built-in units all have both `Symbol` and `Name`, so this is currently unreachable** — precisely why no existing test can find it. Only a **custom unit**
/// can construct it.
/// </remarks>
public class CrossContributionUnitKeyConsistencyTests {
    /// <summary>没有名称也没有符号的单位 / A unit with neither a name nor a symbol.</summary>
    private sealed class NamelessUnit : PhysicalUnit {
        private readonly DerivedQuantity _quantity;

        public NamelessUnit(DerivedQuantity quantity) => _quantity = quantity;

        public override string? Name => null;
        public override string? Symbol => null;
        public override DerivedQuantity Quantity => _quantity;
        public override UnitConversionRule ConversionRule => SIBaseUnits.Meter.ConversionRule;
    }

    /// <summary>
    /// 用给定单位构造物料 / Build a material in the given unit.
    /// </summary>
    /// <param name="unit">单位 / Unit.</param>
    /// <returns>物料 / The material.</returns>
    /// <remarks>
    /// **单位必须与方案切片一致。** `CuttingPlan.RestWidth` 在物料上界与已用宽度的单位**不同**时
    /// 返回 null，于是 `Project` 也返回 null，判据根本跑不到单位键那一层。
    /// 最初夹具用 `SIBaseUnits.Meter` 建物料、用自定义单位建方案，就撞在这里。
    ///
    /// **The unit must match the plan slices.** `CuttingPlan.RestWidth` returns null when the material bound's unit **differs** from the used width's unit, so
    /// `Project` returns null too and the criterion never reaches the unit-key layer. My first fixture built the material in `SIBaseUnits.Meter` and the plan in a
    /// custom unit, and hit exactly this.
    /// </remarks>
    private static Material<Flt64> MakeMaterial(PhysicalUnit unit) =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: new Quantity<Flt64>(new Flt64(100.0), unit),
                    UpperBound: new Quantity<Flt64>(new Flt64(200.0), unit)
                ),
                Step: new Quantity<Flt64>(new Flt64(1.0), unit)
            )
        );

    private static CuttingPlan<Flt64> MakePlan(string id, PhysicalUnit unit, double width) {
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { new(new Flt64(width), unit) }
        );
        Material<Flt64> material = MakeMaterial(unit);
        return new CuttingPlan<Flt64>(
            id: id,
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> {
                new(production: product, width: new Quantity<Flt64>(new Flt64(width), unit)),
            },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(Product: product, Quantity: new Quantity<Flt64>(new Flt64(width), unit)),
            }
        );
    }

    /// <summary>**键不得为空**（基线落到 `量纲(比例)`，不是空串）/ The key must not be empty (the code falls back to `dimension(scale)`, not to an empty string).</summary>
    /// <remarks>
    /// **这一条我在 写错过，改正。** 当时断言的是"两个不同类型的无名单位必须给出**不同**的键"，
    /// 但查基线后确认：基线 `canonicalUnitKey` 是 `symbol ?: name ?: toString`，
    /// 而 `toString` 是 `量纲(比例)`——**它不看类型**。两个不同类型的无名单位在基线下
    /// **确实共享同一个键**。加类型名会让 C# 与基线对同一份输入给出不同的键，那是语义分叉，不是修正。
    ///
    /// 真正要守的是：**键不能是空串**。 之前这里落到 `""`，那才是缺陷——空串会让
    /// "未知单位"与"另一个未知单位"看起来相同，而基线给的是 `量纲(比例)`，能区分量纲。
    ///
    /// **I got this one wrong and fixed it.** It then asserted that two differently typed nameless units must produce **different** keys, but the
    /// baseline's `canonicalUnitKey` is `symbol ?: name ?: toString` and `toString` is `dimension(scale)` — **it does not look at the type**. Two
    /// differently typed nameless units **do share one key** under the code. Adding the type name makes C# and the code disagree on the same input, which
    /// is a semantic divergence, not a fix.
    ///
    /// What actually needs guarding is that the key **is not empty**. Before it fell back to `""`, and that was the defect: an empty string makes "unknown
    /// unit" look identical to "another unknown unit", whereas the code yields `dimension(scale)` and can still tell dimensions apart.
    /// </remarks>
    [Fact]
    public void NamelessUnits_ShouldYieldADimensionBasedKey() {
        var unit = new NamelessUnit(SIBaseUnits.Meter.Quantity);

        CrossContributionProjection? projection = CrossContributionProjector.Project(MakePlan("P1", unit, 50.0));

        projection.Should().NotBeNull();
        projection!.RestWidth.Should().NotBeNull();
        projection.RestWidth!.UnitKey.Should().NotBeNullOrEmpty("the baseline falls back to dimension(scale), never to an empty string");
        projection.RestWidth.UnitKey.Should().Be(unit.ToString(), "parity with the baseline's symbol ?: name ?: toString()");
    }

    /// <summary>**键不同时支配判定必须保守** / The dominance decision must stay conservative across differing keys.</summary>
    /// <remarks>
    /// **这一条也写错了，改正。** 当时用两个**不同类型但换算相同**的无名单位，
    /// 断言 `Dominates` 返回 `false`。但基线下这两个单位的键**相同**（都是 `量纲(比例)`），
    /// 因此基线**认为它们可比**——旧断言与基线相反。
    ///
    /// 改成用**量纲不同**的两个单位（长度 vs 质量），这样键**真的不同**，
    /// 才能验证"键不同 → 不支配"这条保守规则。
    ///
    /// **I got this one wrong too, fixed.** It used two **differently typed but same-conversion** nameless units and asserted `Dominates` returns
    /// `false`. Under the code those two units have the **same** key (both `dimension(scale)`), so the code **considers them comparable** — the old assertion
    /// was the opposite of the code.
    ///
    /// It now uses two units of **different dimensions** (length vs mass), so the keys genuinely differ and the conservative rule "different key → no dominance" can
    /// actually be exercised.
    /// </remarks>
    [Fact]
    public void Dominance_ShouldBeConservativeAcrossDifferingKeys() {
        var length = new NamelessUnit(SIBaseUnits.Meter.Quantity);
        var mass = new NamelessUnit(SIBaseUnits.Kilogram.Quantity);

        CrossContributionProjection? a = CrossContributionProjector.Project(MakePlan("P1", length, 60.0));
        CrossContributionProjection? b = CrossContributionProjector.Project(MakePlan("P2", mass, 60.0));

        a!.RestWidth!.UnitKey.Should().NotBe(b!.RestWidth!.UnitKey, "length and mass have different dimensions");
        CrossContributionDominance.Dominates(a, b).Should().BeFalse("differing keys are incomparable");
        CrossContributionDominance.Dominates(b, a).Should().BeFalse("differing keys are incomparable");
    }

    /// <summary>**同一个无名单位必须自相等** / The same nameless unit must compare equal to itself.</summary>
    [Fact]
    public void SameNamelessUnit_ShouldStillBeComparable() {
        var unit = new NamelessUnit(SIBaseUnits.Meter.Quantity);

        CrossContributionProjection? wide = CrossContributionProjector.Project(MakePlan("P1", unit, 60.0));
        CrossContributionProjection? narrow = CrossContributionProjector.Project(MakePlan("P2", unit, 50.0));

        wide!.RestWidth!.UnitKey.Should().Be(narrow!.RestWidth!.UnitKey);
    }

    /// <summary>**有符号的单位必须用符号作键** / A unit with a symbol must use the symbol as its key.</summary>
    /// <remarks>
    /// 前三条判据用的全是**无符号无名**的自定义单位，只覆盖了兜底分支。
    /// 于是"符号优先"这一支**完全没有被测到**——这里的第三条变异（删掉 `Symbol` 那一行）因此存活。
    ///
    /// 这条把主分支钉住：`Millimeter` 的键必须**就是 `"mm"`**，而不是类型全名或 `ToString` 渲染串。
    ///
    /// The first three criteria use only **nameless, symbol-less** custom units and therefore cover just the fallback branch. So "symbol first" **went untested**,
    /// which is why the third mutation in (deleting the `Symbol` line) survived. This pins the main branch: `Millimeter`'s key must be **exactly `"mm"`**,
    /// not a type name or a `ToString` rendering.
    /// </remarks>
    [Fact]
    public void SymbolicUnit_ShouldUseItsSymbolAsTheKey() {
        CrossContributionProjection? projection =
            CrossContributionProjector.Project(MakePlan("P1", LengthUnits.Millimeter, 50.0));

        projection.Should().NotBeNull();
        projection!.RestWidth.Should().NotBeNull();
        projection.RestWidth!.UnitKey.Should().Be("mm");
    }

    /// <summary>**只有名称没有符号的单位必须用名称作键** / A unit with a name but no symbol must use the name as its key.</summary>
    /// <remarks>
    /// 补齐最后一条分支。上一轮变异（删掉 `Name` 那一行）存活，正是因为**没有任何单位只有名称**：
    /// 内置单位都有符号，我的自定义单位两者都没有。这条把中间那一支钉住。
    ///
    /// This completes the last branch. The previous mutation (deleting the `Name` line) survived precisely because **no unit had a name without a symbol**:
    /// built-ins all have symbols and my custom units had neither. This pins the middle branch.
    /// </remarks>
    [Fact]
    public void NameOnlyUnit_ShouldUseItsNameAsTheKey() {
        var unit = new NameOnlyUnit(SIBaseUnits.Meter.Quantity);

        CrossContributionProjection? projection = CrossContributionProjector.Project(MakePlan("P1", unit, 50.0));

        projection.Should().NotBeNull();
        projection!.RestWidth.Should().NotBeNull();
        projection.RestWidth!.UnitKey.Should().Be("name-only-unit");
    }

    /// <summary>只有名称的单位 / A unit with a name but no symbol.</summary>
    private sealed class NameOnlyUnit : PhysicalUnit {
        private readonly DerivedQuantity _quantity;

        public NameOnlyUnit(DerivedQuantity quantity) => _quantity = quantity;

        public override string? Name => "name-only-unit";
        public override string? Symbol => null;
        public override DerivedQuantity Quantity => _quantity;
        public override UnitConversionRule ConversionRule => SIBaseUnits.Meter.ConversionRule;
    }

    /// <summary>另一个类型不同、同样无名的单位 / Another nameless unit of a different type.</summary>
    private sealed class OtherNamelessUnit : PhysicalUnit {
        private readonly DerivedQuantity _quantity;

        public OtherNamelessUnit(DerivedQuantity quantity) => _quantity = quantity;

        public override string? Name => null;
        public override string? Symbol => null;
        public override DerivedQuantity Quantity => _quantity;
        public override UnitConversionRule ConversionRule => SIBaseUnits.Meter.ConversionRule;
    }
}
