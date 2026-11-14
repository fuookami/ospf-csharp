// 成员级审计的钉子（B739）/ Pins on the member-level audit (B739).

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **成员级审计的钉子（B739）** / Pins on the member-level audit (B739).
/// </summary>
/// <remarks>
/// 文件 stem 扫只回答"文件在不在"，而"同名不等于完整"这条误差从来没有在产能调度与资源子域里逐个核实过。
/// B739 写了一个按**类型名**比成员的脚本，跑出 19 个"缺成员/缺类型"的名字——**逐个核实后没有一个是真缺口**，
/// 全是语言能力差异（Kotlin `typealias`）、命名差异（`TaskScheduling*` 对 `BunchScheduling*`）、以及正则把泛型参数当成员。
///
/// 真正查出来的只有一个**空壳**：`CapacityQuantityTypes` 一个成员都没有，却写着"数量类型工具方法"，已删除。
///
/// 这里钉四件事：审计脚本在、清单留档了三条误差、空壳确实没了、清单里"没有一个是真缺口"的结论必须在。
/// **自动比对必须人工过一遍再写进清单**——否则清单会把人送去做已经做完的事。
///
/// The stem scan only answers "is the file present", and the "same name is not the same capability" error mode was never verified type by type in the capacity-scheduling and
/// resource sub-domains. B739 wrote a script comparing members by **type name**; it flagged 19 names and **not one was a real gap** — all were language differences (Kotlin
/// `typealias`), naming differences (`TaskScheduling*` vs `BunchScheduling*`), or the regex counting generic parameters as members. The single genuine finding was an **empty shell**:
/// `CapacityQuantityTypes` had no members at all while claiming "quantity type utility methods"; it is gone. Four things are pinned: the audit script exists, the inventory records
/// its three error modes, the shell is really gone, and the "no real gaps" conclusion stays. **Automated comparisons need a human pass before reaching the inventory** — otherwise the
/// table sends people to redo finished work.
/// </remarks>
public class MemberAuditTests {
    /// <summary>
    /// **审计脚本在，且按类型名而不是文件 stem 比对** / The audit script exists and compares by type name, not by file stem.
    /// </summary>
    /// <remarks>
    /// 两点成对：脚本存在 / 脚本里出现"按类型名找 C# 类型"的关键步骤（`csharp_file_for_type` 与 `I` 前缀约定）。
    /// 只钉"文件存在"会让一个改成 stem 比对的版本通过。
    ///
    /// Two paired points: the script exists; and it contains the key step that looks a C# type up by name (the `csharp_file_for_type` helper and the `I` prefix convention).
    /// Pinning only "the file exists" would let a version that reverted to stem comparison pass.
    /// </remarks>
    [Fact]
    public void AuditScript_ShouldCompareByTypeName() {
        string script = DocsFile.ReadAllText(Path.Combine(RepoRoot(), "logs", "member-scan.py"));

        script.Should().Contain("def csharp_file_for_type(",
            "the audit must look types up by name");
        script.Should().Contain("'I' + type_name",
            "the `I` prefix convention is what maps `Capacity` onto `ICapacity`");
        script.Should().Contain("NOISE",
            "generic parameters arriving through signatures must be excluded");
    }

    /// <summary>
    /// **清单留档了三条误差与"没有一个是真缺口"的结论** / The inventory records the three error modes and the "no real gaps" verdict.
    /// </summary>
    /// <remarks>
    /// 三点成对：清单有这一节 / 写着三条误差（typealias、命名差异、泛型参数当成员）/ 明确"逐个核实后没有一个是真缺口"。
    /// **结论句是这一节的全部价值**——只留 19 个名字会让人以为还有 19 个缺口要做。
    ///
    /// Three paired points: the section exists; it names the three error modes (typealias, naming differences, generic parameters counted as members); and it states plainly that
    /// "not one is a real gap". **That verdict is the whole value of the section** — leaving only the 19 names would suggest 19 gaps remain.
    /// </remarks>
    [Fact]
    public void Inventory_ShouldRecordTheAuditAndItsErrorModes() {
        string inventory = DocsFile.ReadAllText(Path.Combine(RepoRoot(), "logs", "kotlin-sync-gap-inventory.md"));

        inventory.Should().Contain("### 成员级审计（B739，产能调度 + 资源子域）");
        inventory.Should().Contain("typealias");
        inventory.Should().Contain("TaskScheduling*ResourceUsage");
        inventory.Should().Contain("逐个核实后没有一个是真缺口");
    }

    /// <summary>
    /// **那个空壳真的没了** / The empty shell is really gone.
    /// </summary>
    /// <remarks>
    /// 两点成对：`CapacityQuantityTypes` 不再有任何声明 / 清单里记着它被删的原因（**没有成员的类比没有这个类更糟**）。
    ///
    /// Two paired points: `CapacityQuantityTypes` has no declaration left; and the inventory records why it went — **a class with no members is worse than no class**.
    /// </remarks>
    [Fact]
    public void EmptyShell_ShouldStayDeleted() {
        string modelDirectory = Path.Combine(RepoRoot(), "src", "Framework.GanttScheduling", "Domain", "CapacityScheduling", "Model");
        foreach (string file in DocsFile.EnumerateFiles(modelDirectory, "*.cs", SearchOption.AllDirectories)) {
            string body = DocsFile.ReadAllText(file);
            body.Should().NotContain("class CapacityQuantityTypes",
                "%s must not resurrect the empty shell", Path.GetFileName(file));
        }
        DocsFile.ReadAllText(Path.Combine(RepoRoot(), "logs", "kotlin-sync-gap-inventory.md"))
            .Should().Contain("一个没有成员的类比没有这个类更糟");
    }

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
