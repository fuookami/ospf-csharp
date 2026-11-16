#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 四种结构为什么没有投影，各自钉在它断掉的那一侧 /
/// Why four kinds have no projection, each pinned at the side where it is cut.
/// </summary>
/// <remarks>
/// **<b>结清</b>而不是"再做一点"：此前结尾说"`CapturedInputBounds` 由谁填必须回答"，
/// 这里回答它，并把四种仍未投影的结构各自断在哪一侧写成可执行事实。
///
/// **四条断点归成两类：**
///
/// - **符号没保留物化需要的东西**：`XorFunction` 没存二元输入；`MaskingFunction` 没存 `ConditionBounds`
/// - **前提捕获了却拿不到，或根本没有源**：`PWL` 的边界捕获在 `BoundDependencySnapshot` 里，
/// 而它是 `internal` 且只提供 `IsCurrent`；`IndicatorStructure` 没有对应的函数符号
///
/// **四者都不是"写一个投影就能解决"，所以它们不在派生分派里。**
///
/// **A step that <b>closes a question</b> rather than doing a little more: it ended with "who fills
/// `CapturedInputBounds` must be answered", and this answers it, pinning where each of the four unprojected kinds is cut.
///
/// **The four cuts fall into two classes: the symbol did not keep what materialisation needs, or the precondition was
/// captured but cannot be handed back (or has no source at all).**
///
/// **None is solvable by writing a projection, which is why none is in the dispatch.**
/// </remarks>
public class DeferredProjectionBlockersTests
{
    /// <summary>
    /// **`Indicator` 没有可投影的函数符号** / `Indicator` has no function symbol to project.
    /// </summary>
    /// <remarks>
    /// **这一条回答的是"为什么 Indicator 的投影不存在"：** **不是没人写，是<b>没有源</b>。
    /// 仓库里只有几个行生成器辅助类（`PositiveIndicatorRows`、`SimpleIndicatorRows`…），
    /// 没有一个 `IndicatorFunction`。**
    ///
    /// **于是 `IndicatorStructure` 只在手工构造时存在。**
    ///
    /// **This answers "why is there no Indicator projection": not nobody wrote it — there is <b>no source</b>. The repo
    /// has row-builder helpers only, and no `IndicatorFunction`.**
    ///
    /// **So `IndicatorStructure` exists only when built by hand.**
    /// </remarks>
    [Fact]
    public void Indicator_ShouldHaveNoFunctionSymbolToProject() {
        string root = RepositoryRoot();

        string[] sources = Directory.GetFiles(
            System.IO.Path.Combine(root, "src/Core/Symbol/Function"), "*.cs", SearchOption.AllDirectories);
        bool anyIndicatorFunction = sources.Any(path =>
            File.ReadAllText(path).Contains("class IndicatorFunction", StringComparison.Ordinal));
        anyIndicatorFunction.Should().BeFalse(
            "if an IndicatorFunction appears, this criterion must be flipped — the blocker is that no source exists");

 // **不用 `Select`：本仓的 `Result` 扩展也定义了一个。**
 // **Not `Select`: this repo's `Result` extensions define one too.**
        string[] files = Directory.GetFiles(
            System.IO.Path.Combine(root, "src/Core/Symbol/Function"), "*ndicator*.cs", SearchOption.AllDirectories);
        var indicators = new System.Collections.Generic.List<string>(files.Length);
        foreach (string path in files) {
            indicators.Add(System.IO.Path.GetFileName(path));
        }
        indicators.Should().NotBeEmpty(
            "there are indicator <b>row builders</b> — the absence of a symbol is the point");
        foreach (string name in indicators) {
            name.Should().NotBe(
                "IndicatorFunction.cs", "an IndicatorFunction would remove the whole blocker");
        }
    }

    /// <summary>
    /// **`MaskingFunction` 没有它自己结构需要的那个边界** /
    /// `MaskingFunction` does not carry the bounds its own structure needs.
    /// </summary>
    /// <remarks>
    /// **`MaskingStructure` 要一个 `ConditionalValue`，而 `ConditionalValue` 要 `ConditionBounds`。**
    /// **`MaskingFunction` 存了 `Input`、`Mask`、`ResultVar`，<b>没存边界</b>。**
    ///
    /// **所以投影要填那个边界，只能自己编一个。** **"编一个"与"从一个真边界捕获"的区别，
    /// 就是那条"shape matches, defence does not"的判据在防的东西。**
    ///
    /// **`MaskingStructure` needs a `ConditionalValue`, and that needs `ConditionBounds`. `MaskingFunction` stores
    /// `Input`, `Mask` and `ResultVar` but <b>no bounds</b>.**
    ///
    /// **So a projection would have to invent one — exactly what the earlier "shape matches, defence does not" criterion guards
    /// against.**
    /// </remarks>
    [Fact]
    public void Masking_ShouldNotCarryTheBoundsItsStructureNeeds() {
        string root = RepositoryRoot();

        string symbol = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Symbol/Function/Masking.cs"));
        symbol.Should().Contain("public IVariableItem Mask",
            "the mask itself is stored, which is why the absence of the bounds is specific rather than general");
        symbol.Should().NotContain("ConditionBounds",
            "if the symbol starts carrying bounds, this blocker is gone and the projection becomes possible");

        string structure = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Intermediate/MaskingStructure.cs"));
        structure.Should().Contain("ConditionalValue Value",
            "the structure requires the conditional value, which is what makes the missing bounds decisive");
    }

