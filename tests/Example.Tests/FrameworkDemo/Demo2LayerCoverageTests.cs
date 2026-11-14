#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B823：把"哪条判据跑在默认层"钉在源码上 / B823: pin which criteria run in the default layer.
/// </summary>
/// <remarks>
/// **放在单独的文件里，不是被钉的那份文件里**：B823 的第一版把这条判据写在同一个文件末尾，于是"把三条判据挪回 `#if`"这个变异
/// **连同这条判据一起挪进去了**——它自己被编译掉，当然不会红。**看守者不能睡在牢房里。**
///
/// **It lives in its own file, not in the file it guards**: the first version of B823 put this criterion at the end of that very file, so the mutation "move the three
/// criteria back inside `#if`" **moved this criterion too** — it was compiled out and of course never turned red. **A guard must not sleep inside the cell.**
/// </remarks>
public sealed class Demo2LayerCoverageTests
{
    /// <summary>
    /// **哪些判据跑在默认层，必须被钉住** / Which criteria run in the default layer must be pinned.
    /// </summary>
    /// <remarks>
    /// B821/B822 把三条判据搬出了 `#if SCIP_INTEGRATION`，让**没有求解器的机器**也能挡住"两条路径不一致"。
    /// 但这件事本身**没有任何东西记着**：谁把它们挪回 `#if` 里，覆盖率会静默下降，而所有判据依然全绿。
    ///
    /// 所以这里读**那份测试文件的源文本**，按行跟踪 `#if`/`#endif` 的深度，断言那三条的方法声明**在深度 0**；
    /// 同时断言**需要求解的那条在深度 > 0**——**后一半是前一半的校验**：如果深度跟踪写坏了、永远返回 0，
    /// 前三条照样过，只有后一条会红。**不成对的断言在这里等于没有断言。**
    ///
    /// **Which criteria run in the default layer must be pinned.** B821/B822 moved three criteria out of `#if SCIP_INTEGRATION` so **a machine without a solver**
    /// still catches "the two paths disagree". Nothing recorded that: move them back inside `#if` and coverage silently drops while everything stays green. So this reads
    /// **that test file's source**, tracks `#if`/`#endif` depth line by line, and asserts the three method declarations sit at **depth 0** — while asserting the one that
    /// genuinely needs a solve sits at **depth > 0**. **The second half validates the first**: a broken depth tracker that always returns 0 still passes the three and
    /// only fails on the last. **An unpaired assertion here would be no assertion at all.**
    /// </remarks>
    [Fact]
    public void TheDefaultLayerCriteria_ShouldStayOutOfTheScipBlock() {
        Dictionary<string, int> depths = MethodDepths(LayerCoverageSource());

        // 默认层：只需要"建出来的模型形状"，求解失败也拿得到。
        // Default layer: only the assembled model's shape, available even when solving fails.
        foreach (string name in new[] {
            "TheThreePipelines_ShouldHaveDistinctShapes",
            "TheApplicationPath_ShouldMatchThePipelinePath",
        }) {
            depths.Should().ContainKey(name);
            depths[name].Should().Be(0,
                name + " must run without a solver; moving it back inside #if SCIP_INTEGRATION drops default-layer coverage silently");
        }

        // 集成层：目标值与状态要解出来才有。**这条同时证明上面的深度跟踪没写坏。**
        // Integration layer: the objective and status require a solve. **This also proves the depth tracker is not broken.**
        depths.Should().ContainKey("WeightRecommendation_ShouldRegisterTheEqualizationDomain");
        depths["WeightRecommendation_ShouldRegisterTheEqualizationDomain"].Should().BeGreaterThan(0,
            "it asserts an objective value and Optimal status, so it cannot run without a solver");
    }

    /// <summary>按行跟踪 `#if`/`#endif`，返回每个方法声明所在处的条件编译深度 / Track `#if`/`#endif` and return each method declaration's depth.</summary>
    /// <param name="source">源文本 / The source text.</param>
    /// <returns>方法名到深度 / Method name to depth.</returns>
    private static Dictionary<string, int> MethodDepths(string source) {
        var depths = new Dictionary<string, int>();
        int depth = 0;
        int declared = 0;
        foreach (string line in source.Split(['\n'])) {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("#if", StringComparison.Ordinal)) {
                ++depth;
                continue;
            }
            if (trimmed.StartsWith("#endif", StringComparison.Ordinal)) {
                --depth;
                continue;
            }
            // `public void xxx(` 正好是本文件里每条判据的声明形态。
            // `public void xxx(` is exactly how every criterion in this file is declared.
            const string marker = "public void ";
            int at = trimmed.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) {
                continue;
            }
            int open = trimmed.IndexOf('(', at + marker.Length);
            if (open < 0) {
                continue;
            }
            string name = trimmed[(at + marker.Length)..open];
            if (name.Contains(' ', StringComparison.Ordinal)) {
                continue;
            }
            depths[name] = depth;
            ++declared;
        }
        if (declared == 0) {
            throw new InvalidOperationException("没有识别到任何方法声明 / no method declaration was recognised");
        }
        return depths;
    }

    /// <summary>读取被判据钉住的那份测试文件的源文本 / Read the source of the guarded test file.</summary>
    /// <returns>源文本 / The source text.</returns>
    private static string LayerCoverageSource() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("找不到 ospf-csharp.sln / cannot locate ospf-csharp.sln");
        }
        return File.ReadAllText(Path.Combine(
            directory.FullName, "tests", "Example.Tests", "FrameworkDemo", "Demo2WeightRecommendationPipelineTests.cs"));
    }

}
