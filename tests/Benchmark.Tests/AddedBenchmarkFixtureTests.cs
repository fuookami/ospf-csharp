#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Unit;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Test.Benchmark;

/// <summary>
/// 补齐的两个基准：**参数真的接到了夹具上** / The two benchmarks added: the parameters really reach the fixture.
/// </summary>
/// <remarks>
/// 一个"参数没接上"的基准会在报告里给出漂亮的数字，**而它测的东西和参数无关**。
/// 这两条判据检查的是"三个场景构造出了不同的输入"，不是"跑得快不快"——**夹具的对错和性能无关，但性能结论依赖夹具对。**
///
/// A benchmark whose parameter never reached the fixture reports a fine number **for something the parameter has nothing to do with**. These criteria check that the
/// three scenarios build different inputs, not how fast anything is — **fixture correctness is independent of performance, and any performance claim depends on it.**
/// </remarks>
public class AddedBenchmarkFixtureTests {
    /// <summary>**`equivalent` 让物料可互相替换，`distinct` 让它们不可** / `equivalent` makes materials interchangeable, `distinct` does not.</summary>
    [Fact]
    public void TheScenario_ShouldChangeTheMaterialBounds() {
        var equivalent = new GenerationCacheBuildBenchmark { Scenario = "equivalent", Workers = 1 };
        equivalent.Setup();
        var distinct = new GenerationCacheBuildBenchmark { Scenario = "distinct", Workers = 1 };
        distinct.Setup();

        Flt64[] equivalentBounds = equivalent.Input.Materials
            .Select(material => material.WidthRange.Width.UpperBound.Value)
            .Distinct().ToArray();
        Flt64[] distinctBounds = distinct.Input.Materials
            .Select(material => material.WidthRange.Width.UpperBound.Value)
            .Distinct().ToArray();

        equivalentBounds.Should().HaveCount(1,
            "the equivalent scenario has to give every material the same upper bound, or the cache can never hit");
        distinctBounds.Should().HaveCount(12,
            "the distinct scenario has to give each material its own bound, or the fixture is the equivalent one under another name");
    }

    /// <summary>**夹具的单位是米与件** / The fixture uses metres and rolls.</summary>
    [Fact]
    public void TheGenerationFixture_ShouldUseBaseUnits() {
        var benchmark = new GenerationCacheBuildBenchmark { Scenario = "custom", Workers = 4 };
        benchmark.Setup();

        benchmark.Input.Materials.Should().HaveCount(12, "the baseline builds twelve materials");
        benchmark.Input.Demands.Should().HaveCount(8, "the baseline builds eight products");
        benchmark.Input.Materials[0].WidthRange.Width.LowerBound.Unit.Should().Be(SIBaseUnits.Meter);
    }

    /// <summary>**列数随 `dataset` 变，分批不丢列** / The column count follows `dataset`, and batching loses none.</summary>
    [Theory]
    [InlineData("small", 256)]
    [InlineData("medium", 512)]
    [InlineData("large", 1024)]
    public void TheDataset_ShouldDecideTheColumnCount(string dataset, int expected) {
        var benchmark = new PatternColumnBenchmark { Dataset = dataset, BatchSize = 32 };
        benchmark.Setup();

        benchmark.Batches.Sum(batch => batch.Count).Should().Be(expected,
            "every column has to reach a batch; a dropped column makes the benchmark measure fewer columns than it claims");
    }

    /// <summary>**批大小不改变总列数** / The batch size does not change the total.</summary>
    [Fact]
    public void TheBatchSize_ShouldNotChangeTheTotal() {
        var single = new PatternColumnBenchmark { Dataset = "small", BatchSize = 1 };
        single.Setup();
        var batched = new PatternColumnBenchmark { Dataset = "small", BatchSize = 32 };
        batched.Setup();

        single.Batches.Sum(batch => batch.Count).Should().Be(batched.Batches.Sum(batch => batch.Count));
        single.Batches.Should().HaveCount(256, "batch size 1 means one column per batch");
        batched.Batches.Should().HaveCount(8, "256 columns in batches of 32 is eight batches");
    }
}
