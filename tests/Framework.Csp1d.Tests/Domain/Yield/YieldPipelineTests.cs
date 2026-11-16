// 超产约束与目标项的判据 / Criteria for the over-production constraint and objective terms.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Service.Pipeline;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Domain.Yield;

/// <summary>
/// **超产约束管线与超产目标项** / The over-production constraint pipeline and its objective terms.
/// </summary>
/// <remarks>
/// **记的缺口**："`YieldConstraintPipeline` / `YieldObjectivePipeline` 无判据"——
/// 这两条此前只在超产面积一路**间接**出现过，没有直接判据。
///
/// **the earlier gap**: these two had only ever been exercised **indirectly** through the over-production area path.
/// </remarks>
public class YieldPipelineTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Product<Flt64> MakeProduct(string id) =>
        new(
            id: id,
            name: id,
            width: new List<Quantity<Flt64>> { Millimetres(60.0) });

    private static ProductDemandShadowPriceKey KeyOf(ProductDemand<Flt64> demand) =>
        new(demand.Product.Id, demand.Quantity.Unit.Symbol ?? demand.Quantity.Unit.ToString());

    /// <summary>**超产上限约束：`over <= upper`，右端是对应需求的** / The over-production bound is `over <= upper` with the matching demand's value.</summary>
    /// <remarks>
    /// 两条需求、上界 5 与 9。断言**约束数量 + 各自右端**：
    /// 只断言"存在"抓不住"两条都用第一个值"。
    ///
    /// Two demands with bounds 5 and 9. The assertions cover **the count and each right-hand side**: "exists" cannot catch "both use the first value".
    /// </remarks>
    [Fact]
    public void YieldConstraintPipeline_ShouldEmitOneBoundPerDemand() {
        var demandA = new ProductDemand<Flt64>(MakeProduct("A"), Millimetres(60.0));
        var demandB = new ProductDemand<Flt64>(MakeProduct("B"), Millimetres(70.0));
        var demands = new List<ProductDemand<Flt64>> { demandA, demandB };
        var config = new YieldModelingConfig<Flt64>(
            OverProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [KeyOf(demandA)] = new Flt64(1.0),
                [KeyOf(demandB)] = new Flt64(1.0),
            },
            OverProductionUpperBound: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [KeyOf(demandA)] = new Flt64(5.0),
                [KeyOf(demandB)] = new Flt64(9.0),
            });
        var yieldAggregation = new YieldAggregation(config, demands);
        var model = new LinearMetaModel<Flt64>();
        yieldAggregation.Register(model).IsOk.Should().BeTrue();

        new YieldConstraintPipeline<Flt64>(yieldAggregation, config, demands).Invoke(model).IsOk.Should().BeTrue();

        var bounds = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Where(c => c.Name.Contains("over_production_bound"))
            .ToList();
        bounds.Should().HaveCount(2, "one over-production bound per demand");
        bounds.All(b => b.Inequality.Comparison == Comparison.LE)
            .Should().BeTrue("the over-production bound is a <= constraint");
        bounds.Select(b => b.Inequality.Rhs.Constant.ToDouble())
            .Should().BeEquivalentTo(new[] { 5.0, 9.0 }, options => options.Using<double>(c => c.Subject.Should().BeApproximately(c.Expectation, 1e-9)).WhenTypeIs<double>());
    }

    /// <summary>**`args` 里的键必须能被 `Refresh` 找回**——否则约束建了也白建 / The `args` key must be findable by `Refresh`, or the constraint is pointless.</summary>
    /// <remarks>
    /// **记的缺口**：约束带上了 `YieldOverProductionBoundShadowPriceKey`，
    /// 但"影子价格回填时能不能按这个键找回"没测过。
    ///
    /// 这条走**完整路径**：
    /// 1. 建约束（`args` 里带键）；
    /// 2. 造一个 `MetaDualSolution`，给这条约束一个**非零**对偶值 7.5；
    /// 3. 调 `Refresh`；
    /// 4. 用**硬编码的键**（不是从约束上读的）查回填结果。
    ///
    /// **第 4 步必须硬编码**—— 的教训：从约束自己的 `args` 读键再查，
    /// **键写错了也查得回来**，判据全绿。硬编码 `("A", "mm")` 才能钉住键的身份。
    ///
    /// **the earlier gap**: the constraint carries a `YieldOverProductionBoundShadowPriceKey`, but whether the shadow price can be found by that key was untested. This walks
    /// the **full path**: build the constraint, fabricate a `MetaDualSolution` giving it a **non-zero** dual of 7.5, call `Refresh`, then look the result up by a
    /// **hard-coded key** — the earlier lesson: reading the key off the constraint's own `args` makes the lookup succeed **even when the key is wrong**. Hard-coding
    /// `("A", "mm")` pins the key's identity.
    /// </remarks>
    [Fact]
    public void YieldConstraintPipeline_ShouldLetRefreshFindTheConstraintByItsArgs() {
        var demandA = new ProductDemand<Flt64>(MakeProduct("A"), Millimetres(60.0));
        var demands = new List<ProductDemand<Flt64>> { demandA };
        var config = new YieldModelingConfig<Flt64>(
            OverProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [KeyOf(demandA)] = new Flt64(1.0),
            },
            OverProductionUpperBound: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [KeyOf(demandA)] = new Flt64(5.0),
            });
        var yieldAggregation = new YieldAggregation(config, demands);
        var model = new LinearMetaModel<Flt64>();
        yieldAggregation.Register(model).IsOk.Should().BeTrue();
        var pipeline = new YieldConstraintPipeline<Flt64>(yieldAggregation, config, demands);
        pipeline.Invoke(model).IsOk.Should().BeTrue();

        MathConstraint bound = model.Constraints.Single(c => c.Name.Contains("over_production_bound"));
        var dualSolution = new MetaDualSolution(
            new Dictionary<MathConstraint, Flt64> { [bound] = new Flt64(7.5) },
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
        var map = new Csp1dDefaultShadowPriceMap();

        // `Refresh` 是接口默认实现，必须按接口类型调用。
        // `Refresh` is a default interface method, so it must be called through the interface.
        ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>> asPipeline = pipeline;
        asPipeline.Refresh(map, model, dualSolution).IsOk.Should().BeTrue();

        // **硬编码的键**，不是从 `bound.Args` 读的。
        // **A hard-coded key**, not one read from `bound.Args`.
        var expectedKey = new YieldOverProductionBoundShadowPriceKey("A", "mm");
        ShadowPrice? price = map[expectedKey];
        price.Should().NotBeNull("the constraint's args key must be reachable from the refreshed map");
        price!.Price.ToDouble().Should().BeApproximately(
            7.5,
            1e-9,
            "the dual value must be carried through the refresh, not dropped");
    }

    /// <summary>**两条同键约束的对偶必须相加，且别的管线的约束不能串味** / Two same-key constraints must accumulate, and another pipeline's constraint must not leak in.</summary>
    /// <remarks>
    /// **记的两个缺口合成一条判据**：
    ///
    /// 1. **累加语义**：同一 key 被多条约束命中时，默认实现是
    ///    `prices[key] = (prices.TryGetValue(key, out p) ? p : Zero) + price`——**相加**。
    /// 2. **分组正确性**：`ConstraintsOfGroup(this)` 按 Range 取本组约束。
    /// 若它把别的管线的约束也算进来，影子价格会**串味**。
    ///
    /// 构造：同一产品的**两条需求**（键相同 `("A","mm")`），对偶分别是 3 和 4；
    /// 再加一条**属于别的管线**的约束，对偶是 100。
    ///
    /// 期望：查回 **7**，不是 3、不是 4、更不是 107。
    ///
    /// | 实现错法 | 结果 |
    /// | --- | --- |
    /// | 覆盖而非累加 | 4 |
    /// | 分组把别的管线算进来 | 107 |
    /// | 正确 | **7** |
    ///
    /// **the earlier two gaps in one criterion**: (1) **accumulation** — the default implementation sums prices for the same key; (2) **grouping** — `ConstraintsOfGroup(this)` takes
    /// a Range, and if it swept in another pipeline's constraints the shadow prices would **leak**. Fixture: two demands of the same product (same key `("A","mm")`) with
    /// duals 3 and 4, plus a **foreign** constraint from another pipeline with dual 100. Expected **7** — not 3, not 4, and certainly not 107 (table above).
    /// </remarks>
    [Fact]
    public void Refresh_ShouldAccumulateSameKeyPricesAndIgnoreOtherGroups() {
        var demandA1 = new ProductDemand<Flt64>(MakeProduct("A"), Millimetres(60.0));
        var demandA2 = new ProductDemand<Flt64>(MakeProduct("A"), Millimetres(80.0));
        var demands = new List<ProductDemand<Flt64>> { demandA1, demandA2 };
        var config = new YieldModelingConfig<Flt64>(
            OverProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [KeyOf(demandA1)] = new Flt64(1.0),
            },
            OverProductionUpperBound: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [KeyOf(demandA1)] = new Flt64(5.0),
            });
        var yieldAggregation = new YieldAggregation(config, demands);
        var model = new LinearMetaModel<Flt64>();
        yieldAggregation.Register(model).IsOk.Should().BeTrue();
        var pipeline = new YieldConstraintPipeline<Flt64>(yieldAggregation, config, demands);
        pipeline.Invoke(model).IsOk.Should().BeTrue();

        var bounds = model.Constraints
            .Where(c => c.Name.Contains("over_production_bound"))
            .ToList();
        bounds.Should().HaveCount(2, "two demands of the same product give two same-key constraints");

        // **别的管线的约束**：用另一个 group 注册，`args` 带同一个键。
        // **A foreign constraint**: registered under another group with the same args key.
        var foreignGroup = new ForeignPipelineGroup();
        model.AddConstraint(
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.Zero),
                new LinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.Zero),
                Comparison.LE),
            foreignGroup,
            name: "foreign_constraint",
            args: new YieldOverProductionBoundShadowPriceKey("A", "mm"));
        // **必须给它对偶值**。第一版没给，于是 `TryGetValue` 失败、它被跳过，
        // "删掉分组过滤"的变异**存活**——判据看起来测了串味，其实没测。
        //
        // **It needs a dual value.** The first version omitted one, so `TryGetValue` failed and the constraint was skipped, leaving the "drop the group filter" mutant
        // **alive** — the criterion looked like it tested leakage but did not.
        MathConstraint foreignConstraint = model.Constraints.Single(c => c.Name == "foreign_constraint");

        var dualSolution = new MetaDualSolution(
            new Dictionary<MathConstraint, Flt64> {
                [bounds[0]] = new Flt64(3.0),
                [bounds[1]] = new Flt64(4.0),
                [foreignConstraint] = new Flt64(100.0),
            },
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
        var map = new Csp1dDefaultShadowPriceMap();

        ICGPipeline<AbstractCsp1dShadowPriceArguments, LinearMetaModel<Flt64>, AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments>> asPipeline = pipeline;
        asPipeline.Refresh(map, model, dualSolution).IsOk.Should().BeTrue();

        ShadowPrice? price = map[new YieldOverProductionBoundShadowPriceKey("A", "mm")];
        price.Should().NotBeNull();
        price!.Price.ToDouble().Should().BeApproximately(
            7.0,
            1e-9,
            "same-key duals accumulate (3 + 4), and the foreign group's 100 must not leak in");
    }

    /// <summary>判据用的"别的管线"分组 / A stand-in group for "another pipeline".</summary>
    private sealed class ForeignPipelineGroup : IMetaConstraintGroup {
        public bool Lazy => false;

        public string Name => "foreign-pipeline";
    }

    /// <summary>**超产目标项用对应需求的系数** / The over-production objective uses the matching demand's coefficient.</summary>
    /// <remarks>
    /// `A` 系数 2、`B` 系数 7。若实现用字典首值，**两条都会是 2**——总数对、分项错。
    ///
    /// `A`'s coefficient is 2 and `B`'s is 7. If the implementation used the dictionary's first value, **both would be 2** — right total, wrong breakdown.
    /// </remarks>
    [Fact]
    public void YieldObjectivePipeline_ShouldUseTheMatchingDemandCoefficient() {
        var demandA = new ProductDemand<Flt64>(MakeProduct("A"), Millimetres(60.0));
        var demandB = new ProductDemand<Flt64>(MakeProduct("B"), Millimetres(70.0));
        var demands = new List<ProductDemand<Flt64>> { demandA, demandB };
        var config = new YieldModelingConfig<Flt64>(
            OverProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [KeyOf(demandA)] = new Flt64(2.0),
                [KeyOf(demandB)] = new Flt64(7.0),
            });
        var yield = new YieldAggregation(config, demands);
        var model = new LinearMetaModel<Flt64>();
        yield.Register(model).IsOk.Should().BeTrue();
        var pipeline = new YieldObjectivePipeline<Flt64>(yield, config, demands);

        IReadOnlyList<LinearMonomial<Flt64>> monomials = pipeline.ObjectiveMonomials();

        monomials.Should().HaveCount(2);
        double coefficientForA = monomials.Single(m => Equals(m.Symbol, yield.OverProduction[0])).Coefficient.ToDouble();
        double coefficientForB = monomials.Single(m => Equals(m.Symbol, yield.OverProduction[1])).Coefficient.ToDouble();
        coefficientForA.Should().BeApproximately(2.0, 1e-9, "A's own penalty is 2");
        coefficientForB.Should().BeApproximately(7.0, 1e-9, "B's own penalty is 7");
    }
}
