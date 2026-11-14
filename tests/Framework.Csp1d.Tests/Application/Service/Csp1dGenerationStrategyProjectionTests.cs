// 生成策略投影的唯一入口判据 / Criteria for the single generation-strategy projection point.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **生成策略的投影必须只有一处实现** / The generation-strategy projection must have exactly one implementation.
/// </summary>
/// <remarks>
/// **B361 记的缺口**："列生成侧与 MILP 侧的三组投影是两份复制代码，将来可能再次偏离。"
///
/// 这不是假想的风险：B358—B360 接三个扩展点时**列生成接了、MILP 直连没接**，
/// 结果**同一个策略在两条入口下行为不同**。**复制粘贴就是那次缺陷的成因。**
///
/// 本批把投影收进 `Csp1dGenerationStrategyProjection`，两条路径都调它。
///
/// **B361's ledger entry**: "the three projections on the column-generation side and the MILP side are two copies that may diverge again".
///
/// This is not a hypothetical risk: when B358—B360 wired the three hooks, **column generation got them and the MILP path did not**, so **one strategy behaved
/// differently per entry point**. **Copy-paste was the cause.**
///
/// This batch moves the projection into `Csp1dGenerationStrategyProjection`, called by both paths.
/// </remarks>
public class Csp1dGenerationStrategyProjectionTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static CuttingPlan<Flt64> MakePlan(string id = "p1") {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        return new CuttingPlan<Flt64>(
            id: id,
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(50.0)),
            });
    }

    /// <summary>带固定返回值的策略 / A strategy with fixed return values.</summary>
    private sealed class FixedStrategy : ICsp1dGenerationStrategy<Flt64> {
        private readonly bool _candidate;
        private readonly bool _dominance;
        private readonly string? _key;

        public FixedStrategy(string name, bool candidate, bool dominance, string? key) {
            Name = name;
            _candidate = candidate;
            _dominance = dominance;
            _key = key;
        }

        public string Name { get; }

        public bool AcceptCandidate(CuttingPlan<Flt64> candidate, IReadOnlyList<CuttingPlan<Flt64>> existingPlans) => _candidate;

        public bool AcceptDominance(CuttingPlan<Flt64> candidate, IReadOnlyList<CuttingPlan<Flt64>> existingPlans) => _dominance;

        public string? CanonicalKeyFor(CuttingPlan<Flt64> candidate) => _key;
    }

    /// <summary>**空策略列表投影出空列表** / An empty strategy list projects to empty lists.</summary>
    /// <remarks>
    /// **反向对照**：若投影无条件塞一个默认回调，两条路径就会**各多一次钩子调用**，
    /// 而判据只看"非空"是发现不了的。
    ///
    /// **Reverse control**: if the projection unconditionally inserted a default callback, both paths would gain an **unrequested hook call**, which a "non-empty" criterion
    /// could not detect.
    /// </remarks>
    [Fact]
    public void EmptyStrategies_ShouldProjectToEmptyLists() {
        IReadOnlyList<ICsp1dGenerationStrategy<Flt64>> strategies =
            global::System.Array.Empty<ICsp1dGenerationStrategy<Flt64>>();

        Csp1dGenerationStrategyProjection.CandidateFilters(strategies).Should().BeEmpty();
        Csp1dGenerationStrategyProjection.DominanceAcceptOverrides(strategies).Should().BeEmpty();
        Csp1dGenerationStrategyProjection.CanonicalKeyOverrides(strategies).Should().BeEmpty();
    }

    /// <summary>**投影必须保序，且每个策略一个回调** / The projection must preserve order with one callback per strategy.</summary>
    /// <remarks>
    /// 顺序有意义：解析规范键时是"**第一个返回非 null 的胜出**"（基线 `firstNotNullOfOrNull`）。
    /// 若投影把顺序打乱，两个策略都返回键时会**取错那一个**——而"非空"判据发现不了。
    ///
    /// Order matters: key resolution is "**the first non-null wins**" (baseline `firstNotNullOfOrNull`). If the projection reordered, a case with two key-providing
    /// strategies would **pick the wrong one** — invisible to a "non-empty" criterion.
    /// </remarks>
    [Fact]
    public void Projection_ShouldPreserveOrderAndCount() {
        var strategies = new List<ICsp1dGenerationStrategy<Flt64>> {
            new FixedStrategy("first", candidate: true, dominance: true, key: "key-first"),
            new FixedStrategy("second", candidate: false, dominance: false, key: "key-second"),
        };

        IReadOnlyList<global::System.Func<CuttingPlan<Flt64>, IReadOnlyList<CuttingPlan<Flt64>>, bool>> filters =
            Csp1dGenerationStrategyProjection.CandidateFilters(strategies);
        IReadOnlyList<global::System.Func<CuttingPlan<Flt64>, string?>> keys =
            Csp1dGenerationStrategyProjection.CanonicalKeyOverrides(strategies);

        filters.Should().HaveCount(2);
        keys.Should().HaveCount(2);
        CuttingPlan<Flt64> plan = MakePlan();
        filters[0](plan, global::System.Array.Empty<CuttingPlan<Flt64>>()).Should().BeTrue();
        filters[1](plan, global::System.Array.Empty<CuttingPlan<Flt64>>()).Should().BeFalse();
        keys[0](plan).Should().Be("key-first");
        keys[1](plan).Should().Be("key-second");
    }

    /// <summary>**键解析取第一个非 null 的覆盖** / Key resolution takes the first non-null override.</summary>
    /// <remarks>
    /// 这条钉住 `Resolve` 的语义，也就是基线 `firstNotNullOfOrNull`。
    /// 若实现改成"最后一个胜出"或"任一非 null 就拼串"，判据会红。
    ///
    /// This pins `Resolve`'s semantics, i.e. the baseline's `firstNotNullOfOrNull`. If the implementation switched to "last wins" or concatenated, this would fail.
    /// </remarks>
    [Fact]
    public void Resolve_ShouldTakeTheFirstNonNullOverride() {
        var strategies = new List<ICsp1dGenerationStrategy<Flt64>> {
            new FixedStrategy("abstain", candidate: true, dominance: true, key: null),
            new FixedStrategy("winner", candidate: true, dominance: true, key: "chosen"),
            new FixedStrategy("later", candidate: true, dominance: true, key: "ignored"),
        };
        CuttingPlan<Flt64> plan = MakePlan();

        CuttingPlanCanonicalKey resolved = Csp1dGenerationStrategyProjection.Resolve(
            plan,
            Csp1dGenerationStrategyProjection.CanonicalKeyOverrides(strategies));

        resolved.MaterialId.Should().Be("chosen", "the first non-null override wins");
        resolved.MachineId.Should().BeNull("the degenerate key built from a custom string has no machine id");
    }

    /// <summary>**全部放弃覆盖时退回默认键** / With every override abstaining the default key is used.</summary>
    /// <remarks>
    /// **反向对照**：只测"取第一个非 null"证明不了"没有非 null 时会退回默认键"。
    ///
    /// **Reverse control**: "take the first non-null" does not prove "with none non-null it falls back to the default key".
    /// </remarks>
    [Fact]
    public void Resolve_ShouldFallBackToTheDefaultKey() {
        var strategies = new List<ICsp1dGenerationStrategy<Flt64>> {
            new FixedStrategy("abstain", candidate: true, dominance: true, key: null),
        };
        CuttingPlan<Flt64> plan = MakePlan();

        CuttingPlanCanonicalKey resolved = Csp1dGenerationStrategyProjection.Resolve(
            plan,
            Csp1dGenerationStrategyProjection.CanonicalKeyOverrides(strategies));

        resolved.Should().Be(CuttingPlanCanonicalKey.From(plan));
        resolved.MaterialId.Should().Be("M1", "the default key carries the material id");
    }
}
