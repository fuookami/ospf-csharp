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
/// 核了 `docs/kotlin-sync-acceptance.md`，四处与实测不符。其中 `9.10` 最要紧：它写着"基线 **231** 文件 / C# **101** 文件"，
/// 而实测是 **185 / 175**——**一份把差距写成 130 个文件的表，会让下一个人以为还差一大半，而实际只差 10 个。**
///
/// 这和 的 SCIP 计数是同一个形状：**错的数字会改变工作量判断，方向不重要。**
///
/// checked the acceptance table and found four numbers wrong. `9.10` mattered most: it said **231 / 101** against a measured **185 / 175** — **a 130-file gap reads
/// as "most of the work remains" when ten files do.** Same shape as the earlier SCIP count: **a wrong number changes the judgement of the work, and the direction does not
/// matter.**
/// </remarks>
public class AcceptanceNumbersRemeasuredTests
{
    /// <summary>**"口径不同"能用一个对得上号的数字，让缺口消失** / A scope mismatch can make a gap vanish behind a number that lines up.</summary>
    /// <remarks>
    /// 计划里两处写着 `framework-plugin` **61 vs 48/53**，并下结论"**命名口径差异，不是缺口**"。
    ///
    /// **按同一口径重数：基线 `ospf-kotlin-framework-plugin` 61 个主源码 vs 本仓 `Framework.Plugin` 15 个。**
    /// 那 48/53 是**把所有 `*Plugin*` 项目加在一起**（`Core.Plugin.Gurobi`/`Scip`/`Heuristic`… 共 129 个.cs）——
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
    /// **Recounting like-for-like: the baseline module has 61 main sources, this repo's `Framework.Plugin` has 15.**
    /// The 48/53 summed **every `*Plugin*` project** (`Core.Plugin.Gurobi`/`Scip`/`Heuristic`… 129.cs files in all) —
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
    /// 按文件数扫了 22 个模块，**而文件数相等不代表内容相等**——一个文件可以有三个类型，也可以一个都没有。
    /// **换了口径**：提取基线每个模块的公开类型名（剔掉 `internal`），逐一在本仓源码里查找。
    ///
    /// **这一条钉住"口径换了且结论写下来了"**：矩阵里必须有脚本路径、有命中率、有已知的三处低命中。
    /// **它不钉"命中率是多少"**——那个数每补一处就变，钉死会让每条修复都变成一次红。
    ///
    /// **Sweeping 22 modules by file count shows equal counts do not mean equal content** — one file may hold three types or none.
    /// **A different measure**: extract each baseline module's public type names (dropping `internal`) and look each one up in this repo.
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
    /// **扫描得到 `core` 命中率 74.3%、166 个未出现。而"未出现"掩盖了三种情况：真缺、换了机制、改了名。**
    /// **只有第一种要补。**
    ///
    /// **这一条钉的是"分类这件事做过、且三个成因都写下来了"**——
    /// **它不钉具体数字**，因为每补一处分类就会变；**而钉死数字会让每条修复都变成一次红。**
    ///
    /// **Measured: `core` at 74.3% with 166 absent. "Absent" hides three things**: genuinely missing, changed
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
