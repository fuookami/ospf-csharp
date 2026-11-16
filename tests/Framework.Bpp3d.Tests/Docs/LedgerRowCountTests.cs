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
/// **纯文档，无新增源码。** 它把"19 / 19 有对应"从一句断言变成一次核算。
///
/// 在那之前，"19"只作为**一个字符串**被钉在能力矩阵里（`Contain("**19**")`）。
/// **字符串能证明有人打对了字，证明不了那张表真的有 19 行。**
/// **删掉一行、重名一行、拼错一个文件名，三种情况都会让字符串判据继续绿。**
///
/// **No source change.** It turned "19 of 19 have counterparts" from an assertion into an accounting. Before that, "19" was pinned only as **a string** in the capability matrix.
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
    /// §三那一节的范围 / The span of section three.
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

    /// <summary>§三里每个基线文件名 / Every baseline filename in section three.</summary>
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

}
