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
/// **验收表里的数字必须能被重新量出来。** 一处曾经写着"**231** 文件 / **101** 文件"，
/// 而实测是 **185 / 175**——**一份把差距写成 130 个文件的表，会让下一个人以为还差一大半，而实际只差 10 个。**
///
/// 这和 SCIP 的计数是同一个形状：**错的数字会改变工作量判断，方向不重要。**
///
/// checked the acceptance table and found four numbers wrong. `9.10` mattered most: it said **231 / 101** against a measured **185 / 175** — **a 130-file gap reads
/// as "most of the work remains" when ten files do.** Same shape as the earlier SCIP count: **a wrong number changes the judgement of the work, and the direction does not
/// matter.**
/// </remarks>
public class AcceptanceNumbersRemeasuredTests
{
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
