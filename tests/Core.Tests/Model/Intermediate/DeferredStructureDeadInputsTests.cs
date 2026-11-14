#nullable enable

using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 延迟结构上那些<b>没有生产者</b>的输入字段 / The structure inputs that have <b>no producer</b>.
/// </summary>
/// <remarks>
/// B1256 做的是 B1255 结尾提的那件事：查"搬过去了但没人读"的字段。
///
/// **结果比预期更糟一档：** **不是"没人读"，而是<b>没人写</b>。**
///
/// **四个字段在 `src/` 里<b>没有任何赋值</b>，只有测试会设它们：**
///
/// | 字段 | 所在结构 | 缺了会怎样 |
/// | --- | --- | --- |
/// | `CapturedInputBounds` | `UnivariateLinearPiecewiseStructure` | <b>拒绝</b>物化 |
/// | `ExplicitM` | `UnivariateLinearPiecewiseStructure` | 退回自动推导 |
/// | `CapturedInputBounds` | `MaxStructure` | <b>静默跳过</b>检查 |
/// | `CapturedInputBounds` | `IndicatorStructure` | <b>静默跳过</b>检查 |
///
/// **前两个"拒绝"是可接受的行为——它把缺口暴露出来。**
/// **后两个"静默跳过"才是危险的：** **检查写在代码里、看着像防住了，而它<b>永远不会触发</b>，
/// 因为那个字段在生产路径上恒为 null。**
///
/// **一条永不触发的检查比没有检查更糟：** **它让人以为这里有防护。**
///
/// B1256 does what B1255's closing note proposed: look for "carried over but never read" fields.
///
/// **The result is one notch worse than expected: not "never read" but <b>never written</b>.**
///
/// **Four fields have <b>no assignment</b> anywhere in `src/`; only tests set them.** The two that <b>refuse</b> are
/// acceptable — they expose the gap. **The two that <b>silently skip</b> are the dangerous ones: the check is written and
/// looks like a defence, but it can never fire because the field is always null in production.**
///
/// **A check that never fires is worse than no check: it makes people believe there is a defence.**
/// </remarks>
public class DeferredStructureDeadInputsTests
{
    private static readonly string[] Structures = [
        "src/Core/Model/Intermediate/UnivariateLinearPiecewiseStructure.cs",
        "src/Core/Model/Intermediate/MaxStructure.cs",
        "src/Core/Model/Intermediate/IndicatorStructure.cs",
    ];

    /// <summary>
    /// **这几个字段在 `src/` 里无人赋值** / None of these fields is assigned anywhere in `src/`.
    /// </summary>
    /// <remarks>
    /// **断言的是"没有生产者"，所以它在有人补上生产者时会变红——那时该做的是把这一条改写成
    /// "生产者在这里，而它的值来自 X"。**
    ///
    /// **这个方向是刻意的：** **一个描述缺口的判据应该在缺口被补上时变红，
    /// 而不是在缺口扩大时变红。**
    ///
    /// **It asserts "there is no producer", so it goes red when one is added — and then this criterion should become "the
    /// producer is here and its value comes from X".**
    ///
    /// **The direction is deliberate: a criterion describing a gap should go red when the gap closes, not when it widens.**
    /// </remarks>
    [Fact]
    public void TheProofFields_ShouldHaveNoProducerInSource() {
        string root = RepositoryRoot();
        string source = ReadAllSources(System.IO.Path.Combine(root, "src"));

        foreach (string field in new[] { "CapturedInputBounds", "ExplicitM" }) {
            source.Should().NotContain(
                $"{field} =",
                $"'{field}' has no producer today; if one appears, this criterion must be rewritten to say where the value "
                + "comes from");
        }
    }

    /// <summary>
    /// **两种失效方式分开：一个拒绝，两个静默跳过** /
    /// The two failure modes are distinct: one refuses, two skip silently.
    /// </summary>
    /// <remarks>
    /// **`UnivariateLinearPiecewiseStructure` 在证明缺失时<b>返回失败</b>，
    /// 而 `MaxStructure` 与 `IndicatorStructure` 用 `is not null` 把它绕过去。**
    ///
    /// **这个区别是这一批的核心：** **同样的字段、同样的缺失，一个显形、一个隐形。**
    /// **而"隐形"的那两个恰好是<b>已经能真实求解</b>的两个——**
    /// **也就是说，21 次真实求解全部通过，而那三次检查一次都没跑过。**
    ///
    /// **这不会让那 21 次求解变得无效：** **它们证明的是"行压出来的答案对"，
    /// 而不是"边界证明被校验过"。** **把这两件事分开说，是这一批要留下的东西。**
    ///
    /// **`UnivariateLinearPiecewiseStructure` <b>returns a failure</b> when the proof is missing, while `MaxStructure` and
    /// `IndicatorStructure` step around it with `is not null`.**
    ///
    /// **That difference is the batch's core: same field, same absence, one visible and one invisible.** **And the invisible
    /// two are exactly the ones that already solve for real — so all 21 real solves passed while those three checks never
    /// ran once.**
    ///
    /// **That does not invalidate the 21 solves: they prove "the rows force the right answer", not "the bound proof was
    /// verified".** **Keeping those two statements apart is what this batch leaves behind.**
    /// </remarks>
    [Fact]
    public void OneShouldRefuseTwoShouldSkipSilently() {
        string root = RepositoryRoot();

        string piecewise = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Intermediate/UnivariateLinearPiecewiseStructure.cs"));
        piecewise.Should().Contain("has no input bounds proof",
            "piecewise refuses: that is why its deferred road is blocked rather than quietly wrong");

        string max = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Intermediate/MaxStructure.cs"));
        max.Should().Contain("if (captured is null)",
            "max tolerates an absent proof by returning Ok at once, which is what makes its check vacuous in production");

        string indicator = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Intermediate/IndicatorStructure.cs"));
        indicator.Should().Contain("CapturedInputBounds is not null",
            "indicator tolerates it the same way; the guard exists and can never fire");
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
