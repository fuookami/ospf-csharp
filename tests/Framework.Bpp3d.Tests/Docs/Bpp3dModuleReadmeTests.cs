// Bpp3d 模块 README 的存在性与内容测试 / Existence and content tests for the Bpp3d module READMEs.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// Bpp3d 模块的双语 README / The Bpp3d module's bilingual READMEs.
/// </summary>
/// <remarks>
/// 这两份文档的价值全在**它敢说"不是一一对应""不是模型约束""没有 PWL 路径"**。
/// 若后续批次把这些如实说明删掉，README 就退化成一份好看的能力清单，而这不会让任何代码失败。
///
/// The value of these two documents is entirely in **daring to say "not one-to-one", "not a model constraint", "no PWL path"**. If a later batch drops
/// those honest notes the README degrades into a good-looking feature list — and no code fails.
/// </remarks>
public sealed class Bpp3dModuleReadmeTests {
    private static string ReadmePath(string fileName)
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && directory is not null; ++depth) {
            string candidate = Path.Combine(directory, "src", "Framework.Bpp3d", fileName);
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new FileNotFoundException($"src/Framework.Bpp3d/{fileName} not found from the test output directory");
    }

    private static string ReadReadme(string fileName) =>
        DocsFile.ReadAllText(ReadmePath(fileName)).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Theory]
    [InlineData("README.md")]
    [InlineData("README_ch.md")]
    public void ModuleReadme_ShouldExist(string fileName)
    {
        File.Exists(ReadmePath(fileName)).Should().BeTrue();
        ReadReadme(fileName).Trim().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ModuleReadme_ShouldLinkTheTwoLanguages()
    {
        ReadReadme("README.md").Should().Contain("(README_ch.md)");
        ReadReadme("README_ch.md").Should().Contain("(README.md)");
    }

    /// <summary>**必须说明目录是按层拆的** / It must say the layout splits by layer.</summary>
    [Fact]
    public void ModuleReadme_ShouldStateTheLayoutIsNotOneToOne()
    {
        ReadReadme("README.md").Should().Contain("split by layer rather than by problem family");
        ReadReadme("README_ch.md").Should().Contain("按层拆而不是按问题域拆");
    }

    /// <summary>**必须说明约束是前置校验** / It must state the constraints are pre-checks.</summary>
    [Fact]
    public void ModuleReadme_ShouldStateConstraintsArePreChecks()
    {
        ReadReadme("README.md").Should().Contain("Capacity constraints are pre-checks, not model constraints");
        ReadReadme("README_ch.md").Should().Contain("容量约束是前置校验，不是模型约束");
    }

    /// <summary>**必须说明评分按件数而非重量** / It must state the score counts items, not weight.</summary>
    [Fact]
    public void ModuleReadme_ShouldStateTheScoringCaveat()
    {
        // 断言**正文那一段**而不是表格行：表格里也有同样的措辞，改掉一处仍能通过。
        // Assert the **paragraph**, not the table row: the table repeats the wording, so changing one place still passes.
        ReadReadme("README.md").Should().Contain("**2. The shadow-price score counts items, not weight.**");
        ReadReadme("README_ch.md").Should().Contain("**2. 影子价格评分按件数计，不是按重量。**");
    }

    /// <summary>**必须说明没有 PWL 体积路径** / It must state there is no PWL volume path.</summary>
    [Fact]
    public void ModuleReadme_ShouldStateTheMissingPwlPath()
    {
        ReadReadme("README.md").Should().Contain("**3. There is no PWL volume path.**");
        ReadReadme("README_ch.md").Should().Contain("**3. 没有 PWL 体积路径。**");
    }

    /// <summary>**不得宣称跨语言等价或性能** / It must not claim cross-language equivalence or performance.</summary>
    [Fact]
    public void ModuleReadme_ShouldNotClaimUnmeasuredParity()
    {
        ReadReadme("README.md").Should().Contain("No claim of cross-language equivalence");
        ReadReadme("README_ch.md").Should().Contain("不主张跨语言等价");
    }
}
