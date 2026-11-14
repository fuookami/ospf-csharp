#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 收口核算：每一个仍缺的项都必须点名原因 / Close-out: every remaining gap must name its cause.
/// </summary>
/// <remarks>
/// **"还缺"不是一条信息。** 一条不带原因的 ❌ 让读者无法判断它什么时候能补、由谁补、是不是根本不该补。
///
/// **"Still missing" is not information.** A ❌ without a cause leaves the reader unable to tell when it can be filled, by whom, or whether it should be filled at all.
/// </remarks>
public class SyncCloseOutTests {
    private static string Matrix() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "docs", "kotlin-capability-matrix.md");
            if (File.Exists(candidate)) {
                return File.ReadAllText(candidate);
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("docs/kotlin-capability-matrix.md not found");
    }

    /// <summary>**每一条 ❌ 都要点名原因** / Every ❌ names its cause.</summary>
    /// <remarks>
    /// 允许的原因形状有限：**具体失败字符串**（`licence` / `Access denied` / `password authentication failed` / `err_` …）、
    /// **"本机没有"**、或**"按设计不提供"**。
    ///
    /// 一条光秃秃的 ❌ 会让人以为"忘了做"，**而真相可能是"这个环境做不到"或"这是刻意的分叉"。**
    ///
    /// **The allowed shapes are finite**: a concrete failure string, "not on this machine", or "not provided by design". A bare ❌ reads as "someone forgot", **while the truth may be
    /// "this environment cannot" or "this is a deliberate fork".**
    /// </remarks>
    [Fact]
    public void TheRemainingGaps_ShouldEachNameWhy() {
        string matrix = Matrix();

        string[] rows = matrix
            .Split('\n')
            .Where(line => line.Contains('❌'))
            // **图例行本身不是缺口。**
            // **The legend row is not a gap.**
            .Where(line => !line.StartsWith("图例", StringComparison.Ordinal))
            .ToArray();

        rows.Should().NotBeEmpty("the ledger records what is still missing");

        string[] causeMarkers = [
            "licence", "license", "Access denied", "password authentication failed",
            "err_", "未设", "本机", "按设计", "未试", "分叉", "连文件都没有", "SDK 不在", "未评估", "未排期",
        ];

        string[] unexplained = rows
            .Where(line => !causeMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        unexplained.Should().BeEmpty(
            "a ❌ without a cause cannot be told apart from 'someone forgot':\n" + string.Join("\n", unexplained));
    }

    /// <summary>**缺口台账必须点名"需真实后端"这一类的存在** / The ledger names the backend-bound remainder.</summary>
    /// <remarks>
    /// 这是 P4 收口的核心事实：**剩下的不是功能，是三个必须有真实后端才能验证的 `analyze` 类本体。**
    /// 它必须写在矩阵里，否则下一个人会以为还有一大堆功能没写。
    ///
    /// **This is the central fact of the P4 close-out**: what remains is not features but three `analyze` class bodies that need a real backend to verify. It has to be in the matrix, or the
    /// next reader will think a pile of functionality is still unwritten.
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldNameTheBackendBoundRemainder() {
        Matrix().Should().Contain("只剩 `analyze` 类本体（需真实后端）",
            "the backend-bound remainder is the one thing a reader must not have to guess");
    }
}
