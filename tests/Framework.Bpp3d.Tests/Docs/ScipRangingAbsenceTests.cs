#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **SCIP 没有区间，而原因是"导不进来"，不是"还没做"** /
/// SCIP has no ranges, and the reason is "not importable", not "not done yet".
/// </summary>
/// <remarks>
/// **矩阵里 `ScipFixedIntegerLpBackend.SuppliesSensitivityRanges = false` 此前写着"区间需要后端原生的 ranging 能力，
/// SCIP 这一端尚未做"。**
///
/// **"尚未做"是一个可以一直挂着的说法，而它让人以为只差一次实现。B1289 去查了到底差什么：**
///
/// **`ScipNative.cs` 一次导入 53 个 SCIP 函数（`NativeLibrary.GetExport`），
/// 其中<b>没有任何一个**与 RHS 区间、灵敏度或最优基有关。**
///
/// **SCIP 也不像 Gurobi 那样有 `SARHSLow`／`SARHSUp` 这类属性：** **它的等价做法是"把 RHS 挪一点再解一次"——
/// 而这正是 `ConstraintPerturbationPipeline` 已经在做的事。** **所以 SCIP 这一端不是"缺一块实现"，
/// 而是"<b>换一条路走，而那条路已经存在**"。**
///
/// ### 判据钉什么
///
/// **钉"导入表里没有区间函数"这件事本身：** **一旦有人把 `SCIP...Sensitivity` 之类的函数导进来，
/// 判据会红，逼着回来重新看那个 `false`——而不是让一句"尚未做"永远留着。**
///
/// **The matrix says `SuppliesSensitivityRanges = false` with the reason "ranges need a backend's native ranging capability; not done
/// for SCIP".** **"Not done yet" is a phrase that can hang forever, and it implies one implementation is all that is missing.**
/// **B1289 checked what is actually missing: `ScipNative.cs` imports 53 SCIP functions through `NativeLibrary.GetExport`, and
/// <b>none</b> of them concerns RHS ranges, sensitivity or an optimal basis.** **SCIP has no `SARHSLow`/`SARHSUp` equivalent; its
/// way is "displace the RHS and solve again", which `ConstraintPerturbationPipeline` already does — so this end is not "one
/// implementation short" but "there is another road, and it already exists".**
///
/// **The criterion pins the import table: once someone imports a `SCIP...Sensitivity`-like function it reddens and forces a second
/// look at that `false`, instead of letting "not done yet" stand forever.**
/// </remarks>
public class ScipRangingAbsenceTests
{
    /// <summary>**导入表里没有区间函数** / The import table has no range function.</summary>
    [Fact]
    public void TheScipImports_ShouldCarryNoRangingFunction() {
        string root = RepositoryRoot();
        string natives = File.ReadAllText(System.IO.Path.Combine(
            root, "src", "Core.Plugin.Scip", "ScipNative.cs"));

        string[] imported = Regex.Matches(natives, "\"(SCIP[A-Za-z]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();

        imported.Should().NotBeEmpty(
            "the SCIP backend binds its native library by export name, so the list must be non-empty");

        // **三个词各自都足够具体：`SCIPsetObjsense` 含有 "sens" 但不含 "Sensitivity"。**
        // **Each of the three words is specific enough: `SCIPsetObjsense` contains "sens" but not "Sensitivity".**
        string[] rangingLike = imported
            .Where(name => name.Contains("Sensitivity", StringComparison.OrdinalIgnoreCase)
                || name.Contains("SARHS", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Ranging", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        rangingLike.Should().BeEmpty(
            "no imported SCIP function provides RHS ranges; the moment one exists, SuppliesSensitivityRanges = false must be revisited");
    }

    /// <summary>
    /// **常量与理由必须同时在场** / The constant and the reason must both be present.
    /// </summary>
    /// <remarks>
    /// **只写 `false` 而不写为什么，下一个人只会把它当成"TODO"。**
    /// **而理由里必须点出替代路径——否则读者会以为 SCIP 上根本没有敏感性能力，
    /// 而实际上有，只是走的是重解那条路。**
    ///
    /// **A `false` without a reason reads as a TODO.** **The reason must name the alternative path, or a reader concludes SCIP has no
    /// sensitivity capability at all — while it does, through re-solving.**
    /// </remarks>
    [Fact]
    public void TheAbsence_ShouldNameItsReasonAndItsSubstitute() {
        string root = RepositoryRoot();
        string factory = File.ReadAllText(System.IO.Path.Combine(
            root, "src", "Core.Plugin.Scip", "ScipFixedIntegerLpBackend.cs"));

        factory.Should().Contain("SuppliesSensitivityRanges = false", "the capability is declared absent");
        factory.Should().Contain(
            "ConstraintPerturbationPipeline",
            "the reason must name the substitute path, or the absence reads as a TODO instead of a decision");
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