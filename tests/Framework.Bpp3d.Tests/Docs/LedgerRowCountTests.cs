#nullable enable

using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 台账的 19 行必须真的数得出来 / The ledger's nineteen rows have to actually count to nineteen.
/// </summary>
/// <remarks>
/// **B1056 没有新增源码。** 它把"19 / 19 有对应"从一句断言变成一次核算。
///
/// 在那之前，"19"只作为**一个字符串**被钉在能力矩阵里（`Contain("**19**")`）。
/// **字符串能证明有人打对了字，证明不了那张表真的有 19 行。**
/// **删掉一行、重名一行、拼错一个文件名，三种情况都会让字符串判据继续绿。**
///
/// **B1056 added no source code.** It turned "19 of 19 have counterparts" from an assertion into an accounting. Before that, "19" was pinned only as **a string** in the capability matrix.
/// **A string proves someone typed the right digits; it does not prove the table has nineteen rows.** **Delete a row, duplicate a name, or misspell a filename, and the string criterion stays green in all three
/// cases.**
/// </remarks>
public class LedgerRowCountTests
{
    private const string LedgerPath = "docs/kotlin-sync-ledger.md";
    private const int BaselineFileCount = 19;

    private static string Ledger()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("repository root not found");
        }
        return File.ReadAllText(Path.Combine(directory.FullName, LedgerPath));
    }

    /// <summary>
    /// §三 那一节的范围 / The span of section three.
    /// </summary>
    /// <remarks>
    /// **必须在节内解析。** 台账别处也有以 `.kt` 开头的行——"语言所限"那张表
    /// 写的是 `Http4kRemoteSolverHttpTransport.kt` → `HttpClient`，**它本来就该没有 `.cs` 对应。**
    /// **全表扫会把它算进来，于是数出 20 行并指责第 20 行没有 C# 文件。**
    ///
    /// **The parse has to stay inside the section.** The ledger has `.kt` rows elsewhere — the "language limit" table maps `Http4kRemoteSolverHttpTransport.kt` to `HttpClient`, **and that row should have no
    /// `.cs` counterpart.** **A whole-file scan counts it, reaches twenty rows, and then complains that the twentieth has no C# file.**
    /// </remarks>
    private static string SectionThree() {
        string ledger = Ledger();
        int start = ledger.IndexOf("## 三、", StringComparison.Ordinal);
        if (start < 0) {
            throw new InvalidOperationException("section three not found");
        }
        int end = ledger.IndexOf("## 四、", start, StringComparison.Ordinal);
        return end < 0 ? ledger.Substring(start) : ledger.Substring(start, end - start);
    }

    /// <summary>§三 里每个基线文件名 / Every baseline filename in section three.</summary>
    private static IReadOnlyList<string> BaselineNames()
    {
        var names = new List<string>();
        foreach (string line in SectionThree().Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("| `", StringComparison.Ordinal))
            {
                continue;
            }
            int close = trimmed.IndexOf("` |", 2, StringComparison.Ordinal);
            if (close < 0)
            {
                continue;
            }
            string name = trimmed.Substring(3, close - 3);
            if (name.EndsWith(".kt", StringComparison.Ordinal))
            {
                names.Add(name);
            }
        }
        return names;
    }

    /// <summary>**行数必须等于 19** / The row count has to be nineteen.</summary>
    /// <remarks>
    /// **删掉一行**是这张表最常见的腐坏方式：**表还是对的，只是少了一行，
    /// 而"19 / 19"那句话从此不再被任何东西检查。**
    ///
    /// **Deleting a row is how this table most often rots**: **the table stays true, just one row shorter, and the sentence "19 of 19" is no longer checked by anything.**
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldHaveNineteenRows()
    {
        IReadOnlyList<string> names = BaselineNames();

        names.Should().HaveCount(BaselineFileCount,
            "the total is what every other number in this ledger reconciles against");
    }

    /// <summary>**名字必须互不相同** / The names have to be distinct.</summary>
    /// <remarks>
    /// **同一个文件写两行会让行数凑够 19 而实际只覆盖 18 个。**
    ///
    /// **Writing one file twice reaches nineteen rows while covering eighteen files.**
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldNotRepeatABaselineName()
    {
        IReadOnlyList<string> names = BaselineNames();

        names.Distinct(StringComparer.Ordinal).Should().HaveCount(names.Count,
            "two rows for one file cover one file, not two");
    }

    /// <summary>**每一行都要给出对应的 C# 侧** / Every row has to name its counterpart.</summary>
    /// <remarks>
    /// **这一行只写"未搬"会让 19 / 19 那句话变成假的**——
    /// **而 19 / 19 正是"`core/analysis` 已同步完"这句话的唯一依据。**
    ///
    /// **A row saying "not ported" would make the "19 of 19" claim false** — **and nineteen of nineteen is the only basis for saying `core/analysis` is synced.**
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldNameACounterpartForEveryRow()
    {
        string ledger = Ledger();

        foreach (string name in BaselineNames())
        {
            string? row = ledger
                .Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("| `" + name + "`", StringComparison.Ordinal));

            row.Should().NotBeNull($"{name} has a row");
            // **这张表的约定是写类名、不带 `.cs`**（例如 `ConflictEvidenceSupport`）。
            // **所以查的是"第二格里有反引号包着的名字"，不是"有 .cs 后缀"。**
            // **The table's convention is a bare class name without `.cs`**, **so the check is "a backticked name in the second cell", not "a .cs suffix".**
            string[] cells = row!.Split('|');
            cells.Should().HaveCountGreaterThanOrEqualTo(2, $"{name} has to be a table row");
            cells[2].Should().Contain("`",
                $"{name} has to name the C# carrier in its second cell, or the row says nothing");
        }
    }
}
