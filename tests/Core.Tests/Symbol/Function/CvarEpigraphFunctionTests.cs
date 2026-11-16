#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 上界式在非最优阈值处严格大于 CVaR / The upper bound strictly exceeds the CVaR off the optimum.
/// </summary>
/// <remarks>
/// 对齐基线 `Cvar.kt` 的 `CvarEpigraphFunction`。
///
/// **`ResultPolynomial` 不是 CVaR，而是它的一个上界**——
/// **只在最优阈值处取等号，其余处严格大于。**
///
/// adds
///
/// **`ResultPolynomial` is not the CVaR but an upper bound on it** —
/// **equality only at the optimal threshold, strictly greater elsewhere.**
/// </remarks>
public class CvarEpigraphFunctionTests
{
    /// <summary>
    /// **非最优阈值处上界式严格大于 CVaR** / Off the optimum the bound strictly exceeds the CVaR.
    /// </summary>
    /// <remarks>
    /// **这是"上界"这个词唯一的可测含义。**
    /// **四条损失 `−4, −2, 0, 2`、等概率、`α = 0.5` 时：**
    ///
    /// **CVaR = 在四个阈值里取最小值。`θ = 2` 时 `max(0, Lᵢ − 2)` 全为 0，上界式给 `2`——
    /// 而真实 CVaR 远小于 2。**
    ///
    /// **所以"把 `ResultPolynomial` 当 CVaR 用"会高估**，而它必须进目标被最小化。
    ///
    /// **This is the only measurable meaning of "upper bound".**
    /// **With losses `−4, −2, 0, 2`, equal probabilities and `α = 0.5`:**
    /// **the CVaR is the minimum over the four thresholds, while at `θ = 2` every `max(0, Lᵢ − 2)` is zero and the
    /// bound gives `2` — far above the true CVaR.**
    /// **So "use `ResultPolynomial` as the CVaR" overestimates, and it must be minimised.**
    /// </remarks>
    [Fact]
    public void TheResultPolynomial_ShouldBeAnUpperBoundNotTheCvar()
    {
        CvarEpigraphFunction<Flt64> function = Build();
        IReadOnlyDictionary<ISymbol, Flt64> values = Values();

        double cvar = function.Evaluate(values)!.Value.ToDouble();

        // 阈值取 2（最高损失）：max(0, Lᵢ − 2) 全为 0，于是上界式 = 2。
        // With the threshold at two (the largest loss) every excess is zero, so the bound is two.
        double atWorstThreshold = BoundAt(function, threshold: 2.0);
        atWorstThreshold.Should().BeApproximately(2.0, 1e-12,
            "at the highest threshold every excess is zero");
        atWorstThreshold.Should().BeGreaterThan(cvar,
            "the bound is an upper bound and this threshold is not optimal");
    }

    /// <summary>**最优阈值处取等号** / Equality holds at the optimal threshold.</summary>
    /// <remarks>
    /// **这条钉住"上界确实是紧的"**：**只证明"大于等于"而不能达到，就只是"一个大数"，与 CVaR 无关。**
    ///
    /// **This pins that the bound is tight**: **a bound that is only "greater or equal" and never attained is just a
    /// large number, unrelated to the CVaR.**
    /// </remarks>
    [Fact]
    public void TheResultPolynomial_ShouldAttainTheCvarAtTheOptimalThreshold()
    {
        CvarEpigraphFunction<Flt64> function = Build();
        double cvar = function.Evaluate(Values())!.Value.ToDouble();

        // 逐个阈值试，必须存在一个恰好等于 CVaR。
        // Try every threshold: one must equal the CVaR exactly.
        double best = double.PositiveInfinity;
        foreach (double threshold in new[] { -4.0, -2.0, 0.0, 2.0 }) {
            double bound = BoundAt(function, threshold);
            if (bound < best) {
                best = bound;
            }
        }

        best.Should().BeApproximately(cvar, 1e-12,
            "the minimum of the bound over thresholds is the CVaR");
    }

    /// <summary>**`Evaluate` 与 `CvarFunction` 给出同一个值** / Both classes agree on the value.</summary>
    /// <remarks>
    /// **两者的语义定义是同一个式子，区别只在编码方式**——
    /// **如果两者不等，说明其中一个的编码改变了语义。**
    ///
    /// **Both share one semantic definition; only the encoding differs** —
    /// **a disagreement means one encoding changed the meaning.**
    /// </remarks>
    [Fact]
    public void TheEvaluate_ShouldAgreeWithCvarFunction()
    {
        CvarEpigraphFunction<Flt64> epigraph = Build();
        CvarFunction<Flt64> plain = new(
            losses: Losses(),
            probabilities: Probabilities(),
            alpha: new Flt64(0.5),
            converter: IIntoValue<Flt64>.Identity,
            name: "plain");

        epigraph.Evaluate(Values())!.Value.ToDouble().Should().BeApproximately(
            plain.Evaluate(Values())!.Value.ToDouble(), 1e-12,
            "the epigraph and the plain form define the same quantity");
    }

