#nullable enable

using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// P4—P9 的阶段区间与收口状态 / Phase ranges and close-out status for P4—P9.
/// </summary>
/// <remarks>
/// **阶段编号是接力的坐标。** 一条"P4 未完成"如果没有区间，读者无法判断它指的是哪一段，
/// **而阶段边界一旦靠记忆维护，下一次交接就会把"已收口"的阶段重开一遍。**
///
/// **Phase numbers are the coordinates of the relay.** A "P4 is not done" without a range cannot be acted on, and **once phase boundaries are maintained from memory, the next hand-off
/// reopens a phase that was already closed.**
/// </remarks>
public class PhaseRangeTests {
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

    private static IReadOnlyList<int> BatchNumbers(string plan, int phase) =>
        plan.Split('\n')
            .Where(line => line.StartsWith($"### P{phase} Batch ", StringComparison.Ordinal))
            .Select(line => {
                string tail = line.Substring($"### P{phase} Batch ".Length).Trim();
                int end = 0;
                while (end < tail.Length && char.IsAsciiDigit(tail[end])) {
                    end++;
                }
                return end > 0 ? int.Parse(tail[..end]) : -1;
            })
            .Where(number => number > 0)
            .ToArray();

}
