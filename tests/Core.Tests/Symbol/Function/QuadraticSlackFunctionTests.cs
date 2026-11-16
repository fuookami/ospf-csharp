#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 松弛是无符号的，两侧都要算 / Slack is unsigned, so both sides must be computed.
/// </summary>
/// <remarks>
/// 对齐、 补三个类。
///
/// **只取一侧会在半个定义域上恒为 0，且不报错。**
///
/// **Keeping one side makes the value identically zero over half the domain, silently.**
/// </remarks>
public class QuadraticSlackFunctionTests
{
    /// <summary>**两个多项式之间的差是无符号的** / The gap between two polynomials is unsigned.</summary>
    /// <remarks>
    /// **`l = x`、`r = 0`，所以差是 `|x|`。**
    /// **只留 `max(l − r, 0)` 会让 `x = −3` 给出 `0`，而正确值是 `3`。**
    ///
    /// **`l = x` and `r = 0`, so the gap is `|x|`.**
    /// **Keeping only `max(l − r, 0)` makes `x = −3` give zero while the correct value is three.**
    /// </remarks>
    [Fact]
    public void TheSlack_ShouldBeUnsigned()
    {
        QuadraticSlackFunction function = BuildSlack();

        function.Evaluate(Values(3.0)).Should().Be(new Flt64(3.0), "three minus zero is three");
        function.Evaluate(Values(-3.0)).Should().Be(
            new Flt64(3.0), "minus three minus zero is minus three, and slack is unsigned");
        function.Evaluate(Values(0.0)).Should().Be(Flt64.Zero, "they coincide at the origin");
    }

    /// <summary>**区间内是 0，两侧都是距离** / Zero inside, distance on both sides.</summary>
    [Fact]
    public void TheSlackRange_ShouldBeZeroInsideAndDistanceOutside()
    {
        QuadraticSlackRangeFunction function = BuildSlackRange();

        // 区间是 [−2, 2]，输入是 x。
        // The interval is [-2, 2] and the input is x.
        function.Evaluate(Values(0.0)).Should().Be(Flt64.Zero, "zero lies inside");
        function.Evaluate(Values(2.0)).Should().Be(Flt64.Zero, "the upper bound itself is inside");
        function.Evaluate(Values(-2.0)).Should().Be(Flt64.Zero, "the lower bound itself is inside");
        function.Evaluate(Values(5.0)).Should().Be(new Flt64(3.0), "five exceeds the upper bound by three");
        function.Evaluate(Values(-5.0)).Should().Be(
            new Flt64(3.0), "minus five falls below the lower bound by three");
    }

    /// <summary>**只有一侧时另一半恒为 0** / One side alone leaves the other half at zero.</summary>
    /// <remarks>
    /// **这条判据专门盯"只算了一侧"**：**上界违背量在上界以下恒为 0，下界违背量在下界以上恒为 0。**
    /// **如果实现只保留一个，两半之中必有一半全错。**
    /// **所以两侧各取一个点，且两点都<b>非零</b>——**
    /// **只用 `0` 与 `2` 测（都在区间内）两个实现都会通过。**
    ///
    /// **This criterion targets "only one side was computed"**: **the upper violation is zero below the upper bound and
    /// the lower violation is zero above the lower bound.** **Keeping one makes one half entirely wrong.**
    /// **So take one point on each side, both <b>non-zero</b>** — **testing only `0` and `2` (both inside) would pass
    /// either implementation.**
    /// </remarks>
    [Fact]
    public void TheSlackRange_ShouldCoverBothHalves()
    {
        QuadraticSlackRangeFunction function = BuildSlackRange();

        double? above = function.Evaluate(Values(5.0))?.ToDouble();
        double? below = function.Evaluate(Values(-5.0))?.ToDouble();

        above.Should().Be(3.0, "the upper half must be non-zero above the interval");
        below.Should().Be(3.0, "the lower half must be non-zero below the interval");
    }

    /// <summary>**二次不等式返回 0 或 1** / The inequality yields zero or one.</summary>
    [Fact]
    public void TheInequality_ShouldYieldZeroOrOne()
    {
        QuadraticInequalityFunction function = BuildInequality();

        // 判定 x² ≤ 4，也就是 x ∈ [−2, 2]。
        // Judges x^2 <= 4, i.e. x in [-2, 2].
        function.Evaluate(Values(0.0)).Should().Be(Flt64.One, "zero squared is at most four");
        function.Evaluate(Values(2.0)).Should().Be(Flt64.One, "four is at most four");
        function.Evaluate(Values(3.0)).Should().Be(Flt64.Zero, "nine exceeds four");
        function.Evaluate(Values(-3.0)).Should().Be(Flt64.Zero, "nine exceeds four on the negative side too");
    }

