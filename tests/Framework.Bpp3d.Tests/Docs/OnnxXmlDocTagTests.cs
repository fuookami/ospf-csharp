#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **XML 注释里不许出现裸 HTML 标签** / No raw HTML tags inside XML doc comments.
/// </summary>
/// <remarks>
/// **这条判据存在，是因为同一个错我犯了六次：** **`<b>` 写进 `///` 会让 `CS1570` 响，而每次都是先写、再被警告抓到、再改。**
/// **六次说明"记住它"不管用，那就让它在写坏的当场失败。** **这条判据只覆盖 ONNX 插件目录：** **其余工程的同类问题由各自的警告负责，别把责任铺得太开。**
///
/// **This criterion exists because I made the same mistake six times: raw HTML inside `///` breaks the XML doc — sometimes as CS1570, sometimes silently — and each time it was written
/// and then fixed after the fact.**
/// **Six times means "remember it" does not work, so it has to fail where it is written.** The criterion covers the ONNX plugin directory only: the other projects answer to their
/// own warnings, and spreading the responsibility wider would not help.
/// </remarks>
public class OnnxXmlDocTagTests
{
    /// <summary>**插件目录里没有裸 HTML 标签** / No raw HTML tags in the plugin directory.</summary>
    [Fact]
    public void ThePluginSources_ShouldCarryNoRawHtmlInXmlDocs() {
        string directory = Path.Combine(RepositoryRoot(), "src", "Core.Plugin.Onnx");
        Directory.Exists(directory).Should().BeTrue("the plugin directory has to exist for this to mean anything");

        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)) {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) {
                continue;
            }
            int line = 0;
            foreach (string text in File.ReadLines(file)) {
                ++line;
                if (!text.TrimStart().StartsWith("///", StringComparison.Ordinal)) {
                    continue;
                }
                // 只查 HTML 格式化标签：`<summary>`／`<param>` 这些是合法的 XML 注释标签，不是这个判据的对象。
                // Only HTML formatting tags: summary, param and the rest are legitimate XML doc tags and not what this guards.
                if (Regex.IsMatch(text, @"</?(b|i|em|strong|br|span|p|code|pre|ul|li)[ />]", RegexOptions.IgnoreCase)) {
                    offenders.Add(Path.GetFileName(file) + ":" + line + " " + text.Trim());
                }
            }
        }

        offenders.Should().BeEmpty("a raw HTML tag in a doc comment produces invalid XML documentation, and this is the sixth time it was written by hand");
    }

    private static string RepositoryRoot() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
            directory = Path.GetDirectoryName(directory);
        }
        return directory ?? throw new InvalidOperationException("solution root not found");
    }
}