    /// <summary>
    /// **`PWL` 的边界捕获读不回来** / The piecewise bounds capture cannot be read back.
    /// </summary>
    /// <remarks>
    /// **这是这里最重要的一条，因为它回答的正是此前留下的那个问题：**
    ///
    /// **`CapturedInputBounds` 由谁填？** **答案：<b>没有人填，而且现在也填不了。</b>**
    ///
    /// **`UnivariateLinearPiecewiseFunction` 确实捕获了它（`_inputDependencies`），
    /// 但那个类型是 `internal` 的 `BoundDependencySnapshot`，并且只公开一个 `IsCurrent`——
    /// 它只能回答"边界还是当初那些吗"，<b>不能回答"当初是哪些"</b>。**
    ///
    /// **所以投影要填 `CapturedInputBounds`，只能拿当前范围充数——**
    /// **而那样这个"证明"就恒为真，物化器那道检查也就变成了形式。**
    ///
    /// **结论：PWL 的延迟物化对真实函数符号<b>目前不可用</b>，而不是"还没写"。**
    /// **补它要先让那个捕获能被读出（放开可见性并加一个只读出口），那会改变"快照只用于核对"
    /// 这个既有语义——所以那是单独一批的事。**
    ///
    /// **This is the most important criterion here, because it answers exactly what was left open: who fills
    /// `CapturedInputBounds`? The answer is <b>nobody, and today nobody can.</b>**
    ///
    /// **`UnivariateLinearPiecewiseFunction` does capture it (`_inputDependencies`), but that type is the `internal`
    /// `BoundDependencySnapshot` exposing only `IsCurrent` — it can answer "are the bounds still those" but <b>not "which
    /// were they"</b>.**
    ///
    /// **So a projection could only fill the proof with the current range — making the proof a tautology and the
    /// materialiser's check a formality.**
    ///
    /// **Conclusion: piecewise deferred materialisation is <b>unavailable</b> for real symbols, not merely unwritten.
    /// Restoring it means making the capture readable, which changes "the snapshot only verifies" — a batch of its own.**
    /// </remarks>
    [Fact]
    public void Piecewise_ShouldKeepItsBoundsCaptureUnreadable() {
        string root = RepositoryRoot();

        string symbol = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Symbol/Function/UnivariateLinearPiecewise.cs"));
        symbol.Should().Contain("_inputDependencies",
            "the symbol does capture the input bounds — the blocker is that they cannot be read back");

        string snapshot = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Symbol/Function/BoundDependencySnapshot.cs"));
        snapshot.Should().Contain("internal sealed class BoundDependencySnapshot",
            "widening this is the first step of unblocking piecewise; while it is internal no projection can fill the proof");
        snapshot.Should().NotContain("public LinearPolynomialBounds",
            "there is no read-only outlet for the captured bounds; adding one changes what the snapshot means");

        string structure = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Intermediate/UnivariateLinearPiecewiseStructure.cs"));
        structure.Should().Contain("has no input bounds proof",
            "the structure still refuses a missing proof, so a projection cannot skip it");
    }

    /// <summary>
    /// **四种仍然不在派生分派里，而分派里恰好六种** / Four kinds are still not dispatched, and exactly six are.
    /// </summary>
    /// <remarks>
    /// **数目字写死在这里，是为了让"加了一种"与"撤了一种"都必须经过这个文件——
    /// 而带上名字，是为了让失败信息自己说清是哪一种变了。**
    ///
    /// **The count and the names are both pinned here so that adding or removing a kind must pass through this file, and so a
    /// failure names which kind changed.**
    /// </remarks>
    [Fact]
    public void TheDispatch_ShouldHoldSixKindsAndNoneOfTheFour() {
        string root = RepositoryRoot();
        string model = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Mechanism/MechanismModel.cs"));

        string[] projected = ["AbsFunction", "ExtremumFunctionBase", "SemiFunction", "AndFunction", "OrFunction", "NotFunction"];
        foreach (string kind in projected) {
            model.Should().Contain($"case {kind}", $"'{kind}' is projected and dropping it must not pass silently");
        }

        string[] blocked = ["XorFunction", "MaskingFunction", "UnivariateLinearPiecewiseFunction", "IndicatorStructure"];
        foreach (string kind in blocked) {
            model.Should().NotContain(
                $"case {kind}",
                $"'{kind}' is blocked on its own side (see the other criteria in this class) and must not be projected "
                + "until that blocker is gone");
        }
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
