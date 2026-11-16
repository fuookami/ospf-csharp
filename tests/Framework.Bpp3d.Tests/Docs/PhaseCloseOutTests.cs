#nullable enable

using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 阶段收口必须有证据 / A phase close-out must carry evidence.
/// </summary>
/// <remarks>
/// **"已关闭"是一句可以被抄下去的话。** 它没有说谁关的、依据是什么、最后一条是什么——
/// 于是下一任维护者只能选择相信或不相信。
///
/// **"Closed" is a sentence that can be copied forward.** It does not say who closed it, on what basis, or what the last entry was, leaving the next maintainer to either believe it or not.
/// </remarks>
public class PhaseCloseOutTests {
    private static string StatusPath() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "docs", "kotlin-sync-status.md");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("docs/kotlin-sync-status.md not found");
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
