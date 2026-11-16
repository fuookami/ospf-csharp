// MILP 直连路径的生成策略扩展点判据 / Criteria for the generation-strategy hooks on the MILP-direct path.

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
/// **MILP 直连路径也必须让三个生成策略扩展点生效**
/// / The MILP-direct path must honour the three generation-strategy hooks too.
/// </summary>
/// <remarks>
/// **记的缺口**：`Csp1dMilp.InitialPlansAsync` 是 `Csp1dColumnGeneration.InitialPlanPoolAsync` 的
/// **平行副本**，但**三个扩展点一个都没接**——于是同一个策略**经由列生成生效、经由 MILP 直连静默失效**。
///
/// 基线不是这样： 的 `initialPlans` 签名里就有
/// `candidateFilters` / `canonicalKeyOverrides` / `dominanceAcceptOverrides` 三者，且都在用。
///
/// **这里把两侧拉齐。**
///
/// **The gap recorded**: `Csp1dMilp.InitialPlansAsync` is a **parallel copy** of `Csp1dColumnGeneration.InitialPlanPoolAsync` with **none of the three hooks wired** — so
/// the same strategy works through column generation and **silently fails** through the MILP path.
///
/// The code is not like that: `initialPlans` at takes `candidateFilters` / `canonicalKeyOverrides` / `dominanceAcceptOverrides` and uses all three.
///
/// ** aligns the two.**
/// </remarks>
public sealed class Csp1dMilpGenerationStrategyTests {
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

    /// <summary>记录收到的生成输入 / Records the generation input it received.</summary>
    private sealed class CapturingInitialGenerator : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> {
        private readonly bool _yieldPlans;

        public CapturingInitialGenerator(bool yieldPlans = false) {
            _yieldPlans = yieldPlans;
        }

        public GenerationInput<Flt64>? LastInput { get; private set; }

