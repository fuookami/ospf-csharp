#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 扫描脚本必须一直在 / The scanner script has to keep existing.
/// </summary>
/// <remarks>
/// **缺口全部来自这个扫描**，而各轮的扫描是**每批临时写一次的 Python**。
/// **临时脚本的结局是：下一会话不知道它存在，于是要么重写，要么相信"已经全搬完了"。**
///
/// **而"相信已经全搬完了"是这里最贵的错**：它会让人跳过整个核对环节。
///
/// **All of the gaps came from this scan**, and each round'ss scan was **throwaway Python**. **The fate of a throwaway script is that the next session does not know it exists, and either rewrites it or believes
/// everything is already implemented.** **Believing that is the expensive error here**: it makes people skip the whole check.
/// </remarks>
public class BaselineScannerTests
{
    private static DirectoryInfo Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("repository root not found");
        }
        return directory;
    }

    private static string Scanner() =>
        File.ReadAllText(Path.Combine(Root().FullName, "scripts", "scan-kotlin-baseline.py"));

    /// <summary>四个维度都得扫 / All four dimensions have to be scanned.</summary>
    /// <remarks>
    /// **少一个维度不会报错，只会让那一类名字永远不被检查**——
    /// **而"没被检查"与"检查过且通过"看起来完全一样。**
    ///
    /// **Dropping a dimension raises nothing; it just means that kind of name is never checked** — **and "not checked" looks exactly like "checked and passed".**
    /// </remarks>
    [Fact]
    public void TheScanner_ShouldCoverEveryDimension()
    {
        string scanner = Scanner();

        // **查的是 `found` 初始化块本身，不是"整个文件里出现过这个词"。**
        // 第一次写的时候查全文，于是删掉 `"properties": []` 之后判据仍是绿的——
        // **因为 `"properties"` 在函数体和文档表里还各出现一次。**
        // **The check is on the `found` initialiser itself, not on "the word appears somewhere in the file".**
        // The first version scanned the whole file, so deleting `"properties": []` left it green —
        // **because the word still appears once in the function body and once in the doc table.**
        int init = scanner.IndexOf("found: dict[", StringComparison.Ordinal);
        init.Should().BeGreaterThan(0, "the scanner has to have one place that names its dimensions");

        // **取一小段就够**：四个键都在初始化块的前三百个字符里。
        // **A short slice is enough**: all four keys sit in the first three hundred characters of the initialiser.
        string declaration = scanner.Substring(init, 300);

        foreach (string dimension in new[] { "functions", "classes", "enums", "properties" })
        {
            declaration.Should().Contain($"\"{dimension}\"",
                $"{dimension} has to stay a scanned dimension");
        }
    }

    /// <summary>**每个"写不了"都要点出对应的绊线判据** / Every blocker has to name its tripwire.</summary>
    /// <remarks>
    /// **一个没有绊线的"写不了"会永远写不了**：卡点解除之后，
    /// **没有人会重跑一遍扫描来发现它其实已经能搬了。**
    ///
    /// **A blocker without a tripwire stays a blocker forever**: once the cause is gone, **nobody reruns the scan to discover it became portable.**
    /// </remarks>
    [Fact]
    public void TheBlockers_ShouldNameTheirTripwire()
    {
        string scanner = Scanner();
        Match block = Regex.Match(scanner, @"KNOWN_BLOCKERS = \{(?<body>.*?)\n\}", RegexOptions.Singleline);

        block.Success.Should().BeTrue("the scanner has to distinguish un-portable from un-ported");
        block.Groups["body"].Value.Should().Contain("FixedIntegerLpBlockerTests",
            "each blocked name has to point at the criterion that fires when the blocker lifts");
    }

    /// <summary>扫描器自己必须能被跑起来 / The scanner itself has to be runnable.</summary>
    /// <remarks>
    /// **一个只存在于仓库里、没人跑过的脚本，和不存在没有区别。**
    /// 这条只查文件在、而且不是空壳——**真的跑一遍由每一批的门禁负责。**
    ///
    /// **A script that exists but is never run is indistinguishable from no script.** This only checks that it is present and not a stub — **actually running it is each batch's job.**
    /// </remarks>
    [Fact]
    public void TheScanner_ShouldNotBeAStub()
    {
        string scanner = Scanner();

        scanner.Should().Contain("argparse", "it takes the Kotlin repo and commit as arguments, not hard-coded paths");
        scanner.Should().Contain("sys.exit(main())", "it has to be an executable entry point");
        scanner.Length.Should().BeGreaterThan(2000, "a two-line script cannot be doing four dimensions");
    }
}
