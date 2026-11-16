#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests;

/// <summary>
/// **删掉的那 8 个死桩，以及它们为什么该被删** / The eight dead stubs that were deleted, and why.
/// </summary>
/// <remarks>
/// **发现 8 个类只返回 `ApplicationError` + `"not implemented"`，从不被构造，而<b>真实实现在组合路径上**
/// （`ComposedSolvers.HarmonySearch` = `Swarm(...)` + `HarmonySearchUpdate`，与一致）。**
///
/// **那一批把矩阵从 ◐ 改回 ✅，并把状态钉住。而那个状态其实是<b>该删**。</b>
///
/// ### 为什么这里删了它们
///
/// **因为它们唯一的可观测效果是"让读者以为这块没做"。**
///
/// **注释里写着一句话：** **"死代码的危害不是占地方，而是它会被读成'未完成'"。
/// 而当时的处置是把它<b>记录**下来——记录等于承认它该留。</b>
///
/// **真正的处置是删掉。** **删掉之后，那条风险<b>不存在了**，而不是被<b>描述**了。</b>
///
/// ### 一并删掉的那份测试
///
/// **`Stub/StubAlgorithmTest.cs` 有 8 条判据，全部断言 `result.IsFailed`。**
///
/// **它们不是影子检查——它们确实测了一件真事，而那件真事是"还没做"。**
/// **删掉实现之后，那份测试的唯一作用就消失了：它守着一个我应该删掉的东西。**
///
/// ### 判据翻转
///
/// **缺口判据在缺口闭合时必须变红、逼着改写。**
/// **这里缺口<b>由删除闭合**，所以判据现在断言"它们不在，而覆盖它们的东西在"。</b>
///
/// **Found eight classes returning `ApplicationError` + `"not implemented"`, never constructed, while the real
/// implementation lives on the composed path — exactly as its composes operators.** **That batch restored the
/// matrix to ✅ and pinned the state; but the state was <b>"delete them".</b>**
///
/// **Their only observable effect was making a reader believe the area is unfinished.** **the earlier own note said "the harm of dead
/// code is not that it takes up space — it is that it reads as 'unfinished'", and the response then was to record it — and
/// recording it is admitting it should stay.** **The actual response is deletion, after which that risk <b>does not exist</b>
/// rather than being <b>described</b>.**
///
/// **The test went with them: `Stub/StubAlgorithmTest.cs` had eight criteria asserting `result.IsFailed` — not shadow checks,
/// but tests of something real, namely that it had not been done. With the implementation gone, their only remaining function was
/// to guard something I should have deleted.**
///
/// **A gap criterion must go red and force a rewrite when the gap closes.** **Here
/// the gap closed by deletion, so it now asserts "they are absent, and what covered them is present".**
/// </remarks>
public class HeuristicDeadStubDeletionTests
{
    /// <summary>
    /// **那 8 个死桩已经不在了** / Those eight dead stubs are gone.
    /// </summary>
    /// <remarks>
    /// **判据按<b>类名**而不是按文件名判：文件名可以变，类名不会——
    /// 而"这个类型存不存在"才是这条判据要说的事。**
    ///
    /// **The criterion tests <b>type names</b>, not file names: files move, type names do not, and "does this type exist" is what
    /// this criterion must say.**
    /// </remarks>
    [Fact]
    public void TheEightDeadStubs_ShouldBeGone() {
        string root = RepositoryRoot();
        string source = ReadAllSources(System.IO.Path.Combine(root, "src"));

        string[] deleted = [
            "class EnergyValleyOptimizer", "class GerminalCenterOptimization", "class HillClimbingAlgorithm",
            "class HarmonySearch", "class NeighborhoodSearch", "class SeagullOptimizationAlgorithm",
            "class WaterCycleAlgorithm", "class WarStrategyOptimizationAlgorithm",
        ];

        foreach (string declaration in deleted) {
            source.Should().NotContain(
                declaration,
                "this parallel copy duplicated the composed path and could never succeed; "
                + "reappearing means someone rebuilt what was deliberately removed");
        }

        // **不再搜 "not implemented" 这个字符串。** **它现在是注释与文档里的正常英文短语**
        // **（`BuiltInAlgorithms.cs` 的注释就在解释"未知算法"与"未实现"的区别），**
        // **而一条会把散文也算进去的判据，与"判据被自己的说明文字弄红"是同一个毛病。**
        // **要钉的是<b>那 8 个类型不在</b>，上面八个断言已经做到了。**
        //
        // **The `"not implemented"` string is no longer searched: it is ordinary English prose in comments and docs now
        // (`BuiltInAlgorithms.cs` explains the difference between "unknown algorithm" and "not implemented"), and a criterion
        // that counts prose is the same flaw as the earlier criterion reddened by its own explanation.** **What must be pinned is
        // that those eight types are gone — the eight assertions above already do that.**
    }

    /// <summary>
    /// **覆盖它们的那 8 个组合入口都在** / The eight composed entry points that covered them are all present.
    /// </summary>
    /// <remarks>
    /// **这是翻转的<b>另一半**：只断言"删掉了"会让人以为能力也没了。**
    ///
    /// **两条一起才说出真实状态：** **那 8 个东西<b>不是能力，是复制品**；能力一直在 `ComposedSolvers` 里。**
    ///
    /// **This is the <b>other half</b>: asserting only "deleted" would read as "the capability is gone too".** **Both together
    /// state the truth: those eight were never the capability, only copies of it; the capability was in `ComposedSolvers` all
    /// along.**
    /// </remarks>
    [Fact]
    public void TheComposedEntryPoints_ShouldStillCoverThem() {
        string root = RepositoryRoot();
        string composed = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Solver/Heuristic/Solver/ComposedSolvers.cs"));

        int entryPoints = composed
            .Split('\n')
            .Count(static line => line.Contains(
                "public static IHeuristicSolver<ObjValue, V> ", StringComparison.Ordinal));

        entryPoints.Should().Be(
            35,
            "35 composed entry points is the measured surface; a different count means the list moved");

        foreach (string entry in new[] {
            "EnergyValley", "GerminalCenter", "HillClimbing", "HarmonySearch",
            "NeighborhoodSearch", "Seagull", "WaterCycle", "WarStrategy",
        }) {
            composed.Should().Contain(
                $" {entry}<",
                $"{entry} is the real implementation of what was deleted; if it vanishes the deletion had a cost");
        }
    }

    private static string ReadAllSources(string root) {
        var text = new System.Text.StringBuilder();
        foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)) {
            text.Append(File.ReadAllText(path)).Append('\n');
        }
        return text.ToString();
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
