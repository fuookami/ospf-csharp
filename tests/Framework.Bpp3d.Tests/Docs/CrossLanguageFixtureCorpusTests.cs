// 六份逐字节相同的跨语言夹具 / The six byte-identical cross-language fixtures.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **低估自己的证据，和夸大一样贵** / Understating one's evidence costs as much as overstating it.
/// </summary>
/// <remarks>
/// 只钉了 `RemoteFixtures/` 下那四份，第七节据此写"只有这三处实测"。 一数：**六份逐字节相同**——
/// `Core.Tests/Fixtures/analysis-fixtures/` 下那两份**同样是逐字节复制**，从来没被算进这句话。
///
/// **4.8 那一行早就写着 checkpoint fixture "与源文件逐字节相同"，而第七节还在说四份。** 两处的账对不上，
/// 下一个人只会信更保守的那个，然后**重做一遍已经做过的事**。
///
/// pinned only the four under `RemoteFixtures/`, which is why section seven said "only these three are measured". counted **six byte-identical fixtures** — the two
/// under `Core.Tests/Fixtures/analysis-fixtures/` are **equally byte-identical** and were never counted. **Row 4.8 already called the checkpoint fixture byte-identical while
/// section seven spoke of four**; given two disagreeing accounts the next reader trusts the conservative one and **redoes work that was already done.**
/// </remarks>
public class CrossLanguageFixtureCorpusTests
{
    /// <summary>**六份都还在，一个字节都没变** / All six survive, byte for byte.</summary>
    /// <remarks>
    /// 哈希取自 `git cat-file -p 12fdb587:<path>` 的输出——**是一侧算的**，不是本仓算完抄过来的。
    /// 这条判据钉的是"文件没被改过"，**不证明内容语义正确**；语义靠解码判据与逐字契约表。
    ///
    /// The hashes come from `git cat-file -p 12fdb587:<path>` — **computed on the code side**, not copied after hashing here. This pins "the file was not edited"; it
    /// **does not prove the semantics**, which the decoding criteria and the verbatim contract table carry.
    /// </remarks>
    [Fact]
    public void TheSixFixtures_ShouldStillMatchTheBaselineBytes() {
        string root = Root();

        (string File, string Prefix)[] fixtures = {
            (@"tests\Framework.Tests\RemoteFixtures\remote-cp-result-v1.json", "58fc538acb808728"),
            (@"tests\Framework.Tests\RemoteFixtures\remote-cp-result-v2.json", "ebad2593bdd9279b"),
            (@"tests\Framework.Tests\RemoteFixtures\remote-linear-model-v2.json", "71a086af6dcac0e0"),
            (@"tests\Framework.Tests\RemoteFixtures\remote-linear-result-v2.json", "e76a8e93e75046b7"),
            (@"tests\Core.Tests\Fixtures\analysis-fixtures\checkpoint-envelope-v3.json", "8c252e592cfa26cb"),
            (@"tests\Core.Tests\Fixtures\analysis-fixtures\checkpoint-wire-contract.tsv", "6eaffdfbdbbb8521"),
        };

        fixtures.Should().HaveCount(6, "six fixtures are byte-identical copies of the pinned baseline");

        foreach ((string file, string prefix) in fixtures) {
            string path = Path.Combine(root, file);
            File.Exists(path).Should().BeTrue(file + " is a byte-identical copy of the baseline and has to exist");
            FileHashPrefix(path).Should().Be(prefix,
                file + " must stay byte-identical to 12fdb587; an edited space voids the cross-language evidence while every decoding criterion stays green");
        }
    }

    /// <summary>**checkpoint envelope 是 1555 字节** / The checkpoint envelope is 1555 bytes.</summary>
    /// <remarks>
    /// 4.8 把字节数写进了文档。**字节数与哈希是两条独立的线**：哈希能证明"没改过"，字节数能证明"就是文档说的那一份"。
    /// 只钉一个的话，文档里的另一个数字可以烂掉。
    ///
    /// Row 4.8 puts the byte count in the document. **Byte count and hash are two independent lines**: the hash proves "unedited", the count proves "the one the document
    /// describes". Pinning only one lets the other rot.
    /// </remarks>
    [Fact]
    public void TheCheckpointEnvelope_ShouldBeItsDocumentedSize() {
        string root = Root();
        string path = Path.Combine(root, "tests", "Core.Tests", "Fixtures", "analysis-fixtures", "checkpoint-envelope-v3.json");
        new FileInfo(path).Length.Should().Be(1555, "row 4.8 states 1555 bytes; a different length means the fixture changed and the row is stale");
    }

    /// <summary>**矩阵写的是六份，不是三处** / The matrix says six fixtures, not three pieces.</summary>
    [Fact]
    public void TheMatrix_ShouldCountTheFixturesCorrectly() {
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        matrix.Contains("六份夹具逐字节相同", StringComparison.Ordinal)
            .Should().BeTrue("the heading has to state what was measured, not the smaller subset somebody remembered");
        // **只看标题行**：更正说明里引用了旧标题，全表查会把它当成"还在"。
        // **Check the heading line only**: the correction note quotes the old heading, and a whole-file search would read that as "still there".
        string heading = matrix.Split('\n')
            .FirstOrDefault(line => line.StartsWith("## 七、", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("section seven heading not found");
        heading.Contains("只有这三处实测", StringComparison.Ordinal)
            .Should().BeFalse("that heading understated the evidence; B958 replaced it");
        // **两份新增钉的哈希要在表里**：只写"六份"而不列，下一个人还得再数一遍。
        // **Both new hashes have to be in the table**: saying "six" without listing them makes the next reader count again.
        matrix.Contains("8c252e592cfa26cb", StringComparison.Ordinal).Should().BeTrue("the checkpoint envelope's hash is now pinned");
        matrix.Contains("6eaffdfbdbbb8521", StringComparison.Ordinal).Should().BeTrue("and the wire contract table's too");
    }

    /// <summary>**缺的那两个文件仍然写着缺** / The two absent files are still stated as absent.</summary>
    /// <remarks>
    /// 第七节现在六份里有两份是 ✅ 新增的，容易让人以为"analysis 那两块也补上了"。
    /// **`analysis-contract.tsv` 与 `analysis-cases.tsv` 不在那六份里**——它们根本不存在。
    ///
    /// Section seven now has two newly ✅ rows, which easily reads as "the analysis pieces were filled in too". **`analysis-contract.tsv` and `analysis-cases.tsv` are not
    /// among the six** — they do not exist at all.
    /// </remarks>
    [Fact]
    public void TheAnalysisContract_ShouldStillBeNamedAsMissing() {
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        string section = matrix.Substring(matrix.IndexOf("## 七、", StringComparison.Ordinal));
        section.Should().Contain("analysis-contract.tsv", "the missing contract stays visible next to the six that are present");
        section.Should().Contain("本仓连文件都没有", "and it is stated as absent, not as partial");

        // **而它确实不在**：文档说缺，文件就得真缺。
        // **And it really is absent**: if the document says missing, missing it must be.
        string fixtures = Path.Combine(root, "tests", "Core.Tests", "Fixtures", "analysis-fixtures");
        File.Exists(Path.Combine(fixtures, "analysis-contract.tsv")).Should().BeFalse();
        File.Exists(Path.Combine(fixtures, "analysis-cases.tsv")).Should().BeFalse();
    }

    private static string FileHashPrefix(string path) {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    private static string Root() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        return directory.FullName;
    }
}
