#nullable enable

using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 阶段核算台账必须可回溯 / The phase ledger has to be traceable.
/// </summary>
/// <remarks>
/// **台账的价值全在"依据"上。** 一条结论如果不指向计划里能搜到的条目，
/// 它就和状态文档里那句"已关闭"没有区别——**可以被抄下去，不能被核对。**
///
/// **The ledger's whole value is its basis.** A conclusion that points at nothing searchable in the plan is no better than the status document's "closed": **it can be copied forward,
/// it cannot be checked.**
/// </remarks>
public class PhaseLedgerTests {
    private static string DocsPath(string name) {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "docs", name);
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException($"docs/{name} not found");
    }

    private static string PlanPath() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "logs", "kotlin-sync-plan.md");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("logs/kotlin-sync-plan.md not found");
    }

}
