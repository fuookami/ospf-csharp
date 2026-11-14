// 验收表里的数字，重新量一遍 / Re-measure the numbers in the acceptance table.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **验收表里的数字要能被重新量出来** / The numbers in the acceptance table have to be re-measurable.
/// </summary>
/// <remarks>
/// B956 核了 `docs/kotlin-sync-acceptance.md`，四处与实测不符。其中 `9.10` 最要紧：它写着"基线 **231** 文件 / C# **101** 文件"，
/// 而实测是 **185 / 175**——**一份把差距写成 130 个文件的表，会让下一个人以为还差一大半，而实际只差 10 个。**
///
/// 这和 B948 的 SCIP 计数是同一个形状：**错的数字会改变工作量判断，方向不重要。**
///
/// B956 checked the acceptance table and found four numbers wrong. `9.10` mattered most: it said **231 / 101** against a measured **185 / 175** — **a 130-file gap reads
/// as "most of the work remains" when ten files do.** Same shape as B948's SCIP count: **a wrong number changes the judgement of the work, and the direction does not
/// matter.**
/// </remarks>
public class AcceptanceNumbersRemeasuredTests
{
    /// <summary>**Demo2 的文件数是 175** / Demo2 has 175 files.</summary>
    [Fact]
    public void TheDemo2FileCount_ShouldBeMeasuredNotRemembered() {
        string root = Root();
        int actual = Directory.GetFiles(
            Path.Combine(root, "src", "Example", "FrameworkDemo", "Demo2"),
            "*.cs",
            SearchOption.AllDirectories).Length;

        string table = Table(root);
        table.Contains("C# **" + actual + "** 文件", StringComparison.Ordinal)
            .Should().BeTrue("the table states the C# file count, so it has to state the measured one (" + actual + ")");
        // **旧读数不许留下**：`101` 是项目早期的数字，留着就会有人照它估算工作量。
        // **The old reading must go**: `101` was an early number, and leaving it in makes somebody size the work by it.
        table.Contains("C# **101** 文件", StringComparison.Ordinal)
            .Should().BeFalse("101 was an early reading; the measured count is " + actual);
    }

