#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Service.Pipeline;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.service;

/// <summary>
/// 单位键必须**只有一份实现**，且与基线 `symbol ?: name ?: toString` 逐字一致。
/// The unit key must have **exactly one implementation**, byte-for-byte with its `symbol ?: name ?: toString`.
/// </summary>
/// <remarks>
/// 这里在 `Framework.Csp1d` 里查出**五份**单位键实现：
///
/// | # | 位置 | 兜底 |
/// | --- | --- | --- |
/// | 1 | `GenerationQuantityMath.UnitKey` | `ToString` |
/// | 2 | `GenerationWidthIndex.CanonicalUnitKey` | `ToString` |
/// | 3 | `GenerationMaterialWidthRangeKey.CanonicalUnitKey` | `ToString` |
/// | 4 | `CrossContributionProjector.Measure` | **空串** |
/// | 5 | `CuttingPlanDemandContribution` 的 `UnitKey` | **空串** |
///
/// 五份各写一遍，**任何一份与基线分叉都会让"同一单位"在不同代码路径上判成不同结果，
/// 而不会有任何测试发现**。第 4、5 份甚至停在了更早的、会返回空串的版本。
///
/// 现在全部委托给 `GenerationQuantityMath.UnitKey`。
///
/// found **five** unit-key implementations in `Framework.Csp1d` (table above). With five copies, **any one diverging from the code makes "the same
/// unit" resolve differently on different code paths, and no test would notice**. Copies 4 and 5 had even stalled at the earlier empty-string version.
///
/// All of them now delegate to `GenerationQuantityMath.UnitKey`.
/// </remarks>
public class UnitKeyConvergenceTests {
    /// <summary>没有名称也没有符号的单位 / A unit with neither a name nor a symbol.</summary>
    private sealed class NamelessUnit : PhysicalUnit {
        private readonly DerivedQuantity _quantity;

        public NamelessUnit(DerivedQuantity quantity) => _quantity = quantity;

        public override string? Name => null;
        public override string? Symbol => null;
        public override DerivedQuantity Quantity => _quantity;
        public override UnitConversionRule ConversionRule => SIBaseUnits.Meter.ConversionRule;
    }

    /// <summary>**有符号的单位必须用符号** / A symbolic unit must use its symbol.</summary>
    [Fact]
    public void SymbolicUnit_ShouldUseItsSymbol() =>
        GenerationQuantityMath.UnitKey(LengthUnits.Millimeter).Should().Be("mm");

    /// <summary>**无名单位必须落到 `ToString`，不得是空串** / A nameless unit must fall back to `ToString`, never to an empty string.</summary>
    /// <remarks>
    /// 这正是第 4、5 份实现的缺陷：它们返回空串。基线返回 `量纲(比例)`。
    /// 空串会让两个**不同量纲**的未知单位看起来相同，而基线能按量纲区分。
    ///
    /// This is exactly the defect in copies 4 and 5: they returned an empty string. The code returns `dimension(scale)`. An empty string makes two unknown units
    /// of **different dimensions** look identical, while the code can tell them apart.
    /// </remarks>
    [Fact]
    public void NamelessUnit_ShouldFallBackToItsStringRepresentation() {
        var length = new NamelessUnit(SIBaseUnits.Meter.Quantity);
        var mass = new NamelessUnit(SIBaseUnits.Kilogram.Quantity);

        GenerationQuantityMath.UnitKey(length).Should().Be(length.ToString());
        GenerationQuantityMath.UnitKey(length).Should().NotBeNullOrEmpty();
        GenerationQuantityMath.UnitKey(length).Should().NotBe(GenerationQuantityMath.UnitKey(mass));
    }

