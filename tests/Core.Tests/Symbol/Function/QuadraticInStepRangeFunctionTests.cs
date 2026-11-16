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
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 容差带内返回 `null`，不是猜一个 / The tolerance band returns `null`, not a guess.
/// </summary>
/// <remarks>
/// 对齐`QuadraticInStepRangeFunction` 与 `QuadraticLinearFunction` 两个空壳换成真实编码。
///
/// **区间归属是三段语义**：**在内给 `x`、明确在外给 0、容差带内给 `null`。**
/// **第三段是核心**：**把容差带并进任意一侧都会让"明确在外"失去意义。**
///
/// Replaces two stubs with real encodings, following the implementation.
///
/// **The membership is three-way**; **the third case is the point** — **folding the band into either side destroys
/// "clearly out".**
/// </remarks>
public class QuadraticInStepRangeFunctionTests
{
    /// <summary>**区间内给 `x` 本身** / In range gives the value itself.</summary>
    [Fact]
    public void TheEvaluate_ShouldReturnTheValueInsideTheRange()
    {
        QuadraticInStepRangeFunction function = Build();
        RealVar x = InputVariable(function);

        function.Evaluate(Values(function, x, 3.0)).Should().Be(
            new Flt64(3.0), "three is inside [2, 5]");
        function.Evaluate(Values(function, x, 2.0)).Should().Be(
            new Flt64(2.0), "the lower bound counts as inside");
        function.Evaluate(Values(function, x, 5.0)).Should().Be(
            new Flt64(5.0), "the upper bound counts as inside");
    }

    /// <summary>**明确在外给 0** / Clearly outside gives zero.</summary>
    [Fact]
    public void TheEvaluate_ShouldReturnZeroClearlyOutside()
    {
        QuadraticInStepRangeFunction function = Build();
        RealVar x = InputVariable(function);

        function.Evaluate(Values(function, x, 0.0)).Should().Be(
            Flt64.Zero, "zero is well below the lower bound of two");
        function.Evaluate(Values(function, x, 9.0)).Should().Be(
            Flt64.Zero, "nine is well above the upper bound of five");
    }

    /// <summary>
    /// **容差带内给 `null`，不是 0 也不是 `x`** / The tolerance band gives `null`.
    /// </summary>
    /// <remarks>
    /// **这是这里唯一真正区分"三段"与"两段"的判据。**
    ///
    /// **容差是 `1e-6`，下界是 2，所以 `1.9999995` 落在 `(2 − 1e-6, 2)` 这个带里。**
    /// **两段实现会给它 0 或 2 附近的值，只有三段实现给 `null`。**
    ///
    /// **This is the one criterion that separates "three-way" from "two-way".**
    ///
    /// **The tolerance is `1e-6` and the lower bound is two, so `1.9999995` lies in `(2 − 1e-6, 2)`.**
    /// **A two-way implementation gives zero or a value near two; only a three-way one gives `null`.**
    /// </remarks>
    [Fact]
    public void TheEvaluate_ShouldReturnNullInsideTheToleranceBand()
    {
        QuadraticInStepRangeFunction function = Build();
        RealVar x = InputVariable(function);

        function.Evaluate(Values(function, x, 1.9999995)).Should().BeNull(
            "just below the lower bound but inside the tolerance band is undefined, not zero");
        function.Evaluate(Values(function, x, 5.0000005)).Should().BeNull(
            "just above the upper bound but inside the tolerance band is undefined");
    }

