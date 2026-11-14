// 迁移指南的存在性与内容测试 / Existence and content tests for the migration guide.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 迁移指南必须存在且不丢掉关键条目 / The migration guide must exist and keep its key entries.
/// </summary>
/// <remarks>
/// **文档悄悄少掉一段不会让任何代码失败**。这里只钉住"最容易在后续批次里被顺手删掉"的几条：
/// 已实测的跨语言范围、前置校验与模型约束的区别、空壳的处置原则。
///
/// **A silently deleted documentation section breaks no code.** This only pins the entries most likely to be dropped by a later batch: the measured
/// cross-language scope, the difference between a pre-check and a model constraint, and the shell-disposal rule.
/// </remarks>
public sealed class MigrationGuideTests {
    private static string GuidePath()
    {
        // 从测试程序集向上找仓库根的 docs/ 目录。
        // Walk up from the test assembly to the repository root's docs/ directory.
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; ++depth) {
            string candidate = Path.Combine(directory, "docs", "kotlin-migration-guide.md");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new FileNotFoundException("docs/kotlin-migration-guide.md not found from the test output directory");
    }

    private static string ReadGuide() =>
        DocsFile.ReadAllText(GuidePath()).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void MigrationGuide_ShouldExist() {
        File.Exists(GuidePath()).Should().BeTrue();
        ReadGuide().Trim().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>**必须写明实测范围只有三处** / It must state that only three things were measured across languages.</summary>
    [Fact]
    public void MigrationGuide_ShouldStateTheMeasuredScope() {
        string guide = ReadGuide();

        guide.Should().Contain("SerializedSolution");
        guide.Should().Contain("RemoteTaskSubmitRequest");
        guide.Should().Contain("sha256");
        guide.Should().Contain("不证明性能");
    }

    /// <summary>**必须区分前置校验与模型约束** / It must distinguish a pre-check from a model constraint.</summary>
    [Fact]
    public void MigrationGuide_ShouldDistinguishPreCheckFromModelConstraint() {
        string guide = ReadGuide();

        guide.Should().Contain("前置校验");
        guide.Should().Contain("在优化结果上不同");
    }

    /// <summary>**必须写明空壳的处置原则** / It must state the shell-disposal rule.</summary>
    [Fact]
    public void MigrationGuide_ShouldCarryTheShellRule() {
        string guide = ReadGuide();

        guide.Should().Contain("空壳");
        guide.Should().Contain("删掉空壳并记录缺口");
    }

    /// <summary>**必须写明不切换 Kotlin checkout** / It must state that the Kotlin checkout is never switched.</summary>
    [Fact]
    public void MigrationGuide_ShouldForbidSwitchingTheKotlinCheckout() =>
        ReadGuide().Should().Contain("不切换 Kotlin checkout");
}
