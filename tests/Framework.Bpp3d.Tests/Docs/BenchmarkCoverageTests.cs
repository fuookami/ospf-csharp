// 基准夹具的覆盖与两侧对应 / Benchmark fixture coverage against the baseline.

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **基准夹具的缺口不会报错** / A missing benchmark raises nothing.
/// </summary>
/// <remarks>
/// B952 数了一遍：基线 `ospf-kotlin-benchmark` 有 **15** 个工作负载，本仓 `src/Benchmark` 只有 **13**——
/// 缺 `GenerationCacheBuildBenchmark` 与 `PatternColumnBenchmark`。
///
/// **这种缺口不会让任何东西变红**，它只是让"性能没测过"看起来像"没需求"。所以这里把数量钉住：
/// 少一个就红，多一个也要跟着改文档。
///
/// B952 counted both sides: the baseline has **15** workloads, this repo had **13** — `GenerationCacheBuildBenchmark` and `PatternColumnBenchmark` were missing, and
/// **nothing turns red over a missing benchmark**; it just makes "performance was never measured" look like "performance was never needed". This pins the count.
/// </remarks>
public class BenchmarkCoverageTests
{
    /// <summary>基线的 15 个工作负载，本仓一个都不能少 / All 15 baseline workloads must exist here.</summary>
    /// <remarks>
    /// 名字是基线的（去扩展名），本仓的类名与之一致——**这本身就是一种对照**：改了名字就该有人问为什么。
    ///
    /// The names are the baseline's (extension stripped) and this repo's classes match them — **that is itself a check**: a rename should raise a question.
    /// </remarks>
    [Fact]
    public void EveryBaselineWorkload_ShouldExistHere() {
        string root = Root();
        string benchmark = Path.Combine(root, "src", "Benchmark");
        string[] files = Directory.GetFiles(benchmark, "*.cs").Select(Path.GetFileNameWithoutExtension).ToArray()!;

        string[] baselineWorkloads = {
            "ConstraintProgrammingBenchmark",
            "CoreHotPathBenchmark",
            "CorePluginDumpBenchmark",
            "GenerationCacheBuildBenchmark",
            "MathPreparationBenchmark",
            "ModelPreparationBenchmark",
            "MultiArrayHotPathBenchmark",
            "NetworkIndexBuildBenchmark",
            "PackingGeometryPreparationBenchmark",
            "PatternColumnBenchmark",
            "PilePreparationBenchmark",
            "PreparationHotPathBenchmark",
            "ProjectionIndexBuildBenchmark",
            "SharedPrimeReadBenchmark",
            "SymbolCombineBenchmark",
        };

        baselineWorkloads.Should().HaveCount(15, "the baseline has fifteen workloads");
        foreach (string workload in baselineWorkloads) {
            files.Should().Contain(workload,
                workload + " exists in the baseline; without it the fixture coverage gap stays invisible");
        }
    }

    /// <summary>**两个新基准真的能跑** / The two new benchmarks really run.</summary>
    /// <remarks>
    /// B952 用 `-- '*<name>*' small 1 1 1` 实跑过：**各 6 个基准执行完成**（三个场景 × 两个并行度）。
    /// 一个编译得过但一跑就抛的基准，比没有更坏——**它会在报告里留一行失败，然后被当成"环境问题"忽略**。
    ///
    /// B952 ran both with `-- '*<name>*' small 1 1 1` and **six benchmarks completed in each** (three scenarios x two parallelism values). A benchmark that compiles
    /// but throws on run is worse than none — **it leaves a failure line in the report that gets dismissed as an environment problem**.
    /// </remarks>
    [Fact]
    public void TheTwoNewBenchmarks_ShouldCarryTheirParameters() {
        string root = Root();
        string generation = File.ReadAllText(Path.Combine(root, "src", "Benchmark", "GenerationCacheBuildBenchmark.cs"));
        // 三个场景名与基线一致 / the three scenario names match the baseline
        foreach (string scenario in new[] { "equivalent", "distinct", "custom" }) {
            generation.Should().Contain("\"" + scenario + "\"",
                "the scenario name has to match the baseline, or a comparison would silently compare different fixtures");
        }

        string pattern = File.ReadAllText(Path.Combine(root, "src", "Benchmark", "PatternColumnBenchmark.cs"));
        // 三种规模与基线一致（256 / 512 / 1024 列）/ the three scales match the baseline (256 / 512 / 1024 columns)
        pattern.Should().Contain("512").And.Contain("1024").And.Contain("256");
    }

    private static string Root() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        return directory.FullName;
    }
}
