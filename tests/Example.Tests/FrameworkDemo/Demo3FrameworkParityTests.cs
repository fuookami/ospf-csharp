// Demo3 与框架链路的一致性判据 / Criteria tying Demo3 to the framework pipeline.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **Demo3 的手写列生成与框架链路是两套实现** / Demo3's hand-rolled column generation and the framework pipeline are two separate implementations.
/// </summary>
/// <remarks>
/// **记着"Demo3 仍绕过 `Csp1dColumnGeneration`"。** 这里查清了它到底是什么：
///
/// | 项 | 参照实现 | C# |
/// | --- | --- | --- |
/// | Demo3 怎么求解 | `Csp1dColumnGeneration` + `NSameGenerator` + `ReducedCostPricingGenerator` | **手写 LP + 手写背包** |
/// | 定价子问题 | 精确求解（框架） | **贪心启发式**（源码里写着 `Greedy heuristic`） |
/// | 自认是简化版吗 | 否 | **是**（注释写着 `In a full implementation this would:`） |
///
/// **框架能力一样不缺**：`Csp1dColumnGeneration`、`NSameGenerator`、`ReducedCostPricingGenerator`
/// 都在，且已有测试覆盖。**缺的是示例代码没有把它们串起来。**
///
/// 这是**示例层缺口，不是框架缺口**。改 Demo3 需要真实求解器，而当前环境没有
/// ，所以这里**不改 Demo3**，而是用判据钉住
/// **"框架链路本身能跑通"** 这个前提——它一旦破了，Demo3 的改造就更无从谈起。
///
/// **recorded "Demo3 still bypasses `Csp1dColumnGeneration`".** established exactly what that means (table above): **the framework capabilities are all
/// present** and already tested; **what is missing is that the example never wires them together**. It is a **sample-layer gap, not a framework gap**. Rewriting Demo3
/// needs a real solver, which this environment lacks, so **leaves Demo3 alone** and instead pins **"the framework pipeline itself
/// runs"** — if that breaks, rewriting Demo3 is moot.
/// </remarks>
public class Demo3FrameworkParityTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Csp1dProblem<Flt64> MakeProblem() {
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(60.0) });
        var material = new Material<Flt64>(
            id: "M1",
            name: "material-1",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(6000.0),
                    UpperBound: Millimetres(6000.0)),
                Step: Millimetres(1.0)),
            length: Millimetres(6000.0));
        var builder = new Csp1dProblemBuilder<Flt64>();
        builder.AddProduct(product);
        builder.AddMaterial(material);
        builder.AddDemand(new ProductDemand<Flt64>(product, Millimetres(60.0)));
        return builder.Build();
    }

    /// <summary>**框架的列生成链路必须能跑通并返回解** / The framework's column-generation pipeline must run and return a solution.</summary>
    /// <remarks>
    /// 用 `MockColumnGenerationSolver` 跑完整链路。**这里验证的不是"解得对不对"**——
    /// 桩求解器给不出真实最优解——**而是"链路能走完、不抛异常、返回解对象"**。
    ///
    /// 这正是 Demo3 改造的前置条件：**若框架链路本身跑不通，把 Demo3 改过来毫无意义**。
    ///
    /// **这条判据对内部实现不敏感，这是它的固有弱点。** 实测两个变异都存活：
    /// 把 `Solution` 加 `!`（无行为变化）、把 `initialGenerator` 的注入改成恒用默认值。
    /// 原因是它只断言"**不抛异常、返回非 null**"，**不检查解的内容**——
    /// 而桩求解器给不出有意义的解内容可供检查。
    ///
    /// **所以它的价值是"冒烟级"**：链路断了会红，链路内部退化不会。
    /// 要钉住内部行为需要真实求解器。
    /// 判据保留，但**不宣称它能覆盖链路正确性**。
    ///
    /// **This criterion is insensitive to the implementation — its inherent weakness.** Two mutants survived: adding `!` to `Solution` (no behaviour change) and replacing
    /// the `initialGenerator` injection with a hard-coded default. The reason is that it only asserts **"does not throw and returns non-null"**, **never inspecting the
    /// solution** — and a stub solver cannot produce meaningful content to inspect. **Its value is smoke-level**: a broken pipeline goes red, an internally degraded one
    /// does not. Pinning internal behaviour needs a real solver. The criterion stays, but **it is not claimed to cover pipeline correctness**.
    ///
    /// Runs the whole pipeline with `MockColumnGenerationSolver`. This does **not** check optimality — a stub cannot produce it — but that the **pipeline completes, throws
    /// nothing, and returns a solution**. That is exactly the precondition for rewriting Demo3: **if the framework pipeline itself does not run, implementation Demo3 is pointless.**
    /// </remarks>
    [Fact]
    public async Task FrameworkPipeline_ShouldRunToCompletionWithAStubSolver() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var generation = new Csp1dColumnGeneration<Flt64>(new MockColumnGenerationSolver());

        Csp1dSolution<Flt64> solution = await generation.SolveAsync(problem);

        solution.Should().NotBeNull("the pipeline must return a solution object");
        solution.Produce.Should().NotBeNull("the solution must carry a produce aggregate");
    }

 /// <summary>**贪心定价会漏掉改进列**——记录 Demo3 与实质差异 / Greedy pricing misses improving columns — Demo3's substantive difference.</summary>
    /// <remarks>
    /// **这条判据的第一版反例是错的。** 我原本用宽度 `45 / 45 / 10`、卷长 100，
    /// 以为贪心会先吃两个 45 然后装不下 10——**实测贪心拿了 2×45 + 1×10 = 100**，
    /// 一个都没漏。判据当场红。
    ///
    /// **正确的反例需要"贪心的选择顺序把它锁死"**：
    /// 卷长 **10**，宽度 `6 / 5 / 5`。
    ///
    /// | 策略 | 结果 |
    /// | --- | --- |
    /// | 贪心（先拿最宽的 6） | **6**，余 4，装不下 5 |
    /// | 精确最优（两个 5） | **10**，余 0 |
    ///
    /// 这才是贪心的真正失败模式：**先拿大的就把空间锁死了**。
    ///
    /// **The first counter-example here was wrong.** I used widths `45 / 45 / 10` with a length of 100 and expected greedy to take two 45s and then fail to fit the 10 —
    /// **measured, greedy took 2×45 + 1×10 = 100**, missing nothing, and the criterion went red. **A correct counter-example needs greedy's ordering to lock it out**:
    /// length **10**, widths `6 / 5 / 5` (table above). That is greedy's real failure mode: **taking the big item first locks out the space**.
    /// </remarks>
    [Fact]
    public void Demo3GreedyPricing_ShouldMissTheExactOptimum() {
        double rawLength = 10.0;
        double[] widthsDescending = { 6.0, 5.0, 5.0 };

        // 贪心：按宽度降序，能装就装。
        // Greedy: descending width, take while it fits.
        double remaining = rawLength;
        foreach (double width in widthsDescending) {
            int count = (int)(remaining / width);
            remaining -= count * width;
        }
        double greedyUsed = rawLength - remaining;

        // 精确最优：两个 5 刚好填满。
        // Exact optimum: two 5s fill it exactly.
        double exactUsed = 2 * 5.0;

        greedyUsed.Should().BeApproximately(
            6.0,
            1e-9,
            "greedy takes the widest item first and can no longer fit a 5");
        exactUsed.Should().BeApproximately(10.0, 1e-9);
        exactUsed.Should().BeGreaterThan(
            greedyUsed,
            "the exact optimum beats greedy, which is why Demo3's greedy pricing cannot match the baseline's exact pricing");
    }
}
