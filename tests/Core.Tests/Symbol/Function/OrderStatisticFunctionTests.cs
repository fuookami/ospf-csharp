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
/// 第 k 大与"前 k 大之和"的边界差一 / The k-th largest and the top-k sum differ by one at the boundary.
/// </summary>
/// <remarks>
/// B1150 补 `KthLargestFunction` / `TopKSumFunction`（基线 `OrderStatistic.kt`）。
///
/// **两者都靠一个比较网络把输入降序排列**（每对比较 = 一个 `MaxFunction` + 一个 `MinFunction`），
/// **所以"第 k 大"就是"排序后第 k 个"。**
///
/// B1150 adds the two order statistics from the baseline.
///
/// **Both sort the inputs descending through a comparison network** (each comparison is one `MaxFunction` plus one
/// `MinFunction`), **so "the k-th largest" is "the k-th after sorting".**
/// </remarks>
public class OrderStatisticFunctionTests
{
    /// <summary>**`k` 从 0 开始：`k = 0` 是最大值** / `k` is zero-based: `k = 0` is the maximum.</summary>
    /// <remarks>
    /// **从 1 开始的约定会让 `k = 0` 与 `k = n` 都越界，而越界检查的边界会差一位。**
    /// **这条同时钉住起点：`k = 0` 给 5（最大），`k = 2` 给 1（最小）。**
    ///
    /// **A one-based convention would put both `k = 0` and `k = n` out of range, and the bounds check would be off by
    /// one.** **This pins the origin from both ends: `k = 0` gives 5 (the maximum) and `k = 2` gives 1 (the minimum).**
    /// </remarks>
    [Fact]
    public void TheKthLargest_ShouldBeZeroBased()
    {
        var x1 = new RealVar("kth-x1");
        var x2 = new RealVar("kth-x2");
        var x3 = new RealVar("kth-x3");
        LinearPolynomial<Flt64>[] inputs = {
            Polynomial(x1, 1.0), Polynomial(x2, 1.0), Polynomial(x3, 1.0),
        };
        IReadOnlyDictionary<ISymbol, Flt64> values = Values((x1, 3.0), (x2, 5.0), (x3, 1.0));

        new KthLargestFunction<Flt64>(inputs, 0, IIntoValue<Flt64>.Identity, name: "kth0")
            .Evaluate(values).Should().Be(new Flt64(5.0), "k = 0 is the largest of 3, 5 and 1");

        new KthLargestFunction<Flt64>(inputs, 1, IIntoValue<Flt64>.Identity, name: "kth1")
            .Evaluate(values).Should().Be(new Flt64(3.0), "k = 1 is the middle value");

        new KthLargestFunction<Flt64>(inputs, 2, IIntoValue<Flt64>.Identity, name: "kth2")
            .Evaluate(values).Should().Be(new Flt64(1.0), "k = 2 is the smallest");
    }

    /// <summary>**"第 k 大"不是"前 k 大之和"** / The k-th largest is not the top-k sum.</summary>
    /// <remarks>
    /// **`k = 1` 时前者给 3、后者给 8（5 + 3）。** **两者都用同一个排序网络，
    /// 所以"结果多项式取错了一行"这种错在单条判据下可能通过。**
    ///
    /// **With `k = 1` the former gives 3 and the latter 8 (5 + 3).** **Both share the sorting network, so "the result
    /// polynomial took the wrong row" can pass a single criterion.**
    /// </remarks>
    [Fact]
    public void TheTopKSum_ShouldSumRatherThanPick()
    {
        var x1 = new RealVar("topk-x1");
        var x2 = new RealVar("topk-x2");
        var x3 = new RealVar("topk-x3");
        LinearPolynomial<Flt64>[] inputs = {
            Polynomial(x1, 1.0), Polynomial(x2, 1.0), Polynomial(x3, 1.0),
        };
        IReadOnlyDictionary<ISymbol, Flt64> values = Values((x1, 3.0), (x2, 5.0), (x3, 1.0));

        new TopKSumFunction<Flt64>(inputs, 1, IIntoValue<Flt64>.Identity, name: "topk1")
            .Evaluate(values).Should().Be(new Flt64(5.0), "the top one is 5");

        new TopKSumFunction<Flt64>(inputs, 2, IIntoValue<Flt64>.Identity, name: "topk2")
            .Evaluate(values).Should().Be(new Flt64(8.0), "the top two are 5 and 3");
    }

