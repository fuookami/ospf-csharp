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
/// 极差是 `max − min`，不是"两两之差的最大值" / The range is `max - min`.
/// </summary>
/// <remarks>
/// 对齐的 `RangeFunction`。
///
/// **两者在只有两个输入时相同，三个及以上时才分得开。**
///
/// **The two agree for two inputs and only separate from three upward.**
/// </remarks>
public class RangeFunctionTests
{
    /// <summary>**三个输入时极差是 `max − min`** / With three inputs the range is `max - min`.</summary>
    /// <remarks>
    /// **取值 `[0, 1, 10]`：极差是 `10 − 0 = 10`。**
    /// **而"两两之差的最大值"也是 `10`——这个取值分不开两者。**
    ///
    /// **真正分开两者的是"中间值夹在两端之间"的平凡情形与"只看相邻"的错误实现**：
    /// **如果实现成"首尾相减"，`[0, 1, 10]` 恰好也给 10；**
    /// **所以还需要一个首尾<b>不是</b>极值的取值。**
    ///
    /// **Values `[0, 1, 10]`: the range is `10 - 0 = 10`.** **"The largest pairwise difference" is also `10`, so this
    /// choice cannot separate the two.** **What does separate them is an implementation that subtracts the first from
    /// the last**, **so a case where the extremes are not the endpoints is needed too.**
    /// </remarks>
    [Fact]
    public void TheRange_ShouldBeMaximumMinusMinimum()
    {
        RangeFunction<Flt64> function = Build(new[] { 0.0, 1.0, 10.0 });

        function.Evaluate(Values()).Should().Be(new Flt64(10.0), "ten minus zero is ten");
    }

    /// <summary>
    /// **极值不在首尾时也正确** / Correct when the extremes are not the endpoints.
    /// </summary>
    /// <remarks>
    /// **这是唯一能分开"真极差"与"首尾相减"的取值。**
    ///
    /// **取值 `[10, 0, 3]`：极差是 `10 − 0 = 10`，而首尾相减给 `|3 − 10| = 7`。**
    /// **两者相差 3，不会被舍入吞掉。**
    ///
    /// **This is the only choice that separates a real range from "last minus first".**
    ///
    /// **Values `[10, 0, 3]`: the range is `10 - 0 = 10`, while last minus first gives `|3 - 10| = 7`.**
    /// **They differ by three, which rounding cannot swallow.**
    /// </remarks>
    [Fact]
    public void TheRange_ShouldNotBeTheEndpointsDifference()
    {
        RangeFunction<Flt64> function = Build(new[] { 10.0, 0.0, 3.0 });

        function.Evaluate(Values()).Should().Be(
            new Flt64(10.0), "the extremes are the first two, not the endpoints");
    }

    /// <summary>**单个输入的极差是 0** / A single input has range zero.</summary>
    [Fact]
    public void TheRange_ShouldBeZeroForASingleInput()
    {
        RangeFunction<Flt64> function = Build(new[] { 7.0 });

        function.Evaluate(Values()).Should().Be(Flt64.Zero, "a single value is both its max and its min");
    }

    /// <summary>**空输入被拒绝** / Empty input is rejected.</summary>
    /// <remarks>
    /// **空集的极差没有定义**，**给 0 会让"我漏了一个输入"看起来像"极差是零"。**
    ///
    /// **The range of the empty set is undefined**, and **zero would make "I forgot an input" look like "the range is
    /// zero".**
    /// </remarks>
    [Fact]
    public void TheRange_ShouldRejectAnEmptyInput()
    {
        Action empty = () => _ = new RangeFunction<Flt64>(
            inputs: Array.Empty<LinearPolynomial<Flt64>>(),
            converter: IIntoValue<Flt64>.Identity,
            name: "empty");

        empty.Should().Throw<ArgumentException>("an empty range is undefined");
    }

    /// <summary>**结果式是 `max − min`** / The result polynomial is `max - min`.</summary>
    /// <remarks>
    /// **两个结果式各自是一组辅助变量之和，所以相减后系数应当成对出现（一正一负）。**
    /// **如果实现里漏掉了取负，两个结果式会相加，于是系数全为正。**
    ///
    /// **Each result is a sum over helper variables, so after subtracting the coefficients should come in
    /// positive/negative pairs.** **Dropping the negation would add them instead, making every coefficient positive.**
    /// </remarks>
    [Fact]
    public void TheResultPolynomial_ShouldSubtractRatherThanAdd()
    {
        RangeFunction<Flt64> function = Build(new[] { 0.0, 1.0, 10.0 });
        LinearPolynomial<Flt64> result = function.ResultPolynomial;

        result.Monomials.Should().HaveCount(
            function.Maximum.ResultPolynomial.Monomials.Count +
            function.Minimum.ResultPolynomial.Monomials.Count,
            "the result keeps both sides rather than simplifying them away");

        bool anyNegative = false;
        foreach (LinearMonomial<Flt64> monomial in result.Monomials) {
            if (monomial.Coefficient.CompareTo(Flt64.Zero) < 0) {
                anyNegative = true;
            }
        }

        anyNegative.Should().BeTrue("subtracting the minimum negates its coefficients");
    }

    /// <summary>**`max` 与 `min` 的约束都被注册** / Both sides register their constraints.</summary>
    [Fact]
    public async Task TheRegisterConstraints_ShouldWriteBothSides()
    {
        RangeFunction<Flt64> function = Build(new[] { 0.0, 1.0, 10.0 });

        using var parent = new LinearMetaModel<Flt64>("range");
        var declared = new List<IVariableItem>();
        foreach (ISymbol symbol in function.InputSymbols) {
            declared.Add((IVariableItem)symbol);
        }

        declared.AddRange(function.HelperVariables);
        parent.Add(declared);

        LinearMechanismModel<Flt64> model =
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        function.RegisterConstraints(model);

        var names = new List<string>();
        foreach (LinearConstraintImpl<Flt64> row in
            (IList<LinearConstraintImpl<Flt64>>)((IAbstractLinearMechanismModel<Flt64>)model).Constraints) {
            names.Add(row.Name);
        }

        names.Should().Contain(
            n => n.Contains("_max", StringComparison.Ordinal),
            "the maximum must register its own rows");
        names.Should().Contain(
            n => n.Contains("_min", StringComparison.Ordinal),
            "the minimum must register its own rows");
    }

    /// <summary>**输入符号就是那些多项式里的变量** / The inputs are the polynomials' symbols.</summary>
    [Fact]
    public void TheInputSymbols_ShouldBeThePolynomialsSymbols()
    {
        RangeFunction<Flt64> function = Build(new[] { 0.0, 1.0, 10.0 });

        function.InputSymbols.Should().BeEmpty("constants carry no symbols");
        function.Inputs.Should().HaveCount(3);
    }

    private static RangeFunction<Flt64> Build(double[] constants) {
        var inputs = new LinearPolynomial<Flt64>[constants.Length];
        for (int index = 0; index < constants.Length; ++index) {
            inputs[index] = new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), new Flt64(constants[index]));
        }

        return new RangeFunction<Flt64>(
            inputs: inputs,
            converter: IIntoValue<Flt64>.Identity,
            name: "range");
    }

    private static IReadOnlyDictionary<ISymbol, Flt64> Values() =>
        new Dictionary<ISymbol, Flt64>();
}
