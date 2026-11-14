// `ExtractShadowPriceMap` 的判据 / Criteria for `ExtractShadowPriceMap`.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Utils.Error;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **`ExtractShadowPriceMap` 必须按 `constraint.Origin.Args` 的键回填，并累加同键对偶** / It must key off `constraint.Origin.Args` and accumulate same-key duals.
/// </summary>
/// <remarks>
/// **B388 记的缺口**："生产代码传的那个 `converter` 无判据"。
///
/// 这个方法在**没有 model** 时用：直接读 `constraint.Origin.Args`，不走 CGPipeline refresh。
/// 两条路径必须给出一致结果，否则**定价阶段的 reduced cost 取决于走了哪条路**。
///
/// 本批同时删掉了一个**只写不读**的 `Csp1dDefaultShadowPriceMap`（基线与 C# 都有，
/// 每轮迭代白写一次、从不被读取或返回）。**删除不改变行为**，只是清理。
///
/// **B388's gap**: "the `converter` production code passes has no criteria". This method is used **when there is no model**: it reads `constraint.Origin.Args` directly
/// instead of going through CGPipeline refresh. Both paths must agree, or **reduced cost depends on which path ran**. This batch also deleted a **write-only**
/// `Csp1dDefaultShadowPriceMap` (present in both the baseline and C# — written once per iteration, never read or returned). **Deleting it changes no behaviour**, it is
/// pure cleanup.
/// </remarks>
public class ExtractShadowPriceMapTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Product<Flt64> MakeProduct(string id) =>
        new(
            id: id,
            name: id,
            width: new List<Quantity<Flt64>> { Millimetres(60.0) });

    private static Csp1dProduceContext<Flt64> MakeContext(IReadOnlyList<ProductDemand<Flt64>> demands) {
        var produce = new ProduceAggregation<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>>(),
            demands: demands,
            materials: new List<Material<Flt64>>(),
            machines: new List<Machine<Flt64>>());
        return new Csp1dProduceContextBuilder<Flt64>(new ProduceInput<Flt64>(
                cuttingPlans: new List<CuttingPlan<Flt64>>(),
                demands: demands,
                materials: new List<Material<Flt64>>(),
                machines: new List<Machine<Flt64>>()))
            .IsFinalMilp(true)
            .Build();
    }

    /// <summary>**键从 `Origin.Args` 来，同键对偶累加** / Keys come from `Origin.Args` and same-key duals accumulate.</summary>
    /// <remarks>
    /// 两条约束、**同一个键**，对偶 3 与 4 ⇒ 期望 **7**。
    ///
    /// | 实现错法 | 结果 |
    /// | --- | --- |
    /// | 覆盖而非累加 | 4 |
    /// | 键取自别处 | 查不到 |
    /// | 正确 | **7** |
    ///
    /// Two constraints with the **same key** and duals 3 and 4 give **7** (table above).
    /// </remarks>
    [Fact]
    public void ExtractShadowPriceMap_ShouldAccumulateSameKeyDualsFromOriginArgs() {
        var demandA = new ProductDemand<Flt64>(MakeProduct("A"), Millimetres(60.0));
        Csp1dProduceContext<Flt64> context = MakeContext(new List<ProductDemand<Flt64>> { demandA });

        var key = new ProductDemandShadowPriceKey("A", "mm");
        var model = new LinearMetaModel<Flt64>();
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> constraints = AddTwoConstraints(model, key);

        var dualSolution = new Dictionary<IConstraint<Flt64, LinearCategory>, Flt64> {
            [constraints[0]] = new Flt64(3.0),
            [constraints[1]] = new Flt64(4.0),
        };

        ShadowPriceMap<Flt64> prices = context.ExtractShadowPriceMap(dualSolution);

        prices[key].Should().NotBeNull("the key must come from the constraint's Origin.Args");
        prices[key]!.Value.ToDouble().Should().BeApproximately(
            7.0,
            1e-9,
            "same-key duals accumulate (3 + 4), they do not overwrite");
    }

    /// <summary>**没有 `Origin.Args` 的约束被跳过，不产生键**——反向对照 / Constraints without `Origin.Args` are skipped — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"**给每条约束都造一个键**"的实现也可能蒙混过关。
    ///
    /// Without this, an implementation that **invents a key for every constraint** could slip through.
    /// </remarks>
    [Fact]
    public void ExtractShadowPriceMap_ShouldSkipConstraintsWithoutArgs() {
        var demandA = new ProductDemand<Flt64>(MakeProduct("A"), Millimetres(60.0));
        Csp1dProduceContext<Flt64> context = MakeContext(new List<ProductDemand<Flt64>> { demandA });

        var model = new LinearMetaModel<Flt64>();
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> constraints = AddTwoConstraints(model, key: null);

        var dualSolution = new Dictionary<IConstraint<Flt64, LinearCategory>, Flt64> {
            [constraints[0]] = new Flt64(5.0),
        };

        ShadowPriceMap<Flt64> prices = context.ExtractShadowPriceMap(dualSolution);

        // **断言"任何键都查不到"，而不是只查一个键。** 第一版只查 `("A","mm")`，
        // 于是"给缺键的约束造一个 `("bogus","x")` 兜底键"的变异**存活**——
        // 兜底键不是 `("A","mm")`，判据查不到它，就绿了。
        //
        // **Assert that *no* key is present, not just one.** The first version only probed `("A","mm")`, so a mutant that **invents a `("bogus","x")` fallback key** for
        // key-less constraints **survived** — the fallback is not `("A","mm")`, the probe missed it, and the criterion went green.
        prices[new ProductDemandShadowPriceKey("bogus", "x")].Should().BeNull(
            "a constraint without a CSP1D args key must not get a fallback key either");
        prices[new ProductDemandShadowPriceKey("A", "mm")].Should().BeNull(
            "a constraint without a CSP1D args key must not produce any shadow price");
    }

    /// <summary>建两条带 `Origin` 的线性约束 / Build two linear constraints carrying `Origin`.</summary>
    /// <param name="model">提供 token 表与转换器的模型 / Model supplying the token table and converter.</param>
    /// <param name="key">影子价格键，null 表示不带 / Shadow price key, null for none.</param>
    /// <returns>两条线性约束 / The two linear constraints.</returns>
    /// <remarks>
    /// `LinearConstraintImpl` 的构造函数是 `internal`，**但 `Create` 是公开工厂**且接受 `origin`。
    /// `origin` 是一个已加进模型的 `MathConstraint`——**`Origin` 正是 `ExtractShadowPriceMap`
    /// 读取 `Args` 的入口**。
    ///
    /// 关系用 `LinearRelationImpl` + 空 `LinearFlattenData` 构造：
    /// **判据只关心键与对偶，不关心约束的数学内容**。
    ///
    /// `LinearConstraintImpl`'s constructor is `internal`, **but `Create` is a public factory** accepting `origin` — a `MathConstraint` already added to the model,
    /// which is **exactly where `ExtractShadowPriceMap` reads `Args` from**. The relation is a `LinearRelationImpl` over empty `LinearFlattenData`:
    /// **the criterion cares about keys and duals, not the constraint's arithmetic**.
    /// </remarks>
    private static IReadOnlyList<IConstraint<Flt64, LinearCategory>> AddTwoConstraints(
        LinearMetaModel<Flt64> model,
        Csp1dShadowPriceKey? key) {
        var result = new List<IConstraint<Flt64, LinearCategory>>();
        for (int i = 0; i < 2; i++) {
            model.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.Zero),
                    new LinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison.LE),
                null,
                name: $"constraint_{i}",
                args: key);
            MathConstraint origin = model.Constraints[^1];

            var relation = new LinearRelationImpl<Flt64>(
                new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.Zero),
                Comparison.LE,
                Name: $"relation_{i}");
            Result<LinearConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>> created = LinearConstraintImpl<Flt64>.Create(
                relation,
                model.Tokens,
                model.Converter,
                name: $"impl_{i}",
                origin: origin);
            created.IsOk.Should().BeTrue();
            result.Add(created.Value!);
        }
        return result;
    }
}
