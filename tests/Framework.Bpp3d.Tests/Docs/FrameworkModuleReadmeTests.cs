// 框架模块 README 的存在性与内容测试 / Existence and content tests for the framework module READMEs.

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 各框架模块的双语 README / The bilingual READMEs of each framework module.
/// </summary>
/// <remarks>
/// **文档悄悄消失或悄悄改口不会让任何代码失败**。这里钉住两件事：每份文档都在，
/// 并且它们各自的"如实说明"没有被改成"已完成"。
///
/// **A document silently disappearing or silently changing its story breaks no code.** These pin two things: every file is present, and each one's honest
/// note has not been rewritten into "done".
/// </remarks>
public sealed class FrameworkModuleReadmeTests {
    private static readonly string[] Modules = {
        "Framework.Bpp2d",
        "Framework.Csp1d",
        "Framework.NetworkScheduling",
        "Framework.GanttScheduling",
        "Framework.GanttNetworkScheduling",
    };

    private static string ReadmePath(string module, string fileName)
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; ++depth) {
            string candidate = Path.Combine(directory, "src", module, fileName);
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new FileNotFoundException($"src/{module}/{fileName} not found from the test output directory");
    }

    private static string ReadReadme(string module, string fileName) =>
        DocsFile.ReadAllText(ReadmePath(module, fileName)).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void EveryModule_ShouldShipBothLanguages() {
        var missing = new List<string>();
        foreach (string module in Modules) {
            foreach (string fileName in new[] { "README.md", "README_ch.md" }) {
                if (!File.Exists(ReadmePath(module, fileName))) {
                    missing.Add($"{module}/{fileName}");
                }
            }
        }
        missing.Should().BeEmpty();
    }

    [Fact]
    public void EveryModule_ShouldLinkTheTwoLanguages() {
        foreach (string module in Modules) {
            ReadReadme(module, "README.md").Should().Contain("(README_ch.md)", $"{module} links to its Chinese README");
            ReadReadme(module, "README_ch.md").Should().Contain("(README.md)", $"{module} links to its English README");
        }
    }

    /// <summary>**每个模块都不得宣称跨语言等价** / No module may claim cross-language equivalence.</summary>
    [Fact]
    public void EveryModule_ShouldNotClaimUnmeasuredParity() {
        foreach (string module in Modules) {
            ReadReadme(module, "README.md").Should().Contain("No claim of cross-language equivalence", module);
            ReadReadme(module, "README_ch.md").Should().Contain("不主张跨语言等价", module);
        }
    }

    /// <summary>**CSP1D 必须写明 DFS 路径缺失且缓存无使用者** / CSP1D must state the missing DFS path and the unused caches.</summary>
    [Fact]
    public void Csp1dReadme_ShouldStateTheShellGap() {
        ReadReadme("Framework.Csp1d", "README.md").Should().Contain("deleted in Batch 281");
        ReadReadme("Framework.Csp1d", "README.md").Should().Contain("always zero");
        ReadReadme("Framework.Csp1d", "README_ch.md").Should().Contain("Batch 281 已删除");
        ReadReadme("Framework.Csp1d", "README_ch.md").Should().Contain("缓存命中 KPI 恒为 0");
    }

    /// <summary>**NetworkScheduling 必须区分"完成"与"穷尽"** / NetworkScheduling must separate finishing from proving.</summary>
    [Fact]
    public void NetworkReadme_ShouldSeparateCompletionFromExhaustion() {
        ReadReadme("Framework.NetworkScheduling", "README.md")
            .Should().Contain("A search that finished is not a search that proved");
        ReadReadme("Framework.NetworkScheduling", "README_ch.md")
            .Should().Contain("搜索跑完了不等于证明了没有更好的");
    }

    /// <summary>**桥接必须写明 `null` 不是 `false`** / The bridge must state that null is not false.</summary>
    [Fact]
    public void BridgeReadme_ShouldStateNullIsNotFalse() {
        ReadReadme("Framework.GanttNetworkScheduling", "README.md")
            .Should().Contain("**not `false`**");
        ReadReadme("Framework.GanttNetworkScheduling", "README_ch.md")
            .Should().Contain("**不是 `false`**");
    }

    /// <summary>**桥接必须写明 Demo4 没用它** / The bridge must state that Demo4 does not use it.</summary>
    [Fact]
    public void BridgeReadme_ShouldStateDemo4DoesNotUseIt() {
        ReadReadme("Framework.GanttNetworkScheduling", "README.md")
            .Should().Contain("does **not** use this bridge");
        ReadReadme("Framework.GanttNetworkScheduling", "README_ch.md")
            .Should().Contain("**没有**用这个桥接");
    }

    /// <summary>**GanttScheduling 的缺口必须是"未确认"而非"现状"** / GanttScheduling's gaps must be marked unconfirmed.</summary>
    [Fact]
    public void GanttReadme_ShouldMarkItsGapsAsUnconfirmed() {
        ReadReadme("Framework.GanttScheduling", "README.md")
            .Should().Contain("**unconfirmed** rather than current");
        ReadReadme("Framework.GanttScheduling", "README_ch.md")
            .Should().Contain("应视为**未确认**而非现状");
    }
}
