#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 交接区的"从这里开始"必须是活的 / The hand-off "start here" has to be alive.
/// </summary>
/// <remarks>
/// **一个过期的"从这里开始"比没有更糟**：下一个人会先去做一件已经做完的事，
/// **然后在 diff 里发现它做完了，再回头重新判断从哪里开始。**
///
/// **A stale "start here" is worse than none**: the next person does finished work first, **then discovers it in the diff and has to re-decide where to start.**
/// </remarks>
public class HandoffFreshnessTests {
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
