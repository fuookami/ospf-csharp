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
/// 正部是 `max(p, 0)`，不是负部 / The positive part is `max(p, 0)`, not the negative one.
/// </summary>
/// <remarks>
/// 对齐、 补两个类。
///
/// **正部的编码是 `max(p, 0) = −min(−p, 0)`**：**外层那个取负就是"正部"与"负部"的全部差别。**
///
/// **The positive part is encoded as `max(p, 0) = −min(−p, 0)`**: **the outer negation is the whole difference between
/// the positive and negative parts.**
/// </remarks>
public class QuadraticAbsFunctionTests
{
    /// <summary>**绝对值取到正值** / The absolute value comes out positive.</summary>
    [Fact]
    public void TheAbs_ShouldBeNonNegativeOnBothSides()
    {
        QuadraticAbsFunction function = BuildAbs();

        function.Evaluate(Values(3.0)).Should().Be(new Flt64(3.0), "the absolute value of three is three");
        function.Evaluate(Values(-3.0)).Should().Be(
            new Flt64(3.0), "the absolute value of minus three is also three");
    }

    /// <summary>**绝对值不是恒等** / The absolute value is not the identity.</summary>
    /// <remarks>
    /// **只测正值，`|p|` 与 `p` 无法区分。**
    /// **负值那一点才是唯一能分开两者的。**
    ///
    /// **Testing only the positive side cannot tell `|p|` from `p`.**
    /// **The negative point is the only one that separates them.**
    /// </remarks>
    [Fact]
    public void TheAbs_ShouldDifferFromTheIdentityOnTheNegativeSide()
    {
        QuadraticAbsFunction function = BuildAbs();

        function.Evaluate(Values(-3.0)).Should().NotBe(
            new Flt64(-3.0), "the absolute value of minus three is not minus three");
    }

    /// <summary>**正部在负输入处是 0** / The positive part is zero on negative input.</summary>
    /// <remarks>
    /// **这是"正部"与"负部"的分界点**：**如果忘了外层取负，这一点会给出 `3` 而不是 `0`。**
    ///
    /// **This is where the positive and negative parts diverge**: **dropping the outer negation makes this point give
    /// three rather than zero.**
    /// </remarks>
    [Fact]
    public void ThePositivePart_ShouldBeZeroOnNegativeInput()
    {
        QuadraticPositivePartFunction function = BuildPositivePart();

        function.Evaluate(Values(-3.0)).Should().Be(
            Flt64.Zero, "the positive part of a negative number is zero");
        function.Evaluate(Values(3.0)).Should().Be(
            new Flt64(3.0), "the positive part of a positive number is itself");
    }