    /// <summary>**`alpha = 0` 时 CVaR 就是平均值** / At `alpha = 0` the CVaR is the mean.</summary>
    /// <remarks>
    /// **这是一个可以手算的锚点**：**`α = 0` 时尾部是全部情景，CVaR 退化成期望值。**
    /// **而 `α = 0.5` 时它退化成最坏一半的平均。**
    /// **两个锚点都比"某个数看着合理"有力。**
    ///
    /// **This is a hand-computable anchor**: **at `α = 0` the tail is every scenario and the CVaR degenerates to the
    /// expectation.** **At `α = 0.5` it becomes the mean of the worst half.** **Both beat "some number looks
    /// plausible".**
    /// </remarks>
    [Fact]
    public void TheEvaluate_ShouldDegenerateToTheMeanAtAlphaZero()
    {
        CvarEpigraphFunction<Flt64> function = new(
            losses: Losses(),
            probabilities: Probabilities(),
            alpha: Flt64.Zero,
            converter: IIntoValue<Flt64>.Identity,
            name: "at_zero");

        function.Evaluate(Values())!.Value.ToDouble().Should().BeApproximately(
            -1.0, 1e-12, "the mean of minus four, minus two, zero and two is minus one");
    }

    /// <summary>**每个情景一条行，且系数是 `Lᵢ` 的、`−θ`、`−uᵢ`** / One row per scenario.</summary>
    [Fact]
    public void TheExcessRows_ShouldHaveOnePerScenario()
    {
        CvarEpigraphFunction<Flt64> function = Build();
        IReadOnlyList<LinearInequality<Flt64>> rows = function.ExcessRows();

        rows.Should().HaveCount(4, "one row per scenario");
        for (int index = 0; index < rows.Count; ++index) {
            rows[index].Comparison.Should().Be(Comparison.LE);
            rows[index].Name.Should().EndWith($"excess_lb_{index}");
            rows[index].Rhs.Constant.ToDouble().Should().BeApproximately(0.0, 1e-12);
        }
    }

    /// <summary>**`uᵢ` 由变量范围承担非负，不另写行** / `uᵢ ≥ 0` comes from the range, not a row.</summary>
    /// <remarks>
    /// **少一条行而语义完全一样**：**这既是 `URealVar` 的用处，也是"行数与情景数相等"的原因。**
    /// **如果为非负另写行，行数会翻倍——而判据会数出 8 而不是 4。**
    ///
    /// **One row fewer with the same meaning**: **that is what the `URealVar` is for, and why the row count equals
    /// the scenario count.** **Writing extra non-negativity rows would double it, and the criterion would count eight
    /// rather than four.**
    /// </remarks>
    [Fact]
    public void TheExcessVars_ShouldBeNonNegativeByTheirRange()
    {
        CvarEpigraphFunction<Flt64> function = Build();

        function.ExcessVars.Should().HaveCount(4);
        foreach (URealVar excess in function.ExcessVars) {
            excess.Range.LowerBound.Should().NotBeNull();
            excess.Range.LowerBound!.Value.Unwrap().ToDouble().Should().BeGreaterThanOrEqualTo(0.0,
                "a URealVar is non-negative by construction");
        }

        function.ExcessRows().Should().HaveCount(4, "no extra non-negativity rows");
    }

    /// <summary>**阈值变量的范围覆盖所有情景** / The threshold covers every scenario.</summary>
    /// <remarks>
    /// **窄了会把最优阈值排除在外，于是最优解落在边界上、结果偏大，而模型仍然可行。**
    ///
    /// **Narrower would exclude the optimal threshold, so the optimum lands on a boundary, the result is too large
    /// and the model stays feasible.**
    /// </remarks>
    [Fact]
    public void TheThresholdVar_ShouldCoverEveryScenario()
    {
        CvarEpigraphFunction<Flt64> function = Build();

        function.ThresholdVar.Range.LowerBound.Should().NotBeNull();
        function.ThresholdVar.Range.UpperBound.Should().NotBeNull();

        // 损失是 −4、−2、0、2，所以阈值范围必须是 [−4, 2]。
        // The losses are -4, -2, 0, 2, so the threshold range must be [-4, 2].
        function.ThresholdVar.Range.LowerBound!.Value.Unwrap().ToDouble().Should()
            .BeApproximately(-4.0, 1e-12, "the smallest loss bounds the threshold from below");
        function.ThresholdVar.Range.UpperBound!.Value.Unwrap().ToDouble().Should()
            .BeApproximately(2.0, 1e-12, "the largest loss bounds it from above");
    }