    /// <summary>**匿名单位的单位键（第三回退落空串）** / The anonymous unit's key matches the code (third fallback lands on an empty string).</summary>
    /// <remarks>
    /// 三处一起看才准：
    ///
    /// 三条取值规则，按优先级回落：
    /// 具体单位 `toString` 取 `symbol ?? name ?? "(${dimensionSymbol})(${scale})"`；
    /// 匿名单位取 `symbol ?? ""`；
    /// 规范键取 `symbol ?? name ?? toString`。
    ///
    /// **`AnonymousPhysicalUnit` 的 `toString` 是 `symbol ?: ""`**——它**既不回退到 name，也不渲染量纲**。
    /// 于是"既无 symbol 又无 name 的匿名单位"的单位键就是**空串**，两个不同的匿名单位**共用一个键**。
    ///
    /// **这是行为，不是 C# 的偏离。** 这里一开始把 `Symbol ?? ""` 记在 `DerivedPhysicalUnit` 名下
    /// 并标为"C# 的真实隐患"——**两处都错了**：`DerivedPhysicalUnit` 并不覆盖 `ToString`（它继承基类），
    /// 而 `AnonymousPhysicalUnit` 的写法与基线**逐字相同**。
    ///
    /// 判据钉住这两点，**防止将来有人把 `AnonymousPhysicalUnit.ToString` "修"成基类的三回退**——
    /// 那会让 C# 与基线分叉，而且**没有任何现有判据会发现**。
    ///
    /// The three baselines must be read together (snippet above).
    ///
    /// **`AnonymousPhysicalUnit`'s `toString` is `symbol ?: ""`** — it **neither falls back to the name nor renders the dimension**. So a symbol-less, nameless anonymous
    /// unit's key is **an empty string**, and two distinct anonymous units **share one key**.
    ///
 /// **That is its behaviour, not a C# divergence.** initially attributed `Symbol ?? ""` to `DerivedPhysicalUnit` and labelled it "a real C#
    /// hazard" — **wrong on both counts**: `DerivedPhysicalUnit` does not override `ToString` (it inherits the base), and `AnonymousPhysicalUnit`'s version is
    /// **byte-for-byte identical** to the code.
    ///
    /// The criteria pin both facts, **preventing someone from "fixing" `AnonymousPhysicalUnit.ToString` into the base's three-step fallback** — that would diverge
    /// from the code, and **no existing criterion would notice**.
    /// </remarks>
    [Fact]
    public void AnonymousUnit_ShouldKeyAsTheBaselineDoes() {
        var named = new AnonymousPhysicalUnit(
            SIBaseUnits.Meter.Quantity,
            SIBaseUnits.Meter.ConversionRule,
            name: "anon-named");
        GenerationQuantityMath.UnitKey(named).Should().Be("anon-named",
            "the name is the second fallback in canonicalUnitKey");

        var symbolOnly = new AnonymousPhysicalUnit(
            SIBaseUnits.Meter.Quantity,
            SIBaseUnits.Meter.ConversionRule,
            symbol: "asym");
        GenerationQuantityMath.UnitKey(symbolOnly).Should().Be("asym");

        var nameless = new AnonymousPhysicalUnit(
            SIBaseUnits.Meter.Quantity,
            SIBaseUnits.Meter.ConversionRule);
        GenerationQuantityMath.UnitKey(nameless).Should().BeEmpty(
            "AnonymousPhysicalUnit.toString() is symbol ?: \"\", matching the baseline");
    }

