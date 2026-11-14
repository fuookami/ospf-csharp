// 模块 README 的钉子 / Pins on the module README.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **README 必须保留"未实现"的那句** / The README must keep the "not implemented" sentence.
/// </summary>
/// <remarks>
/// 能力文档最容易腐烂的方式不是写错，而是**把"还没做"悄悄删掉**——留下一张全绿的表格，
/// 而读者再也不知道哪一项其实没接上。所以把那句"未实现"钉成判据。
///
/// Capability docs rot most often not by being wrong but by **quietly deleting "not done yet"**, leaving an all-green table with no reader able to tell which item is
/// actually missing. So the "not implemented" sentence is pinned as a criterion.
/// </remarks>
public class Csp2dReadmeTests {
    /// **中英两份 README 都必须在，且都写着"检验数定价未实现"** / Both READMEs must exist and both must state that reduced-cost pricing is not implemented.
    [Fact]
    public void Csp2dReadme_ShouldKeepTheUnimplementedCaveat() {
        string root = RepoRoot();

        string english = DocsFile.ReadAllText(Path.Combine(root, "src", "Framework.Csp2d", "README.md"));
        string chinese = DocsFile.ReadAllText(Path.Combine(root, "src", "Framework.Csp2d", "README_ch.md"));

        english.Should().Contain("residual demand", "the drive-by-residual fact is the whole caveat");
        english.Should().Contain("not implemented");
        chinese.Should().Contain("剩余需求");
        chinese.Should().Contain("未实现");

        english.Should().Contain("102", "the default case count is documentation, not a guess");
        english.Should().Contain("105", "the real-solver case count must be stated separately");
    }

    /// **README 不许声称跨语言等价或性能提升** / The README must not claim cross-language equivalence or a speed-up.
    [Fact]
    public void Csp2dReadme_ShouldNotClaimUnmeasuredEquivalence() {
        string root = RepoRoot();
        string english = DocsFile.ReadAllText(Path.Combine(root, "src", "Framework.Csp2d", "README.md"));
        string chinese = DocsFile.ReadAllText(Path.Combine(root, "src", "Framework.Csp2d", "README_ch.md"));

        english.Should().Contain("No claim of cross-language equivalence");
        chinese.Should().Contain("不声称跨语言等价");
    }

    /// <summary>仓库根目录 / The repository root.</summary>
    /// <returns>绝对路径 / The absolute path.</returns>
    private static string RepoRoot() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            if (File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new DirectoryNotFoundException("ospf-csharp.sln not found from the test output directory");
    }
}
