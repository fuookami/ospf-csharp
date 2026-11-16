#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 先减后取绝对值，不是先取绝对值再减 / Subtract first, then take absolute values.
/// </summary>
/// <remarks>
/// 补 `L1DistanceFunction` / `LInfinityDistanceFunction`。
///
/// **两个函数只差最后一步（求和 vs 取最大），所以"逐项相减再取绝对值"共享一份。**
///
/// **这个方向错误是这里唯一真正的坑**：
/// **`|a − b|` 与 `|a| − |b|` 在 `a`、`b` 符号相反时符号相反**——
/// **而"距离非负"这条不足以分开两者**（某些输入下两者都非负）。
///
/// **They differ only in the last step (sum versus maximum), so "subtract pairwise, then absolute" is shared.**
///
/// **The one real trap is that direction**.
/// **`|a − b|` and `|a| − |b|` have opposite signs when `a` and `b` differ in sign** —
/// **and "the distance is non-negative" cannot separate them**, since both are non-negative on some inputs.
/// </remarks>
public class DistanceFunctionTests
{
    /// <summary>**L1 在符号相反的输入上仍为正** / L1 stays positive when the signs differ.</summary>
    /// <remarks>
    /// **取 `a = 3`、`b = -1`：`|a − b| = 4`，而 `|a| − |b| = 2`。**
    /// **两者都非负，所以"距离 ≥ 0"这条判据在两种实现下都绿。**
    ///
    /// **With `a = 3` and `b = -1`: `|a − b| = 4` while `|a| − |b| = 2`.**
    /// **Both are non-negative, so a "distance ≥ 0" criterion is green under either implementation.**
    /// </remarks>
    [Fact]
    public void TheL1Distance_ShouldSubtractBeforeTakingAbsoluteValues()
    {
        var x = new RealVar("l1-sign-x");
        var y = new RealVar("l1-sign-y");

        var function = new L1DistanceFunction<Flt64>(
            left: new[] { Polynomial(x, 1.0, 0.0) },
            right: new[] { Polynomial(y, 1.0, 0.0) },
            converter: IIntoValue<Flt64>.Identity,
            name: "l1-sign");

        // x = 3、y = -1 → |3 - (-1)| = 4。
        function.Evaluate(Values((x, 3.0), (y, -1.0))).Should().Be(
            new Flt64(4.0),
            "|3 - (-1)| is four, while |3| - |-1| would be two");
    }

    /// <summary>**L1 是多分量之和，不是其中之一** / L1 sums every component.</summary>
    /// <remarks>
    /// **只算第一个分量、或算成最大值，在单分量输入上都通过**——
    /// **所以必须用两个分量、且两个分量的贡献不同。**
    ///
    /// **Using only the first component, or taking the maximum, both pass on a single-component input** —
    /// **so this needs two components whose contributions differ.**
    /// </remarks>
    [Fact]
    public void TheL1Distance_ShouldSumEveryComponent()
    {
        var x1 = new RealVar("l1-sum-x1");
        var x2 = new RealVar("l1-sum-x2");
        var y1 = new RealVar("l1-sum-y1");
        var y2 = new RealVar("l1-sum-y2");

        var function = new L1DistanceFunction<Flt64>(
            left: new[] { Polynomial(x1, 1.0, 0.0), Polynomial(x2, 1.0, 0.0) },
            right: new[] { Polynomial(y1, 1.0, 0.0), Polynomial(y2, 1.0, 0.0) },
            converter: IIntoValue<Flt64>.Identity,
            name: "l1-sum");

        // 分量差分别是 2 与 5 → 和 7；若取最大则得 5。
        function.Evaluate(Values((x1, 2.0), (x2, 5.0), (y1, 0.0), (y2, 0.0))).Should().Be(
            new Flt64(7.0),
            "the components contribute 2 and 5, so the sum is seven and the maximum would be five");
    }

    /// <summary>**L∞ 取最大，不是求和** / L∞ takes the maximum, not the sum.</summary>
    /// <remarks>
    /// **同一个输入：和是 7，最大是 5。** **两条判据用同一组数、给出不同的期望值，
    /// 于是"两个函数实现成同一个东西"这件事会立刻暴露。**
    ///
    /// **Same input: the sum is seven and the maximum is five.** **Both criteria use the same numbers with different
    /// expectations, so "both functions implemented as the same thing" shows up at once.**
    /// </remarks>
    [Fact]
    public void TheLInfinityDistance_ShouldTakeTheMaximumNotTheSum()
    {
        var x1 = new RealVar("linf-x1");
        var x2 = new RealVar("linf-x2");
        var y1 = new RealVar("linf-y1");
        var y2 = new RealVar("linf-y2");

        var function = new LInfinityDistanceFunction<Flt64>(
            left: new[] { Polynomial(x1, 1.0, 0.0), Polynomial(x2, 1.0, 0.0) },
            right: new[] { Polynomial(y1, 1.0, 0.0), Polynomial(y2, 1.0, 0.0) },
            converter: IIntoValue<Flt64>.Identity,
            name: "linf");

        function.Evaluate(Values((x1, 2.0), (x2, 5.0), (y1, 0.0), (y2, 0.0))).Should().Be(
            new Flt64(5.0),
            "the largest component difference is five, while the sum would be seven");
    }

