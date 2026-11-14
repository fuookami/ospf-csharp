// P9 摘要里那几条"做不到"，逐条复核 / Re-check the "cannot be done" claims in the P9 summary.

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **"做不到"比"做完了"更贵** / A stale "cannot" costs more than a stale "done".
/// </summary>
/// <remarks>
/// `docs/kotlin-sync-status.md` 的 P9 摘要里躺着三条"做不到"：
///
/// | 原话 | 实测（B953） |
/// | --- | --- |
/// | `PatternColumnBenchmark` 无法移植：本仓**根本没有 `Framework.Bpp1d`** | **项目在** |
/// | `GenerationCacheBuildBenchmark` 无法移植：`DFSGenerator` 是 `internal` | **是 `public`** |
/// | `Item.Weight` 不参与任何约束：`BinType.Capacity` 未用于重量限制 | **`BinType.MaxWeight` 参与**，而 `Capacity` **基线里也没有** |
///
/// **三条全错，而三条都是"做不了"这种方向。** 说"做完了"而其实没做，会让人少做一步；
/// 说"做不了"而其实能做，**那一步永远不会有人去做**。
///
/// Three "cannot" claims sat in the P9 summary and **all three were false**, all in the "cannot" direction. Saying "done" when it is not costs one missing step; saying
/// "cannot" when it can **means nobody ever takes that step**.
/// </remarks>
public class P9SummaryClaimsTests
{
    /// <summary>**`Framework.Bpp1d` 存在** / `Framework.Bpp1d` exists.</summary>
    [Fact]
    public void TheBpp1dProject_ShouldExist() {
        string root = Root();
        Directory.Exists(Path.Combine(root, "src", "Framework.Bpp1d")).Should().BeTrue(
            "the P9 summary said this project did not exist at all, which is why the benchmark was never ported");
        File.Exists(Path.Combine(root, "src", "Framework.Bpp1d", "Framework.Bpp1d.csproj")).Should().BeTrue();
    }

    /// <summary>**`DFSGenerator` 是 `public`** / `DFSGenerator` is `public`.</summary>
    [Fact]
    public void TheDfsGenerator_ShouldBePublic() {
        string root = Root();
        string path = Path.Combine(
            root, "src", "Framework.Csp1d", "Domain", "CuttingPlanGeneration", "service", "DFSGenerator.cs");
        File.Exists(path).Should().BeTrue();

        string text = File.ReadAllText(path);
        text.Should().Contain("public sealed class DFSGenerator",
            "the P9 summary said it was internal, which would indeed have blocked the benchmark");
        text.Should().NotContain("internal sealed class DFSGenerator");
    }

    /// <summary>**重量真的参与方案校验** / Weight really takes part in pattern validation.</summary>
    /// <remarks>
    /// 基线在 `PackingProblem.validatePattern` 里把物品重量换算到箱体单位、按件数缩放、求和，再与 `maxWeight` 比较。
    /// C# 的 `PackingProblem.ValidatePattern` 逐行对应。**这条判据钉的是"重量被读了"，不是"重量字段在"。**
    ///
    /// The baseline converts each item's weight into the bin's unit, scales by the count, sums, and compares against `maxWeight`; the C# `ValidatePattern` matches it
    /// line for line. **This pins "the weight is read", not "the weight field exists".**
    /// </remarks>
    [Fact]
    public void ThePatternValidation_ShouldConsumeTheBinWeight() {
        string root = Root();
        string path = Path.Combine(root, "src", "Framework.Bpp1d", "Domain", "Item", "Model", "PackingProblem.cs");
        string text = File.ReadAllText(path);

        text.Should().Contain("bin.MaxWeight", "the weight limit has to be read for the pattern to be checked against it");
        text.Should().Contain("方案超过箱体容量",
            "and exceeding it has to be refused, not silently accepted");
        // **`BinType.Capacity` 在基线里也不存在**：那条笔记点了一个不存在的字段。
        // **`BinType.Capacity` does not exist in the baseline either**: that note named a field that was never there.
        text.Should().NotContain("Capacity",
            "the baseline's BinType is (id, length, maxWeight, cost, inventory); a Capacity field would be an invention");
    }

    /// <summary>**P9 摘要不再说这三件事做不到** / The P9 summary no longer says these three cannot be done.</summary>
    [Fact]
    public void TheP9Summary_ShouldNotRepeatTheThreeFalseClaims() {
        string root = Root();
        string status = File.ReadAllText(Path.Combine(root, "docs", "kotlin-sync-status.md"));

        // **数两处，不是"至少一处"**：B953 的变异验证发现，只断言"包含 Retracted"时，
        // 抹掉其中一条的撤回声明**判据仍然绿**——另一条把它顶住了。**"至少一个"是最容易写成空判据的形状。**
        //
        // **Count two occurrences, not "at least one"**: B953's mutation test found that erasing one retraction **left the criterion green**, because the other one
        // covered for it. **"At least one" is the easiest shape to write a vacuous criterion in.**
        status.Split("Retracted in Batch 953").Length.Should().Be(3,
            "both claims need their own retraction; one covering for the other is exactly the hole this found");
        // **原文那两句必须留下划线**：删掉它们会让"曾经这么写过"这段历史消失，而下一批又会重新推一遍。
        // **The original sentences stay struck through**: deleting them erases the history, and the next batch re-derives the same false claim.
        // **回显一小段，不回显整篇**：`Contain` 失败时会把整份 status 打出来，
        // 而那份文档有几千行——**判据的失败信息要能读**。
        // **Echo a fragment, not the document**: a failing `Contain` prints the whole status file, which runs to thousands of lines —
        // **a failure message has to be readable.**
        status.Contains("~~`PatternColumnBenchmark.kt` cannot be ported", StringComparison.Ordinal)
            .Should().BeTrue("the original claim has to survive as struck-through history");
        status.Contains("~~`Infra.Item.Weight` is carried but takes part in no constraint", StringComparison.Ordinal)
            .Should().BeTrue("and so does the second one");
        // **而它们不能再以"现在成立"的形式出现**：只看带删除线的原文会放过"又抄了一遍不带删除线的"。
        // **And they must not appear again as current fact**: matching only the struck-through text would let an un-struck copy slip in.
        // **按不跨行的片段数**：原文被换行切成三行，直接匹配整句会漏。
        // **Count a fragment that does not span lines**: the original is wrapped across three lines, so matching the whole sentence would miss it.
        status.Split("no `Framework.Bpp1d` project here at all").Length.Should().Be(2,
            "the phrase may appear once inside the struck-through original, never twice");
        status.Split("`DFSGenerator`, which is `internal`").Length.Should().Be(2,
            "and the internal claim likewise survives only as history");
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
