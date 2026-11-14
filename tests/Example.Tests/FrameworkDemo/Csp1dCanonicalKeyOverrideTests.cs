#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **生成策略的 `CanonicalKeyFor` 扩展点必须真的影响去重** / The generation strategy's `CanonicalKeyFor` extension point must actually affect deduplication.
/// </summary>
/// <remarks>
/// **这批要闭的缺口**：`ICsp1dGenerationStrategy` 上定义了三个扩展点
/// （`AcceptCandidate` / `CanonicalKeyFor` / `AcceptDominance`），
/// **但整个 `src/` 里一个调用点都没有**——典型的"接口在、连接件是空壳"。
/// B358 只接 `CanonicalKeyFor`，本文件是它的判据。
///
/// **The gap this batch closes**: `ICsp1dGenerationStrategy` defines three extension points (`AcceptCandidate` / `CanonicalKeyFor` / `AcceptDominance`),
/// **yet `src/` has zero call sites** — the textbook "interfaces present, connecting piece is a shell". B358 wires only `CanonicalKeyFor`; this file is its criterion.
/// </remarks>
public sealed class Csp1dCanonicalKeyOverrideTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Csp1dProblem<Flt64> MakeProblem() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));
        var productA = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var productB = new Product<Flt64>(
            id: "B",
            name: "product-b",
            width: new List<Quantity<Flt64>> { Millimetres(60.0) });
        var builder = new Csp1dProblemBuilder<Flt64>();
        builder.AddProduct(productA);
        builder.AddProduct(productB);
        builder.AddMaterial(material);
        builder.AddDemand(new ProductDemand<Flt64>(productA, Millimetres(50.0)));
        builder.AddDemand(new ProductDemand<Flt64>(productB, Millimetres(60.0)));
        return builder.Build();
    }

    /// <summary>把所有方案映射到同一个键的策略 / A strategy mapping every plan to the same key.</summary>
    private sealed class ConstantKeyStrategy : ICsp1dGenerationStrategy<Flt64> {
        public string Name => "constant-key";

        public string? CanonicalKeyFor(CuttingPlan<Flt64> candidate) => "same-key-for-everything";
    }

    /// <summary>从不覆盖（返回 null）的策略 / A strategy that never overrides (returns null).</summary>
    private sealed class NullKeyStrategy : ICsp1dGenerationStrategy<Flt64> {
        public string Name => "null-key";

        public string? CanonicalKeyFor(CuttingPlan<Flt64> _) => null;
    }

    /// <summary>记录收到的生成输入 / An initial generator that records the generation input it received.</summary>
    private sealed class CapturingInitialGenerator : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> {
        public GenerationInput<Flt64>? LastInput { get; private set; }

        public IReadOnlyList<CuttingPlan<Flt64>> Generate(GenerationInput<Flt64> input) {
            LastInput = input;
            return global::System.Array.Empty<CuttingPlan<Flt64>>();
        }
    }

    /// <summary>统计 `AcceptDominance` 被调用次数的策略 / A strategy counting `AcceptDominance` calls.</summary>
    private sealed class DominanceStrategy : ICsp1dGenerationStrategy<Flt64> {
        public string Name => "dominance";

        public int Calls { get; private set; }

        public bool AcceptDominance(CuttingPlan<Flt64> candidate, IReadOnlyList<CuttingPlan<Flt64>> existingPlans) {
            ++Calls;
            return true;
        }
    }

    /// <summary>**`AcceptDominance` 必须投影到 `GenerationInput` 上** / `AcceptDominance` must be projected onto `GenerationInput`.</summary>
    /// <remarks>
    /// **B358 记的缺口**：`AcceptDominance` 在 `src/` 里零调用点。生成器级别的判据
    /// （`GenerationDominanceOverrideTests`）证明了"字段一旦有值就会被消费"，
    /// **但证明不了"配置的策略真的变成了那个字段"**——两层之间还能断。
    ///
    /// 这条判据补上那一层：捕获初始生成器收到的 `GenerationInput`，断言
    /// `DominanceAcceptOverrides` **非空**。
    ///
    /// **The gap recorded in B358**: `AcceptDominance` had zero call sites. The generator-level criteria (`GenerationDominanceOverrideTests`) prove "once the field has a
    /// value it is consumed", **but not "the configured strategy really became that field"** — the two layers can still be disconnected.
    ///
    /// This criterion closes that layer: it captures the `GenerationInput` the initial generator receives and asserts `DominanceAcceptOverrides` is **non-empty**.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldProjectAcceptDominanceOntoTheGenerationInput() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var capture = new CapturingInitialGenerator();
        var generation = new Csp1dColumnGeneration<Flt64>(
            new MockColumnGenerationSolver(),
            initialGenerator: capture);
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .AddGenerationStrategy(new DominanceStrategy())
            .Build();

        await generation.SolveWithTraceAsync(problem, config);

        capture.LastInput.Should().NotBeNull("the initial generator must be invoked");
        capture.LastInput!.DominanceAcceptOverrides.Should().NotBeEmpty(
            "the configured AcceptDominance must be projected onto the generation input");
    }

    /// <summary>统计 `AcceptCandidate` 被调用次数的策略 / A strategy counting `AcceptCandidate` calls.</summary>
    private sealed class CandidateStrategy : ICsp1dGenerationStrategy<Flt64> {
        public string Name => "candidate";

        public int Calls { get; private set; }

        public bool AcceptCandidate(CuttingPlan<Flt64> candidate, IReadOnlyList<CuttingPlan<Flt64>> existingPlans) {
            ++Calls;
            return true;
        }
    }

    /// <summary>**`AcceptCandidate` 必须投影到 `GenerationInput` 上** / `AcceptCandidate` must be projected onto `GenerationInput`.</summary>
    /// <remarks>
    /// 与 `AcceptDominance` 那条同形：生成器级判据只能证明"字段有值就会被消费"，
    /// **证明不了"配置的策略真的变成了那个字段"**。
    ///
    /// **This mirrors the `AcceptDominance` criterion**: a generator-level criterion can only prove "a non-empty field is consumed", **not "the configured strategy
    /// really became that field"**.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldProjectAcceptCandidateOntoTheGenerationInput() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var capture = new CapturingInitialGenerator();
        var generation = new Csp1dColumnGeneration<Flt64>(
            new MockColumnGenerationSolver(),
            initialGenerator: capture);
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .AddGenerationStrategy(new CandidateStrategy())
            .Build();

        await generation.SolveWithTraceAsync(problem, config);

        capture.LastInput.Should().NotBeNull("the initial generator must be invoked");
        capture.LastInput!.CandidateFilters.Should().NotBeEmpty(
            "the configured AcceptCandidate must be projected onto the generation input");
    }

    /// <summary>
    /// **候选过滤在初始池收到空列表、在定价收到当前列池** / The candidate filter sees an empty list in the initial pool and the current pool in pricing.
    /// </summary>
    /// <remarks>
    /// **这是 `AcceptCandidate` 与另外两个扩展点的实质差别**：基线在 `initialPlanPool` 里
    /// **不设 `existingPlans`**（所以是空列表），在定价里传**当前列池**。
    ///
    /// **我第一版判据写错了**：断言"初始池的过滤永远看到空列表"，实测 `found 2`——
    /// 那是因为**定价也会调用同一个过滤**，而定价传的是含 2 条方案的当前列池。
    /// 判据把两种上下文混为一谈了。
    ///
    /// 正确形态是**看调用序列**：第一次调用（初始池）看到 **0 条**，
    /// 之后某次调用（定价）看到 **非 0 条**。
    ///
    /// | 实现 | 序列 |
    /// | --- | --- |
    /// | 正确（基线） | 首项 `0`，且存在 `> 0` |
    /// | 到处都是空列表 | 全 `0` → **无 `> 0`** |
    /// | 到处都填满 | 首个就 `> 0` → **首项不是 `0`** |
    ///
    /// **This is the substantive difference** between `AcceptCandidate` and the other two: the baseline **does not set `existingPlans`** in `initialPlanPool` (hence an empty
    /// list) and passes the **current column pool** in pricing.
    ///
    /// **My first version of this criterion was wrong**: it asserted "the initial-pool filter always sees an empty list" and measured `found 2` — because **pricing calls
    /// the same filter too**, with a current pool of 2 plans. The criterion conflated the two contexts.
    ///
    /// The correct shape is **the call sequence**: the first call (initial pool) sees **0**, and some later call (pricing) sees **non-zero** (table above).
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldGiveTheCandidateFilterDifferentExistingListsPerContext() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var seenCounts = new List<int>();
        var generation = new Csp1dColumnGeneration<Flt64>(new MockColumnGenerationSolver());
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .AddGenerationStrategy(new CountingCandidateStrategy(
                (IReadOnlyList<CuttingPlan<Flt64>> existing) => {
                    seenCounts.Add(existing.Count);
                }))
            .Build();

        await generation.SolveWithTraceAsync(problem, config);

        seenCounts.Should().NotBeEmpty("the candidate filter must be invoked at least once");
        seenCounts[0].Should().Be(
            0,
            "the initial pool has no existing plans, so the first call must see an empty list");
        seenCounts.Should().Contain(
            count => count > 0,
            "pricing must pass the current column pool, otherwise the hook cannot express 'do not collide with existing plans'");
    }

    /// <summary>回调式候选策略 / A callback-based candidate strategy.</summary>
    private sealed class CountingCandidateStrategy : ICsp1dGenerationStrategy<Flt64> {
        private readonly global::System.Action<IReadOnlyList<CuttingPlan<Flt64>>> _onExisting;

        public CountingCandidateStrategy(global::System.Action<IReadOnlyList<CuttingPlan<Flt64>>> onExisting) {
            _onExisting = onExisting;
        }

        public string Name => "counting-candidate";

        public bool AcceptCandidate(CuttingPlan<Flt64> candidate, IReadOnlyList<CuttingPlan<Flt64>> existingPlans) {
            _onExisting(existingPlans);
            return true;
        }
    }

    /// <summary>**覆盖生效时，不同方案必须被折叠成一条** / With an active override, different plans must collapse into one.</summary>
    /// <remarks>
    /// `MakeProblem()` 有两个不同产品（50mm 与 60mm），初始生成器会产出**两条规范键不同**的方案。
    /// `ConstantKeyStrategy` 把两条都映射成同一个字符串，于是**去重后只剩一条**。
    ///
    /// **若 `CanonicalKeyFor` 没有接线**（B358 之前的状态），两条都会留下，`InitialPlanCount == 2`。
    ///
    /// `MakeProblem()` has two distinct products (50mm and 60mm), so the initial generator yields **two plans with different canonical keys**.
    /// `ConstantKeyStrategy` maps both to the same string, so **deduplication leaves one**.
    ///
    /// **If `CanonicalKeyFor` were unwired** (the pre-B358 state), both would remain and `InitialPlanCount` would be 2.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldCollapsePlansWhenTheStrategyOverridesTheCanonicalKey() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var generation = new Csp1dColumnGeneration<Flt64>(new MockColumnGenerationSolver());
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .AddGenerationStrategy(new ConstantKeyStrategy())
            .Build();

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem, config);

        result.Trace.InitialPlanCount.ToULong().Should().Be(
            1UL,
            "the strategy maps both products' plans to one key, so only one may survive deduplication");
    }

    /// <summary>**不覆盖时必须保留两条——否则判据证明不了"覆盖生效"** / Without an override both plans must survive, or the criterion proves nothing.</summary>
    /// <remarks>
    /// **这是上一条的反向对照**。若没有它，上一条在"去重逻辑本来就只留一条"的实现里也会绿，
    /// 那样就证明不了**是覆盖起了作用**。
    ///
    /// **This is the reverse control for the previous criterion.** Without it the previous one would also pass on an implementation that always keeps a single plan, which
    /// would prove nothing about **whether the override did the work**.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldKeepBothPlansWhenTheStrategyDoesNotOverride() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var generation = new Csp1dColumnGeneration<Flt64>(new MockColumnGenerationSolver());
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .AddGenerationStrategy(new NullKeyStrategy())
            .Build();

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem, config);

        result.Trace.InitialPlanCount.ToULong().Should().Be(
            2UL,
            "a null override must fall back to the default canonical key, which differs for the two products");
    }
}
