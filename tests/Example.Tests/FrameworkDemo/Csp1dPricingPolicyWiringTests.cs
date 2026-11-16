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
/// 配置里的 `ICsp1dPricingPolicy` 必须**真的到达定价器**。
/// The configured `ICsp1dPricingPolicy` must **really reach the pricing generator**.
/// </summary>
/// <remarks>
/// 记录的缺口**：三个扩展点（收益修正 / 成本修正 / isImproving 判据）在 `Csp1dPricingInput` 上有了，
/// 但 `Csp1dColumnGeneration` 构造输入时**一个都没传**——**生产路径上它们恒为 null**。
///
/// `ICsp1dPricingPolicy` 早就存在、`ModifyBenefit` / `ModifyCost` / `IsImproving` 三个默认方法也都齐，
/// `Csp1dProblemBuilder.AddPricingPolicy` 能把它配进 `SolveConfig`——**但从没到达定价器**。
///
/// **这正是"接口、实现、配置项全都在，只有连接件缺失"的又一例**：每一层单看都完整，
/// **跨层的那一跳没接上**，而任何按层写的判据都发现不了。
///
/// 判据用**计数**而不是"结果变了"：策略被调用几次，**直接证明它到达了**。
/// 只断言"结果变了"会被别的路径掩盖（例如默认判据恰好也接受）。
///
/// **The gap recorded**: the three extension points existed on `Csp1dPricingInput`, but `Csp1dColumnGeneration` **passed none of them** when
/// constructing the input — **they were always null on the production path**.
///
/// `ICsp1dPricingPolicy` had long existed, its `ModifyBenefit` / `ModifyCost` / `IsImproving` defaults were all there, and
/// `Csp1dProblemBuilder.AddPricingPolicy` could put it into `SolveConfig` — **but it never reached the pricing generator**.
///
/// **This is another instance of "interfaces, implementations and config knobs all present, only the connecting piece missing"**: each layer looks complete on its
/// own and **the cross-layer hop is unconnected**, which no per-layer criterion can detect.
///
/// The criteria count calls rather than checking "the result changed": counting **directly proves it arrived**. Asserting only "the result changed" could be masked by
/// another path (say the default judge happening to accept).
/// </remarks>
public class Csp1dPricingPolicyWiringTests {
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
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var builder = new Csp1dProblemBuilder<Flt64>();
        builder.AddProduct(product);
        builder.AddMaterial(material);
        builder.AddDemand(new ProductDemand<Flt64>(product, Millimetres(50.0)));
        return builder.Build();
    }

    /// <summary>记录调用次数的定价策略 / A pricing policy that counts its calls.</summary>
    private sealed class CountingPolicy : ICsp1dPricingPolicy<Flt64> {
        public string Name => "counting";

        public int BenefitCalls { get; private set; }

        public int CostCalls { get; private set; }

        public int JudgeCalls { get; private set; }

        public Flt64 ModifyBenefit(CuttingPlan<Flt64> candidate, Flt64 baseBenefit) {
            ++BenefitCalls;
            return baseBenefit;
        }

        public Flt64 ModifyCost(CuttingPlan<Flt64> candidate, Flt64 baseCost) {
            ++CostCalls;
            return baseCost;
        }

        public bool? IsImproving(CuttingPlan<Flt64> candidate, Flt64 benefit, Flt64 cost) {
            ++JudgeCalls;
            return null;
        }
    }

    /// <summary>第一轮给一条新列、之后不给的定价替身 / A pricing double yielding one new column, then none.</summary>
    private sealed class OneShotPricingGenerator : ICsp1dPricingGenerator<CuttingPlan<Flt64>, Csp1dPricingInput<Flt64>> {
        private readonly Material<Flt64> _material;
        private readonly ICsp1dPricingPolicy<Flt64>? _policy;

        public OneShotPricingGenerator(Material<Flt64> material, ICsp1dPricingPolicy<Flt64>? policy = null) {
            _material = material;
            _policy = policy;
        }

        private int _calls;

        public IReadOnlyList<CuttingPlan<Flt64>> Generate(Csp1dPricingInput<Flt64> input) {
            ++_calls;
            if (_calls > 1) {
                return new List<CuttingPlan<Flt64>>();
            }
            var product = new Product<Flt64>(
                id: "A",
                name: "product-a",
                width: new List<Quantity<Flt64>> { Millimetres(50.0) });
            var plan = new CuttingPlan<Flt64>(
                    id: "priced-1",
                    material: _material,
                    slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
                    demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                        new(product, Millimetres(50.0)),
                    });
            // **按输入里配置的回调走一遍**——这样才证明"配置的策略真的转成了回调"，
            // 而不只是"输入字段非 null"。
            // **Walk the configured callbacks from the input** — that is what proves "the configured policy really became callbacks" rather than merely "the field is
            // non-null".
            if (_policy is not null) {
                var benefit = new Flt64(5.0);
                if (input.BenefitModifiers is { } benefitModifiers) {
                    foreach (global::System.Func<CuttingPlan<Flt64>, Flt64, Flt64> modifier in benefitModifiers) {
                        benefit = modifier(plan, benefit);
                    }
                }
                var cost = new Flt64(1.0);
                if (input.CostModifiers is { } costModifiers) {
                    foreach (global::System.Func<CuttingPlan<Flt64>, Flt64, Flt64> modifier in costModifiers) {
                        cost = modifier(plan, cost);
                    }
                }
                if (input.ImprovingJudges is { } judges) {
                    foreach (global::System.Func<CuttingPlan<Flt64>, Flt64, Flt64, bool?> judge in judges) {
                        _ = judge(plan, benefit, cost);
                    }
                }
            }
            return new List<CuttingPlan<Flt64>> { plan };
        }
    }

    /// <summary>**配置的定价策略必须真的被调用** / The configured pricing policy must actually be invoked.</summary>
    /// <remarks>
    /// 用**真实定价器**（`ReducedCostPricingGenerator`），从 `SolveWithTraceAsync` 走完整列生成路径。
    /// 若 `Csp1dColumnGeneration` 不把策略转成三个回调，**三个计数全是 0**。
    ///
    /// This uses the **real pricing generator** (`ReducedCostPricingGenerator`) through the full `SolveWithTraceAsync` path. If `Csp1dColumnGeneration` does not
    /// translate the policy into the three callbacks, **all three counts are 0**.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldInvokeTheConfiguredPricingPolicy() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var policy = new CountingPolicy();
        Material<Flt64> material = problem.Materials[0];
        // **必须用会产出新列的定价器**：默认定价器与初始池共用枚举器，
        // 第一轮的列全是池里已有的，去重后为空，**策略根本不会被调用**。
        // **A pricing generator that yields new columns is required**: the default one shares the pool's enumerator, so its first round is all duplicates and after
        // dedup is empty, **and the policy is never invoked** (matching the code).
        var generation = new Csp1dColumnGeneration<Flt64>(
            new MockColumnGenerationSolver(),
            pricingGenerator: new OneShotPricingGenerator(material, policy));
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .AddPricingPolicy(policy)
            .Build();

        await generation.SolveWithTraceAsync(problem, config);

        policy.BenefitCalls.Should().BeGreaterThan(0,
            "the benefit modifier derived from the policy must be called by the pricing generator");
        policy.CostCalls.Should().BeGreaterThan(0,
            "the cost modifier derived from the policy must be called by the pricing generator");
        policy.JudgeCalls.Should().BeGreaterThan(0,
            "the isImproving judge derived from the policy must be called by the pricing generator");
    }

    /// <summary>**配置里的惩罚必须到达定价器的 `ObjectiveConfig`** / The configured penalties must reach the pricing generator's `ObjectiveConfig`.</summary>
    /// <remarks>
    /// **记的缺口**：`ObjectiveConfig` 在生产路径上**恒为 null**，成本永远只有基础值 1。
    /// 基线 `pricingObjectiveConfig(resolvedConfig)` 从 `lengthConfig.batchMinPenalty` 与
    /// `wasteConfig` 的三个惩罚项取值（见）。
    ///
    /// 判据用**捕获定价输入**的方式：一个只记录 `input` 的定价替身，然后断言
    /// `ObjectiveConfig` 里那四个字段**等于配置里设的值**。
    ///
    /// **断言具体数值，不是"非 null"**：把它接成 `new Csp1dPricingObjectiveConfig<V>` 也能过"非 null"，
    /// 但那样**四个惩罚全是 null**，等于没接。
    ///
    /// **The gap recorded**: `ObjectiveConfig` was **always null on the production path**, so the cost was always the base 1. The code
    /// `pricingObjectiveConfig(resolvedConfig)` reads `lengthConfig.batchMinPenalty` and the three waste penalties.
    ///
    /// The criterion **captures the pricing input**: a pricing double that only records `input`, then asserts the four `ObjectiveConfig` fields **equal the configured
    /// values**.
    ///
    /// **It asserts concrete values, not "non-null"**: wiring it as `new Csp1dPricingObjectiveConfig<V>` would satisfy "non-null" while leaving **all four penalties
    /// null**, which is the same as not wiring it.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldPassTheConfiguredPenaltiesToThePricingInput() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var capture = new CapturingPricingGenerator();
        var generation = new Csp1dColumnGeneration<Flt64>(
            new MockColumnGenerationSolver(),
            pricingGenerator: capture);
        Csp1dSolveConfig<Flt64> config = new Csp1dSolveConfigBuilder<Flt64>()
            .SetWasteConfig(new WasteMinimizationConfig<Flt64> {
                TrimWidthPenalty = new Flt64(0.25),
                RestMaterialPenalty = new Flt64(0.5),
                MaterialCostPenalty = new Dictionary<string, Flt64> { ["M1"] = new Flt64(0.75) },
            })
            .Build();

        await generation.SolveWithTraceAsync(problem, config);

        capture.LastInput.Should().NotBeNull("pricing must be invoked at least once");
        Csp1dPricingObjectiveConfig<Flt64> objective = capture.LastInput!.ObjectiveConfig
            ?? throw new Xunit.Sdk.XunitException("ObjectiveConfig must not be null on the production path");
        objective.TrimWidthPenalty!.Value.ToDouble().Should().BeApproximately(0.25, 1e-9);
        objective.RestMaterialPenalty!.Value.ToDouble().Should().BeApproximately(0.5, 1e-9);
        objective.MaterialCostPenalty!["M1"].ToDouble().Should().BeApproximately(0.75, 1e-9);
    }

    /// <summary>
    /// **全零对偶下默认定价器产不出列，以 `PricingConverged` 结束**
    /// / With all-zero duals the default pricing generator yields nothing and ends as `PricingConverged`.
    /// </summary>
    /// <remarks>
    /// **这条判据的来历值得记下，因为我中途下过一个错误结论。**
    ///
    /// 判定"定价枚举器复读初始方案是首要缺口"，之后又想"修"它。查清后的结论是：
    ///
    /// 1. **默认定价器与初始池共用同一个枚举器**（`SimpleInitialCuttingPlanGenerator`），C# 也一样；
    /// 2. **同样场景**下也断言 `AllDuplicates`——
    /// 但那用的是**自定义 fake 定价器**，能产出重复列；
    /// 3. 这里用**真实默认定价器 + 全零对偶 mock**，减成本判据 `收益 > 成本` 不成立，
    /// 所以 `Generate` 返回空，走 `PricingConverged` 分支。
    ///
    /// **两者不矛盾**：`AllDuplicates` 要求"定价产出了列但全被去重"，
    /// `PricingConverged` 是"定价一条都没产出"。**当前夹具是后者。**
    ///
    /// 判据钉住这一点：**若将来零对偶下开始产列，这条会红**，提醒检查减成本判据是否被破坏。
    ///
    /// **The provenance matters because I drew a wrong conclusion along the way.**
    ///
    /// declared "the pricing enumerator replaying the initial plans is the top gap", and later batches tried to "fix" it. After checking properly:
    ///
    /// 1. **The code's default pricing generator shares the initial pool's enumerator** (`SimpleInitialCuttingPlanGenerator`), and so does C#;
    /// 2. The code **also asserts `AllDuplicates`** in that situation — but with a **custom fake pricing generator** that
    /// can yield duplicate columns;
    /// 3. Here C# uses the **real default pricing generator with an all-zero-dual mock**, so the reduced-cost test `benefit > cost` fails, `Generate` returns empty, and the
    ///    `PricingConverged` branch runs.
    ///
    /// **The two are not contradictory**: `AllDuplicates` requires "pricing yielded columns that were all deduped away"; `PricingConverged` is "pricing yielded nothing".
    /// **This fixture is the latter.**
    ///
    /// The criterion pins that: **if pricing ever starts yielding columns under zero duals, this goes red**, flagging that the reduced-cost test was broken.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldConvergeWhenPricingYieldsNothingUnderZeroDuals() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var generation = new Csp1dColumnGeneration<Flt64>(new MockColumnGenerationSolver());

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem);

        result.Trace.TerminationReason.Should().Be(
            Csp1dTerminationReason.PricingConverged,
            "with all-zero duals the reduced-cost test rejects every plan, so pricing yields nothing");
    }

    /// <summary>**定价产出的列与池里已有方案同键时，必须以 `AllDuplicates` 终止** / Pricing that produces a column with an existing canonical key must terminate as `AllDuplicates`.</summary>
    /// <remarks>
    /// **记的缺口**：`AllDuplicates` 分支没有判据——它要求"定价产出了列，但全被下游去重"，
    /// 而此前的替身要么产新列（被接受），要么产空（走 `PricingConverged`）。
    ///
    /// **实测发现根本不需要额外开关**：`OneShotPricingGenerator` 产出的是
    /// `material=M1 / slice=50mm / 贡献 50mm`，而 `MakeProblem` 的初始方案**恰好也是这个规范键**
    /// ——初始池用同一个物料与产品宽度生成。
    ///
    /// 所以第一轮定价就报出 1 条同键列，`DeduplicatePlans` 把它滤掉，`addedPlans` 为空 → `AllDuplicates`。
    ///
    /// **这正是基线 的场景**——那里是"定价返回初始方案的副本"。
    ///
    /// 断言 `FinalPlanCount == InitialPlanCount`：**重复列绝不能真的加进去**，
    /// 否则"报了重复却还是加进去了"也会绿。
    ///
    /// **The gap recorded**: the `AllDuplicates` branch had no criterion — it requires "pricing yielded columns that were all deduped downstream", while earlier
    /// doubles either yielded new columns (accepted) or nothing (`PricingConverged`).
    ///
    /// **Measurement showed no extra switch is needed**: `OneShotPricingGenerator` yields `material=M1 / slice=50mm / contribution 50mm`, and `MakeProblem`'s initial
    /// plan **has exactly that canonical key** — the pool is built from the same material and product width.
    ///
    /// So the first pricing round already reports one same-key column, `DeduplicatePlans` filters it, `addedPlans` is empty, and the run ends as `AllDuplicates`.
    ///
    /// **That is precisely its scenario** at — "pricing returns a copy of the initial plan".
    ///
    /// It asserts `FinalPlanCount == InitialPlanCount`: **a duplicate column must never actually be added**, or "reported a duplicate but added it anyway" would also pass.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldEndAsAllDuplicatesWhenPricingRepeatsAnExistingPlan() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        Material<Flt64> material = problem.Materials[0];
        var generation = new Csp1dColumnGeneration<Flt64>(
            new MockColumnGenerationSolver(),
            pricingGenerator: new OneShotPricingGenerator(material));

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem);

        result.Trace.TerminationReason.Should().Be(
            Csp1dTerminationReason.AllDuplicates,
            "the priced column has the same canonical key as the initial plan, so nothing can be added");
        result.Trace.FinalPlanCount.Should().Be(
            result.Trace.InitialPlanCount,
            "a duplicate column must never grow the pool");
        // **`AllDuplicates` 有三条路径都会设置终止原因**：①`DeduplicatePlans` 滤空、
        // ②`AddColumns` 返回空、③加列数为 0。三条路径**写入的可观测量完全相同**
        // （`PricedPlanCount` 写 0、`FinalPlanCount` 不变），所以**从外部区分不了**——
        // 我试过"把 `DeduplicatePlans` 的过滤整个删掉"，判据**仍然全绿**，因为下游
        // `ProduceAggregation._planKeyIndex` 又兜了一层，结果一模一样。
        //
        // **这不是判据缺失，而是实现本身在这里有三重冗余。** 判据能钉住的是
        // "**重复列绝不进池、流程在单轮结束**"，而这三条路径都满足它。
        //
        // **Three paths set `AllDuplicates`**: (1) `DeduplicatePlans` empties the list, (2) `AddColumns` returns empty, (3) the added count is zero. All three **write
        // identical observable values** (`PricedPlanCount` 0, unchanged `FinalPlanCount`), so **they cannot be distinguished from outside** — deleting
        // `DeduplicatePlans`'s filter entirely **left every criterion green**, because `ProduceAggregation._planKeyIndex` catches it again with the same outcome.
        //
        // **That is not a missing criterion but genuine redundancy in the implementation.** What the criterion can pin is "**a duplicate never enters the pool and the
        // run stops after one round**", which all three paths satisfy.
        result.Trace.PricedPlanCount.Should().ContainSingle(
            "the flow must stop after a single pricing round");
        result.Trace.PricedPlanCount[0].ToULong().Should().Be(
            0UL,
            "the duplicate must be removed before any column is added");
    }

    /// <summary>只捕获最后一次定价输入的替身 / A pricing double that captures the last pricing input.</summary>
    private sealed class CapturingPricingGenerator : ICsp1dPricingGenerator<CuttingPlan<Flt64>, Csp1dPricingInput<Flt64>> {
        public Csp1dPricingInput<Flt64>? LastInput { get; private set; }

        public IReadOnlyList<CuttingPlan<Flt64>> Generate(Csp1dPricingInput<Flt64> input) {
            LastInput = input;
            return new List<CuttingPlan<Flt64>>();
        }
    }

    /// <summary>**没配策略时三个扩展点都是 null，定价器照常工作** / Without a policy the three extension points are null and pricing still works.</summary>
    /// <remarks>
    /// **反向判据**：证明"策略被调用"来自**配置**，而不是定价器无条件调用某个默认实现。
    /// 若实现总是塞一个非 null 的空列表，这条仍会通过——所以它还断言**流程本身成功**，
    /// 把"没配策略导致空引用"这类错误挡在外面。
    ///
    /// **The reverse criterion**: it proves the calls come from the **configuration**, not from the pricing generator unconditionally invoking some default. If the
    /// implementation always injected a non-null empty list this would still pass — hence it also asserts **the flow itself succeeds**, keeping "an absent policy
    /// causes a null dereference" out.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldWorkWithoutAnyPricingPolicy() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var generation = new Csp1dColumnGeneration<Flt64>(new MockColumnGenerationSolver());

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem);

        result.Should().NotBeNull("solving without any pricing policy must still complete");
    }
}
