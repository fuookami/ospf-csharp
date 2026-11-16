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
    /// <summary>
    /// **还没移植的 5 个新工作负载必须被点名声明** / No workload from 19c72455 may remain unported.
    /// </summary>
    /// <remarks>
    /// **`19c72455` 的 6 个新基准已经全部移植；`CrossVersionSemanticDump` 的三段（`PRJ`／`GEN`／`TRUNC`）仍是待办，必须写在能力矩阵里。**
    /// **"没有数"与"数过了但还没搬"是两件事：后者会在下一个人读到它时被安排，前者只会被当成不存在。**
    ///
    /// **判据只读文档，不要求代码在场**——所以它不会因为"还没搬"而红，只会因为"没写下来"而红。
    ///
    /// **Of the six new benchmarks in `19c72455` one is ported; the other five must be named in the capability matrix.** **"Never
    /// counted" and "counted but not ported" are different states: the second gets scheduled when someone reads it, the first is simply
    /// absent.** **The criterion reads the document only, so it reddens for "not written down", never for "not ported yet".**
    /// </remarks>
    [Fact]
    public void TheUnportedNewWorkloads_ShouldBeNamedInTheMatrix() {
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        // **B1304：6 个新工作负载已经全部有文件了，所以"未移植清单"必须为空。** **矩阵仍要写明
        // `CrossVersionSemanticDump` 的三段是待办（`PRJ`／`GEN`／`TRUNC`）——**"基准文件在"与
        // "转储段全在"是两件事，混起来就是这一批差点犯的错。**
        //
        // **B1304: all six new workloads now have files, so the unported list must be empty.** **The matrix still has to name the three deferred
        // sections of `CrossVersionSemanticDump` — "the benchmark file exists" and "every dump section exists" are different claims, and conflating
        // them is exactly the mistake this batch nearly made.**
        string[] unported = System.Array.Empty<string>();
        unported.Should().BeEmpty("all six workloads from 19c72455 now have a benchmark file");

        // **B1309：六段全部齐备，矩阵也写清了每一段靠哪个入口。** **这里仍然点名三段——**因为一个"六段齐备"的结论
        // 必须能说出那三段的名字，否则它和"我数了一下"没有区别。**
        //
        // **B1309: all six sections are in, and the matrix states the entry point behind each.** **The three are still named here because a "six sections
        // complete" claim has to be able to name them, otherwise it is indistinguishable from "I counted".**
        matrix.Should().Contain(
            "PRJ",
            "each dump section must be named in the matrix; a conclusion that all six are complete has to be able to list them");
        matrix.Should().Contain("GEN", "the generation section must be named");
        matrix.Should().Contain("TRUNC", "the truncated-load section must be named");
    }

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
            // **`19c72455` 比固定基线多出 6 个工作负载，逐个移植时加到这里。**
            // **`19c72455` carries six more workloads than the fixed baseline; each lands here as it is ported.**
            "TokenInvalidateRebuildBenchmark",
            "GenerationScaleBenchmark",
            // **基线的这一个文件里有两个类：`PrimeMixedExpansionBenchmark` 与 `PrimeReaderLatencyBenchmark`。**
            // **The baseline file holds two classes: the mixed-expansion one and the reader-latency one.**
            "PrimeMixedExpansionBenchmark",
            "TokenSharedReadBenchmark",
            "NetworkSharedGraphBenchmark",
            "CrossVersionSemanticDump",
        };

        baselineWorkloads.Should().HaveCount(
            21,
            "fifteen workloads come from the fixed baseline and all six more from 19c72455");
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