    /// <summary>**倒序区间被拒绝** / Reversed bounds are rejected.</summary>
    /// <remarks>
    /// **否则"区间"是空的，两侧违背量同时为正，函数在任何点都非零——
    /// 而模型仍然可行，只是目标值莫名其妙地大。**
    ///
    /// **Otherwise the interval is empty, both violations are positive at once and the function is non-zero
    /// everywhere — while the model stays feasible and only the objective is inexplicably large.**
    /// </remarks>
    [Fact]
    public void TheSlackRange_ShouldRejectReversedBounds()
    {
        Action reversed = () => _ = new QuadraticSlackRangeFunction(
            input: Linear(),
            lower: new Flt64(2.0),
            upper: new Flt64(-2.0),
            converter: IIntoValue<Flt64>.Identity,
            name: "reversed");

        reversed.Should().Throw<ArgumentException>("an empty interval makes both violations positive at once");
    }

    /// <summary>**两个正部各自注册约束** / Both positive parts register their constraints.</summary>
    [Fact]
    public async Task TheSlack_ShouldRegisterBothHalves()
    {
        QuadraticSlackFunction function = BuildSlack();

        using var parent = new QuadraticMetaModel<Flt64>("quadratic-slack");
        var declared = new List<IVariableItem>();
        foreach (ISymbol symbol in function.InputSymbols) {
            declared.Add((IVariableItem)symbol);
        }

        declared.AddRange(function.HelperVariables);
        parent.Add(declared);

        QuadraticMechanismModel<Flt64> model =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        function.RegisterConstraints(model);

        var names = new List<string>();
        foreach (QuadraticConstraintImpl<Flt64> row in
            (IList<QuadraticConstraintImpl<Flt64>>)((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints) {
            names.Add(row.Name);
        }

        names.Should().Contain(
            n => n.Contains("_positive", StringComparison.Ordinal),
            "the positive half must register its own rows");
        names.Should().Contain(
            n => n.Contains("_negative", StringComparison.Ordinal),
            "the negative half must register its own rows");
    }

    /// <summary>**结果多项式是两部分之和** / The result polynomial is the sum of both halves.</summary>
    [Fact]
    public void TheSlack_ResultPolynomial_ShouldSumBothHalves()
    {
        QuadraticSlackFunction function = BuildSlack();

        function.ResultPolynomial.Monomials.Should().HaveCount(
            function.PositivePart.ResultPolynomial.Monomials.Count +
            function.NegativePart.ResultPolynomial.Monomials.Count,
            "the result is the sum of both halves");
    }

    /// <summary>**桥接行真的进模型** / The inequality's bridge reaches the model.</summary>
    [Fact]
    public async Task TheInequality_BridgeRow_ShouldReachTheModel()
    {
        QuadraticInequalityFunction function = BuildInequality();

        using var parent = new QuadraticMetaModel<Flt64>("quadratic-ineq");
        var declared = new List<IVariableItem>();
        foreach (ISymbol symbol in function.InputSymbols) {
            declared.Add((IVariableItem)symbol);
        }

        declared.AddRange(function.HelperVariables);
        parent.Add(declared);

        QuadraticMechanismModel<Flt64> model =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        function.RegisterConstraints(model);

        var names = new List<string>();
        foreach (QuadraticConstraintImpl<Flt64> row in
            (IList<QuadraticConstraintImpl<Flt64>>)((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints) {
            names.Add(row.Name);
        }

        names.Should().Contain(
            n => n.EndsWith("_bridge_row_0", StringComparison.Ordinal),
            "the bridge equality must be present");
    }

    private static QuadraticSlackFunction BuildSlack() =>
        new(
            left: Linear(),
            right: new QuadraticPolynomial<Flt64>(
                Array.Empty<QuadraticMonomial<Flt64>>(), Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            name: "qslack");

    private static QuadraticSlackRangeFunction BuildSlackRange() =>
        new(
            input: Linear(),
            lower: new Flt64(-2.0),
            upper: new Flt64(2.0),
            converter: IIntoValue<Flt64>.Identity,
            name: "qslackrange");

    private static QuadraticInequalityFunction BuildInequality() =>
        new(
            lhs: new QuadraticPolynomial<Flt64>(
                new[] { new QuadraticMonomial<Flt64>(Flt64.One, SharedVariable, SharedVariable) },
                Flt64.Zero),
            rhs: new Flt64(4.0),
            sign: Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE,
            converter: IIntoValue<Flt64>.Identity,
            name: "qineq");

    /// <summary>`x`，变量带范围 / `x` with a bounded variable.</summary>
    private static QuadraticPolynomial<Flt64> Linear() =>
        new(new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, SharedVariable) }, Flt64.Zero);

    private static readonly RealVar SharedVariable = CreateSharedVariable();

    private static RealVar CreateSharedVariable() {
        var variable = new RealVar("qs-x");
        variable.Range.Geq(new Flt64(-10.0));
        variable.Range.Leq(new Flt64(10.0));
        return variable;
    }

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(double value) =>
        new Dictionary<ISymbol, Flt64> { [SharedVariable] = new Flt64(value) };
}
