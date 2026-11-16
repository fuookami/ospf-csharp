#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// argmax 给的是“哪个”，不是“多少” / argmax gives which one, not how much.
/// </summary>
/// <remarks>
/// 补 ArgMaxFunction 与 ArgMinFunction（基线 OrderStatistic.kt）。
///
/// 与 max/min 的区别只有一个，而它足够让人写错：max 给值，argmax 给下标。
/// 下标是用选择器变量的加权和表达的，第 i 个选择器的系数是 i，第 0 个不参与。
///
/// They differ from max/min in exactly one way, and that is enough to get wrong: max gives a value, argmax gives an
/// index. The index is a weighted sum of the selector variables, where selector i carries coefficient i and
/// selector 0 does not participate.
/// </remarks>
public class ArgExtremumFunctionTests
{
    /// <summary>argmax 返回最大值的下标 / argmax returns the index of the largest.</summary>
    [Fact]
    public void TheArgMax_ShouldReturnTheLargestIndex()
    {
        ArgMaxFunction<Flt64> argMax = MakeArgMax();

        argMax.Evaluate(Values(1.0, 5.0, 3.0)).Should().Be(new Flt64(1),
            "the second candidate is the largest");
        argMax.Evaluate(Values(9.0, 5.0, 3.0)).Should().Be(new Flt64(0));
        argMax.Evaluate(Values(1.0, 5.0, 7.0)).Should().Be(new Flt64(2));
    }

    /// <summary>argmin 返回最小值的下标 / argmin returns the index of the smallest.</summary>
    [Fact]
    public void TheArgMin_ShouldReturnTheSmallestIndex()
    {
        ArgMinFunction<Flt64> argMin = MakeArgMin();

        argMin.Evaluate(Values(1.0, 5.0, 3.0)).Should().Be(new Flt64(0));
        argMin.Evaluate(Values(9.0, 5.0, 3.0)).Should().Be(new Flt64(2),
            "the third candidate is the smallest");
    }

    /// <summary>**返回的是下标，不是值** / It returns the index, not the value.</summary>
    /// <remarks>
    /// **这是 argmax 与 max 唯一的分歧点，而它是这类函数最容易写错的地方**：
    /// **把 `Evaluate` 写成返回最大值本身，所有“看起来对”的测试都会过**——
    /// **因为候选值是 1、5、3 时，最大值 5 恰好也是个合法的下标（且刚好是正确答案）。**
    /// **所以候选值必须选成“值域与下标值域不相交”的样子。**
    ///
    /// **This is the one point where argmax and max diverge, and it is what goes wrong here**:
    /// **returning the maximum value instead of the index passes every "plausible" test** —
    /// **with candidates 1, 5, 3 the maximum 5 happens to be a legal index and even the right answer.**
    /// **So the candidates have to be chosen so that the value range and the index range do not overlap.**
    /// </remarks>
    [Fact]
    public void TheResult_ShouldBeAnIndexNotAValue()
    {
        ArgMaxFunction<Flt64> argMax = MakeArgMax();

        // 候选值是 100、200、150，最大值 200 远不是合法下标（只有 0/1/2）。
        // The candidates are 100, 200, 150, so the maximum 200 is nowhere near a legal index.
        argMax.Evaluate(Values(100.0, 200.0, 150.0)).Should().Be(new Flt64(1),
            "the answer is the index 1, not the value 200");
    }

    /// <summary>**并列时取先出现的那个，结果稳定** / Ties keep the first, deterministically.</summary>
    /// <remarks>
    /// **并列不是“随便选一个”**：**同一组输入给出不同下标，在优化里等于“结果不可复现”。**
    ///
    /// **A tie is not arbitrary**: **the same inputs yielding different indices means unreproducible results in
    /// optimisation.**
    /// </remarks>
    [Fact]
    public void TheTie_ShouldKeepTheFirstOccurrence()
    {
        ArgMaxFunction<Flt64> argMax = MakeArgMax();

        argMax.Evaluate(Values(5.0, 5.0, 5.0)).Should().Be(Flt64.Zero);
        argMax.Evaluate(Values(1.0, 5.0, 5.0)).Should().Be(new Flt64(1),
            "the first of the two maxima wins");
    }