        public IReadOnlyList<CuttingPlan<Flt64>> Generate(GenerationInput<Flt64> input) {
            LastInput = input;
            if (!_yieldPlans) {
                return global::System.Array.Empty<CuttingPlan<Flt64>>();
            }
            // **必须产出两条规范键不同的方案**，否则去重没有东西可去，"覆盖是否生效"观察不到。
            // **Two plans with different canonical keys are required**, or deduplication has nothing to do and "did the override apply" is unobservable.
            Material<Flt64> material = input.Materials[0];
            var plans = new List<CuttingPlan<Flt64>>();
            foreach (ProductDemand<Flt64> demand in input.Demands) {
                plans.Add(new CuttingPlan<Flt64>(
                    id: "cap-" + demand.Product.Id,
                    material: material,
                    slices: new List<CuttingPlanSlice<Flt64>> {
                        new(demand.Product, demand.Product.Width[0]),
                    },
                    demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                        new(demand.Product, demand.Quantity),
                    }));
            }
            return plans;
        }
    }

    /// <summary>三个扩展点全部有实现、并统计调用次数的策略 / A strategy implementing all three hooks and counting calls.</summary>
    private sealed class FullStrategy : ICsp1dGenerationStrategy<Flt64> {
        public string Name => "full";

        public int CandidateCalls { get; private set; }

        public int DominanceCalls { get; private set; }

        public int KeyCalls { get; private set; }

        public bool AcceptCandidate(CuttingPlan<Flt64> candidate, IReadOnlyList<CuttingPlan<Flt64>> existingPlans) {
            ++CandidateCalls;
            return true;
        }

        public bool AcceptDominance(CuttingPlan<Flt64> candidate, IReadOnlyList<CuttingPlan<Flt64>> existingPlans) {
            ++DominanceCalls;
            return true;
        }

        public string? CanonicalKeyFor(CuttingPlan<Flt64> candidate) {
            ++KeyCalls;
            return "one-key-for-all";
        }
    }

    /// <summary>**MILP 直连路径必须把三个覆盖投影到生成输入上** / The MILP path must project all three hooks onto the generation input.</summary>
    /// <remarks>
    /// 判据落在"**输入上带了三个回调**"：`CandidateFilters` 与 `DominanceAcceptOverrides` 是
    /// `GenerationInput` 的字段，可直接断言；`canonicalKeyOverrides` 不是字段（它只用于去重），
    /// 所以用**调用计数**证明它被用过——生成器被调用一次，去重按每个方案解析一次键。
    ///
    /// **一条输入上没有覆盖，这条就会红。**
    ///
    /// The criterion is "**the input carries the three callbacks**": `CandidateFilters` and `DominanceAcceptOverrides` are `GenerationInput` fields and can be asserted
    /// directly; `canonicalKeyOverrides` is not a field (it only drives deduplication), so **call counting** proves it was used.
    ///
    /// **If the input lacked the overrides, this would fail.**
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldProjectAllThreeHooksOntoTheGenerationInput() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var capture = new CapturingInitialGenerator();
        var strategy = new FullStrategy();
        var milp = new Csp1dMilp<Flt64>(new MockColumnGenerationSolver(), capture);
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .AddGenerationStrategy(strategy)
            .Build();

        await milp.SolveAsync(problem, config);

        capture.LastInput.Should().NotBeNull("the initial generator must be invoked");
        capture.LastInput!.CandidateFilters.Should().NotBeEmpty("AcceptCandidate must be projected");
        capture.LastInput!.DominanceAcceptOverrides.Should().NotBeEmpty("AcceptDominance must be projected");
    }

    /// <summary>**MILP 路径的去重必须真的问覆盖要键** / The MILP path's dedup must actually ask the override for a key.</summary>
    /// <remarks>
    /// **这条是变异测试逼出来的。** 最初只断言"输入上带了三组回调"，
    /// 于是变异"去重改回默认键"**存活**——因为替身返回空，去重根本没有跑。
    ///
    /// 修法：替身**产出两条规范键不同的方案**，再用 `CanonicalKeyFor` 的**调用计数**证明去重问过它。
    ///
    /// 这是"**判据落在错误的层**"的第 N 次：断言"字段非空"证明不了"字段被用"。
    ///
    /// **A mutation forced this criterion.** My first version only asserted "the input carries the three callbacks", so the mutation "dedup falls back to the default key"
    /// **survived** — the double returned nothing, so dedup never ran.
    ///
    /// The fix: have the double **yield two plans with different canonical keys**, then use `CanonicalKeyFor`'s **call count** to prove dedup asked it. This is yet another
    /// case of **the criterion landing on the wrong layer**: "the field is non-empty" cannot prove "the field is used".
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldAskTheCanonicalKeyOverrideDuringDeduplication() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var capture = new CapturingInitialGenerator(yieldPlans: true);
        var strategy = new FullStrategy();
        var milp = new Csp1dMilp<Flt64>(new MockColumnGenerationSolver(), capture);
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .AddGenerationStrategy(strategy)
            .Build();

        await milp.SolveAsync(problem, config);

        strategy.KeyCalls.Should().BeGreaterThan(
            0,
            "deduplication must resolve keys through the strategy override, not the default canonical key");
    }

    /// <summary>**没有策略时不得凭空产生覆盖** / Without strategies no overrides may appear.</summary>
    /// <remarks>
    /// **反向对照**：若实现无条件塞一个"恒 true"的默认覆盖，上一条仍会绿，
    /// 但那就等于**多了一次没人要求的钩子调用**。这条确认空配置下两个列表都是空的。
    ///
    /// **Reverse control**: if the implementation unconditionally inserted an always-true default override, the previous criterion would still pass, but that would be **an
    /// unrequested hook call**. This confirms both lists are empty under an empty configuration.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldNotInventOverridesWithoutStrategies() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var capture = new CapturingInitialGenerator();
        var milp = new Csp1dMilp<Flt64>(new MockColumnGenerationSolver(), capture);

        await milp.SolveAsync(problem);

        capture.LastInput.Should().NotBeNull();
        capture.LastInput!.CandidateFilters.Should().BeEmpty("no strategy was configured");
        capture.LastInput!.DominanceAcceptOverrides.Should().BeEmpty("no strategy was configured");
    }
}