    /// <summary>**匿名单位的 `ToString` 必须是 `symbol ?: ""`，不得回退到 name** / The anonymous unit's `ToString` must be `symbol ?: ""`, never falling back to the name.</summary>
    /// <remarks>
    /// **这条是为一个"通过 `UnitKey` 杀不死"的变异补的。** 把 `AnonymousPhysicalUnit.ToString`
    /// 改成 `Symbol ?? Name ?? ""` 时，**`UnitKey` 的判据全绿**——因为 `UnitKey` 本来就在 `ToString` **之前**
    /// 先试 `Name`，多一层回退**不改变它的结果**。
    ///
    /// 但 `ToString` **本身有直接调用方**（渲染、日志、id 拼装）。这里是 `symbol ?: ""`——
    /// **有 name 也不回退**，所以 `ToString` 返回**空串**。若 C# 回退到 name，两边的诊断输出就会不同。
    ///
    /// **"`UnitKey` 等价"不等于"实现等价"**：判据必须落在**被改动的那个方法**上，
    /// 而不是落在它的一个消费者上。
    ///
    /// **This exists for a mutant that cannot be killed through `UnitKey`.** Changing `AnonymousPhysicalUnit.ToString` to `Symbol ?? Name ?? ""` leaves **every
    /// `UnitKey` criterion green** — `UnitKey` already tries `Name` **before** `ToString`, so an extra fallback layer **does not change its result**.
    ///
    /// But `ToString` **has direct callers** (rendering, logs, id assembly), and the code is `symbol ?: ""` — **no name fallback**, so `ToString` returns **an
    /// empty string** even when a name exists. A C# name fallback would make diagnostic output differ.
    ///
    /// **"Equivalent for `UnitKey`" is not "equivalent".** A criterion must land on **the method that was changed**, not on one of its consumers.
    /// </remarks>
    [Fact]
    public void AnonymousUnit_ToStringShouldNotFallBackToTheName() {
        var named = new AnonymousPhysicalUnit(
            SIBaseUnits.Meter.Quantity,
            SIBaseUnits.Meter.ConversionRule,
            name: "anon-named");

        named.ToString().Should().BeEmpty("the baseline is `symbol ?: \"\"`; the name is not part of ToString()");
        GenerationQuantityMath.UnitKey(named).Should().Be("anon-named", "UnitKey still prefers the name");
    }

    /// <summary>**`DerivedPhysicalUnit` 不覆盖 `ToString`，继承基类的三回退** / `DerivedPhysicalUnit` does not override `ToString`; it inherits the three-step fallback.</summary>
    /// <remarks>
    /// 这条钉住"派生单位**不是**匿名单位"这个区分：`DerivedPhysicalUnit` 继承基类，
    /// 所以无 symbol、无 name 时**渲染量纲**，而匿名单位返回空串。
    ///
    /// **两个基类在这一点上行为不同，容易看混**——这里正是看混了。
    ///
    /// This pins the distinction that "a derived unit **is not** an anonymous unit": `DerivedPhysicalUnit` inherits the base, so with no symbol and no name it
    /// **renders the dimension**, whereas an anonymous unit returns an empty string.
    ///
    /// **The two base classes behave differently here and are easy to confuse** — confused them.
    /// </remarks>
    [Fact]
    public void DerivedUnit_ShouldInheritTheBaseToStringFallback() {
        var derived = new CountOnlyUnit(SIBaseUnits.Meter.Quantity);
        GenerationQuantityMath.UnitKey(derived).Should().NotBeNullOrEmpty(
            "DerivedPhysicalUnit inherits PhysicalUnit.ToString(), which renders the dimension");
    }

    /// <summary>继承 `DerivedPhysicalUnit` 的最小实现 / A minimal implementation deriving from `DerivedPhysicalUnit`.</summary>
    private sealed class CountOnlyUnit : DerivedPhysicalUnit {
        public CountOnlyUnit(DerivedQuantity quantity) : base(SIBaseUnits.Meter) => _quantity = quantity;

        private readonly DerivedQuantity _quantity;

        public override string? Name => null;

        public override string? Symbol => null;

        public override DerivedQuantity Quantity => _quantity;
    }