    /// <summary>**`alpha = 1` 被拒绝** / `alpha = 1` is rejected.</summary>
    [Fact]
    public void TheEpigraph_ShouldRejectAlphaOne()
    {
        Action atOne = () => _ = new CvarEpigraphFunction<Flt64>(
            losses: Losses(),
            probabilities: Probabilities(),
            alpha: Flt64.One,
            converter: IIntoValue<Flt64>.Identity);
        atOne.Should().Throw<ArgumentException>("the denominator would be zero");

        Action negative = () => _ = new CvarEpigraphFunction<Flt64>(
            losses: Losses(),
            probabilities: Probabilities(),
            alpha: new Flt64(-0.1),
            converter: IIntoValue<Flt64>.Identity);
        negative.Should().Throw<ArgumentException>();
    }

    /// <summary>**概率之和必须为 1** / The probabilities must sum to one.</summary>
    [Fact]
    public void TheEpigraph_ShouldRejectProbabilitiesThatDoNotSumToOne()
    {
        Action skewed = () => _ = new CvarEpigraphFunction<Flt64>(
            losses: Losses(),
            probabilities: new[] {
                new Flt64(0.25), new Flt64(0.25), new Flt64(0.25), new Flt64(0.26),
            },
            alpha: new Flt64(0.5),
            converter: IIntoValue<Flt64>.Identity);
        skewed.Should().Throw<ArgumentException>("a weighted average needs weights summing to one");
    }

    /// <summary>**行数等于情景数** / Registration writes one row per scenario.</summary>
    [Fact]
    public async Task TheRegisterConstraints_ShouldWriteOneRowPerScenario()
    {
        CvarEpigraphFunction<Flt64> function = Build();
        // **损失是常数，没有 monomial**：**`Monomials[0]` 会越界**，
        // 所以从 `InputSymbols` 取（本类实现了 `IHasResultPolynomial`，它不为 `null`）。
        //
        // **The losses are constants with no monomials**, so `Monomials[0]` is out of range;
        // take them from `InputSymbols` (this class implements `IHasResultPolynomial`, so it is not `null`).
        var declared = new List<IVariableItem>();
        foreach (ISymbol symbol in function.InputSymbols) {
            declared.Add((IVariableItem)symbol);
        }

        declared.AddRange(function.HelperVariables);

        using var parent = new LinearMetaModel<Flt64>("cvar-epigraph");
        parent.Add(declared).IsOk.Should().BeTrue();
        LinearMechanismModel<Flt64> model =
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        var mechanism = (IAbstractLinearMechanismModel<Flt64>)model;
        int before = mechanism.Constraints.Count;
        function.RegisterConstraints(model).IsOk.Should().BeTrue();
        int after = mechanism.Constraints.Count;

        (after - before).Should().Be(4, "one excess row per scenario and nothing else");
    }

    private static CvarEpigraphFunction<Flt64> Build() =>
        new(
            losses: Losses(),
            probabilities: Probabilities(),
            alpha: new Flt64(0.5),
            converter: IIntoValue<Flt64>.Identity,
            name: "epigraph");

    /// <summary>四条损失：`−4`、`−2`、`0`、`2` / Four losses.</summary>
    private static IReadOnlyList<LinearPolynomial<Flt64>> Losses() => new[] {
        Constant(-4.0), Constant(-2.0), Constant(0.0), Constant(2.0),
    };

    private static IReadOnlyList<Flt64> Probabilities() => new[] {
        new Flt64(0.25), new Flt64(0.25), new Flt64(0.25), new Flt64(0.25),
    };

    /// <summary>
    /// 在给定阈值处算上界式 / Evaluate the bound at a fixed threshold.
    /// </summary>
    /// <remarks>
    /// **直接按定义算 `θ + Σ pᵢ·max(0, Lᵢ − θ)/(1−α)`**——
    /// **不走 `ResultPolynomial`，因为那需要求解器给出 `uᵢ` 的值。**
    /// **这正是"按语义验证"与"按编码验证"的分工。**
    ///
    /// **Compute `θ + Σ pᵢ·max(0, Lᵢ − θ)/(1−α)` directly from the definition** —
    /// **not through `ResultPolynomial`, which would need the solver to supply the `uᵢ`.**
    /// **That is the split between "verify by semantics" and "verify by encoding".**
    /// </remarks>
    private static double BoundAt(CvarEpigraphFunction<Flt64> function, double threshold) {
        double[] losses = { -4.0, -2.0, 0.0, 2.0 };
        double denominator = 1.0 - function.Alpha.ToDouble();
        double total = threshold;

        for (int index = 0; index < losses.Length; ++index) {
            double excess = losses[index] - threshold;
            if (excess > 0) {
                total += function.Probabilities[index].ToDouble() * excess / denominator;
            }
        }

        return total;
    }

    private static LinearPolynomial<Flt64> Constant(double value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value));

    private static IReadOnlyDictionary<ISymbol, Flt64> Values() =>
        new Dictionary<ISymbol, Flt64>();
}
