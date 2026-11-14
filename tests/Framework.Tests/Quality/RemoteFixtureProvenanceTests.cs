using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Quality;

/// <summary>
/// B854：每个 wire fixture 都必须**标注来源** / Every wire fixture must carry its provenance.
/// </summary>
/// <remarks>
/// `tests/Framework.Tests/RemoteFixtures/README.md` 里有一张来源表，因为它区分两种**强度完全不同**的证据：
///
/// | 来源 | 证据强度 |
/// | --- | --- |
/// | 由固定 Kotlin `12fdb587` 产出 | **跨语言**证据 |
/// | 由 C# 产出（再送回 Kotlin 严格解码） | 单侧证据 + 一次回读 |
///
/// **没标来源的 fixture 是不可证伪的证据**：它和 C# 自己生成的样本长得一模一样，
/// 而"跨语言契约通过"这句话会因此变得无法检验。B854 之前没有任何判据盯着这张表——
/// 加一个文件不会红，删一个文件也不会红。
///
/// The README carries a provenance table because it separates two **very different strengths of evidence**: a fixture produced by the pinned Kotlin `12fdb587` is
/// **cross-language** evidence, while one produced by C# (then strictly decoded by Kotlin) is one-sided evidence plus a read-back. **An unlabelled fixture is
/// unfalsifiable evidence**: it looks exactly like a sample C# generated itself, so the claim "the cross-language contract passes" stops being testable. Before B854
/// nothing watched that table — adding a file red nothing, deleting one red nothing.
/// </remarks>
public sealed class RemoteFixtureProvenanceTests
{
    /// <summary>**每个 fixture 文件都在来源表里** / Every fixture file appears in the provenance table.</summary>
    [Fact]
    public void EveryFixtureFile_ShouldBeListedWithItsProvenance() {
        string directory = FixtureDirectory();
        string readme = File.ReadAllText(Path.Combine(directory, "README.md"));

        var unlisted = new List<string>();
        foreach (string file in Directory.EnumerateFiles(directory)) {
            string name = Path.GetFileName(file);
            if (name == "README.md") {
                continue;
            }
            if (!readme.Contains(name, StringComparison.Ordinal)) {
                unlisted.Add(name);
            }
        }

        unlisted.Should().BeEmpty(
            "a fixture without a recorded source is indistinguishable from one we generated ourselves; the cross-language claim cannot be checked against it");
    }

    /// <summary>**来源表里没有已删除的 fixture** / The provenance table names no deleted fixture.</summary>
    /// <remarks>
    /// 反方向同样要紧：表里列着一个不存在的文件，会让读者以为那项证据还在。
    ///
    /// The other direction matters as much: a table listing a file that no longer exists makes readers believe that piece of evidence is still here.
    /// </remarks>
    [Fact]
    public void TheProvenanceTable_ShouldNotNameMissingFixtures() {
        string directory = FixtureDirectory();
        string readme = File.ReadAllText(Path.Combine(directory, "README.md"));

        var missing = new List<string>();
        foreach (string line in readme.Split('\n')) {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal)) {
                continue;
            }
            // 表格行形如：`| `name.json` | 来源 |` / Table rows read `| `name.json` | provenance |`.
            int first = trimmed.IndexOf('`');
            int second = first < 0 ? -1 : trimmed.IndexOf('`', first + 1);
            if (first < 0 || second < 0) {
                continue;
            }
            string name = trimmed[(first + 1)..second];
            if (!File.Exists(Path.Combine(directory, name))) {
                missing.Add(name);
            }
        }

        missing.Should().BeEmpty("a listed fixture that does not exist makes the recorded evidence look stronger than it is");
    }

    /// <summary>**"已知边界"那一节必须留着** / The "known boundaries" section must stay.</summary>
    /// <remarks>
    /// 这一节写着"调度模型、能力摘要与 CP checkpoint envelope 尚未对跑"和"不因为这些 fixture 通过就宣称整体互通"。
    /// **它是这份证据的限度声明**：删掉它，剩下的表格就会被读成"整体已经互通"。
    ///
    /// That section says the schedule model, capability digest and CP checkpoint envelope have not been run against each other yet, and that these fixtures passing is
    /// no claim of overall interop. **It is the limitation statement for this evidence**: delete it and the remaining table reads as "interop is done".
    /// </remarks>
    [Fact]
    public void TheKnownBoundariesSection_ShouldStay() {
        string readme = File.ReadAllText(Path.Combine(FixtureDirectory(), "README.md"));

        readme.Should().Contain("已知边界", "the limitation statement is what keeps the table from being read as overall interop");
        readme.Should().Contain("尚未对跑", "the list of untested pairs must stay explicit");
    }

    /// <summary>找到 fixture 目录 / Locate the fixture directory.</summary>
    /// <returns>目录路径 / The directory path.</returns>
    private static string FixtureDirectory() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = Path.Combine(directory.FullName, "tests", "Framework.Tests", "RemoteFixtures");
            if (Directory.Exists(candidate)) {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("could not locate the remote fixture directory");
    }
}