    /// <summary>**分量数不等时抛异常，而不是按短的截断** / Unequal counts throw rather than truncate.</summary>
    /// <remarks>
    /// **按短的截断会让多出来的分量静默消失，距离于是偏小**——
    /// **而"距离偏小"在任何"距离非负"或"距离有界"的判据下都不会红。**
    ///
    /// **Truncating to the shorter side silently drops components, which makes the distance smaller** —
    /// **and "too small" never goes red under any "distance ≥ 0" or "distance bounded" criterion.**
    /// </remarks>
    [Fact]
    public void BothDistances_ShouldRejectUnequalComponentCounts()
    {
        var x = new RealVar("mismatch-x");
        var y1 = new RealVar("mismatch-y1");
        var y2 = new RealVar("mismatch-y2");

        Action l1 = () => _ = new L1DistanceFunction<Flt64>(
            left: new[] { Polynomial(x, 1.0, 0.0) },
            right: new[] { Polynomial(y1, 1.0, 0.0), Polynomial(y2, 1.0, 0.0) },
            converter: IIntoValue<Flt64>.Identity);
        l1.Should().Throw<ArgumentException>("one side has one component and the other has two");

        Action linf = () => _ = new LInfinityDistanceFunction<Flt64>(
            left: new[] { Polynomial(x, 1.0, 0.0) },
            right: new[] { Polynomial(y1, 1.0, 0.0), Polynomial(y2, 1.0, 0.0) },
            converter: IIntoValue<Flt64>.Identity);
        linf.Should().Throw<ArgumentException>();
    }

    /// <summary>**零分量也被拒绝** / Zero components are rejected too.</summary>
    /// <remarks>
    /// **零分量的距离恒为 0，看起来"合法"，而它会让一个"最小化距离"的目标毫无作用。**
    ///
    /// **A zero-component distance is identically zero, which looks "legal" while making a "minimise the distance"
    /// objective do nothing.**
    /// </remarks>
    [Fact]
    public void BothDistances_ShouldRejectZeroComponents()
    {
        Action l1 = () => _ = new L1DistanceFunction<Flt64>(
            left: Array.Empty<LinearPolynomial<Flt64>>(),
            right: Array.Empty<LinearPolynomial<Flt64>>(),
            converter: IIntoValue<Flt64>.Identity);
        l1.Should().Throw<ArgumentException>("the distance of nothing is not a distance");

        Action linf = () => _ = new LInfinityDistanceFunction<Flt64>(
            left: Array.Empty<LinearPolynomial<Flt64>>(),
            right: Array.Empty<LinearPolynomial<Flt64>>(),
            converter: IIntoValue<Flt64>.Identity);
        linf.Should().Throw<ArgumentException>();
    }

    /// <summary>**两个距离都暴露每个分量的绝对差作为内层函数** / Both expose their per-component absolutes.</summary>
    /// <remarks>
    /// **L∞ 比 L1 多一个内层（取最大），L1 的内层数正好等于分量数。**
    /// **这条钉住"L∞ 真的把最大值那一步也登记进去了"**——
    /// **否则它的辅助变量与约束会缺一条，而评估值仍然正确。**
    ///
    /// **L∞ has one more inner function than L1 (the maximum), and L1's count equals the component count.**
    /// **This pins that L∞ really registers the maximum step** — **otherwise its helpers and constraints would be
    /// missing one while its evaluated value stayed correct.**
    /// </remarks>
    [Fact]
    public void TheInnerFunctions_ShouldCountTheMaximumForLInfinityOnly()
    {
        var x1 = new RealVar("inner-x1");
        var x2 = new RealVar("inner-x2");
        var y1 = new RealVar("inner-y1");
        var y2 = new RealVar("inner-y2");

        var l1 = new L1DistanceFunction<Flt64>(
            left: new[] { Polynomial(x1, 1.0, 0.0), Polynomial(x2, 1.0, 0.0) },
            right: new[] { Polynomial(y1, 1.0, 0.0), Polynomial(y2, 1.0, 0.0) },
            converter: IIntoValue<Flt64>.Identity,
            name: "inner-l1");
        var linf = new LInfinityDistanceFunction<Flt64>(
            left: new[] { Polynomial(x1, 1.0, 0.0), Polynomial(x2, 1.0, 0.0) },
            right: new[] { Polynomial(y1, 1.0, 0.0), Polynomial(y2, 1.0, 0.0) },
            converter: IIntoValue<Flt64>.Identity,
            name: "inner-linf");

        l1.InnerFunctions.Should().HaveCount(2, "two components, two absolutes");
        linf.InnerFunctions.Should().HaveCount(3, "two absolutes plus the maximum");
    }

    /// <summary>造一个 <c>scale·variable + constant</c> / Build `scale*variable + constant`.</summary>
    private static LinearPolynomial<Flt64> Polynomial(RealVar variable, double scale, double constant) =>
        new(new[] { new LinearMonomial<Flt64>(new Flt64(scale), variable) }, new Flt64(constant));

    /// <summary>把若干 (变量, 值) 打包成评估字典 / Pack (variable, value) pairs into an evaluation dictionary.</summary>
    private static IReadOnlyDictionary<Fuookami.Ospf.Math.Symbol.ISymbol, Flt64> Values(
        params (RealVar Variable, double Value)[] pairs) {
        var values = new Dictionary<Fuookami.Ospf.Math.Symbol.ISymbol, Flt64>();
        foreach ((RealVar variable, double value) in pairs) {
            values[variable] = new Flt64(value);
        }

        return values;
    }
}
