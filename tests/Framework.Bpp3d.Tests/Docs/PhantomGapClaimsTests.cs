// 点了名字却在不存在的能力 / Capabilities that were named but do not exist in the code.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **"缺口"要点名一个真有的东西** / A "gap" has to name something the code really has.
/// </summary>
/// <remarks>
/// 核了 P9 摘要里最后两条"未实现"：
///
/// | 原话 | 实测 |
/// | --- | --- |
/// | **PWL 诊断**未实现 | `framework-bpp1d`/`framework-bpp3d` 的 `main` 里 **`pwl` 零次** |
/// | **shape-DTO 缓存**未实现 | 源码里**任何地方**都没有 shape 缓存；`cache` 只出现在基准名与 README 散文里 |
///
/// **两条都点了一个不存在的东西。** 这种"缺口"比漏记更坏：它列在待办里，谁去补都会先花一批时间
/// 找那个不存在的能力，**而找到之后只能得出"没有"** —— 与 `BinType.Capacity` 那处是同一个形状，第三次。
///
/// checked the last two "not implemented" notes: the BPP modules have **zero** `pwl` in `main`, and the code has **no shape cache anywhere**. **Both named something
/// that does not exist**, which is worse than a missing entry: it sits on the to-do list, and whoever picks it up spends a batch looking for it — **and can only
/// conclude "it is not there"**. Same shape as the earlier `BinType.Capacity`, for the third time.
/// </remarks>
public class PhantomGapClaimsTests
{
    /// <summary>**BPP 模块里没有 PWL** / No PWL in the BPP modules.</summary>
    /// <remarks>
    /// 这条判据钉的是**形状**，不是本仓的实现：它证明"PWL 诊断"从来不是 BPP 侧的东西。
    /// 真正的 PWL 缺口在求解器插件与函数展开层。
    ///
    /// This pins **its shape**, not this repo's implementation: it proves "PWL diagnostics" was never a BPP-side thing. The real PWL gap is in the solver
    /// plugins and the function expansion layer, measured.
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
    /// `cache` 只出现在基准名（`GenerationCacheBuildBenchmark` 等）与 README 散文里，**没有一处是 shape 缓存**。
    /// 所以"shape-DTO 缓存未实现"这句话没有对应物。
    ///
    /// In the code `cache` appears only in benchmark names and README prose — **never as a shape cache** — so "shape-DTO cache not implemented" has no counterpart.
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
