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
/// | 原话 | 实测 |
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
