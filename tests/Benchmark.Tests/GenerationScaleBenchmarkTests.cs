#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Xunit;

namespace Fuookami.Ospf.Test.Benchmark;

/// <summary>
/// **`GenerationScaleBenchmark` 的参数必须真的接到夹具上** /
/// The parameters of `GenerationScaleBenchmark` must really reach the fixture.
/// </summary>
/// <remarks>
/// **这个基准有四个参数维度（场景／生成器／并行度／方案上限），任一维度没接上，报告里都会出现一组"看起来很合理"的数字，
/// 而它测的是别的东西。** **三条判据各钉一个维度：**
///
/// **① 生成器维度立刻能从夹具类型看出来**（`NSumGenerator`／`FullSumGenerator`／`DFSGenerator`）；
/// **② 方案上限直接影响"是否有方案"**（上限 200 时必须真的停下，而不是搜到底）；
/// **③ 场景维度只改物料宽度上界**，所以 `distinct` 的 12 个物料上界必须两两不同、`equivalent` 必须全部相同。
///
/// **With four parameter dimensions any one can fail to land, leaving a plausible-looking set of numbers for something else.** **The three
/// criteria pin one dimension each: the generator is visible in the fixture's type; the budget really stops the search; and the scenario only
/// changes the material width bound, so `distinct` must give twelve different bounds and `equivalent` twelve identical ones.**
/// </remarks>
public sealed class GenerationScaleBenchmarkTests
{
    /// <summary>**`GeneratorName` 选出对应的生成器** / `GeneratorName` selects the matching generator.</summary>
    [Theory]
    [InlineData("dfs")]
    [InlineData("nsum")]
    [InlineData("fullsum")]
    public void TheGeneratorDimension_ShouldSelectTheGenerator(string generator) {
        var benchmark = new GenerationScaleBenchmark { GeneratorName = generator, Scenario = "equivalent", Workers = 1, Budget = 200 };
        benchmark.Setup();

        Type expected = generator switch {
            "nsum" => typeof(NSumGenerator<Fuookami.Ospf.Math.Algebra.Number.Flt64>),
            "fullsum" => typeof(FullSumGenerator<Fuookami.Ospf.Math.Algebra.Number.Flt64>),
            _ => typeof(DFSGenerator<Fuookami.Ospf.Math.Algebra.Number.Flt64>),
        };

        benchmark.Instance.GetType().Should().Be(
            expected,
            $"the {generator} parameter must build the {expected.Name} and nothing else");
    }

    /// <summary>**场景维度只改物料宽度上界** / The scenario dimension only changes the material width bound.</summary>
    [Fact]
    public void TheScenarioDimension_ShouldChangeTheWidthBounds() {
        var equivalent = new GenerationScaleBenchmark { Scenario = "equivalent", GeneratorName = "dfs", Workers = 1, Budget = 200 };
        equivalent.Setup();
        var distinct = new GenerationScaleBenchmark { Scenario = "distinct", GeneratorName = "dfs", Workers = 1, Budget = 200 };
        distinct.Setup();

        equivalent.Input.Materials.Should().HaveCount(12, "the fixture holds twelve materials");
        equivalent.Input.Demands.Should().HaveCount(8, "the fixture holds eight demands, one per product");

        var equivalentBounds = new HashSet<string>(
            equivalent.Input.Materials.Select(material => material.WidthRange.UpperBound.ToString()!));
        equivalentBounds.Should().HaveCount(1, "`equivalent` gives every material the same upper bound, so they can stand in for each other");

        var distinctBounds = new HashSet<string>(
            distinct.Input.Materials.Select(material => material.WidthRange.UpperBound.ToString()!));
        distinctBounds.Should().HaveCount(12, "`distinct` gives every material its own upper bound");
    }

    /// <summary>**方案上限真的会截断搜索** / The budget really truncates the search.</summary>
    /// <remarks>
    /// **上限 200 时方案数必须 ≤ 200；上限放大后搜索必须走得更远。** **如果上限没接上，两条判据都会落在同一侧——
    /// 那正是"参数没生效"的样子。**
    ///
    /// **With a budget of 200 the plan count must stay at or below 200, and a larger budget must go further.** **If the budget never landed,
    /// both checks fall on the same side — which is exactly what a parameter that does nothing looks like.**
    /// </remarks>
    [Fact]
    public void TheBudgetDimension_ShouldTruncateTheSearch() {
        var capped = new GenerationScaleBenchmark { Scenario = "equivalent", GeneratorName = "dfs", Workers = 1, Budget = 200 };
        capped.Setup();
        var uncapped = new GenerationScaleBenchmark { Scenario = "equivalent", GeneratorName = "dfs", Workers = 1, Budget = 100_000 };
        uncapped.Setup();

        int cappedPlans = capped.GenerateWithReport().Plans.Count;
        int uncappedPlans = uncapped.GenerateWithReport().Plans.Count;

        cappedPlans.Should().BeLessThanOrEqualTo(200, "a budget of 200 is an upper bound on the plans produced");
        uncappedPlans.Should().BeGreaterThan(
            cappedPlans,
            "the larger budget must reach further, or the budget parameter never reached the generator");
    }

    /// <summary>**摘要方法真的消费了报告** / The summary method really consumes the report.</summary>
    [Fact]
    public void TheSummary_ShouldReportPlansAndStopReason() {
        var benchmark = new GenerationScaleBenchmark { Scenario = "equivalent", GeneratorName = "dfs", Workers = 1, Budget = 200 };
        benchmark.Setup();

        string summary = benchmark.GenerateSummary();

        summary.Should().MatchRegex(
            @"^\d+:[A-Za-z]+$",
            "the summary is `<plans>:<stop reason>`, so a caller can see the result was read rather than dropped");
    }
}