    /// <summary>**常数项也被取负** / The constant term is negated too.</summary>
    /// <remarks>
    /// **`p = x + 5`**：**在 `x = −10` 处 `p = −5`，所以正部是 `0`。**
    /// **如果只取负了 monomial 而没取负常数项，`−p` 会变成 `−x + 5`，
    /// 于是 `min` 在 `x = −10` 处给出 `15`，取负后正部变成 `−15`——一个负数。**
    ///
    /// **`p = x + 5`**: **at `x = −10`, `p = −5`, so the positive part is zero.**
    /// **Negating only the monomials leaves `−p = −x + 5`, so the `min` gives fifteen at `x = −10` and the negated
    /// positive part becomes minus fifteen — a negative number.**
    /// </remarks>
    [Fact]
    public void ThePositivePart_ShouldNegateTheConstantTerm()
    {
        var x = new RealVar("qp-x");
        x.Range.Geq(new Flt64(-10.0));
        x.Range.Leq(new Flt64(10.0));
        var shifted = new QuadraticPolynomial<Flt64>(
            new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, x) }, new Flt64(5.0));

        var function = new QuadraticPositivePartFunction(
            input: shifted,
            converter: IIntoValue<Flt64>.Identity,
            name: "qp-shifted");

        function.NegativeInput.Constant.ToDouble().Should().BeApproximately(
            -5.0, 1e-12, "negating the input must flip the constant term as well");
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-10.0) }).Should().Be(
            Flt64.Zero, "minus ten plus five is negative, so the positive part is zero");
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.0) }).Should().Be(
            new Flt64(5.0), "zero plus five is positive, so the positive part is five");
    }

    /// <summary>**必然非负时没有选择器** / A necessarily non-negative input needs no selector.</summary>
    /// <remarks>
    /// **`p = x + 10` 在 `x ∈ [−10, 10]` 上界是 `[0, 20]`，必然非负**，
    /// **于是 `min(−p, 0) = −p` 直接成立，不需要选。**
    ///
    /// **为什么不用 `x²`**：**`QuadraticBounds.Of` 对 `x·x` 用四角法，在 `[−10, 10]` 上给出 `[−100, 100]`——
    /// 它<b>看不到</b>"平方恒非负"。** **这是真实的能力局限，不是这里引入的**，
    /// **判据因此改用界明确的线性输入来测这一分支。**
    ///
    /// **`p = x + 10` over `x ∈ [−10, 10]` has bounds `[0, 20]`, so it is necessarily non-negative**,
    /// **and `min(−p, 0) = −p` needs no selector.**
    ///
    /// **Why not `x²`**: **`QuadraticBounds.Of` uses the four-corner rule on `x·x`, giving `[−100, 100]` over
    /// `[−10, 10]` — it <b>cannot see</b> that a square is never negative.** **That is a real limitation, not one
    /// introduced here**, **so the criterion uses a linear input with an unambiguous bound instead.**
    /// </remarks>
    [Fact]
    public void ThePositivePart_ShouldDropSelectorsWhenTheSignIsFixed()
    {
        QuadraticPositivePartFunction nonNegative = BuildShiftedPositivePart(10.0);
        QuadraticPositivePartFunction signed = BuildPositivePart();

        nonNegative.InputIsNonNegative.Should().BeTrue("x plus ten is at least zero on the box");
        nonNegative.SelectorVars.Should().BeEmpty("a fixed sign needs no selector");

        signed.InputIsNonNegative.Should().BeFalse("x changes sign");
        signed.InputIsNonPositive.Should().BeFalse("x changes sign");
        signed.SelectorVars.Should().NotBeEmpty("an unfixed sign needs selectors");
    }

    /// <summary>**结果多项式是 `−resultVar`** / The result polynomial is `−resultVar`.</summary>
    /// <remarks>
    /// **`polynomial` 是 `−1 · resultVar`**：**因为 `resultVar = min(−p, 0)`，
    /// 而正部是它的相反数。** **漏掉这个负号会让结果多项式与 `ResultVar` 同号，即负部。**
    ///
    /// **`polynomial` is `−1 · resultVar`**: **since `resultVar = min(−p, 0)` and the positive part is
    /// its negation.** **Dropping the sign makes the result polynomial agree with `ResultVar`, i.e. the negative
    /// part.**
    /// </remarks>
    [Fact]
    public void ThePositivePart_ResultPolynomial_ShouldBeTheNegatedResultVar()
    {
        QuadraticPositivePartFunction function = BuildPositivePart();

        function.ResultPolynomial.Monomials.Should().HaveCount(1);
        function.ResultPolynomial.Monomials[0].Symbol.Should().BeSameAs(function.ResultVar);
        function.ResultPolynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(
            -1.0, 1e-12, "the positive part is minus the minimum");
    }

    /// <summary>**零点归成干净的零** / The origin normalises to a clean zero.</summary>
    [Fact]
    public void ThePositivePart_ShouldNormaliseZeroAtTheOrigin()
    {
        QuadraticPositivePartFunction function = BuildPositivePart();

        Flt64? atZero = function.Evaluate(Values(0.0));
        atZero.Should().NotBeNull();
        atZero!.Value.ToDouble().Should().Be(0.0);
        double.IsNegative(atZero.Value.ToDouble()).Should().BeFalse(
            "a normalised zero is not negative zero");
    }

    /// <summary>**桥接行真的进模型** / The bridge row reaches the model.</summary>
    [Fact]
    public async Task TheAbs_BridgeRow_ShouldReachTheModel()
    {
        QuadraticAbsFunction function = BuildAbs();

        using var parent = new QuadraticMetaModel<Flt64>("quadratic-abs");
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

    private static QuadraticAbsFunction BuildAbs() =>
        new(
            polynomial: Signed(),
            converter: IIntoValue<Flt64>.Identity,
            name: "qabs");

    private static QuadraticPositivePartFunction BuildPositivePart() =>
        new(
            input: Signed(),
            converter: IIntoValue<Flt64>.Identity,
            name: "qpp");

    private static QuadraticPositivePartFunction BuildShiftedPositivePart(double shift) =>
        new(
            input: new QuadraticPolynomial<Flt64>(
                new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, SharedVariable) }, new Flt64(shift)),
            converter: IIntoValue<Flt64>.Identity,
            name: "qpp-shifted-positive");

    /// <summary>`x`，变量带范围 / `x` with a bounded variable.</summary>
    private static QuadraticPolynomial<Flt64> Signed() =>
        new(new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, SharedVariable) }, Flt64.Zero);

    private static readonly RealVar SharedVariable = CreateSharedVariable();

    private static RealVar CreateSharedVariable() {
        var variable = new RealVar("qa-x");
        variable.Range.Geq(new Flt64(-10.0));
        variable.Range.Leq(new Flt64(10.0));
        return variable;
    }

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(double value) =>
        new Dictionary<ISymbol, Flt64> { [SharedVariable] = new Flt64(value) };
}
