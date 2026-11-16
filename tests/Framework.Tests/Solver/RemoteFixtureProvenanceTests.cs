// fixture 的来源必须被守护 / The fixtures' provenance has to be guarded.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

/// <summary>
/// **fixture 的来源必须被守护** / The fixtures' provenance has to be guarded.
/// </summary>
/// <remarks>
/// `RemoteFixtures/README.md` 写着四份 fixture"从固定 Kotlin `12fdb587` 逐字节复制"。
/// **这句话此前没有任何判据守护**：fixture 被谁改一个空格，跨语言证据就悄悄失效，
/// 而 `RemoteWireContractTest` 的几十条解码判据**照样全绿**——它们测的是"这份文件能解码"，
/// **不是"这份文件是原样副本"**。
///
/// 这里把四个 SHA-256 钉住。**换 fixture 必须同时改这里**，那时就会有人问"新文件从哪来"——
/// 这正是要的：**来源变化必须是显式动作，不能是顺手一改。**
///
/// The README says these four fixtures are byte-for-byte copies of the pinned Kotlin `12fdb587` fixtures, and **nothing guarded that claim**: a stray space edited into
/// one of them silently voids the cross-language evidence while the dozens of decoding criteria **stay green**, because they test "this file decodes", **not "this file
/// is the original copy"**. These four SHA-256 values are now pinned. **Replacing a fixture means editing this file too**, and at that point somebody asks where the new
/// file came from — which is the point: **a provenance change has to be an explicit act, not an incidental edit.**
/// </remarks>
public class RemoteFixtureProvenanceTests {
    /// <summary>四份 fixture 的固定哈希 / The pinned hashes of the four fixtures.</summary>
    /// <remarks>
    /// 实测来源：`git -C D:/workspace/ospf/ospf-kotlin cat-file -p 12fdb587:ospf-kotlin-framework/src/test/resources/fixtures/<name>`
    /// 与本地文件逐字节比对。**四个都 SAME。**
    ///
    /// Measured by comparing `git cat-file` output at `12fdb587` against the local files; **all four are identical.**
    /// </remarks>
    private static readonly Dictionary<string, string> BaselineHashes = new(StringComparer.Ordinal) {
        ["remote-cp-result-v1.json"] = "58fc538acb808728",
        ["remote-cp-result-v2.json"] = "ebad2593bdd9279b",
        ["remote-linear-model-v2.json"] = "71a086af6dcac0e0",
        ["remote-linear-result-v2.json"] = "e76a8e93e75046b7",
    };

    /// <summary>**四份逐字节副本仍然逐字节相同** / The four copies are still byte-for-byte identical.</summary>
    [Fact]
    public void TheByteForByteFixtures_ShouldStillMatchTheBaseline() {
        string root = FixtureRoot();
        foreach ((string name, string prefix) in BaselineHashes) {
            string path = Path.Combine(root, name);
            File.Exists(path).Should().BeTrue(name + " is the cross-language evidence, so it cannot go missing");

            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            hash.Should().StartWith(prefix,
                name + " is supposed to be a byte-for-byte copy of the pinned baseline fixture; "
                + "if this changed, the file has to be re-copied and this hash re-measured");
        }
    }

    /// <summary>**README 必须标注每一份的来源** / The README has to record each file's provenance.</summary>
    /// <remarks>
    /// README 自己写着"来源必须标注，因为'由产出'和'由 C# 产出'是两种不同强度的证据"。
    /// **那句规则要能被检查**，否则新加一份 fixture 时没人会想起来标注。
    ///
    /// The README states that provenance must be recorded, because "produced by the reference" and "produced by C#" are two different strengths of evidence.
    /// **That rule has to be checkable**, or nobody remembers to annotate a newly added fixture.
    /// </remarks>
    [Fact]
    public void EveryFixture_ShouldHaveItsProvenanceInTheReadme() {
        string root = FixtureRoot();
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));

        string[] fixtures = Directory.GetFiles(root, "*.json")
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToArray();
        fixtures.Should().NotBeEmpty("the cross-language contract tests read these files");

        foreach (string fixture in fixtures) {
            readme.Should().Contain(fixture,
                fixture + " has no provenance row, and an unannotated fixture is exactly the 'produced by C#' case pretending to be baseline output");
        }
    }

    /// <summary>**已知边界必须写明"还没有对跑"的部分** / The known boundaries must state what has not been exercised.</summary>
    /// <remarks>
    /// README 写着"调度模型、能力摘要与 CP checkpoint envelope 尚未对跑"，以及"不因为这些 fixture 通过就宣称整体互通"。
    /// **这两句是这张表的诚实性所在**：少了它们，读者会把"三处往返"读成"协议互通"。
    ///
    /// The README says the scheduling model, capability summary and CP checkpoint envelope have not been exercised, and that these fixtures passing must not be read as
    /// overall interoperation. **Those two sentences are the table's honesty**: without them a reader takes "three round trips" for "the protocol interoperates".
    /// </remarks>
    [Fact]
    public void TheKnownBoundaries_ShouldStillBeStated() {
        string readme = File.ReadAllText(Path.Combine(FixtureRoot(), "README.md"));
        readme.Should().Contain("尚未对跑",
            "the un-exercised parts have to stay named, or the three round trips read as full interoperation");
        readme.Should().Contain("不因为这些 fixture 通过就宣称整体互通",
            "the disclaimer is the whole reason this table is trustworthy");
    }

    /// <summary>**找源码目录，不是输出目录** / Find the source directory, not the output one.</summary>
    /// <remarks>
    /// 输出目录里的 `remote-fixtures` **只拷 `.json`**，没有 `README.md`——而这里要看 README。
    /// 所以从仓库根找源码目录：**证据文件在哪，来源说明就在哪。**
    ///
    /// The output copy holds **only the `.json` files**, no `README.md`, and reads the README. So it finds the source directory from the repository root:
    /// **the provenance note lives next to the evidence.**
    /// </remarks>
    private static string FixtureRoot() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new DirectoryNotFoundException("repository root not found");
        }
        string path = Path.Combine(directory.FullName, "tests", "Framework.Tests", "RemoteFixtures");
        Directory.Exists(path).Should().BeTrue(path + " holds the cross-language evidence");
        return path;
    }
}