    /// <summary>**需求约束管线的影子价格单位键必须与唯一实现一致** / The demand-constraint pipeline's shadow-price unit key must agree with the single implementation.</summary>
    /// <remarks>
    /// 这是**第六份**单位键实现，而且它与消费方**真的分叉**过：
    ///
    /// | 角色 | 旧写法 | 结果 |
    /// | --- | --- | --- |
    /// | **生产方**（`DemandConstraintPipeline` 写对偶价格键） | `Symbol ?? ToString` | 匿名单位 → **空串** |
    /// | **消费方**（定价器查对偶价格键） | `Symbol ?? Name ?? ToString` | 匿名单位 → **name** |
    ///
    /// 对**有 name、无 symbol** 的单位，生产方的 `ToString` 恰好也回退到 name，**这条碰巧一致**；
    /// 但对**匿名单位**，`AnonymousPhysicalUnit.ToString` 是 `Symbol ?? ""`，
    /// **两边算出不同的键，对偶价格永远查不到**——列生成因此在第一轮就收敛。
    ///
    /// **判据用匿名单位**：它正是两条路径分叉的那种单位。**用普通毫米单位测不出来**——
    /// 那种单位有 symbol，两条路径都返回 `"mm"`，**判据会假绿**。
    ///
    /// This is the **sixth** unit-key implementation, and it really did diverge from the consumer (table above). For a **named, symbol-less** unit the producer's
    /// `ToString` also happens to fall back to the name, **so that case agrees**; but for an **anonymous unit** `AnonymousPhysicalUnit.ToString` is `Symbol ?? ""`,
    /// so **the two sides compute different keys and the dual price is never found** — which is why column generation converged on the first round.
    ///
    /// **The criterion uses an anonymous unit**, precisely the kind where the two paths diverge. **A plain millimetre unit cannot detect this** — it has a symbol, both
    /// paths return `"mm"`, and **the criterion would be falsely green**.
    /// </remarks>
    [Fact]
    public void DemandPipelineUnitKey_ShouldAgreeWithTheSingleImplementation() {
        var units = new PhysicalUnit[] {
            LengthUnits.Millimeter,
            new AnonymousPhysicalUnit(
                SIBaseUnits.Meter.Quantity,
                SIBaseUnits.Meter.ConversionRule,
                name: "anon-named"),
            new AnonymousPhysicalUnit(
                SIBaseUnits.Meter.Quantity,
                SIBaseUnits.Meter.ConversionRule),
        };

        foreach (PhysicalUnit unit in units) {
            DemandConstraintPipeline<Flt64>.ShadowPriceUnitSymbol(unit)
                .Should().Be(GenerationQuantityMath.UnitKey(unit),
                    "the producer and the consumer must agree on the key, or the dual price is never found");
        }
    }

    /// <summary>**宽度索引必须与唯一实现一致** / The width index must agree with the single implementation.</summary>
    [Fact]
    public void CanonicalUnitKey_ShouldAgreeWithTheQuantityMathKey() {
        var units = new PhysicalUnit[] {
            LengthUnits.Millimeter,
            SIBaseUnits.Meter,
            new NamelessUnit(SIBaseUnits.Meter.Quantity),
        };

        foreach (PhysicalUnit unit in units) {
            GenerationWidthIndex<Flt64>.CanonicalUnitKey(unit)
                .Should().Be(GenerationQuantityMath.UnitKey(unit), "both must route through one implementation");
        }
    }

    /// <summary>**需求贡献的单位键必须与唯一实现一致** / The demand-contribution unit key must agree with the single implementation.</summary>
    /// <remarks>
    /// 这条覆盖第 5 份——它停在**空串**版本，是五份里最早的分叉。
    ///
    /// This covers copy 5 — stuck at the **empty-string** version, the earliest divergence of all five.
    /// </remarks>
    [Fact]
    public void DemandContributionUnitKey_ShouldAgreeWithTheSingleImplementation() {
        var unit = new NamelessUnit(SIBaseUnits.Meter.Quantity);
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { new(new Flt64(50.0), unit) }
        );
        var contribution = new CuttingPlanDemandContribution<Flt64>(
            Product: product,
            Quantity: new Quantity<Flt64>(new Flt64(50.0), unit)
        );

        ICuttingPlanDemandContributionForCanonicalKey forKey = contribution;

        forKey.UnitKey.Should().Be(GenerationQuantityMath.UnitKey(unit));
        forKey.UnitKey.Should().NotBeNullOrEmpty("the baseline never returns an empty key");
    }
}