    /// <summary>**`TopKSum` 允许 `k = n` 而 `KthLargest` 不允许** / Top-k allows k = n, k-th does not.</summary>
    /// <remarks>
    /// **"前 n 大之和"有意义（就是全部之和），"第 n 大"不存在。**
    /// **两者边界差一是定义上的差别，不是不一致**——
    /// **所以这里同时断言"允许"与"拒绝"，而不是只测一边。**
    ///
    /// **"The sum of all n" exists while "the n-th largest" does not.**
    /// **The off-by-one is a difference in definition, not an inconsistency** —
    /// **so this asserts both the acceptance and the rejection rather than only one side.**
    /// </remarks>
    [Fact]
    public void TheTwoBoundaries_ShouldDifferByOne()
    {
        var x1 = new RealVar("bound-x1");
        var x2 = new RealVar("bound-x2");
        LinearPolynomial<Flt64>[] inputs = { Polynomial(x1, 1.0), Polynomial(x2, 1.0) };
        IReadOnlyDictionary<ISymbol, Flt64> values = Values((x1, 3.0), (x2, 5.0));

        // k = n = 2：TopKSum 允许，结果就是全部之和。
        new TopKSumFunction<Flt64>(inputs, 2, IIntoValue<Flt64>.Identity, name: "bound-topk")
            .Evaluate(values).Should().Be(new Flt64(8.0), "the sum of all two inputs");

        // 同一个 k 对 KthLargest 越界。
        Action kth = () => _ = new KthLargestFunction<Flt64>(
            inputs, 2, IIntoValue<Flt64>.Identity);
        kth.Should().Throw<ArgumentOutOfRangeException>(
            "there is no third value among two inputs");
    }

    /// <summary>**`k = 0` 与 `k = n` 都跳过排序** / Both `k = 0` and `k = n` skip the sort.</summary>
    /// <remarks>
    /// **跳过排序省掉 n(n−1) 个函数，而那些函数的辅助变量与约束本来会让模型毫无必要地变大。**
    /// **这条用"内层函数个数为 0"来钉住它**——**若哪天有人为了"统一路径"去掉这个分支，
    /// 评估值仍然正确，但模型会多出一堆无用变量。**
    ///
    /// **Skipping the sort saves n(n−1) functions whose helpers and constraints would otherwise bloat the model.**
    /// **This pins it through "zero inner functions"** — **if someone later removes the branch for "one code path",
    /// the evaluated value stays correct while the model grows a pile of useless variables.**
    /// </remarks>
    [Fact]
    public void TheTopKSum_ShouldSkipSortingAtTheTwoEnds()
    {
        var x1 = new RealVar("skip-x1");
        var x2 = new RealVar("skip-x2");
        var x3 = new RealVar("skip-x3");
        LinearPolynomial<Flt64>[] inputs = {
            Polynomial(x1, 1.0), Polynomial(x2, 1.0), Polynomial(x3, 1.0),
        };

        new TopKSumFunction<Flt64>(inputs, 0, IIntoValue<Flt64>.Identity, name: "skip0")
            .InnerFunctions.Should().BeEmpty("k = 0 needs no ordering at all");

        new TopKSumFunction<Flt64>(inputs, 3, IIntoValue<Flt64>.Identity, name: "skip3")
            .InnerFunctions.Should().BeEmpty("k = n needs no ordering at all");

        new TopKSumFunction<Flt64>(inputs, 1, IIntoValue<Flt64>.Identity, name: "skip1")
            .InnerFunctions.Should().NotBeEmpty("an interior k does need the network");
    }

