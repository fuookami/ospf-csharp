// 核心热点、插件导出、多维数组与符号组合基准的规模有效性测试
// Scale-validity tests for the core hot-path, plugin dump, multi-array and symbol-combine benchmarks.

using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// 这四组基准的参数必须真的改变输入规模，且被测方法必须真的**消费**那个规模。
/// These four benchmark groups' parameters must really change the input size, and the measured methods must really **consume** that size.
/// </summary>
/// <remarks>
/// 基准最容易犯的错误是**夹具变了、被测路径没变**（B290 已记录过一次）。这里的判据是
/// **"结果随规模单调变化"**：如果被测方法返回的计数不随 `small → medium → large` 增长，
/// 那三档测的就是同一条路径，报出来的"规模趋势"是假的。
///
/// The easiest benchmark mistake is **changing the fixture while the measured path stays the same** (B290 recorded one). The criterion here is
/// **"the result grows monotonically with the scale"**: if the measured method's returned count does not grow from `small → medium → large`, the three
/// tiers measure one path and the reported "scale trend" is fake.
/// </remarks>
public sealed class BenchmarkScaleValidityTests {
    /// <summary>**核心热点的合并规模必须随档位增长** / The core hot-path merge size must grow with the tier.</summary>
    [Fact]
    public void CoreHotPath_ShouldGrowMonotonicallyAcrossTiers() {
        int small = MeasureLinear("small");
        int medium = MeasureLinear("medium");
        int large = MeasureLinear("large");

        small.Should().BeLessThan(medium);
        medium.Should().BeLessThan(large);

        static int MeasureLinear(string dataset) {
            var benchmark = new CoreHotPathBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.MergeLinearFlattenDataLikeP19();
        }
    }

    /// <summary>**二次合并也要随档位增长** / The quadratic merge must grow as well.</summary>
    [Fact]
    public void CoreHotPath_ShouldGrowQuadraticallyAcrossTiers() {
        int small = MeasureQuadratic("small");
        int medium = MeasureQuadratic("medium");
        int large = MeasureQuadratic("large");

        small.Should().BeLessThan(medium);
        medium.Should().BeLessThan(large);

        static int MeasureQuadratic(string dataset) {
            var benchmark = new CoreHotPathBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.MergeQuadraticFlattenDataLikeP19();
        }
    }

    /// <summary>**插件导出的变量数必须随档位增长** / The plugin dump's variable count must grow with the tier.</summary>
    /// <remarks>
    /// 只断言"非零"是不够的：如果 `Setup` 忽略了 `Dataset`，三档都会返回同一个非零数。
    ///
    /// Asserting "non-zero" is not enough: if `Setup` ignored `Dataset`, all three tiers would return the same non-zero count.
    /// </remarks>
    [Fact]
    public void CorePluginDump_ShouldGrowMonotonicallyAcrossTiers() {
        int small = Measure("small");
        int medium = Measure("medium");
        int large = Measure("large");

        small.Should().BeLessThan(medium);
        medium.Should().BeLessThan(large);

        static int Measure(string dataset) {
            var benchmark = new CorePluginDumpBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.PrepareVariableDumpingDataHotPath();
        }
    }

    /// <summary>**多维数组的元素总数必须随档位增长** / The multi-array element count must grow with the tier.</summary>
    /// <remarks>
    /// **不能用 `FromListRowMajor` 测这件事**：它返回的是某个位置的**元素值**（`i % 97`），
    /// 与数组规模无关——`small` 可能返回 88、`large` 可能返回 15，看起来像"规模变小了"。
    /// `FlattenColumnMajor` 返回 `list.Count`，那才是规模。
    ///
    /// **`FromListRowMajor` cannot measure this**: it returns the **element value** at one position (`i % 97`), unrelated to the array size — `small` might
    /// return 88 while `large` returns 15, looking like "the scale shrank". `FlattenColumnMajor` returns `list.Count`, which is the size.
    /// </remarks>
    [Fact]
    public void MultiArrayHotPath_ShouldGrowMonotonicallyAcrossTiers() {
        int small = Measure("small");
        int medium = Measure("medium");
        int large = Measure("large");

        small.Should().BeLessThan(medium);
        medium.Should().BeLessThan(large);

        static int Measure(string dataset) {
            var benchmark = new MultiArrayHotPathBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.FlattenColumnMajor();
        }
    }

    /// <summary>**符号组合的线性单项式数必须随档位增长** / The symbol-combine linear monomial count must grow with the tier.</summary>
    [Fact]
    public void SymbolCombine_ShouldGrowMonotonicallyAcrossTiers() {
        int small = Measure("small");
        int medium = Measure("medium");
        int large = Measure("large");

        small.Should().BeLessThan(medium);
        medium.Should().BeLessThan(large);

        static int Measure(string dataset) {
            var benchmark = new SymbolCombineBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.CombineLinearIterable();
        }
    }

    /// <summary>**符号组合的二次单项式数也要随档位增长** / The quadratic monomial count must grow as well.</summary>
    [Fact]
    public void SymbolCombine_ShouldGrowQuadraticallyAcrossTiers() {
        int small = Measure("small");
        int medium = Measure("medium");
        int large = Measure("large");

        small.Should().BeLessThan(medium);
        medium.Should().BeLessThan(large);

        static int Measure(string dataset) {
            var benchmark = new SymbolCombineBenchmark { Dataset = dataset };
            benchmark.Setup();
            return benchmark.CombineQuadraticIterable();
        }
    }
}