    /// <summary>**结果多项式没有常数项** / The result polynomial has no constant term.</summary>
    /// <remarks>
    /// **下标 0 对应“所有选择器都是 0”，而它不能靠常数项表达**——
    /// **写了常数项，下标 0 就会变成那个常数。**
    ///
    /// **Index 0 means "all selectors off", which a constant term cannot express** —
    /// **a constant term would turn index 0 into that constant.**
    /// </remarks>
    [Fact]
    public void TheResultPolynomial_ShouldHaveNoConstantTerm()
    {
        ArgMaxFunction<Flt64> argMax = MakeArgMax();

        argMax.ResultPolynomial.Constant.Should().Be(Flt64.Zero,
            "index 0 is all selectors off, so the constant term must stay zero");
        argMax.ResultPolynomial.Monomials.Should().NotBeEmpty(
            "the index is expressed through the selectors, so they have to appear");
    }

    /// <summary>**第一个选择器不参与加权** / Selector zero carries no weight.</summary>
    /// <remarks>
    /// **它的系数是 0**：**下标 0 就是“它被选中”，不需要额外一项。**
    /// **把它也加进去（系数 0）会多一个零项，而零项会让某些化简逻辑做出多余的判断。**
    ///
    /// **Its coefficient is zero**: **index 0 simply means it is selected, with no extra term.**
    /// **Adding it with coefficient zero would introduce a zero term, and zero terms make simplification logic
    /// work harder for nothing.**
    /// </remarks>
    [Fact]
    public void TheFirstSelector_ShouldCarryNoWeight()
    {
        ArgMaxFunction<Flt64> argMax = MakeArgMax();

        argMax.ResultPolynomial.Monomials.Should().HaveCount(2,
            "three candidates mean three selectors, and the first carries no weight");
        argMax.ResultPolynomial.Monomials.Should().NotContain(
            term => ReferenceEquals(term.Symbol, argMax.SelectorVars[0]),
            "selector zero is the zero index, not a weighted term");
    }

    /// <summary>**少于一个候选要拒绝** / Fewer than one candidate is rejected.</summary>
    [Fact]
    public void TheEmptyCandidates_ShouldBeRejected()
    {
        Action empty = () => new ArgMaxFunction<Flt64>(
            polynomials: Array.Empty<LinearPolynomial<Flt64>>(),
            converter: IIntoValue<Flt64>.Identity);

        empty.Should().Throw<ArgumentException>(
            "argmax over nothing has no index to return");
    }

    /// <summary>造一个三候选的 argmax / Build a three-candidate argmax.</summary>
    private static ArgMaxFunction<Flt64> MakeArgMax() => new(
        polynomials: Candidates(),
        converter: IIntoValue<Flt64>.Identity);

    /// <summary>造一个三候选的 argmin / Build a three-candidate argmin.</summary>
    private static ArgMinFunction<Flt64> MakeArgMin() => new(
        polynomials: Candidates(),
        converter: IIntoValue<Flt64>.Identity);

    /// <summary>三个候选，各是一个变量 / Three candidates, each one variable.</summary>
    private static LinearPolynomial<Flt64>[] Candidates() => new[] {
        Single(A, 1.0),
        Single(B, 1.0),
        Single(C, 1.0),
    };

    private static LinearPolynomial<Flt64> Single(RealVar variable, double coefficient) => new(
        new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), variable) },
        Flt64.Zero);

    private static Dictionary<ISymbol, Flt64> Values(double a, double b, double c) => new() {
        [A] = new Flt64(a),
        [B] = new Flt64(b),
        [C] = new Flt64(c),
    };

    private static readonly RealVar A = new("a");
    private static readonly RealVar B = new("b");
    private static readonly RealVar C = new("c");
}
