#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Quality;

/// <summary>
/// B860：双语文档的**两种约定**，每个 README 必须至少满足一种 / Two conventions for bilingual docs, and every README must satisfy one.
/// </summary>
/// <remarks>
/// 仓里实际存在两种约定：
///
/// | 约定 | 例子 | 判据怎么认 |
/// | --- | --- | --- |
/// | 同目录两个文件 | `src/Framework.Bpp3d/README.md` + `README_ch.md` | 兄弟文件存在 |
/// | 单文件双节 | `src/Starters/Core/README.md` 里 `## [zh]` 与 `## [en]` | 两个节标题都在 |
///
/// **两种都行，一种都不满足就不行。** B860 之前没有任何判据管这件事：新加一个只写中文的 README 不会红，
/// 而"双语文档"这句计划里的验收项就此变成一句没人核对的话。
///
/// The repository actually uses two conventions (table above). **Either is fine; neither is not.** Before B860 nothing watched this: adding a Chinese-only README red
/// nothing, and the plan's "bilingual documentation" acceptance line became a sentence nobody checks.
/// </remarks>
public sealed class ReadmeLanguagePairingTests
{
    /// <summary>**每个 README 都必须双语** / Every README must be bilingual.</summary>
    [Fact]
    public void EveryReadme_ShouldSatisfyOneOfTheTwoConventions() {
        var violations = new List<string>();

        foreach (string path in Readmes()) {
            string directory = Path.GetDirectoryName(path)!;
            string text = File.ReadAllText(path);

            bool hasSibling = File.Exists(Path.Combine(directory, "README_ch.md"));
            bool hasInlineSections = HasHeading(text, "## [zh]") && HasHeading(text, "## [en]");

            if (!hasSibling && !hasInlineSections) {
                violations.Add(path);
            }
        }

        violations.Should().BeEmpty(
            "a README in one language only makes the bilingual claim unverifiable; use README_ch.md or both ## [zh] / ## [en] sections");
    }

    /// <summary>**兄弟文件不能是空壳** / The sibling file must not be an empty shell.</summary>
    /// <remarks>
    /// 空文件能骗过"文件存在"，却什么都没说——本仓记录过好几次这种空壳（B817）。
    ///
    /// An empty file passes "the file exists" while saying nothing — an empty shell this repository has recorded before (B817).
    /// </remarks>
    [Fact]
    public void TheChineseSibling_ShouldNotBeAnEmptyShell() {
        var empties = new List<string>();

        foreach (string path in Readmes()) {
            string sibling = Path.Combine(Path.GetDirectoryName(path)!, "README_ch.md");
            if (!File.Exists(sibling)) {
                continue;
            }
            string text = File.ReadAllText(sibling);
            if (text.Trim().Length < 20 || !text.TrimStart().StartsWith("#", StringComparison.Ordinal)) {
                empties.Add(sibling);
            }
        }

        empties.Should().BeEmpty("a nearly empty README_ch.md satisfies the file check while documenting nothing");
    }

    /// <summary>是否有一行恰好是这个标题 / Whether a line is exactly this heading.</summary>
    /// <param name="text">内容 / The text.</param>
    /// <param name="heading">标题 / The heading.</param>
    /// <returns>是否存在 / Whether it exists.</returns>
    private static bool HasHeading(string text, string heading) {
        foreach (string line in text.Split('\n')) {
            if (string.Equals(line.Trim(), heading, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>找出仓根与 `src/` 下的所有 README / Find every README at the repository root and under `src/`.</summary>
    /// <returns>路径列表 / The paths.</returns>
    private static List<string> Readmes() {
        var found = new List<string>();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null) {
            if (Directory.Exists(Path.Combine(root.FullName, "src"))) {
                break;
            }
            root = root.Parent;
        }
        if (root is null) {
            throw new DirectoryNotFoundException("could not locate the repository root");
        }

        string rootReadme = Path.Combine(root.FullName, "README.md");
        if (File.Exists(rootReadme)) {
            found.Add(rootReadme);
        }
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "README.md", SearchOption.AllDirectories)) {
            if (path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) {
                continue;
            }
            found.Add(path);
        }
        return found;
    }
}
