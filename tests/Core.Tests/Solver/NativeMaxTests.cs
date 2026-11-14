#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 原生 MAX primitive：Big-M 覆盖、自引用与仿射界 / Big-M coverage, self-reference, affine bounds.
/// </summary>
/// <remarks>
/// B1205 按基线 `NativeMax.kt` 的 `NativeMaxData` / `NativeMaxInputData` / `prepareNativeMax` 补。
///
/// B1205 adds the baseline's MAX primitive.
/// </remarks>
public class NativeMaxTests
{
    /// <summary>
    /// **合格的结构转换出输入、键与 Big-M** / A valid structure converts.
    /// </summary>
    [Fact]
    public void AValidStructure_ShouldConvert() {
        Result<NativeMaxData, ErrorCode, Error<ErrorCode>> result = NativeMax.Prepare(Structure());

        result.Should().BeOfType<Ok<NativeMaxData, ErrorCode, Error<ErrorCode>>>();
        NativeMaxData data = result.Value;
        data.Inputs.Should().HaveCount(2);
        data.ResultKey.Should().Be(Result.Key);
        data.SelectorKeys.Should().HaveCount(2);
        data.BigMValues.Should().HaveCount(2);
        data.Name.Should().Be("max");
        data.Minimum.Should().BeFalse();
    }

    /// <summary>
    /// **Big-M 的覆盖边界：恰好等于需求量<b>通过</b>，少一点即拒** /
    /// The Big-M coverage boundary: exactly the required amount <b>passes</b>, less is rejected.
    /// </summary>
    /// <remarks>
    /// **这是本批最要紧的一条判据。**
    ///
    /// **算例：两个输入 `x` 与 `y`，各自界 `[0, 10]。**
    /// **所以第一段的 `required = maxUpper - lower = 10 - 0 = 10`。**
    ///
    /// | Big-M | 结果 |
    /// | --- | --- |
    /// | `10` | **通过（恰好够）** |
    /// | `9.99` | **拒绝** |
    /// | `100` | **通过（有余量）** |
    ///
    /// **`bigM == required` 通过这一点与 `NativeAbs` 的严格上界方向不同：**
    /// **那里的"恰好等于上限"没有余量，这里的"恰好等于需求量"是够用的——
    /// 因为没有别的项会再吃掉这段余量。**
    ///
    /// **判据必须测 <b>`10` 与 `9.99` 这一对</b>：只测 `100` 通过的话，
    /// 把 `bigM < required` 写成 `bigM <= required` 或者干脆不查，都发现不了。**
    ///
    /// **This is the batch's key criterion.**
    ///
    /// **The case: two inputs `x` and `y`, each bounded `[0, 10]`, so the first input's
    /// `required = maxUpper - lower = 10 - 0 = 10`.**
    ///
    /// **That `bigM == required` passes points the other way from `NativeAbs`'s strict bound: there "exactly at the
    /// limit" has no headroom, here "exactly the required amount" is enough — nothing else eats into it.**
    ///
    /// **The criterion must test <b>the pair `10` and `9.99`</b>: with only `100` passing, writing
    /// `bigM <= required` or dropping the check entirely would go unnoticed.**
    /// </remarks>
    [Fact]
    public void TheBigMCoverage_ShouldBeInclusiveAtTheRequirement() {
        NativeMax.Prepare(Structure(bigMs: new[] { 10.0, 10.0 })).Should()
            .BeOfType<Ok<NativeMaxData, ErrorCode, Error<ErrorCode>>>(
                "exactly the required amount covers the domain");

        NativeMax.Prepare(Structure(bigMs: new[] { 9.99, 10.0 })).Should()
            .BeOfType<Failed<NativeMaxData, ErrorCode, Error<ErrorCode>>>(
                "a hundredth short leaves the global maximum unreachable");

        NativeMax.Prepare(Structure(bigMs: new[] { 100.0, 100.0 })).Should()
            .BeOfType<Ok<NativeMaxData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **Big-M 必须为正** / Big-Ms must be positive.
    /// </summary>
    /// <remarks>
    /// **`0` 在这里没有意义：一个恒定输入的 `required` 为 `maxUpper - lower`，
    /// 只在它自己就是最大值时才是 0，而那时它不需要 fallback。**
    ///
    /// **Zero is meaningless here: a constant input's `required` is `maxUpper - lower`, zero only when it is itself the
    /// maximum — and then it needs no fallback.**
    /// </remarks>
    [Fact]
    public void TheBigMs_ShouldBePositive() {
        NativeMax.Prepare(Structure(bigMs: new[] { 0.0, 100.0 })).Should()
            .BeOfType<Failed<NativeMaxData, ErrorCode, Error<ErrorCode>>>();
        NativeMax.Prepare(Structure(bigMs: new[] { -5.0, 100.0 })).Should()
            .BeOfType<Failed<NativeMaxData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **输入不能引用自己的结果或选择列** / An input must not reference its own result or selectors.
    /// </summary>
    /// <remarks>
    /// **一个"最大值函数引用了自己的结果"的结构会写出 `result >= result` 这样的行，
    /// 形式上满足、什么都不约束。**
    ///
    /// **A max-function referencing its own result writes rows like `result >= result`, formally satisfied and
    /// constraining nothing.**
    /// </remarks>
    [Fact]
    public void TheInput_ShouldNotReferenceItsOwnResultOrSelectors() {
        // **输入里含结果列。**
        // **The input contains the result column.**
        var selfReferencing = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, Result) }, Flt64.Zero);