    /// <summary>**SQLite 那条不再是"只验证了仓储接口"** / SQLite is no longer "only the repository interface was verified".</summary>
    /// <remarks>
    /// B950 补了 `SqliteRoundTripTests`：**文件库**（不是 `:memory:`）+ **关连接再开**。
    /// 判据同时钉住那两条判据真的存在，否则文档说"已实测"而测试被删了也没人知道。
    ///
    /// B950 added `SqliteRoundTripTests`: a **file** database and a **closed-then-reopened** connection. This also pins that those criteria exist, or the document could
    /// claim "measured" after the tests were deleted.
    /// </remarks>
    [Fact]
    public void TheSqliteRoundTrip_ShouldExistAndBeCited() {
        string root = Root();
        string tests = Path.Combine(root, "tests", "Framework.Plugin.Tests", "Persistence", "SqliteRoundTripTests.cs");
        File.Exists(tests).Should().BeTrue("the table cites this class, so it has to exist");

        string source = File.ReadAllText(tests);
        source.Should().Contain("SurviveAClosedConnection",
            "the point of a round trip is that the data survives the connection; testing inside one connection proves nothing about persistence");

        string table = Table(root);
        table.Contains("SQLite 已实测", StringComparison.Ordinal)
            .Should().BeTrue("the row has to say what was measured");
        // **只在 5.4 那一行里查**：更正记录里引用了原话，全表查会把它当成"还在"。
        // **Check inside the 5.4 row only**: the correction note quotes the old wording, and a table-wide search would read that as "still there".
        string row54 = table.Split('\n')
            .FirstOrDefault(line => line.StartsWith("| 5.4 |", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("row 5.4 not found");
        row54.Contains("只验证了仓储接口", StringComparison.Ordinal)
            .Should().BeFalse("that was true before B950 and is not true now");
    }

    /// <summary>**三个后端是真跑通过的，不是"部分许可"** / Three backends really ran; it is not a partial licence.</summary>
    [Fact]
    public void TheReachableBackends_ShouldBeStatedAsVerified() {
        string root = Root();
        string table = Table(root);

        // **B949 实跑的数字**：829 / 26 / 37。表里若把它们写成"部分许可"，等于把已验证的降级。
        // **The numbers B949 measured**: 829 / 26 / 37. Writing them as "partial licence" downgrades what was verified.
        table.Contains("829 通过", StringComparison.Ordinal).Should().BeTrue("Gurobi's gate really ran");
        table.Contains("26", StringComparison.Ordinal).Should().BeTrue("CPLEX's gate really ran");
        table.Contains("37", StringComparison.Ordinal).Should().BeTrue("COPT's gate really ran");
        table.Contains("部分许可", StringComparison.Ordinal)
            .Should().BeFalse("all three had a working licence and all three ran; 'partial' understates it");
    }

    /// <summary>**端口开着不等于能用** / An open port does not mean usable.</summary>
    [Fact]
    public void TheDatabaseBoundary_ShouldDistinguishPortsFromCredentials() {
        string root = Root();
        string table = Table(root);

        table.Contains("端口开着但无可用凭据", StringComparison.Ordinal)
            .Should().BeTrue("B950 measured that 3306/5432/6379 answer and reject every credential, which is a different obstacle from having no server");
        // **`⊘3` 原来一句"无服务"把两种情况混成了一种**：Kafka/MongoDB 确实没实例，MySQL/PG/Redis 有实例没凭据。
        // **`⊘3` used to say "no service", conflating two cases**: Kafka and MongoDB have no instance; MySQL, PostgreSQL and Redis have one but no credentials.
        table.Contains("9092 与 27017 端口关闭", StringComparison.Ordinal)
            .Should().BeTrue("and the ones with no instance at all have to be named as such");
    }

    /// <summary>**性能证据必须带得动统计，不是一次快照** / Performance evidence has to carry statistics, not a snapshot.</summary>
    /// <remarks>
    /// **旧的 Benders 日志是 N=1**：`StdErr = 0.000 us`、`ConfidenceInterval = [NaN; NaN]`。
    /// **那三个均值看着有六位有效数字，而它们只是三次单跑的读数**——
    /// **它甚至答不了"三个数据集有没有区别"：旧日志给出 49.7 / 49.1 / 53.7，非单调。**
    ///
    /// **B1101 用 3 fork / 3 warmup / 15 迭代重测，N = 40—45，均值 70.2 / 81.4 / 94.3（small/medium/large，单调），StdErr 0.4%。**
    /// **这条判据钉的是"日志里有统计量且不是 NaN"**：**把 N 退回 1，它必红。**
    ///
    /// **The old Benders log had N=1**: `StdErr = 0.000 us`, `ConfidenceInterval = [NaN; NaN]`.
    /// **Those means carried six significant digits while being three single readings** —
    /// **they could not even answer whether the three datasets differ: the old log gave 49.7 / 49.1 / 53.7, non-monotone.**
    /// **B1101 re-measured with 3 forks / 3 warmups / 15 iterations, N = 40—45, means 70.2 / 81.4 / 94.3 (small/medium/large, monotone), StdErr 0.4%.**
    /// **What this pins is that the log carries statistics and that they are not NaN**: **dropping N back to 1 turns it red.**
    /// </remarks>
    [Fact]
    public void ThePerformanceLog_ShouldCarryRealStatistics() {
        string root = Root();
        string path = Path.Combine(root, "logs", "p8-batch1101-benders-console.log");
        File.Exists(path).Should().BeTrue("the re-measured log is the evidence for row 9.8");

        string log = File.ReadAllText(path);
        log.Should().Contain("IterationCount=15, LaunchCount=3, WarmupCount=3",
            "one iteration is a snapshot, not a measurement");

        log.Should().NotContain("ConfidenceInterval = [NaN us; NaN us]",
            "a NaN interval means N is too small to say anything");
        log.Should().NotContain("StdErr = 0.000 us",
            "a zero standard error at N=1 is the signature of a single reading");

        // **三个数据集各有一条均值，且每条都带置信区间。**
        // **Three datasets, one mean each, every one with a confidence interval.**
        Regex.Matches(log, @"ConfidenceInterval = \[[0-9.]+ us; [0-9.]+ us\]").Count
            .Should().BeGreaterThanOrEqualTo(3, "each dataset reports its own interval");

        Table(root).Should().Contain("p8-batch1101-benders-console.log",
            "row 9.8 has to point at the log this criterion reads");
    }

    /// <summary>**"没有对跑"里有一半是格式不通，而格式已经通了** / Half of "no side-by-side run" was a format mismatch, and the format now matches.</summary>
    /// <remarks>
    /// 验收表 9.9 写着"**没有对跑**，不宣称提升"，标 ❌。**核对下来基线有完整的 benchmark 模块，
    /// 还有 `compare-benchmark-results.ps1`——它只读 JMH 的 `primaryMetric.score/scoreError/scoreUnit`，
    /// 而本仓用 BenchmarkDotNet，输出的是 `Statistics.Mean/StandardError`。**
    ///
    /// **两个格式都对，但它们不是同一个。** 所以"没有对跑"里有一半是**格式不通**，
    /// **而格式不通可以修**：`logs/b1115-bdn-to-jmh.py` 做转换，**并且已经用基线自己的脚本验证过**
    /// （33 条，输出含均值、样本数、单位与 ok 状态）。
    ///
    /// **这条判据钉三件事**：转换脚本在、脚本要求的 `rawData` 字段在、验收表 9.9 承认格式已通。
    /// **剩下那一半（真的跑一次 Kotlin 侧）仍没做，所以 9.9 仍是 ❌——这条判据不改变它。**
    ///
    /// Row 9.9 says "**no side-by-side run**" and is marked ❌. **Checked: the baseline has a full benchmark module**
    /// **and `compare-benchmark-results.ps1` — which reads only JMH's `primaryMetric.score/scoreError/scoreUnit`,**
    /// **while this repo uses BenchmarkDotNet, whose output carries `Statistics.Mean/StandardError`.**
    ///
    /// **Both formats are right; they are not the same one.** So half of "no side-by-side run" is **a format mismatch,**
    /// **and that can be fixed**: `logs/b1115-bdn-to-jmh.py` converts, **and it was validated with the baseline's own script**
    /// (33 entries, output carrying mean, sample count, unit and an ok status).
    ///
    /// **This pins three things**: the converter exists, the `rawData` field the script demands is emitted, and row 9.9 admits the format now matches.
    /// **The other half — actually running the Kotlin side — is still not done, so 9.9 stays ❌; this criterion does not change that.**
    /// </remarks>
    [Fact]
    public void TheBenchmarkFormats_ShouldBeBridgeable()
    {
        string root = Root();

        string converter = Path.Combine(root, "logs", "b1115-bdn-to-jmh.py");
        File.Exists(converter).Should().BeTrue("the converter is the bridge between the two benchmark formats");

        string source = File.ReadAllText(converter);
        source.Should().Contain("rawData",
            "the baseline script derives the sample count from primaryMetric.rawData and fails outright without it");
        source.Should().Contain("primaryMetric", "JMH puts the measurement under primaryMetric");
        source.Should().Contain("scoreUnit", "JMH declares the unit per benchmark");

        Table(root).Should().Contain("b1115-bdn-to-jmh.py",
            "row 9.9 has to point at the bridge, or the next reader re-derives it");
    }

    /// <summary>**"不切换 checkout"这条限制要有实测理由** / The "do not switch the checkout" rule needs a measured reason.</summary>
    /// <remarks>
    /// **"不切换 checkout"原来是任务约束，本仓文档里没有给出理由。**
    /// **而一条没有理由的约束，下一个人只会照抄，不会理解——也就不会在理由消失时解除它。**
    ///
    /// **B1116 实测的理由**：`ospf-kotlin` 的 `main` 比固定基线 `12fdb587` **领先 7 个提交**，
    /// `81 files changed, 13054 insertions(+), 1586 deletions(-)`，其中 **10 个是 `core/src/main` 的生产文件**，
    /// **包含 `MetaModel.kt` 与 `MechanismModel.kt`——正是基准测的热路径。**
    ///
    /// **所以"在 `main` 上跑基准"测的不是基线。** 这条判据钉住"文档里写着这个理由"。
    ///
    /// **"Do not switch the checkout" was a task constraint with no reason recorded here.**
    /// **A constraint without a reason gets copied without being understood — and then never lifted when the reason expires.**
    ///
    /// **The reason measured in B1116**: `main` is **7 commits ahead** of the pinned baseline `12fdb587`,
    /// `81 files changed, 13054 insertions(+), 1586 deletions(-)`, of which **10 are `core/src/main` production files**,
    /// **including `MetaModel.kt` and `MechanismModel.kt` — the very hot paths the benchmarks measure.**
    ///
    /// **So "run the benchmark on `main`" would not measure the baseline.** This pins that the reason is written down.
    /// </remarks>
    [Fact]
    public void TheNoCheckoutSwitchRule_ShouldCarryItsMeasuredReason()
    {
        string table = Table(Root());

        table.Should().Contain("12fdb587", "the pinned baseline is named");
        table.Should().Contain("领先 7 个提交",
            "the distance from main to the baseline is measured, not assumed");
        table.Should().Contain("core/src/main",
            "the reason is that production hot paths differ, not merely that the trees differ");
        table.Should().Contain("不切换 checkout",
            "the rule and its reason live in the same place");
    }

    /// <summary>**"口径不同"能用一个对得上号的数字，让缺口消失** / A scope mismatch can make a gap vanish behind a number that lines up.</summary>
    /// <remarks>
    /// 计划里两处写着 `framework-plugin` **61 vs 48/53**，并下结论"**命名口径差异，不是缺口**"。
    ///
    /// **B1117 按同一口径重数：基线 `ospf-kotlin-framework-plugin` 61 个主源码 vs 本仓 `Framework.Plugin` 15 个。**
    /// 那 48/53 是**把所有 `*Plugin*` 项目加在一起**（`Core.Plugin.Gurobi`/`Scip`/`Heuristic`… 共 129 个 .cs）——
    /// **拿一个模块比全部插件项目，数字自然"对得上"，于是缺口看起来消失了。**
    ///
    /// **同口径下也确实有真缺的**：基线的四个 Mongo 翻译器（`MongoBooleanTranslator` 422 行、`MongoScalarTranslator` 209、
    /// `MongoUpdateTranslator` 120、`MongoOrderByTranslator` 83，**共 18 条离线判据**）**C# 一个都没有**，
    /// 而 `src/` 下 `BsonDocument` 出现 **0** 次——`MongoRepository` 只会 `Filter.Eq("_id", …)`。
    ///
    /// **这条判据钉三件事**：重测脚本在、矩阵里写着真实的两个计数、且矩阵不许再只用"命名口径差异"解释。
    ///
    /// The plan says `framework-plugin` is **61 vs 48/53** and concludes "**a naming difference, not a gap**".
    ///
    /// **B1117 recounts like-for-like: the baseline module has 61 main sources, this repo's `Framework.Plugin` has 15.**
    /// The 48/53 summed **every `*Plugin*` project** (`Core.Plugin.Gurobi`/`Scip`/`Heuristic`… 129 .cs files in all) —
    /// **one module compared against every plugin project, so the number lined up and the gap disappeared.**
    ///
    /// **Like-for-like there is a real gap too**: the four baseline Mongo translators (`MongoBooleanTranslator` 422 lines,
    /// `MongoScalarTranslator` 209, `MongoUpdateTranslator` 120, `MongoOrderByTranslator` 83, **18 offline criteria between them**)
    /// **have no C# counterpart**, and `BsonDocument` appears **0** times under `src/` — `MongoRepository` only does `Filter.Eq("_id", …)`.
    ///
    /// **This pins three things**: the recount script exists, the matrix carries both real counts, and the matrix may not explain it away as naming alone.
    /// </remarks>
    [Fact]
    public void ThePluginGap_ShouldBeComparedLikeForLike()
    {
        string root = Root();

        string script = Path.Combine(root, "logs", "b1117-module-sizes.py");
        File.Exists(script).Should().BeTrue("the recount has to be reproducible, not remembered");

        string matrix = File.ReadAllText(
            Path.Combine(root, "docs", "kotlin-capability-matrix.md"));
        string row = matrix.Split('\u000a').First(line => line.Contains("MongoDB 翻译层", StringComparison.Ordinal));

        row.Should().Contain("logs/b1117-module-sizes.py",
            "the row points at the recount, so the numbers can be re-derived");
        row.Should().Contain("61", "the baseline module count is stated");
        row.Should().Contain("Framework.Plugin", "the comparison names the counterpart module, not every plugin project");
        row.Should().Contain("BsonDocument",
            "the absence is proven by a symbol search, not by a file count");
        row.Should().Contain("422",
            "the size of what is missing is stated, so the gap has a magnitude");
    }

    /// <summary>**按类型名扫，而不是按文件数** / Scan by type name, not by file count.</summary>
    /// <remarks>
    /// B1117 按文件数扫了 22 个模块，**而文件数相等不代表内容相等**——一个文件可以有三个类型，也可以一个都没有。
    /// **B1124 换了口径**：提取基线每个模块的公开类型名（剔掉 `internal`），逐一在本仓源码里查找。
    ///
    /// **这一条钉住"口径换了且结论写下来了"**：矩阵里必须有脚本路径、有命中率、有已知的三处低命中。
    /// **它不钉"命中率是多少"**——那个数每补一处就变，钉死会让每条修复都变成一次红。
    ///
    /// B1117 swept 22 modules by file count, **and equal counts do not mean equal content** — one file may hold three types or none.
    /// **B1124 changed the measure**: extract each baseline module's public type names (dropping `internal`) and look each one up in this repo.
    ///
    /// **This pins that the measure changed and the conclusion is written down**: the matrix must carry the script path, the hit rates,
    /// **and the three known low ones. It does not pin the rates themselves** — those move with every fix, and freezing them would make every fix red.
    /// </remarks>
    [Fact]
    public void TheTypeCoverageScan_ShouldBeRecordedWithItsMeasure()
    {
        string root = Root();
        File.Exists(Path.Combine(root, "logs", "b1124-type-coverage.py"))
            .Should().BeTrue("the scan has to be reproducible, not remembered");

        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        // **只检查目标行，不检查全文**：第一版搜全文，而 `公开类型名` 在别处也出现过一次，
        // **于是变异（把这一行的口径改成"文件数"）没能让判据红**——
        // **搜全文的判据验证的是全文，不是那一行。**
        //
        // **Check the target row only, not the whole file**: the first version searched the file, and `公开类型名`
        // appears elsewhere too, **so the mutation (relabelling this row as "file counts") left the criterion green** —
        // **a whole-file search verifies the file, not the row.**
        string[] matrixLines = matrix.Split('\u000a');
        int rowIndex = Array.FindIndex(matrixLines,
            line => line.StartsWith("| 类型面对齐", StringComparison.Ordinal));
        rowIndex.Should().BeGreaterThanOrEqualTo(0, "the type-coverage row exists");
        string row = matrixLines[rowIndex];

        row.Should().Contain("b1124-type-coverage.py",
            "the row points at the script, so the rates can be re-derived");
        row.Should().Contain("公开类型名",
            "the measure is named, or a reader would assume file counts again");
        row.Should().Contain("quantities",
            "the low-hit modules are named, so they are not quietly forgotten");
        row.Should().Contain("18.8%",
            "the worst module carries its number, which is what makes it the worst");
    }

    /// <summary>**"未出现"必须被拆开，而不是当成一个数** / "Absent" has to be broken apart, not counted.
    /// <remarks>
    /// B1124 扫出 `core` 命中率 74.3%、166 个未出现。**而"未出现"掩盖了三种情况：真缺、换了机制、改了名。**
    /// **只有第一种要补。**
    ///
    /// **这一条钉的是"分类这件事做过、且三个成因都写下来了"**——
    /// **它不钉具体数字**，因为每补一处分类就会变；**而钉死数字会让每条修复都变成一次红。**
    ///
    /// B1124 measured `core` at 74.3% with 166 absent. **"Absent" hides three things: genuinely missing, changed
    /// mechanism, renamed.** **Only the first needs work.**
    ///
    /// **This pins that the classification was done and all three causes are written down** —
    /// **it does not pin the numbers**, which move with every fix; **freezing them would make every fix red.**
    /// </remarks>
    [Fact]
    public void TheAbsentTypes_ShouldBeClassifiedByCause()
    {
        string root = Root();
        File.Exists(Path.Combine(root, "logs", "b1130-classify-absent.py"))
            .Should().BeTrue("the classification has to be reproducible, not remembered");

        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));
        string[] lines = matrix.Split('\n');
        int index = Array.FindIndex(lines,
            line => line.StartsWith("| 未出现类型的**成因分类**", StringComparison.Ordinal));
        index.Should().BeGreaterThanOrEqualTo(0, "the classification row exists");
        string row = lines[index];

        row.Should().Contain("b1130-classify-absent.py",
            "the row points at the script, so the split can be re-derived");
        row.Should().Contain("符号层",
            "the symbol-layer cause is named, which is where most of the 42 live");
        row.Should().Contain("泛型特化",
            "the generic-specialisation cause is named, or 14 types look missing when they are not");
        row.Should().Contain("未归类",
            "the unclassified remainder is admitted rather than rounded away");
    }

    private static string Table(string root) =>
        DocsFile.ReadAllText(Path.Combine(root, "docs", "kotlin-sync-acceptance.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

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