    /// <summary>**`k = 0` 的求和是常数 0** / The `k = 0` sum is the constant zero.</summary>
    [Fact]
    public void TheTopKSum_ShouldBeZeroWhenKIsZero()
    {
        var x1 = new RealVar("zero-x1");
        var x2 = new RealVar("zero-x2");
        LinearPolynomial<Flt64>[] inputs = { Polynomial(x1, 1.0), Polynomial(x2, 1.0) };

        new TopKSumFunction<Flt64>(inputs, 0, IIntoValue<Flt64>.Identity, name: "zero")
            .Evaluate(Values((x1, 7.0), (x2, -3.0))).Should().Be(
                Flt64.Zero, "the sum of nothing is zero, whatever the inputs are");
    }

    /// <summary>**负值与重复值都按降序处理** / Negatives and duplicates sort descending too.</summary>
    /// <remarks>
    /// **全正、互异的输入无法分开"降序"与"升序"以外的更多错误**——
    /// **而负值与重复值能分开"绝对值排序"与"符号排序"这两种常见误写。**
    ///
    /// **All-positive distinct inputs cannot separate much beyond descending versus ascending** —
    /// **negatives and duplicates separate "sort by magnitude" from "sort by value", two common mis-writes.**
    /// </remarks>
    [Fact]
    public void TheSorting_ShouldHandleNegativesAndDuplicates()
    {
        var x1 = new RealVar("neg-x1");
        var x2 = new RealVar("neg-x2");
        var x3 = new RealVar("neg-x3");
        LinearPolynomial<Flt64>[] inputs = {
            Polynomial(x1, 1.0), Polynomial(x2, 1.0), Polynomial(x3, 1.0),
        };
        // -5、-1、-3：降序为 -1、-3、-5；若按绝对值排序则为 -1、-3、-5 之外的另一序。
        IReadOnlyDictionary<ISymbol, Flt64> values = Values((x1, -5.0), (x2, -1.0), (x3, -3.0));

        new KthLargestFunction<Flt64>(inputs, 0, IIntoValue<Flt64>.Identity, name: "neg0")
            .Evaluate(values).Should().Be(new Flt64(-1.0), "the largest of -5, -1 and -3 is -1");

        new KthLargestFunction<Flt64>(inputs, 2, IIntoValue<Flt64>.Identity, name: "neg2")
            .Evaluate(values).Should().Be(new Flt64(-5.0), "the smallest is -5");

        // 重复值：第 1 大与第 2 大都是 4。
        IReadOnlyDictionary<ISymbol, Flt64> duplicated = Values((x1, 4.0), (x2, 4.0), (x3, 1.0));
        new KthLargestFunction<Flt64>(inputs, 1, IIntoValue<Flt64>.Identity, name: "dup1")
            .Evaluate(duplicated).Should().Be(new Flt64(4.0), "with two fours the second largest is also four");
    }

    /// <summary>**空输入被拒绝** / Empty input is rejected.</summary>
    [Fact]
    public void TheKthLargest_ShouldRejectEmptyInput()
    {
        Action build = () => _ = new KthLargestFunction<Flt64>(
            Array.Empty<LinearPolynomial<Flt64>>(), 0, IIntoValue<Flt64>.Identity);
        build.Should().Throw<ArgumentException>("there is no largest of nothing");
    }

    /// <summary>造一个 <c>1·variable</c> / Build `1*variable`.</summary>
    private static LinearPolynomial<Flt64> Polynomial(RealVar variable, double scale) =>
        new(new[] { new LinearMonomial<Flt64>(new Flt64(scale), variable) }, Flt64.Zero);

    /// <summary>把若干 (变量, 值) 打包成评估字典 / Pack pairs into an evaluation dictionary.</summary>
    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        params (RealVar Variable, double Value)[] pairs) {
        var values = new Dictionary<ISymbol, Flt64>();
        foreach ((RealVar variable, double value) in pairs) {
            values[variable] = new Flt64(value);
        }

        return values;
    }
}
