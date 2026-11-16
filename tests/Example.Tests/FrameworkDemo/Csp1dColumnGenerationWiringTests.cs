#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.Application.Service;

/// <summary>
/// `Csp1dColumnGeneration` 必须**真的调用**注入的生成器，而不只是把它们存进字段。
/// `Csp1dColumnGeneration` must **actually invoke** the injected generators, not merely store them in fields.
/// </summary>
/// <remarks>
/// 示例 demo3 把 **`NSameGenerator` 当初始生成器、`FullSumGenerator` 当定价枚举器**注入
/// `Csp1dColumnGeneration`，由它驱动整个列生成流程。
///
/// C# 侧这两个注入点都存在（构造函数第 2、3 个参数），`_initialGenerator` 与 `_pricingGenerator`
/// 也都有真实调用点。但 **`Csp1dProblem` 在整个测试项目里从未出现过**——
/// 也就是说**整条列生成链路没有任何测试**，"注入了生成器"这件事**从未被验证过会被使用**。
///
/// 这批用**记录型替身**来证明：替身记下自己有没有被调用，判据看的是**调用记录**，
/// 而不是"结果非空"——后者在生成器返回空时也会因别的路径通过。
///
/// The code demo3 injects **`NSameGenerator` as the initial generator and `FullSumGenerator` as the pricing enumerator** into
/// `Csp1dColumnGeneration`, which drives the whole column-generation flow.
///
/// Both injection points exist on the C# side (constructor arguments 2 and 3) and both `_initialGenerator` and `_pricingGenerator` have real call sites. But
/// **`Csp1dProblem` never appears anywhere in the test project** — meaning **the entire column-generation chain has no tests at all**, and "a generator was
/// injected" **was never verified to be used**.
///
/// proves it with **recording doubles**: the double records whether it was called, and the criterion looks at the **call record**, not at "the result is
/// non-empty" — the latter would also pass through some other path when the generator returns nothing.
/// </remarks>
public class Csp1dColumnGenerationWiringTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>记录调用次数的初始生成器替身 / An initial-generator double that records its invocations.</summary>
    private sealed class RecordingInitialGenerator
        : ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> {
        private readonly ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> _inner;

        public RecordingInitialGenerator(
            ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> inner) =>
            _inner = inner;

        public int Calls { get; private set; }

        public IReadOnlyList<CuttingPlan<Flt64>> Generate(GenerationInput<Flt64> input) {
            ++Calls;
            return _inner.Generate(input);
        }

        public CuttingPlanGenerationReport<CuttingPlan<Flt64>> GenerateWithReport(GenerationInput<Flt64> input) {
            ++Calls;
            return _inner.GenerateWithReport(input);
        }
    }

    /// <summary>记录调用次数的定价生成器替身 / A pricing-generator double that records its invocations.</summary>
    private sealed class RecordingPricingGenerator
        : ICsp1dPricingGenerator<CuttingPlan<Flt64>, Csp1dPricingInput<Flt64>> {
        public int Calls { get; private set; }

        public IReadOnlyList<CuttingPlan<Flt64>> Generate(Csp1dPricingInput<Flt64> input) {
            ++Calls;
            return new List<CuttingPlan<Flt64>>();
        }

        public CuttingPlanGenerationReport<CuttingPlan<Flt64>> GenerateWithReport(Csp1dPricingInput<Flt64> input) {
            ++Calls;
            return new CuttingPlanGenerationReport<CuttingPlan<Flt64>>(
                Plans: new List<CuttingPlan<Flt64>>(),
                Statistics: new CuttingPlanGenerationStatistics());
        }
    }

    private static Csp1dProblem<Flt64> MakeProblem() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)
                ),
                Step: Millimetres(1.0)
            )
        );
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) }
        );

        var builder = new Csp1dProblemBuilder<Flt64>();
        builder.AddProduct(product);
        builder.AddMaterial(material);
        builder.AddDemand(new ProductDemand<Flt64>(product, Millimetres(50.0)));
        return builder.Build();
    }

    /// <summary>**注入的初始生成器必须被调用** / The injected initial generator must be invoked.</summary>
    [Fact]
    public async Task SolveAsync_ShouldInvokeTheInjectedInitialGenerator() {
        var inner = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained().ToConstraints());
        var recording = new RecordingInitialGenerator(inner);
        var columnGeneration = new Csp1dColumnGeneration<Flt64>(
            solver: new Fuookami.Ospf.Example.Tests.FrameworkDemo.MockColumnGenerationSolver(),
            initialGenerator: recording);

        await columnGeneration.SolveAsync(MakeProblem());

        recording.Calls.Should().BePositive(
            "an injected initial generator that is never called is just a stored field");
    }

    /// <summary>**已取消的令牌必须让求解立即抛出** / An already-cancelled token must make the solve throw immediately.</summary>
    /// <remarks>
    /// `Csp1dColumnGeneration` 在 4 个可中断边界调用 `progressContext?.throwIfCancelled`。
    /// **C# 侧原先一个检查点都没有**——`grep CancellationToken` 在该文件里零命中。
    ///
    /// 后果不是"取消得慢"，而是**取消根本不生效**：调用方 `Cancel` 之后，
    /// 求解会继续跑完整轮迭代与 MILP。对长跑任务而言这等于没有取消能力。
    ///
    /// 这条判据用**已取消的令牌**（不是中途取消），因为那是最确定、最不依赖时序的形式。
    ///
    /// **这条判据的能力边界要说清**：它只能证明"**至少有一个**检查点生效"。
    /// 变异测试证实——单独删掉三个检查点中的**任意一个**，这条测试仍然绿；
    /// **三个一起删才会红**。因为三个检查点互为冗余，任何**一个**生效就足以让入口处抛出。
    ///
    /// 也就是说：**这条判据无法区分"有 3 个检查点"与"只有 1 个"**。
    /// 要验证"每轮迭代都会检查取消"，需要**中途取消**的测试（在迭代进行中 `Cancel` 并断言及时抛出），
    /// 那依赖时序，这里没有做。**这里如实记录，不假装判据更强。**
    ///
    /// **The criterion's limits must be stated**: it can only prove "**at least one** checkpoint works". Mutation testing confirmed that deleting **any single**
    /// one of the three leaves this test green; **only deleting all three turns it red**, because the checkpoints are redundant and any **one** suffices to throw at
    /// the entry.
    ///
    /// In other words: **this criterion cannot tell "3 checkpoints" from "1 checkpoint"**. Verifying that "every iteration checks cancellation" needs a
    /// **mid-flight** cancellation test (call `Cancel` during the loop and assert prompt throwing), which depends on timing and was not done in.
    /// **Recording that honestly rather than pretending the criterion is stronger.**
    ///
    /// The code `Csp1dColumnGeneration` calls `progressContext?.throwIfCancelled` at 4 interruptible boundaries. **The C# side had no checkpoints at
    /// all** — `grep CancellationToken` found zero hits in that file.
    ///
    /// The consequence is not "cancellation is slow" but **cancellation does not work at all**: after the caller calls `Cancel`, the solve keeps running the
    /// full iteration loop and MILP. For long-running work that amounts to having no cancellation capability.
    ///
    /// This uses an **already-cancelled token** (not a mid-flight cancel) because that is the most deterministic form and does not depend on timing.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldThrowWhenTheTokenIsAlreadyCancelled() {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var columnGeneration = new Csp1dColumnGeneration<Flt64>(
            solver: new Fuookami.Ospf.Example.Tests.FrameworkDemo.MockColumnGenerationSolver());

        Func<Task> solve = async () => await columnGeneration.SolveAsync(
            MakeProblem(),
            solveConfig: null,
            cancellationToken: source.Token);

        await solve.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>**注入的定价生成器必须被调用** / The injected pricing generator must be invoked.</summary>
    /// <remarks>
    /// 示例 demo3 的定价枚举器是 `FullSumGenerator`，通过 `ReducedCostPricingGenerator` 包装注入。
    /// 这条单独钉住 `pricingGenerator` 这条注入点——**它与 `initialGenerator` 是两个独立的注入点**，
    /// 只验证一个就下结论，正是本项目反复出现的"修好一条路径、兄弟路径保持原样"。
    ///
    /// The sample demo3's pricing enumerator is `FullSumGenerator`, injected through a `ReducedCostPricingGenerator` wrapper. This pins the `pricingGenerator`
    /// injection point separately — **it is independent of `initialGenerator`**, and validating only one is exactly the recurring "fix one path, leave the siblings
    /// untouched" pattern in this project.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldInvokeTheInjectedPricingGenerator() {
        var inner = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained().ToConstraints());
        var pricing = new RecordingPricingGenerator();
        var columnGeneration = new Csp1dColumnGeneration<Flt64>(
            solver: new Fuookami.Ospf.Example.Tests.FrameworkDemo.MockColumnGenerationSolver(),
            initialGenerator: new RecordingInitialGenerator(inner),
            pricingGenerator: pricing);

        await columnGeneration.SolveAsync(MakeProblem());

        pricing.Calls.Should().BePositive(
            "an injected pricing generator that is never called is just a stored field");
    }
}
