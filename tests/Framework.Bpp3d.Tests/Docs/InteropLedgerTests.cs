#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 台账里那句"跨语言"必须与搬进来的文件对得上 / The ledger's interop sentence must match the files behind it.
/// </summary>
/// <remarks>
/// **B1077 没有新增源码。** 它改的是台账里那一行——**"只做了一部分"这句话现在是可数的了。**
///
/// 行里写了"用例表 6 节共 56 行"。**56 是算出来的，不是数出来的**，
/// 所以它会**随契约一起变**：加上一节而台账不改，这句话就变成假话。
///
/// **一个写着"只做了一部分"的格子，读起来像一个已经交代过的欠账。**
/// **而它既没说哪部分是做的，也没说哪部分不是。** 现在两样都有了。
///
/// **B1077 added no source code.** It changed one row in the ledger — **"only partly measured" is now countable.** The row says "56 rows in six sections", and **56 is computed, not eyeballed**, so it
/// **moves with the contract**: add a section without touching the ledger and the sentence becomes false. **A cell reading "only partly measured" reads like a debt already accounted for** — **while it
/// says neither which part is done nor which is not.** Now it says both.
/// </remarks>
public class InteropLedgerTests
{
    private const string LedgerPath = "docs/kotlin-sync-ledger.md";
    private const string CasesPath = "tests/Core.Tests/Analysis/Fixtures/analysis-cases.tsv";
    private const string ContractPath = "tests/Core.Tests/Analysis/Fixtures/analysis-contract.tsv";
    private const string AcceptancePath = "docs/kotlin-sync-acceptance.md";

    private static DirectoryInfo Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("repository root not found");
        }

        return directory;
    }

    private static string[] Lines(string relative) =>
        File.ReadAllLines(Path.Combine(Root().FullName, relative));

    /// <summary>台账里"跨语言互通实测"那一行 / The interop row of the ledger.</summary>
    private static string InteropRow() => Lines(LedgerPath)
        .Single(line => line.StartsWith("| 跨语言互通实测 |", StringComparison.Ordinal));

    /// <summary>用例表的数据行总数 / The total number of case rows.</summary>
    private static int CaseRowCount() => Lines(CasesPath)
        .Where(line => !line.StartsWith("[", StringComparison.Ordinal))
        .Where(line => !line.StartsWith("#", StringComparison.Ordinal))
        .Count(line => line.Length > 0);

    /// <summary>用例表的节数 / The number of case sections.</summary>
    private static int CaseSectionCount() => Lines(CasesPath)
        .Count(line => line.StartsWith("[", StringComparison.Ordinal));

    /// <summary>验收行不能声称某份文件缺失，而那份文件就在盘上 / An acceptance row may not report a file missing while it sits on disk.</summary>
    /// <remarks>
    /// **4.7 那行在 B1071—B1077 之后仍然写着两份 fixture "都缺"。**
    /// **文件已经搬进来六批了，而验收表没动**——**读表的人会以为那还是缺口，于是去重做一遍已经做完的事。**
    ///
    /// **这条判据的判法：文件在盘上 ⇒ 行里不许写"都缺"；文件不在 ⇒ 行里必须写。**
    /// **两个方向都查，因为它要防的正是"表格和磁盘各说各话"。**
    ///
    /// **Row 4.7 still said both fixtures were missing after B1071—B1077.** **The files had been in for six batches and the acceptance table had not moved** — **so a reader takes it as an open gap and redoes work already done.**
    /// **How this decides: file on disk implies the row may not say "both missing"; file absent implies the row must.** **Both directions, because what it guards against is a table and a disk telling different stories.**
    /// </remarks>
    [Fact]
    public void TheAcceptanceRow_ShouldAgreeWithTheDisk()
    {
        string row = Lines(AcceptancePath)
            .Single(line => line.StartsWith("| 4.7 |", StringComparison.Ordinal));

        bool contractExists = File.Exists(Path.Combine(Root().FullName, ContractPath));
        bool casesExists = File.Exists(Path.Combine(Root().FullName, CasesPath));

        if (contractExists && casesExists)
        {
            row.Should().NotContain("都缺",
                "both fixtures are on disk, so the row may not report them as missing");
            row.Should().Contain("56 行",
                "the row has to state what was actually asserted, not just that it was asserted");
        }
        else
        {
            row.Should().Contain("都缺",
                "a fixture that is genuinely absent has to stay reported as absent");
        }
    }

    /// <summary>**台账里的行数必须等于文件里的行数** / The ledger's count has to equal the file's.</summary>
    [Fact]
    public void TheInteropRow_ShouldStateTheRealCaseCount()
    {
        int rows = CaseRowCount();
        int sections = CaseSectionCount();

        rows.Should().BeGreaterThan(0, "the case table has to carry rows");
        InteropRow().Should().Contain($"6 节共 {rows} 行",
            $"the ledger has to state the real case count, which is {rows} rows in {sections} sections");
        sections.Should().Be(6, "a new section changes the sentence, so the ledger has to be edited with it");
    }

    /// <summary>**两份文件都必须被点名** / Both files have to be named.</summary>
    /// <remarks>
    /// **"跨语言已实测"这种话不点名就等于没证据。**
    /// 点名之后，**任何一个想核实的人不用先读一遍源码目录。**
    ///
    /// **"Interop is measured" without naming the files is a claim without evidence.** Named, **anyone who wants to check does not have to read a source tree first.**
    /// </remarks>
    [Fact]
    public void TheInteropRow_ShouldNameBothFixtures()
    {
        string row = InteropRow();

        row.Should().Contain("analysis-contract.tsv", "the vocabulary half has to be named");
        row.Should().Contain("analysis-cases.tsv", "the behaviour half has to be named");
    }

    /// <summary>**"未实测"那半必须留在行里** / The unmeasured half has to stay in the row.</summary>
    /// <remarks>
    /// **这一条方向是反的：它在防"把欠账删掉"。**
    /// 把"契约一致不等于两端跑出同一个结果"删掉，这一行就会读成"跨语言已通过"——
    /// **而那正是目标里明令不许宣称的东西。**
    ///
    /// **This one points the other way: it prevents the debt from being deleted.** Drop the sentence about contract parity not being run parity and the row reads "interop passes" — **exactly what the objective forbids claiming.**
    /// </remarks>
    [Fact]
    public void TheInteropRow_ShouldKeepTheUnmeasuredHalf()
    {
        string row = InteropRow();

        // **`Contain("未实测")` 这条曾经形同虚设**：行首就是"契约层已实测、运行层未实测"，
        // **于是删掉后面整整一句"没有双跑进程、没有摘要对齐、没有输出互换"，它照样绿。**
        // **所以这里逐个点那三样东西**——**少一样就红。**
        //
        // **`Contain("not measured")` was doing nothing**: the row opens with "contract measured, run not measured",
        // **so deleting the whole sentence listing the three missing pieces still passed.**
        // **So this names each of the three** — **drop one and it fails.**
        foreach (string gap in new[] { "双跑进程", "integritySha256", "求解输出互换" })
        {
            row.Should().Contain(gap, $"the row has to name the concrete gap \"{gap}\" it still owes");
        }
    }
}
