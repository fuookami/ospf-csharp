// dominance 接受覆盖的判据 / Criteria for the dominance acceptance override.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.Service;

/// <summary>
/// **`GenerationInput.DominanceAcceptOverrides` 必须真的作用于生成过程**
/// / `GenerationInput.DominanceAcceptOverrides` must actually affect generation.
/// </summary>
/// <remarks>
/// **B358 记的缺口**：`ICsp1dGenerationStrategy.AcceptDominance` 在 `src/` 里**零调用点**。
///
/// 更精确的情况是：`GenerationCollector` 里**早有** `_dominanceAcceptOverride` 钩子
/// （第 187 行会检查它），但**四个生成器构造收集器时一个都没传**——
/// 钩子在、没人喂。本批把 `AcceptDominance` 投影到 `GenerationInput` 并接到四个生成器。
///
/// **The gap recorded in B358**: `ICsp1dGenerationStrategy.AcceptDominance` has **zero call sites** in `src/`.
///
/// More precisely: `GenerationCollector` **already had** the `_dominanceAcceptOverride` hook (line 187 checks it), but **none of the four generators passed it** when
/// constructing the collector — hook present, nothing feeding it. This batch projects `AcceptDominance` into `GenerationInput` and feeds all four generators.
/// </remarks>
public class GenerationDominanceOverrideTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));

    private static Product<Flt64> MakeProduct(string id, double width) =>
        new(
            id: id,
            name: "product-" + id,
            width: new List<Quantity<Flt64>> { Millimetres(width) });

    private static GenerationInput<Flt64> MakeInput(params (string Id, double Width)[] products) =>
        new() {
            Materials = new List<Material<Flt64>> { MakeMaterial() },
            Demands = products
                .Select(product => new ProductDemand<Flt64>(
                    MakeProduct(product.Id, product.Width),
                    Millimetres(product.Width)))
                .ToList(),
        };

    /// <summary>**候选过滤返回 false 时，生成器必须一条都不产** / A candidate filter returning false must leave the generator empty.</summary>
    /// <remarks>
    /// **B359 记的缺口**：`AcceptCandidate`（`candidateFilters`）在 `src/` 里零调用点。
    /// `GenerationCollector` 现在有了 `candidateFilter` 参数，四个生成器与 `SimpleInitialGenerator` 都传了。
    ///
    /// 判据：恒定拒绝的过滤 → `NSumGenerator` 产出为空。
    /// 若生成器没把过滤传下去，产出与不带过滤相同（**非空**），这条会红。
    ///
    /// **The gap recorded in B359**: `AcceptCandidate` (`candidateFilters`) had zero call sites. `GenerationCollector` now has a `candidateFilter` parameter, and the four
    /// generators plus `SimpleInitialGenerator` all pass it.
    ///
    /// The criterion: an always-rejecting filter leaves `NSumGenerator` empty. If the generator failed to pass it down, the output would match the unfiltered run
    /// (**non-empty**) and this would fail.
    /// </remarks>
    [Fact]
    public void ARejectingCandidateFilter_ShouldProduceNothing() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());
        GenerationInput<Flt64> baselineInput = MakeInput(("A", 50.0), ("B", 50.0));
        generator.Generate(baselineInput).Count.Should().BeGreaterThan(0, "the fixture must yield plans without a filter");

        var filtered = new GenerationInput<Flt64> {
            Materials = baselineInput.Materials,
            Demands = baselineInput.Demands,
            CandidateFilters = new List<System.Func<CuttingPlan<Flt64>, IReadOnlyList<CuttingPlan<Flt64>>, bool>> {
                (_, _) => false,
            },
        };

        generator.Generate(filtered).Should().BeEmpty("the filter rejects every candidate");
    }

    /// <summary>**候选过滤必须看到 `ExistingPlans`** / The candidate filter must see `ExistingPlans`.</summary>
    /// <remarks>
    /// 这是 `AcceptCandidate` 与另外两个扩展点**唯一实质不同**的地方：它的第二个参数是"已有方案池"，
    /// 同一个策略在初始池（空）与定价（非空）下行为不同。
    ///
    /// 若实现传的是**空列表**（比如误传 `Array.Empty`），依赖它的策略会静默失效，
    /// **而"过滤被调用过"仍然成立**。所以断言"至少一次看到非空"。
    ///
    /// This is the **one substantive difference** between `AcceptCandidate` and the other two extension points: its second argument is the existing plan pool, so the same
    /// strategy behaves differently in the initial pool (empty) and pricing (non-empty).
    ///
    /// If the implementation passed an **empty list** (say `Array.Empty` by mistake), dependent strategies would silently break **while "the filter was called" still
    /// held**. Hence the assertion "at least one call saw a non-empty list".
    /// </remarks>
    [Fact]
    public void TheCandidateFilter_ShouldSeeTheExistingPlans() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());
        GenerationInput<Flt64> baselineInput = MakeInput(("A", 50.0), ("B", 50.0));
        IReadOnlyList<CuttingPlan<Flt64>> alreadyThere = generator.Generate(baselineInput);
        alreadyThere.Should().NotBeEmpty("the fixture needs plans to hand to the filter");
        int seenNonEmpty = 0;

        var input = new GenerationInput<Flt64> {
            Materials = baselineInput.Materials,
            Demands = baselineInput.Demands,
            ExistingPlans = alreadyThere,
            CandidateFilters = new List<System.Func<CuttingPlan<Flt64>, IReadOnlyList<CuttingPlan<Flt64>>, bool>> {
                (_, existing) => {
                    if (existing.Count > 0) {
                        ++seenNonEmpty;
                    }
                    return true;
                },
            },
        };
        _ = generator.Generate(input);

        seenNonEmpty.Should().BeGreaterThan(
            0,
            "the filter's second argument is the existing plan pool, not an empty list");
    }

    /// <summary>**全部通过的覆盖不改变产出** / Overrides that all pass leave the output unchanged.</summary>
    /// <remarks>
    /// 基线语义是 `all { it(plan, existing) }`；全 true 时应当**一条都不少**。
    /// 若实现把"有覆盖"当成"全拒"，这条会红。
    ///
    /// The baseline semantics is `all { it(plan, existing) }`; with all true **nothing may be lost**. If the implementation treated "has overrides" as "reject
    /// everything", this would fail.
    /// </remarks>
    [Fact]
    public void AllPassingOverrides_ShouldNotChangeTheOutput() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());
        GenerationInput<Flt64> baselineInput = MakeInput(("A", 50.0), ("B", 50.0));

        int withoutOverrides = generator.Generate(baselineInput).Count;

        var withOverrides = new GenerationInput<Flt64> {
            Materials = baselineInput.Materials,
            Demands = baselineInput.Demands,
            DominanceAcceptOverrides = new List<System.Func<CuttingPlan<Flt64>, IReadOnlyList<CuttingPlan<Flt64>>, bool>> {
                (_, _) => true,
                (_, _) => true,
            },
        };
        int withAllTrue = generator.Generate(withOverrides).Count;

        withAllTrue.Should().Be(withoutOverrides, "overrides that all pass must not remove any plan");
    }

    /// <summary>**任一覆盖返回 false，候选就必须被拒** / Any override returning false must reject the candidate.</summary>
    /// <remarks>
    /// **这条钉的是 `All` 而不是 `Any`。** 两个覆盖里**只有第二个返回 false**——
    ///
    /// | 实现 | 结果 |
    /// | --- | --- |
    /// | `All(...)`（基线） | **0 条** |
    /// | `Any(...)`（错误） | 与不带覆盖相同 |
    ///
    /// 若只放一个"恒 false"的覆盖，`All` 与 `Any` 都会拒，**区分不出来**。
    ///
    /// **This pins `All`, not `Any`.** Of two overrides **only the second returns false** (table above). A single always-false override would be rejected by both `All` and
    /// `Any` and **could not tell them apart**.
    /// </remarks>
    [Fact]
    public void OneRejectingOverride_ShouldRejectEveryPlan() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());
        GenerationInput<Flt64> baselineInput = MakeInput(("A", 50.0), ("B", 50.0));

        int withoutOverrides = generator.Generate(baselineInput).Count;
        withoutOverrides.Should().BeGreaterThan(0, "the fixture must yield plans without overrides");

        var withOverrides = new GenerationInput<Flt64> {
            Materials = baselineInput.Materials,
            Demands = baselineInput.Demands,
            DominanceAcceptOverrides = new List<System.Func<CuttingPlan<Flt64>, IReadOnlyList<CuttingPlan<Flt64>>, bool>> {
                (_, _) => true,
                (_, _) => false,
            },
        };
        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(withOverrides);

        plans.Should().BeEmpty("all overrides must pass, so one rejecting override removes every plan");
    }

    /// <summary>**覆盖必须看到已接受的方案** / The override must see the accepted plans.</summary>
    /// <remarks>
    /// 基线把 `existing` 传进覆盖函数，调用方靠它做支配判断。若实现传的是**空列表**，
    /// 依赖 `existing` 的策略会静默失效——**而"被调用过"这件事仍然成立**。
    ///
    /// 所以这里断言"**至少有一次调用看到非空 `existing`**"：只有多切片搜索接受了第一条方案之后，
    /// 后续候选才会看到它。
    ///
    /// The baseline passes `existing` into the override; callers rely on it for dominance. If the implementation passed an **empty list**, strategies depending on
    /// `existing` would silently break — **while "it was called" would still hold**.
    ///
    /// So this asserts "**at least one call saw a non-empty `existing`**": only after the search accepts a first plan do later candidates see it.
    /// </remarks>
    [Fact]
    public void TheOverride_ShouldSeeTheAcceptedPlans() {
        var generator = new NSumGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());
        GenerationInput<Flt64> baselineInput = MakeInput(("A", 50.0), ("B", 50.0));
        int seenNonEmpty = 0;
        int calls = 0;

        var input = new GenerationInput<Flt64> {
            Materials = baselineInput.Materials,
            Demands = baselineInput.Demands,
            DominanceAcceptOverrides = new List<System.Func<CuttingPlan<Flt64>, IReadOnlyList<CuttingPlan<Flt64>>, bool>> {
                (_, existing) => {
                    ++calls;
                    if (existing.Count > 0) {
                        ++seenNonEmpty;
                    }
                    return true;
                },
            },
        };
        _ = generator.Generate(input);

        calls.Should().BeGreaterThan(0, "the override must actually be invoked");
        seenNonEmpty.Should().BeGreaterThan(
            0,
            "later candidates must see the plans already accepted, or existing-aware strategies break silently");
    }
}
