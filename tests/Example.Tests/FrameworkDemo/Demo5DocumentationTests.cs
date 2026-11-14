// Demo5 双语文档的存在性测试 / Existence tests for the Demo5 bilingual docs.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Demo5 的双语文档 / Demo5's bilingual documentation.
/// </summary>
/// <remarks>
/// 这两份文档随程序集复制（见 `Example.csproj` 的 `None … CopyToOutputDirectory`）。
/// **文档悄悄消失不会让任何代码失败**——它是这个仓库里最容易"安静地少掉"的东西，所以用一条测试钉住它。
///
/// Both files are copied next to the assembly (see `Example.csproj`'s `None … CopyToOutputDirectory`).
/// **Documentation disappearing breaks no code** — it is the thing most easily lost silently in this repository, so a test pins it.
/// </remarks>
public sealed class Demo5DocumentationTests {
    private static string DocPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Demo5Docs", fileName);

    /// <summary>读入文档并按 LF 归一化行尾 / Read a document and normalise its line endings to LF.</summary>
    private static string ReadDocument(string fileName) =>
        File.ReadAllText(DocPath(fileName)).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Theory]
    [InlineData("README.md")]
    [InlineData("README_ch.md")]
    public void Demo5Docs_ShouldShipBothLanguages(string fileName) {
        string path = DocPath(fileName);

        File.Exists(path).Should().BeTrue($"{fileName} must be a shipped document");
        File.ReadAllText(path).Trim().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Demo5Docs_ShouldLinkToEachOther() {
        string english = ReadDocument("README.md");
        string chinese = ReadDocument("README_ch.md");

        english.Should().Contain("(README_ch.md)", "the English document links to the Chinese one");
        chinese.Should().Contain("(README.md)", "the Chinese document links to the English one");
    }

    [Fact]
    public void Demo5Docs_ShouldNotPromiseUnmeasuredParity() {
        // **不能出现"与 Kotlin 等价/互通"一类未经实测的主张。**
        // **No claim of Kotlin equivalence or interoperability, which has not been measured.**
        string english = ReadDocument("README.md");
        string chinese = ReadDocument("README_ch.md");

        english.Should().Contain("no claim of\ncross-language equivalence");
        chinese.Should().Contain("不主张跨语言等价");
    }

    [Fact]
    public void Demo5Docs_ShouldDocumentHowToRunThem() {
        // 文档里必须有一条**真能跑**的命令，而不是"参见某处"。
        // The document must carry a command that **actually runs**, not a pointer to somewhere else.
        ReadDocument("README.md").Should().Contain("--filter 'FullyQualifiedName~Demo5'");
        ReadDocument("README_ch.md").Should().Contain("--filter 'FullyQualifiedName~Demo5'");
    }
}
