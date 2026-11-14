#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System.Collections.Generic;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 真实 DFS 的物料缓存构建：等价物料、不同物料、自定义策略回退 / Real DFS cache build: equivalent materials, distinct materials, custom-strategy fallback.
/// </summary>
/// <remarks>
/// **B952 补齐**：基线 `GenerationCacheBuildBenchmark.kt`（75 行）一直有，本仓 `src/Benchmark` 只有 13 个基准，
/// 缺的正是这一个和 `PatternColumnBenchmark`。**基准夹具的缺口不会报错，只会让"性能没测过"看起来像"没需求"。**
///
/// 三个场景的差别全在**物料宽度上界**：`equivalent` 让 12 个物料上界相同（可互相替换），`distinct` 给每个加
/// `index * 0.01`（互不可替换），`custom` 另外加一条恒真的自定义策略。**这三条走生成器里不同的剪枝路径**，
/// 所以夹具必须区分——只跑一个场景等于只测了一条路。
///
/// **B952 fills a gap**: the baseline's `GenerationCacheBuildBenchmark.kt` (75 lines) has always existed while this repo had only 13 benchmarks. **A missing benchmark
/// raises nothing; it just makes "performance was never measured" look like "performance was never needed".** The three scenarios differ only in the **materials'
/// width upper bound** and take different pruning paths through the generator, so running one scenario measures one path.
/// </remarks>
[MemoryDiagnoser]
public class GenerationCacheBuildBenchmark {
    /// <summary>物料与策略场景 / Material and policy scenario.</summary>
    [Params("equivalent", "distinct", "custom")]
    public string Scenario { get; set; } = "equivalent";

    /// <summary>现有物料的并行度 / Parallelism over existing materials.</summary>
    [Params(1, 4)]
    public int Workers { get; set; } = 1;

    /// <summary>夹具输入（测试可见）/ The fixture input (visible to tests).</summary>
    /// <remarks>
    /// **`internal` 而不是 `public`**：基准项目不对外发 API，但测试要能检查夹具真的按参数分场景构造。
    /// 一个"参数没接上"的基准会在报告里给出漂亮的数字，**而它测的东西和参数无关**。
    ///
    /// **`internal`, not `public`**: the benchmark project is not a published API, but the tests have to check the fixture really varies with the parameters. A benchmark
    /// whose parameter never reached the fixture reports a fine number **for something the parameter has nothing to do with**.
    /// </remarks>
    internal GenerationInput<Flt64> Input => _input;

    private GenerationInput<Flt64> _input = null!;
    private DFSGenerator<Flt64> _generator = null!;

    /// <summary>把夹具构造放在计时之外 / Build the fixture outside the timing.</summary>
    [GlobalSetup]
    public void Setup() {
        var demands = new List<ProductDemand<Flt64>>(8);
        for (int index = 0; index < 8; ++index) {
            var product = new Product<Flt64>(
                "p" + index,
                "p" + index,
                [new Quantity<Flt64>(new Flt64(0.17 + index * 0.03), SIBaseUnits.Meter)]);
            demands.Add(ProductDemand<Flt64>.Roll(product, new Quantity<Flt64>(new Flt64(8), RollCountUnit.Instance)));
        }

        var materials = new List<Material<Flt64>>(12);
        for (int index = 0; index < 12; ++index) {
            // **`distinct` 给每个物料加 `index * 0.01`**：上界各不相同，物料之间不能互相替换。
            // **`distinct` adds `index * 0.01`**, so no two materials can stand in for each other.
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
            new MaxKnifeCountConstraint<Flt64>(new UInt64(5UL)),
        };
        if (Scenario == "custom") {
            constraints.Add(new AlwaysSatisfiedConstraint());
        }

        _generator = new DFSGenerator<Flt64>(
            constraints,
            maxPlans: 100_000L,
            parallelism: Workers);
    }

    /// <summary>完整生成并汇总报告 / Generate fully and aggregate the report.</summary>
    /// <returns>包含缓存命中、miss 与访问节点的完整报告 / The full report with cache hits, misses and visited nodes.</returns>
    [Benchmark]
    public CuttingPlanGenerationReport<CuttingPlan<Flt64>> Generate() => _generator.GenerateWithReport(_input);

    /// <summary>恒真的自定义策略 / An always-satisfied custom policy.</summary>
    /// <remarks>
    /// 基线在 `custom` 场景里塞一个 `isSatisfied` 恒为 `true` 的匿名约束。**它不是装饰**：
    /// 生成器要为每条约束走一次判定，多一条就多一次虚调用——**这正是要计时的那部分。**
    ///
    /// The baseline adds an anonymous constraint whose `isSatisfied` is always `true`. **It is not decoration**: the generator consults every constraint on every
    /// candidate, so one more is one more virtual call — **which is exactly the part being timed.**
    /// </remarks>
    private sealed class AlwaysSatisfiedConstraint : ICuttingPlanConstraint<Flt64> {
        /// <summary>恒真 / Always true.</summary>
        /// <param name="context">约束上下文 / The constraint context.</param>
        /// <returns>恒为 true / Always true.</returns>
        public bool IsSatisfied(CuttingPlanConstraintContext<Flt64> context) => true;
    }
}
