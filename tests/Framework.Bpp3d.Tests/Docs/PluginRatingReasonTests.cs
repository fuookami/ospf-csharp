// 每一行定级都要有理由，而且理由要是实测的 / Every rating row needs a reason, and the reason has to be measured.

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **空着的备注比写错的备注更难发现** / An empty remark is harder to catch than a wrong one.
/// </summary>
/// <remarks>
/// 核了矩阵第三节：SCIP 那行写着"**没有真实原生求解的实测**"，而门禁里 `Scip.Tests` **149 通过**，对偶、CP、Benders 报告、IIS、跨语言弹性对照都在里面；
/// Mosek / MindOpt / Hexaly 三行**备注空着**，而三者都有真实代码（1388 / 3009 / 2619 行），卡在许可或 SDK。
///
/// **写错的会被人核**，**空着的只会被当成"没什么可说的"**——而这三行每一行都有实测过的话要说。
///
/// The SCIP row claimed "no real native solving was measured" while the gate runs `Scip.Tests` with **149 passing**; the Mosek / MindOpt / Hexaly rows left the remark
/// **empty** while all three carry real code and are blocked on a licence or SDK. **A wrong remark gets checked; an empty one reads as "nothing to say"** — and each of
/// these had something measured to say.
/// </remarks>
public class PluginRatingReasonTests
{
    /// <summary>**求解器插件表里没有空备注** / No empty remark in the plugin table.</summary>
    /// <remarks>
    /// 只看第三节那张表。``➖`` 的行允许为空吗？**不允许**：那两行写明了"代码本身未实现"，说明空着从来不是这个表想要的样子。
    ///
 /// Scoped to section three. Do the ``➖`` rows get to stay empty? **No**: those two state "the implementation itself does not implement it", which shows the table never
    /// wanted empty cells in the first place.
    /// </remarks>
    [Fact]
    public void ThePluginTable_ShouldHaveNoEmptyRemark() {
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        string section = SectionThree(matrix);
        string[] rows = section.Split('\n')
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains("`Core.Plugin.", StringComparison.Ordinal))
            .ToArray();

        rows.Should().NotBeEmpty("the plugin table names plugin projects");

        foreach (string row in rows) {
            // **末尾那一格就是备注**：`| … | ◐ |  |` 的最后一格为空，正是 抓到的形状。
            // **The trailing cell is the remark**: `| … | ◐ |  |` leaves it empty, which is exactly the shape found.
            string[] cells = row.Trim().Trim('|').Split('|')
                .Select(cell => cell.Trim())
                .ToArray();
            // **判据要的是"末尾那一格不空"，不是"这张表恰好四列"。** '
            // **三列的表（`行 | 原来写的 | 实测`）末尾那格同样是备注，同样不许空着。**
            //
            // **What matters is "the trailing cell is not empty", not "this table has exactly four columns".** A three-column table's trailing cell is a remark too, and it may not be blank.
            cells.Should().HaveCountGreaterThanOrEqualTo(3, "a row carries at least capability and a trailing remark");
            cells[^1].Should().NotBeNullOrWhiteSpace(
                row.Trim() + " leaves the remark empty; each of these rows has a measured blocker or a measured capability to state");
        }
    }

    /// <summary>**SCIP 那行要写着实测证据** / The SCIP row states the measured evidence.</summary>
    /// <remarks>
    /// 这一行此前是**反向失真**：门禁跑的是真实原生求解 149 项，而矩阵说"没有实测"。
    /// **定级比证据弱，和定级比证据强一样，都让下一个人做错决定**——前者让他去重做，后者让他不去做。
    ///
    /// This row was **understated**: the gate runs 149 real native solving tests while the matrix claimed none were measured. **A rating weaker than its evidence
    /// misleads as much as a stronger one** — it makes the next reader redo work that was already done.
    /// </remarks>
    [Fact]
    public void TheScipRow_ShouldStateTheMeasuredEvidence() {
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        string row = SectionThree(matrix).Split('\n')
            .FirstOrDefault(line => line.StartsWith("| SCIP |", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the SCIP row was not found");

        row.Contains("149", StringComparison.Ordinal)
            .Should().BeTrue("the measured count has to be in the row, or the next reader cannot tell it was run");
        row.Contains("没有真实原生求解的实测", StringComparison.Ordinal)
            .Should().BeFalse("that claim was false; the gate runs the SCIP integration suite every batch");
        // **而它确实在跑**：这条判据同时确认那套集成测试真的存在，否则行里的数字是空口。
        // **And it really runs**: this also confirms the integration suite exists, or the number in the row is hearsay.
        string scipTests = Path.Combine(root, "tests", "Core.Plugin.Scip.Tests");
        Directory.GetFiles(scipTests, "*Integration*.cs").Length.Should().BeGreaterOrEqualTo(5,
            "the SCIP row cites integration tests by name, so they have to exist");
    }

    /// <summary>**三个环境受限的插件写着实测的拦截串** / The three environment-blocked plugins state their measured blocker.</summary>
    [Fact]
    public void TheBlockedPlugins_ShouldStateTheirMeasuredBlocker() {
        string root = Root();
        string section = SectionThree(File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md")));

        (string Name, string Blocker)[] blocked = {
            ("Mosek", "err_missing_license_file"),
            ("MindOpt", "License is not valid"),
            ("Hexaly", "HEXALY_HOME"),
        };

        foreach ((string name, string blocker) in blocked) {
            string row = section.Split('\n')
                .FirstOrDefault(line => line.StartsWith("| " + name + " |", StringComparison.Ordinal))
                ?? throw new InvalidOperationException(name + " row not found");
            row.Contains(blocker, StringComparison.Ordinal)
                .Should().BeTrue(name + " is blocked by a measured error string; without it the row is indistinguishable from a code gap");
        }
    }

    /// <summary>**Gurobi 11 不能只写"同上"** / "Same as above" is not an answer for Gurobi 11.</summary>
    /// <remarks>
    /// 本仓是 **4 个类委托**给 `Core.Plugin.Gurobi`，**22 个文件的独立副本**。写"同上"会让读者以为两者形状相同。
    ///
 /// This repo has **four classes delegating** to `Core.Plugin.Gurobi`; this implementation is an **independent 22-file copy**. "Same as above" makes a reader believe the
    /// shapes match.
    /// </remarks>
    [Fact]
    public void TheGurobi11Row_ShouldExplainTheDelegation() {
        string root = Root();
        string section = SectionThree(File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md")));

        string row = section.Split('\n')
            .FirstOrDefault(line => line.StartsWith("| Gurobi 11 |", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the Gurobi 11 row was not found");

        row.Contains("委托", StringComparison.Ordinal)
            .Should().BeTrue("the delegation is the fact that distinguishes it from the baseline's copy");
        row.Trim().EndsWith("| 同上 |", StringComparison.Ordinal)
            .Should().BeFalse("\"same as above\" hid the difference");

        // **委托是真的**：`Core.Plugin.Gurobi11` 里应有对 `GurobiSolver` 的引用。
        // **The delegation is real**: `Core.Plugin.Gurobi11` should reference `GurobiSolver`.
        string solver = File.ReadAllText(Path.Combine(root, "src", "Core.Plugin.Gurobi11", "Gurobi11LinearSolver.cs"));
        solver.Should().Contain("GurobiSolver",
            "the row says these classes delegate, so the reference has to be there");
    }

    private static string SectionThree(string matrix) =>
        matrix.Substring(matrix.IndexOf("## 三、求解器插件", StringComparison.Ordinal));

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