        NativeMax.Prepare(Structure(
                first: selfReferencing, second: Poly(1.0, Y, 0.0))).Should()
            .BeOfType<Failed<NativeMaxData, ErrorCode, Error<ErrorCode>>>();

        // **输入里含选择列。**
        // **The input contains a selector column.**
        var selectorReferencing = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, Selector1) }, Flt64.Zero);

        NativeMax.Prepare(Structure(
                first: selectorReferencing, second: Poly(1.0, Y, 0.0))).Should()
            .BeOfType<Failed<NativeMaxData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **负系数把上下界<b>翻转</b>** / A negative coefficient <b>swaps</b> the bounds.
    /// </summary>
    /// <remarks>
    /// **`-x` 在 `x ∈ [2, 7]` 上的范围是 `[-7, -2]`，不是 `[-2, -7]`。**
    ///
    /// **如果实现写成"下界永远用变量的下界"，那 `-x` 会得到 `lower = -2`、`upper = -7`，
    /// 于是 `lower > upper`——一个不可能的区间。**
    ///
    /// **判据用 `x ∈ [2, 7]` 与输入 `-x`，断言 `lower == -7` 且 `upper == -2`。**
    /// **这个算例的两个界都非零，所以"忘了翻转"与"翻转了"给出完全不同的数。**
    ///
    /// **`-x` over `x ∈ [2, 7]` ranges over `[-7, -2]`, not `[-2, -7]`.**
    ///
    /// **An implementation that always takes the variable's lower bound for the minimum gives `lower = -2`,
    /// `upper = -7` — an impossible interval.**
    ///
    /// **The criterion uses `x ∈ [2, 7]` and the input `-x` and asserts `lower == -7`, `upper == -2`.** **Both bounds are
    /// nonzero, so "forgot to swap" and "swapped" give entirely different numbers.**
    /// </remarks>
    [Fact]
    public void ANegativeCoefficient_ShouldSwapTheBounds() {
        RealVar bounded = Bounded("nm-x", 2.0, 7.0);
        RealVar other = Bounded("nm-y", 2.0, 7.0);

        var negated = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(-1.0), bounded) }, Flt64.Zero);

        Result<NativeMaxData, ErrorCode, Error<ErrorCode>> result = NativeMax.Prepare(
            Structure(
                first: negated, second: Poly(1.0, other, 0.0),
                result: Result, selectors: new[] { Selector1, Selector2 },
                bigMs: new[] { 100.0, 100.0 }));

        result.Should().BeOfType<Ok<NativeMaxData, ErrorCode, Error<ErrorCode>>>();
        result.Value.Inputs[0].LowerBound.Should().Be(-7.0);
        result.Value.Inputs[0].UpperBound.Should().Be(-2.0);
        result.Value.Inputs[0].Terms[bounded.Key].Should().Be(-1.0);
    }

    /// <summary>
    /// **同一变量的两个单项被<b>合并</b>** / Two monomials of one variable are <b>combined</b>.
    /// </summary>
    /// <remarks>
    /// **`x + x` 必须合成 `2x`，否则上下界会按两项分别累加——
    /// 数值上等价，但 `Terms` 里会留下两项同一个键，而上界取"最大值"时的
    /// 边界条件就说不清了。**
    ///
    /// **`x + x` must become `2x`; otherwise the bounds accumulate twice — numerically equivalent, but `Terms` keeps two
    /// entries for one key and the boundary conditions become unclear.**
    /// </remarks>
    [Fact]
    public void RepeatedMonomials_ShouldBeCombined() {
        var doubled = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(Flt64.One, X),
                new LinearMonomial<Flt64>(new Flt64(0.5), X),
            }, Flt64.Zero);

        Result<NativeMaxData, ErrorCode, Error<ErrorCode>> result = NativeMax.Prepare(
            Structure(first: doubled, second: Poly(1.0, Y, 0.0)));

        result.Value.Inputs[0].Terms.Should().HaveCount(1);
        result.Value.Inputs[0].Terms[X.Key].Should().Be(1.5);
    }

    /// <summary>
    /// **合并成零的项被丢弃** / A term that combines to zero is dropped.
    /// </summary>
    /// <remarks>
    /// **`x - x` 合并成零之后既不进 `Terms`，也不参与上下界累加。**
    /// **所以 `lower == upper == constant`——一个恒定输入。**
    ///
    /// **`x - x` combines to zero, enters neither `Terms` nor the bound accumulation, so
    /// `lower == upper == constant` — a constant input.**
    /// </remarks>
    [Fact]
    public void AZeroCombinedTerm_ShouldBeDropped() {
        var cancelling = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(Flt64.One, X),
                new LinearMonomial<Flt64>(new Flt64(-1.0), X),
            }, new Flt64(3.0));

        Result<NativeMaxData, ErrorCode, Error<ErrorCode>> result = NativeMax.Prepare(
            Structure(first: cancelling, second: Poly(1.0, Y, 0.0)));

        result.Value.Inputs[0].Terms.Should().BeEmpty();
        result.Value.Inputs[0].Constant.Should().Be(3.0);
        result.Value.Inputs[0].LowerBound.Should().Be(3.0);
        result.Value.Inputs[0].UpperBound.Should().Be(3.0);
    }

    /// <summary>
    /// **数量不匹配与键重复都被拒** / Count mismatches and duplicate keys are rejected.
    /// </summary>
    [Fact]
    public void TheCountsAndKeys_ShouldBeChecked() {
        NativeMax.Prepare(new MaxStructure(
                Inputs: Array.Empty<LinearPolynomial<Flt64>>(),
                ResultVariable: Result,
                SelectorVariables: Array.Empty<IVariableItem>(),
                BigMValues: Array.Empty<Flt64>(),
                Converter: IIntoValue<Flt64>.Identity)).Should()
            .BeOfType<Failed<NativeMaxData, ErrorCode, Error<ErrorCode>>>(
                "a max over nothing is not a max");

        // **选择列数量与输入不符。**
        // **Selector count does not match the input count.**
        NativeMax.Prepare(Structure(selectors: new IVariableItem[] { Selector1 })).Should()
            .BeOfType<Failed<NativeMaxData, ErrorCode, Error<ErrorCode>>>();

        // **结果列与某个选择列相同。**
        // **The result column equals one of the selectors.**
        NativeMax.Prepare(Structure(selectors: new IVariableItem[] { Result, Selector2 })).Should()
            .BeOfType<Failed<NativeMaxData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **`minimum` 标志被原样带出** / The `minimum` flag comes through unchanged.
    /// </summary>
    /// <remarks>
    /// **`prepareNativeMax` 不区分 max 与 min——它只把这个标志转交出去。**
    /// **一个"取最小值"的结构走的是同一套校验，因为 Big-M 的覆盖要求与方向无关**
    /// （**最小值的 fallback 是 `result <= input + M` 的镜像**）。
    ///
    /// **`prepareNativeMax` does not distinguish max from min; it only passes the flag through.** **A minimum structure
    /// runs the same checks because the Big-M coverage requirement is direction-independent.**
    /// </remarks>
    [Fact]
    public void TheMinimumFlag_ShouldComeThrough() {
        Result<NativeMaxData, ErrorCode, Error<ErrorCode>> result =
            NativeMax.Prepare(Structure(minimum: true));

        result.Should().BeOfType<Ok<NativeMaxData, ErrorCode, Error<ErrorCode>>>();
        result.Value.Minimum.Should().BeTrue(
            "the primitive layer does not act on the flag, only carries it");
    }

    private static MaxStructure Structure(
        LinearPolynomial<Flt64>? first = null,
        LinearPolynomial<Flt64>? second = null,
        IVariableItem? result = null,
        IVariableItem[]? selectors = null,
        double[]? bigMs = null,
        bool minimum = false) {
        var values = new Flt64[(bigMs ?? new[] { 100.0, 100.0 }).Length];
        for (int index = 0; index < values.Length; ++index) {
            values[index] = new Flt64((bigMs ?? new[] { 100.0, 100.0 })[index]);
        }

        return new MaxStructure(
            Inputs: new[] { first ?? Poly(1.0, X, 0.0), second ?? Poly(1.0, Y, 0.0) },
            ResultVariable: result ?? Result,
            SelectorVariables: selectors ?? new[] { Selector1, Selector2 },
            BigMValues: values,
            Converter: IIntoValue<Flt64>.Identity,
            Name: "max") {
            Minimum = minimum,
        };
    }

    private static LinearPolynomial<Flt64> Poly(double coefficient, IVariableItem variable, double constant) =>
        new(new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), variable) },
            new Flt64(constant));

    private static readonly RealVar X = Bounded("nm-x", 0.0, 10.0);

    private static readonly RealVar Y = Bounded("nm-y", 0.0, 10.0);

    /// <summary>造一个带上下界的连续变量 / Build a continuous variable with explicit bounds.</summary>
    /// <remarks>
    /// **仓库里没有 `BoundedReal` 这样的便捷类型，界是通过 `Range.Geq` / `Range.Leq` 设的。**
    /// **本批的判据全靠真实的界，所以这个工厂是必需的而不是方便的。**
    ///
    /// **There is no `BoundedReal` convenience type in this repository; bounds are set through `Range.Geq` /
    /// `Range.Leq`.** **Every criterion here depends on real bounds, so this factory is necessary rather than
    /// convenient.**
    /// </remarks>
    private static RealVar Bounded(string name, double lower, double upper) {
        var variable = new RealVar(name);
        variable.Range.Geq(new Flt64(lower));
        variable.Range.Leq(new Flt64(upper));
        return variable;
    }

    private static readonly RealVar Result = new("nm-result");

    private static readonly BinVar Selector1 = new("nm-sel1");

    private static readonly BinVar Selector2 = new("nm-sel2");
}
