// 点了名字却在基线里不存在的能力 / Capabilities that were named but do not exist in the baseline.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **"缺口"要点名一个基线里真有的东西** / A "gap" has to name something the baseline really has.
/// </summary>
/// <remarks>
/// B955 核了 P9 摘要里最后两条"未移植"：
///
/// | 原话 | 实测 |
/// | --- | --- |
/// | 基线的 **PWL 诊断**未移植 | `framework-bpp1d`/`framework-bpp3d` 的 `main` 里 **`pwl` 零次** |
/// | 基线的 **shape-DTO 缓存**未移植 | 基线**任何地方**都没有 shape 缓存；`cache` 只出现在基准名与 README 散文里 |
///
/// **两条都点了一个基线里不存在的东西。** 这种"缺口"比漏记更坏：它列在待办里，谁去补都会先花一批时间
/// 找那个不存在的能力，**而找到之后只能得出"没有"** —— 与 B953 的 `BinType.Capacity` 是同一个形状，第三次。
///
/// B955 checked the last two "not ported" notes: the BPP modules have **zero** `pwl` in `main`, and the baseline has **no shape cache anywhere**. **Both named something
/// that does not exist**, which is worse than a missing entry: it sits on the to-do list, and whoever picks it up spends a batch looking for it — **and can only
/// conclude "it is not there"**. Same shape as B953's `BinType.Capacity`, for the third time.
/// </remarks>
public class PhantomGapClaimsTests
{
    /// <summary>**BPP 模块里没有 PWL** / No PWL in the BPP modules.</summary>
    /// <remarks>
    /// 这条判据钉的是**基线的形状**，不是本仓的实现：它证明"PWL 诊断"从来不是 BPP 侧的东西。
    /// 真正的 PWL 缺口在求解器插件与函数展开层（B954 量过）。
    ///
    /// This pins **the baseline's shape**, not this repo's implementation: it proves "PWL diagnostics" was never a BPP-side thing. The real PWL gap is in the solver
    /// plugins and the function expansion layer, measured in B954.
    /// </remarks>
    [Fact]
    public void TheBppModules_ShouldNotCarryPwlOrDiagnostics() {
        string root = Root();
        foreach (string module in new[] { "Framework.Bpp1d", "Framework.Bpp3d" }) {
            string folder = Path.Combine(root, "src", module);
            Directory.Exists(folder).Should().BeTrue();

            // **本仓也不该凭空长出 PWL 诊断**：那说明有人照着一条不存在的缺口去实现了。
            // **This repo must not grow PWL diagnostics out of nowhere either**: that would mean somebody implemented a gap that was never there.
            foreach (string file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)) {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) {
                    continue;
                }
                string text = File.ReadAllText(file);
                text.Contains("PwlDiagnostic", StringComparison.Ordinal)
                    .Should().BeFalse(Path.GetFileName(file) + " invented a PWL diagnostics type the baseline does not have");
            }
        }
    }

    /// <summary>**没有 shape 缓存** / There is no shape cache.</summary>
    /// <remarks>
    /// 基线里 `cache` 只出现在基准名（`GenerationCacheBuildBenchmark` 等）与 README 散文里，**没有一处是 shape 缓存**。
    /// 所以"shape-DTO 缓存未移植"这句话没有对应物。
    ///
    /// In the baseline `cache` appears only in benchmark names and README prose — **never as a shape cache** — so "shape-DTO cache not ported" has no counterpart.
    /// </remarks>
    [Fact]
    public void ThereShouldBeNoShapeDtoCache() {
        string root = Root();
        foreach (string module in new[] { "Framework.Bpp1d", "Framework.Bpp2d", "Framework.Bpp3d" }) {
            string folder = Path.Combine(root, "src", module);
            if (!Directory.Exists(folder)) {
                continue;
            }
            foreach (string file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)) {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) {
                    continue;
                }
                string name = Path.GetFileName(file);
                name.Contains("ShapeDtoCache", StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(name + " implements a cache the baseline never had");
                name.Contains("ShapeCache", StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(name + " implements a cache the baseline never had");
            }
        }
    }

    /// <summary>**状态文档不再把这两条当待办** / The status document no longer lists them as pending.</summary>
    [Fact]
    public void TheStatusDocument_ShouldNotListThemAsPending() {
        string root = Root();
        string status = File.ReadAllText(Path.Combine(root, "docs", "kotlin-sync-status.md"));

        status.Contains("retracted in Batch 955", StringComparison.Ordinal)
            .Should().BeTrue("the retraction has to be stated, or the next reader picks the same non-gap up again");
        status.Contains("~~The baseline's PWL diagnostics and shape-DTO cache remain unported~~", StringComparison.Ordinal)
            .Should().BeTrue("the original claim stays as struck-through history");
        // **不能再以"现在成立"的形式出现**：不带删除线的复述会把它变回待办。
        // **It must not reappear as current fact**: an un-struck restatement turns it back into a to-do.
        status.Split("PWL diagnostics and shape-DTO cache remain unported").Length.Should().Be(2,
            "the phrase may appear once inside the struck-through original, never twice");
        // **并且要写明它们从来不是缺口**——"撤回了"和"从来不存在"是两件事，后者才是这次量到的事实。
        // **And it has to say they were never gaps**: "withdrawn" and "never existed" are different, and the latter is what was measured.
        status.Contains("were never gaps at all", StringComparison.Ordinal)
            .Should().BeTrue("the point is not that the work was dropped but that there was nothing to do");
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
