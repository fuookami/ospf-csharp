#nullable enable

using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// **生成器的规模特性**：三个生成器 × 三个场景 × 四个并行度 × 两个方案上限 /
/// The scale characteristics of the generators: three generators by three scenarios by four parallelisms by two budgets.
/// </summary>
/// <remarks>
/// ****
///
/// **它比既有的 `GenerationCacheBuildBenchmark` 多测两件事：**
///
/// **① 换生成器**（`dfs`／`nsum`／`fullsum`）——同一份输入在三套搜索上量级不同，只测 `dfs` 会把"最慢的那套没测过"藏起来；
/// **② 换方案上限**（`100000` 与 `200`）——上限不同会让搜索走完全不同的终止路径：
/// 一个是"搜完"，一个是"撞到上限停下"，**耗时差异来自终止条件而不是搜索规模**。
///
/// **两个基准方法也分工：** `GenerateWithReport` 把手头完整的报告全算出来，`GenerateSummary` 只取"方案数 : 终止原因"，
/// 后者证明结果确实被消费——BenchmarkDotNet 会避免死代码消除，但"结果有没有被读"仍要由基准自己表达清楚。
///
/// **Corresponds to its (112 lines, added in `19c72455`).** **It measures two things
/// `GenerationCacheBuildBenchmark` does not: ① a different generator (`dfs`/`nsum`/`fullsum`) — one generator alone hides the slow one;
/// ② a different budget (`100000` versus `200`) — a budget of 200 stops on the cap rather than on exhaustion, so the cost difference comes
/// from the stop condition, not the search size.** **The two methods split the work as well: `GenerateWithReport` materialises the full
/// report, `GenerateSummary` reads only "plans : stop reason", proving the result is consumed.**
/// </remarks>
[MemoryDiagnoser]
public class GenerationScaleBenchmark
{
    /// <summary>物料与策略场景 / Material and policy scenario.</summary>
    [Params("equivalent", "distinct", "custom")]
    public string Scenario { get; set; } = "equivalent";

    /// <summary>生成器 / The generator.</summary>
    [Params("dfs", "nsum", "fullsum")]
    public string GeneratorName { get; set; } = "dfs";

    /// <summary>生成器内部并行度 / The generator's internal parallelism.</summary>
    [Params(1, 2, 4, 8)]
    public int Workers { get; set; } = 1;

    /// <summary>方案上限 / The plan budget.</summary>
    [Params(100000, 200)]
    public int Budget { get; set; } = 100000;

    /// <summary>夹具输入（测试可见）/ The fixture input (visible to tests).</summary>
    /// <remarks>
    /// **`internal` 而不是 `public`**：基准不必对外发 API，而判据要能核对"参数真的接到了夹具上"——
    /// **一个参数没接上的基准会给出漂亮数字，而它测的东西和参数无关。**
    ///
    /// **`internal`, not `public`**: the benchmark need not publish an API, while a criterion must be able to check the parameters really
    /// reached the fixture — **a benchmark whose parameter never landed reports a fine number for something unrelated**.
    /// </remarks>
    internal GenerationInput<Flt64> Input => _input;

    /// <summary>夹具生成器（测试可见）/ The fixture generator (visible to tests).</summary>
    internal ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> Instance => _instance;

    private GenerationInput<Flt64> _input = null!;
    private ICsp1dInitialCuttingPlanGenerator<CuttingPlan<Flt64>, GenerationInput<Flt64>> _instance = null!;

    /// <summary>固定输入与生成器都在计时之外构造 / Fixed input and generator are built outside timing.</summary>
    [GlobalSetup]
    public void Setup() {
        var products = new List<Product<Flt64>>(8);
        var demands = new List<ProductDemand<Flt64>>(8);
        for (int index = 0; index < 8; ++index) {
            var product = new Product<Flt64>(
                "p" + index,
                "p" + index,
                [new Quantity<Flt64>(new Flt64(0.17 + index * 0.03), SIBaseUnits.Meter)]);
            products.Add(product);
            demands.Add(ProductDemand<Flt64>.Roll(product, new Quantity<Flt64>(new Flt64(8), RollCountUnit.Instance)));
        }

        var materials = new List<Material<Flt64>>(12);
        for (int index = 0; index < 12; ++index) {
            double upperBound = Scenario == "distinct" ? 1.2 + index * 0.01 : 1.2;
            materials.Add(new Material<Flt64>(
                "m" + index,
                "m" + index,
                new WidthRange<Flt64>(
                    Width: new QuantityRange<Flt64>(
                        LowerBound: new Quantity<Flt64>(Flt64.Zero, SIBaseUnits.Meter),
                        UpperBound: new Quantity<Flt64>(new Flt64(upperBound), SIBaseUnits.Meter)),
                    Step: new Quantity<Flt64>(new Flt64(0.01), SIBaseUnits.Meter))));
        }

        _input = new GenerationInput<Flt64> {
            Materials = materials,
            Demands = demands,
            Costars = [],
        };

        var constraints = new List<ICuttingPlanConstraint<Flt64>> {
            new MaxKnifeCountConstraint<Flt64>(new Fuookami.Ospf.Math.Algebra.Number.UInt64(5UL)),
        };
        if (Scenario == "custom") {
            constraints.Add(new AlwaysSatisfiedConstraint());
        }

        long budget = Budget;
        long workers = Workers;
        _instance = GeneratorName switch {
            "nsum" => new NSumGenerator<Flt64>(constraints, maxPlans: budget, parallelism: workers),
            "fullsum" => new FullSumGenerator<Flt64>(constraints, maxPlans: budget, parallelism: workers),
            _ => new DFSGenerator<Flt64>(constraints, maxPlans: budget, parallelism: workers),
        };
    }

    /// <summary>完整生成并返回整份报告 / Generate fully and return the whole report.</summary>
    [Benchmark]
    public CuttingPlanGenerationReport<CuttingPlan<Flt64>> GenerateWithReport() => _instance.GenerateWithReport(_input);

    /// <summary>只读出"方案数 : 终止原因"，证明结果被消费 / Read only "plans : stop reason", proving the result is consumed.</summary>
    [Benchmark]
    public string GenerateSummary() {
        CuttingPlanGenerationReport<CuttingPlan<Flt64>> report = _instance.GenerateWithReport(_input);
        return report.Plans.Count + ":" + report.Statistics.StopReason;
    }

    /// <summary>恒真的自定义策略 / An always-satisfied custom policy.</summary>
    /// <remarks>
    /// **`custom` 场景里塞一条恒真约束，C# 侧照搬。** **它不是装饰：** **生成器对每条约束都要走一次判定，
    /// 多一条就多一次虚调用——这正是要计时的部分。**
    ///
    /// **The code adds an always-true constraint in the `custom` scenario and this mirrors it.** **It is not decoration: the generator
    /// rules on every constraint for every candidate, so one more is one more virtual call — exactly what is being timed.**
    /// </remarks>
    private sealed class AlwaysSatisfiedConstraint : ICuttingPlanConstraint<Flt64> {
        /// <summary>恒真 / Always true.</summary>
        /// <param name="context">约束上下文 / The constraint context.</param>
        /// <returns>恒为 true / Always true.</returns>
        public bool IsSatisfied(CuttingPlanConstraintContext<Flt64> context) => true;
    }
}