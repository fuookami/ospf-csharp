// 框架模块 README 与 csproj 的引用关系交叉核对 / Cross-check framework module README claims against csproj references.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 框架模块 README 里关于"谁引用谁"的说法必须与 csproj 的实际引用一致。
/// Claims about "who references whom" in the framework module READMEs must match the actual csproj references.
/// </summary>
/// <remarks>
/// 抓到一处**自己写错的事实**： 起，九份文档都写着 `GanttScheduling` 与 `NetworkScheduling`
/// "本来就互相引用"，而实际是**单向**的——`NetworkScheduling → GanttScheduling`。
/// 那个 README 里的代码块本身就画着单向箭头，**正文与自己的图表自相矛盾**。
///
/// 这类错误不会让任何代码或测试失败，只能靠把"文档说的引用关系"与"csproj 的真实引用关系"逐条比对。
///
/// caught a **fact we had written wrong ourselves**: since then, nine documents said `GanttScheduling` and `NetworkScheduling` "already reference
/// each other", when it is actually **one-way** — `NetworkScheduling → GanttScheduling`. The README's own code block drew a one-way arrow, so **the prose
/// contradicted its own diagram**. No code or test can catch this; only comparing "what the docs claim" against "what the csprojs actually reference".
/// </remarks>
public sealed class FrameworkModuleReferenceTests {
    private static string SolutionRoot()
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; ++depth) {
            if (File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new DirectoryNotFoundException("ospf-csharp.sln not found from the test output directory");
    }

    /// <summary>读出一个项目直接引用的其他项目名。</summary>
    private static IReadOnlyList<string> References(string module) {
        string csproj = Path.Combine(SolutionRoot(), "src", module, $"{module}.csproj");
        if (!File.Exists(csproj)) {
            return Array.Empty<string>();
        }
        return XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(element => ((string?)element.Attribute("Include") ?? string.Empty).Replace('\\', '/'))
            .Select(reference => reference.Split('/').Reverse().Skip(1).FirstOrDefault() ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();
    }

    private static string ReadDoc(string module, string fileName) =>
        DocsFile.ReadAllText(Path.Combine(SolutionRoot(), "src", module, fileName))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>**不得再出现"互相引用"的错误说法** / The incorrect "reference each other" claim must not reappear.</summary>
    [Fact]
    public void NoDocument_ShouldClaimGanttAndNetworkReferenceEachOther() {
        var offenders = new List<string>();
        foreach (string module in new[] {
            "Framework.GanttScheduling",
            "Framework.NetworkScheduling",
            "Framework.GanttNetworkScheduling",
        }) {
            foreach (string fileName in new[] { "README.md", "README_ch.md" }) {
                string text = ReadDoc(module, fileName);
                if (text.Contains("already reference each other", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("本来就互相引用", StringComparison.Ordinal)) {
                    offenders.Add($"{module}/{fileName}");
                }
            }
        }
        offenders.Should().BeEmpty();
    }

    /// <summary>**README 的引用方向必须与 csproj 一致** / The README's reference direction must match the csproj.</summary>
    [Fact]
    public void TheBridgeRationale_ShouldMatchTheActualReferenceDirection() {
        // 事实：NetworkScheduling → GanttScheduling（单向）。
        References("Framework.NetworkScheduling")
            .Should().Contain("Framework.GanttScheduling", "the premise of the bridge project's existence");
        References("Framework.GanttScheduling")
            .Should().NotContain("Framework.NetworkScheduling", "the reference is one-way, not mutual");

        string bridge = ReadDoc("Framework.GanttNetworkScheduling", "README.md");
        bridge.Should().Contain("already references");
        bridge.Should().Contain("Adding the bridge to `Framework.GanttScheduling` would close a reference cycle");
        bridge.Should().NotContain("either side");
    }

    [Fact]
    public void TheBridgeRationale_ShouldMatchTheActualReferenceDirectionInChinese() {
        string bridge = ReadDoc("Framework.GanttNetworkScheduling", "README_ch.md");
        bridge.Should().Contain("本来就引用");
        bridge.Should().Contain("把桥接塞进 `Framework.GanttScheduling` 会形成循环引用");
        bridge.Should().NotContain("任意一侧");
    }

    /// <summary>**桥接项目必须引用它适配的两侧** / The bridge must reference both sides it adapts.</summary>
    [Fact]
    public void TheBridgeProject_ShouldReferenceBothSides() {
        IReadOnlyList<string> references = References("Framework.GanttNetworkScheduling");
        references.Should().Contain("Framework.GanttScheduling");
        references.Should().Contain("Framework.NetworkScheduling");
    }

    /// <summary>**不存在循环引用** / There must be no reference cycle.</summary>
    /// <remarks>
    /// 这是桥接项目存在的**全部理由**：如果 `GanttScheduling` 真的引用了 `NetworkScheduling`，
    /// 桥接就没必要独立成项目。所以"没有环"必须被钉住，否则将来有人给 `GanttScheduling` 加一条引用，
    /// 文档里那句理由就变成假的，而构建依然会成功。
    ///
    /// This is the **entire reason** the bridge project exists: if `GanttScheduling` really referenced `NetworkScheduling`, the bridge would not need to
    /// be separate. So "no cycle" must be pinned; otherwise someone adds one reference to `GanttScheduling` later, the documented rationale silently
    /// becomes false, and the build still succeeds.
    /// </remarks>
    [Fact]
    public void TheSchedulingModules_ShouldNotFormAReferenceCycle() {
        References("Framework.GanttScheduling").Should().NotContain("Framework.NetworkScheduling");
        References("Framework.NetworkScheduling").Should().Contain("Framework.GanttScheduling");
    }
}
