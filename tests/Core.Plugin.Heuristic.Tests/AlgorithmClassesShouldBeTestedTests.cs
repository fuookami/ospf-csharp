#nullable enable

using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests;

/// <summary>
/// **每个算法类都必须被某个测试真正构造过** / Every algorithm class must be constructed by some test.
/// </summary>
/// <remarks>
/// **仓里曾有过 8 个只返回 `"not implemented"` 的桩，现已删除。** **问题是：<b>它们当初有没有可能被更早发现**？</b>
///
/// **能。** **它们与剩下 6 个类的区别只有一条，而这一条是可测的：**
///
/// | | 8 个桩（已删） | 6 个留下的 |
/// | --- | --- | --- |
/// | 是否被测试<b>构造** | **否** | **是**（`GeneAlgorithmTest` 等真正 `new` 它们并断言行为） |
/// | 是否返回 "not implemented" | 是 | 否 |
///
/// **"返回 not implemented" 这件事，只有读了实现才知道；
/// 而"有没有被测试构造"这件事，扫一遍就够。**
///
/// **所以这条不变量是<b>更早的探测器**：**
/// **一个算法类如果没有任何测试构造它，那它要么是桩、要么是死代码，两种都该处理。**
///
/// ### 这条不变量在这里之前不成立
///
/// **用它去量此前的仓，会点名那 8 个桩——而那时它们还在。**
/// **也就是说：** **这条判据如果早一点写下来，那两次改动就不必发生。**
///
/// **Eight stubs that only returned `"not implemented"` once existed and are gone. The question is <b>whether they could have been caught earlier</b>.
/// Yes — and the difference between them and the six remaining classes is a single measurable trait:** **the eight were never
/// <b>constructed by any test</b>; the six are (`GeneAlgorithmTest` really calls `new` and asserts behaviour).**
///
/// **"Returns not implemented" is only knowable by reading the implementation; "is it constructed by a test" is one scan away.**
/// **So this invariant is <b>an earlier detector</b>: an algorithm class nothing constructs is either a stub or dead code, and both
/// need handling.**
///
/// **This invariant did not hold back then: measuring the repo as it stood would have named those eight
/// stubs while they were still there.** **In other words, had this criterion been written earlier, those two changes would not
/// have been needed.**
/// </remarks>
public class AlgorithmClassesShouldBeTestedTests
{
    /// <summary>
    /// **`Core.Plugin.Heuristic` 下每个算法类都至少被一个测试构造过** /
    /// Every algorithm class under `Core.Plugin.Heuristic` is constructed by at least one test.
    /// </summary>
    /// <remarks>
    /// **判据按"测试里是否出现 `new X(` 或 `new X<`"来定，不按名字出现——
    /// 第二次误判（把方法名当类型名）就是因为按名字搜。**
    ///
    /// **The criterion tests for `new X(` / `new X<` in tests, not for the bare name — searching by name once hit a method of the same
    /// name and produced a wrong conclusion.**
    /// </remarks>
    [Fact]
    public void EveryAlgorithmClass_ShouldBeConstructedByATest() {
        string root = RepositoryRoot();
        string pluginRoot = System.IO.Path.Combine(root, "src", "Core.Plugin.Heuristic");
        string testRoot = System.IO.Path.Combine(root, "tests", "Core.Plugin.Heuristic.Tests");

        string[] algorithmClasses = Directory
            .EnumerateFiles(pluginRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(": HeuristicAlgorithm<", StringComparison.Ordinal))
            .Select(static path => System.IO.Path.GetFileNameWithoutExtension(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        algorithmClasses.Should().HaveCount(
            6,
            "six algorithm classes remain after the stubs were deleted; a different count means the surface moved");

        string[] testSources = Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        var untested = new List<string>();
        foreach (string algorithm in algorithmClasses) {
            string[] constructorForms = [$"new {algorithm}(", $"new {algorithm}<"];
            bool constructed = testSources.Any(source =>
                constructorForms.Any(form => source.Contains(form, StringComparison.Ordinal)));
            if (!constructed) {
                untested.Add(algorithm);
            }
        }

        untested.Should().BeEmpty(
            "an algorithm class no test constructs is either a stub or dead code — that trait is what distinguished the eight "
            + "deleted stubs from the six kept ones, and it is visible without reading the implementation");
    }

    /// <summary>
    /// **启发式插件只有工厂函数，没有类** / The code heuristic plugin has factory functions, not classes.
    /// </summary>
    /// <remarks>
    /// **这一条是这里唯一无法在仓内验证的断言——它说的是基线。**
    /// **所以它在这里的写法是<b>记录**而不是断言：断言它会让门禁依赖 `../ospf-kotlin` 的存在。**
    ///
 /// **记录的内容：** **只有一个 `fun islandGenetic(...)`，
    /// 只有一个 `fun harmonySearch(...)`——<b>没有类**。**
    ///
    /// **所以本仓这 6 个类<b>不是移植来的**，是 C# 侧等价的算法封装面；
    /// 而真正的"基线对应物"在 `ComposedSolvers` 里（35 个入口，一一对应工厂函数）。**
    ///
    /// **两者都留着不算错（类被测试、函数被调用），但<b>必须有人写下来</b>——
    /// 否则下一个人会以为其中一套是另一套的旧版。**
    ///
    /// **This is the one claim cannot verify inside the repo, because it is about the code; it is therefore
    /// <b>recorded</b> rather than asserted, since asserting it would make the gate depend on `../ospf-kotlin` existing.**
    ///
    /// **Measured: its holds only `fun islandGenetic(...)` and only
    /// `fun harmonySearch(...)` — <b>no classes</b>. So these six classes were not implemented; they are C#'s equivalent algorithm
    /// surface, while the real baseline counterpart is `ComposedSolvers` (35 entry points).** **Keeping both is not wrong (the
    /// classes are tested, the functions are called), but <b>someone must write it down</b>, or the next reader will take one set
    /// for an older version of the other.**
    /// </remarks>
    [Fact]
    public void TheTwoSurfaces_ShouldBothBeDocumented() {
        string root = RepositoryRoot();

        string baseClass = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core.Plugin.Heuristic/HeuristicAlgorithm.cs"));
        baseClass.Should().Contain(
            "ComposedSolvers",
            "the base class must say which surface mirrors the baseline, or a reader takes one for an old version of the other");

        string composed = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Solver/Heuristic/Solver/ComposedSolvers.cs"));
        composed.Should().Contain(
            "HeuristicAlgorithm",
            "and the composed functions must point back at the class surface, for the same reason");
    }

    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