    /// <summary>
    /// **容差带的边界是闭的：`lower − tol` 属于"明确在外"** / The band's outer edge is inclusive.
    /// </summary>
    /// <remarks>
    /// **`x = lower − tol` 恰好落在"明确在外"那一侧**（`<=`）。
    /// **这条钉的是容差的方向：容差让带变宽，而不是让区间变宽。**
    ///
 /// **`x = lower − tol` falls on the "clearly out" side** (this implementation uses `<=`).
    /// **This pins the direction of the tolerance: it widens the band, not the interval.**
    /// </remarks>
    [Fact]
    public void TheEvaluate_ShouldTreatTheBandEdgeAsOutside()
    {
        QuadraticInStepRangeFunction function = Build();
        RealVar x = InputVariable(function);

        // lower − tol = 2 − 1e-6。
        // lower - tol = 2 - 1e-6.
        function.Evaluate(Values(function, x, 2.0 - 1e-6)).Should().Be(
            Flt64.Zero, "the outer edge of the band is already 'clearly out'");
    }

    /// <summary>**三个状态变量互斥且完备** / The three states are exclusive and exhaustive.</summary>
    /// <remarks>
    /// **没有 `sum = 1`，三个状态可以同时为 0**——**于是 `y` 的两侧约束都被松弛，
    /// `y` 变成自由变量，而模型仍然可行。**
    ///
    /// **Without `sum = 1` the three states can all be zero** — **so both of `y`'s rows relax, `y` becomes free and
    /// the model stays feasible.**
    /// </remarks>
    [Fact]
    public void ThePartitionRow_ShouldSumToExactlyOne()
    {
        QuadraticInStepRangeFunction function = Build();
        QuadraticInequalityOf<Flt64> row = function.Rows().Single(r => r.Name.EndsWith("_partition"));

        row.Comparison.Should().Be(Comparison.EQ);
        row.Lhs.Monomials.Should().HaveCount(3, "one term per state");
        row.Rhs.Constant.ToDouble().Should().BeApproximately(1.0, 1e-12);

        foreach (QuadraticMonomial<Flt64> monomial in row.Lhs.Monomials) {
            monomial.Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-12);
        }
    }

    /// <summary>**容差只出现在 `below`/`above` 的界上，不在 `inside` 上** / The tolerance appears only on the outside rows.</summary>
    /// <remarks>
    /// **这是"容差让带变宽"的机制**：**`inside` 的界是 `lower` 与 `upper`，
    /// `below` 的界是 `lower − tol`、`above` 的界是 `upper + tol`。**
    /// **如果 `inside` 也吃容差，区间本身就变宽了，而容差带消失。**
    ///
    /// **This is how the tolerance widens the band**: **`inside` is bounded by `lower`/`upper` while `below` uses
    /// `lower − tol` and `above` uses `upper + tol`.**
    /// **Were `inside` to take the tolerance too, the interval itself would widen and the band would vanish.**
    /// </remarks>
    [Fact]
    public void TheTolerance_ShouldOnlyWidenTheOutsideRows()
    {
        QuadraticInStepRangeFunction function = Build();
        IReadOnlyList<QuadraticInequalityOf<Flt64>> rows = function.Rows();

        rows.Should().HaveCount(9);

        // **两行的右端差不是 `tol`**：**`inside_lb` 是 `lower − m`、`below` 是 `lower − tol + m`，
        // 差值是 `2m − tol`。** **所以必须先各自抵消大 M 再比较。**
        //
        // **The two right-hand sides differ by `2m − tol`, not by `tol`**: **`inside_lb` is `lower − m` and `below` is
        // `lower − tol + m`.** **So each must be un-masked before comparing.**
        double m = function.BigM.ToDouble();

        // inside_lb：右端 = lower − m → 抵消后是 lower。
        // below：右端 = lower − tol + m → 抵消后是 lower − tol。
        // inside_lb: rhs = lower - m, so unmasked is lower.
        // below: rhs = lower - tol + m, so unmasked is lower - tol.
        double insideLowerBound = rows[0].Rhs.Constant.ToDouble() + m;
        double belowBound = rows[2].Rhs.Constant.ToDouble() - m;

        insideLowerBound.Should().BeApproximately(2.0, 1e-9, "inside is bounded by the lower bound itself");
        belowBound.Should().BeApproximately(1.999999, 1e-9,
            "below is bounded by the lower bound shifted by the tolerance");

        // above：右端 = upper + tol − m → 抵消后是 upper + tol。
        // above: rhs = upper + tol - m, so unmasked is upper + tol.
        (rows[3].Rhs.Constant.ToDouble() + m).Should().BeApproximately(
            5.000001, 1e-9, "above is bounded by the upper bound shifted by the tolerance");
    }

    /// <summary>**倒序的界被拒绝** / Reversed bounds are rejected.</summary>
    /// <remarks>
    /// **倒序的界会让三段划分里没有任何区间"在内"**，
    /// **而三条状态约束照样可满足（全都落进 below 或 above）。**
    ///
    /// **Reversed bounds leave no interval "in", while the three state rows still satisfy.**
    /// </remarks>
    [Fact]
    public void TheStepRange_ShouldRejectReversedBounds()
    {
        Action reversed = () => _ = Build(lower: 5.0, upper: 2.0);
        reversed.Should().Throw<ArgumentException>("a lower bound above the upper bound is not a range");
    }

    /// <summary>**非正的容差被拒绝** / A non-positive tolerance is rejected.</summary>
    [Fact]
    public void TheStepRange_ShouldRejectANonPositiveTolerance()
    {
        Action zero = () => _ = Build(tolerance: 0.0);
        zero.Should().Throw<ArgumentException>("a zero tolerance collapses the undefined band");

        Action negative = () => _ = Build(tolerance: -1.0);
        negative.Should().Throw<ArgumentException>();
    }

    /// <summary>**九条约束真的被注册** / The nine rows really reach the model.</summary>
    [Fact]
    public async Task TheRegisterConstraints_ShouldWriteAllNineRows()
    {
        QuadraticInStepRangeFunction function = Build();
        RealVar x = InputVariable(function);

        var parent = new QuadraticMetaModel<Flt64>();
        var declared = new List<IVariableItem> { x };
        declared.AddRange(function.HelperVariables);
        parent.Add(declared).IsOk.Should().BeTrue();

        QuadraticMechanismModel<Flt64> model =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        int before = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;
        function.RegisterConstraints(model).IsOk.Should().BeTrue();

        int after = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;
        (after - before).Should().Be(9, "all nine rows must reach the model");
    }

    /// <summary>
    /// 造一个区间归属 / Build one.
    /// </summary>
    /// <remarks>
    /// **变量必须设范围**：**无界变量的 `QuadraticBounds.Of` 会推出一个天文数字的大 M**，
    /// **于是"容差把 `below` 行下移 1e-6"这个差值被 float 精度吞掉——判据自己算出一串无意义的数。**
    /// **第一版就是这样，实测差值 `9e15`。**
    ///
    /// **The variable must be bounded**: **`QuadraticBounds.Of` on an unbounded variable yields an astronomical
    /// big-M**, **so the 1e-6 shift of the `below` row drowns in float precision and the criterion computes
    /// nonsense.** **The first version did exactly that and measured `9e15`.**
    /// </remarks>
    private static QuadraticInStepRangeFunction Build(
        double lower = 2.0, double upper = 5.0, double? tolerance = null) {
        var variable = new RealVar("step-x");
        variable.Range.Geq(Flt64.Zero);
        variable.Range.Leq(new Flt64(10.0));

        return new QuadraticInStepRangeFunction(
            x: new QuadraticPolynomial<Flt64>(
                new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, variable) },
                Flt64.Zero),
            lower: new Flt64(lower),
            upper: new Flt64(upper),
            converter: IIntoValue<Flt64>.Identity,
            outsideTolerance: tolerance is { } value ? new Flt64(value) : null,
            name: "step");
    }

    private static RealVar InputVariable(QuadraticInStepRangeFunction function) =>
        (RealVar)function.X.Monomials[0].Symbol1;

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        QuadraticInStepRangeFunction function, RealVar variable, double value) =>
        new Dictionary<ISymbol, Flt64> { [variable] = new Flt64(value) };
}
