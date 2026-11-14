// Starter README 与 csproj 的模块清单交叉核对 / Cross-check starter README module lists against csproj references.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 每个 starter 的 README **模块清单**必须与 csproj 的 **实际引用**一一对应。
/// Each starter's README **module list** must correspond one-to-one with the csproj's **actual references**.
/// </summary>
/// <remarks>
/// B287 只验证了"引用了该引用的模块"，没有验证"README 说了该说的模块"。这两件事会各自漂移：
/// 加一个 `ProjectReference` 而忘了写 README，读者就看不到它；写了 README 而没有引用，读者就以为能拿到。
/// B288 的交叉核对抓到了一处真实不一致——`GanttScheduling` 引用了 `Framework.NetworkScheduling`
/// 而 README 一字未提。
///
/// B287 verified "it references what it should"; it did not verify "the README says what it should". Those two drift independently: add a
/// `ProjectReference` without the README and readers miss it; write the README without the reference and readers assume they get it. The B288 cross-check
/// caught a real mismatch — `GanttScheduling` referenced `Framework.NetworkScheduling` while the README never mentioned it.
/// </remarks>
public sealed class StarterReadmeConsistencyTests {
    private static readonly string[] Starters = {
        "Bpp3d",
        "Csp1d",
        "GanttScheduling",
        "GanttNetworkScheduling",
        "Framework",
        "Core",
    };

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

    /// <summary>从 csproj 读出被引用的项目名（`../../Framework.Bpp3d/Framework.Bpp3d.csproj` → `Framework.Bpp3d`）。</summary>
    private static IReadOnlyList<string> ReferencedProjects(string starter) =>
        XDocument.Load(Path.Combine(SolutionRoot(), "src", "Starters", starter, $"Starters.{starter}.csproj"))
            .Descendants("ProjectReference")
            .Select(element => ((string?)element.Attribute("Include") ?? string.Empty).Replace('\\', '/'))
            .Select(reference => reference.Split('/').Reverse().Skip(1).FirstOrDefault() ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();

    /// <summary>从 README 读出模块清单（只取 `- **X** --` 形式的条目）。</summary>
    private static IReadOnlyList<string> ReadmeModules(string starter) {
        string text = DocsFile.ReadAllText(
            Path.Combine(SolutionRoot(), "src", "Starters", starter, "README.md")
        ).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Regex.Matches(text, @"^- \*\*([A-Za-z][A-Za-z0-9.]*)\*\* --", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>**README 必须提到每个被引用的模块** / The README must mention every referenced module.</summary>
    [Fact]
    public void EveryReadme_ShouldMentionEveryReferencedModule() {
        var missing = new List<string>();
        foreach (string starter in Starters) {
            IReadOnlyList<string> referenced = ReferencedProjects(starter);
            IReadOnlyList<string> documented = ReadmeModules(starter);
            foreach (string module in referenced) {
                if (!documented.Contains(module, StringComparer.Ordinal)) {
                    missing.Add($"{starter}/README.md is missing {module}");
                }
            }
        }
        missing.Should().BeEmpty();
    }

    /// <summary>**README 不得提到没被引用的模块** / The README must not mention an unreferenced module.</summary>
    /// <remarks>
    /// 这一条防的是反方向：README 承诺了一个包其实拿不到的模块。它比上一条更危险，
    /// 因为**用户照着 README 写 `using` 才发现类型不存在**。
    ///
    /// This guards the other direction: the README promising a module the package does not deliver. It is the more dangerous of the two, because
    /// **the user writes a `using` straight from the README and only then finds the type missing**.
    /// </remarks>
    [Fact]
    public void EveryReadme_ShouldNotMentionUnreferencedModules() {
        var overclaimed = new List<string>();
        foreach (string starter in Starters) {
            IReadOnlyList<string> referenced = ReferencedProjects(starter);
            IReadOnlyList<string> documented = ReadmeModules(starter);
            foreach (string module in documented) {
                if (!referenced.Contains(module, StringComparer.Ordinal)) {
                    overclaimed.Add($"{starter}/README.md promises {module} without referencing it");
                }
            }
        }
        overclaimed.Should().BeEmpty();
    }

    /// <summary>**README 必须至少列出一个模块** / The README must list at least one module.</summary>
    /// <remarks>
    /// 这里刻意不检查"是否写了自己"：starter 名与模块名不是同一种命名
    /// （`Bpp3d` 包的核心模块叫 `Framework.Bpp3d`，`Core` 包的核心模块就叫 `Core`），
    /// 强行映射会逼出一个没有意义的命名规则。**"列出模块"这件事本身才是要钉住的。**
    ///
    /// This deliberately does not check "it names itself": starter names and module names are not the same convention (`Bpp3d`'s core module is
    /// `Framework.Bpp3d` while `Core`'s is just `Core`), and forcing a mapping invents a meaningless naming rule. **That it lists its modules at all is
    /// what needs pinning.**
    /// </remarks>
    [Fact]
    public void EveryReadme_ShouldListAtLeastOneModule() {
        foreach (string starter in Starters) {
            IReadOnlyList<string> documented = ReadmeModules(starter);
            documented.Should().NotBeEmpty($"{starter}/README.md must list its modules");
        }
    }
}
