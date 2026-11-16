#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 累进是连续的，全量折扣会跳降 / Incremental is continuous; all-units jumps down.
/// </summary>
/// <remarks>
/// 补 `IncrementalTariff` 与 `AllUnitsDiscountFunction`（基线 `Tariff.kt`）。
///
/// **两者在第一个区间内完全相同**——**所以只测小值的判据分不开它们。**
/// **分开它们必须跨越一个断点**：
///
/// | 数量 | 累进 | 全量折扣 |
/// | --- | --- | --- |
/// | 99 | 99 × 10 = 990 | 99 × 10 = 990 |
/// | 100 | 990 + 1 × 5 = **995** | **100 × 5 = 500** |
///
/// **全量折扣在断点处跳降（990 → 500），累进只涨了 5。**
///
/// **They agree exactly on the first segment** — **so a criterion that only tests small values cannot separate them.**
/// **Separating them requires crossing a breakpoint**:
///
/// | Quantity | Incremental | All units |
/// | --- | --- | --- |
/// | 99 | 99 × 10 = 990 | 99 × 10 = 990 |
/// | 100 | 990 + 1 × 5 = **995** | **100 × 5 = 500** |
///
/// **The all-units cost drops at the breakpoint (990 to 500) while the incremental one rises by five.**
/// </remarks>
public class TariffTests
{
    /// <summary>**累进在断点处连续，全量折扣跳降** / Continuous versus a jump.</summary>
    /// <remarks>
    /// **这是唯一能分开两者的判据**，**而且它同时钉住"两边都没写反"**：
    /// **如果累进被误实现成全量，99 → 100 会从 990 掉到 500；如果全量被误实现成累进，会从 990 升到 995。**
    ///
    /// **This is the one criterion that separates them**, **and it pins that neither is implemented as the other**:
    /// **an incremental cost implemented as all-units would fall from 990 to 500; an all-units implemented as
    /// incremental would rise from 990 to 995.**
    /// </remarks>
    [Fact]
    public void TheTwoPricings_ShouldDifferAcrossTheBreakpoint()
    {
        // 断点 0、100、200；费率 10、5、2。
        double at99 = Incremental(99.0);
        double at100 = Incremental(100.0);
        at99.Should().BeApproximately(990.0, 1e-9, "99 units at a rate of ten");
        // **第一段覆盖 [0, 100]，所以 100 处仍然是 10 的费率：100 × 10 = 1000。**
        // 新费率只作用于 100 <b>之后</b>的部分。
        // **The first segment covers [0, 100], so at 100 the rate is still ten: 100 x 10 = 1000.**
        // The new rate applies only to the part <b>above</b> 100.
        at100.Should().BeApproximately(1000.0, 1e-9, "the whole first segment bills at ten");

        double discountAt99 = Discount(99.0);
        discountAt99.Should().BeApproximately(990.0, 1e-9, "the first tier prices 99 units at ten");

        // **`boundaryGap = 0` 时断点本身属于<b>低</b>档**（与基线的 `firstOrNull` 一致），
        // **所以 100 处仍然是 10 的单价 = 1000；跳降发生在 100 <b>之上</b>一点。**
        // **这正是上一条"两者在第一个区间内相同"的边界：区间包含右端点。**
        //
        // **With `boundaryGap = 0` the breakpoint itself belongs to the <b>lower</b> tier** (matching the baseline's
        // `firstOrNull`), **so at 100 the unit price is still ten, giving 1000; the drop happens just <b>above</b>
        // 100.** **That is exactly the boundary of the previous criterion: the segment includes its right end.**
        Discount(100.0).Should().BeApproximately(1000.0, 1e-9,
            "with gap zero the breakpoint is still billed at the lower tier's rate");
        Discount(100.001).Should().BeApproximately(500.005, 1e-6,
            "one thousandth above the breakpoint the whole quantity is repriced at five");
    }

    /// <summary>
    /// **第二段内部的取值点必须算对** / An interior point of the second segment must be right.
    /// </summary>
    /// <remarks>
    /// **这条是被一次变异逼出来的**：**把累进误实现成"每个断点重算 `数量 × 该段费率"` 时，
    /// 99 与 100 处的值恰好与正确实现相同**（990 与 1000），
    /// **于是"跨断点比较"那条判据全绿，只有 `baseCost` 那条红。**
    ///
    /// **所以必须测段<b>内部</b>的点**：**150 处正确值是 `1000 + 5×50 = 1250`，
    /// 而那个误实现给的是 1000（因为它把 [100,200] 画成了水平线）。**
    ///
    /// **This criterion was forced by a mutation**: **implementing the incremental tariff as "recompute
    /// `quantity × that segment's rate` at each breakpoint" happens to agree at 99 and 100** (990 and 1000),
    /// **so the "compare across the breakpoint" criterion stayed green and only the `baseCost` one went red.**
    ///
    /// **So an <b>interior</b> point is required**: **at 150 the correct value is `1000 + 5×50 = 1250`, while that
    /// mutation gives 1000 because it drew [100, 200] as a flat line.**
    /// </remarks>
    [Fact]
    public void TheIncrementalCost_ShouldBeRightInsideTheLaterSegments()
    {
        Incremental(150.0).Should().BeApproximately(1250.0, 1e-9,
            "1000 at the first breakpoint plus 50 units at the next rate of five");
        Incremental(250.0).Should().BeApproximately(1600.0, 1e-9,
            "1500 at the second breakpoint plus 50 units at the rate of two");
        Incremental(300.0).Should().BeApproximately(1700.0, 1e-9, "the table ends at 300");
    }

    /// <summary>**两者在第一个区间内完全相同** / They agree on the first segment.</summary>
    /// <remarks>
    /// **这条不是"顺便测一下"，而是说明上一条为什么必须跨界**：
    /// **区间内的值完全一样，所以任何只在这个区间内的判据对两种实现都绿。**
    ///
    /// **This is not a bonus check but the reason the criterion above has to cross the boundary**:
    /// **values inside the segment are identical, so any criterion confined there is green under either
    /// implementation.**
    /// </remarks>
    [Fact]
    public void TheTwoPricings_ShouldAgreeOnTheFirstSegment()
    {
        for (double quantity = 0.0; quantity <= 100.0; quantity += 10.0) {
            Incremental(quantity).Should().BeApproximately(
                Discount(quantity), 1e-9,
                $"at {quantity} units both bill the whole quantity at the first rate");
        }
    }

    /// <summary>**累进在断点处连续** / The incremental cost is continuous at a breakpoint.</summary>
    [Fact]
    public void TheIncrementalCost_ShouldBeContinuousAtEveryBreakpoint()
    {
        // 断点 0、100、200，费率 10、5、2。
        foreach (double breakpoint in new[] { 100.0, 200.0 }) {
            double below = Incremental(breakpoint - 1e-6);
            double at = Incremental(breakpoint);
            double above = Incremental(breakpoint + 1e-6);

            // **容差必须容纳"偏移 × 费率"**：1e-6 的偏移在上限费率 10 下就是 1e-5。
            // 第一版用 1e-6，被自己算出的 1e-5 打红。
            //
            // **The tolerance must accommodate "offset times rate"**: a 1e-6 offset at a rate of 10 is 1e-5.
            // The first version used 1e-6 and was turned red by its own arithmetic.
            (at - below).Should().BeInRange(-1e-3, 1e-3,
                $"no jump just below {breakpoint}");
            (above - at).Should().BeInRange(-1e-3, 1e-3,
                $"no jump just above {breakpoint}");
        }
    }

    /// <summary>**全量折扣在断点处跳降** / The all-units cost jumps down at a breakpoint.</summary>
    [Fact]
    public void TheAllUnitsCost_ShouldDropAtEveryBreakpoint()
    {
        // 断点 0、100、200，费率 10、5、2。
        // **gap = 0 时断点本身属于<b>低</b>档（与基线的 firstOrNull 一致）**，
        // 所以跳降发生在断点<b>之上</b>一点，而不是正好在断点上。
        //
        // **With gap = 0 the breakpoint itself belongs to the <b>lower</b> tier (matching the baseline's firstOrNull)**,
        // so the drop happens just <b>above</b> the breakpoint, not on it.
        (Discount(100.001) - Discount(99.999)).Should().BeLessThan(-400.0,
            "crossing into the second tier reprices all units, a fall of roughly 500");
        (Discount(200.001) - Discount(199.999)).Should().BeLessThan(-400.0,
            "crossing into the third tier reprices all units again");
    }

    /// <summary>**断点必须严格递增** / Breakpoints must strictly increase.</summary>
    /// <remarks>
    /// **相等或倒序的断点会让"第 i 段"没有意义**，
    /// **而分段函数仍然能构造出来（区间宽度为负时斜率变成反的）。**
    ///
    /// **Equal or reversed breakpoints make "segment i" meaningless**,
    /// **while the piecewise function still builds (a negative width flips the slope).**
    /// </remarks>
    [Fact]
    public void BothPricings_ShouldRejectNonIncreasingBreakpoints()
    {
        IncrementalTariff.Create<Flt64>(
            x: Variable(),
            breakpoints: new[] { new Flt64(0.0), new Flt64(100.0), new Flt64(100.0) },
            marginalRates: new[] { new Flt64(10.0), new Flt64(5.0) },
            converter: IIntoValue<Flt64>.Identity)
            .IsOk.Should().BeFalse("two equal breakpoints give a zero-width segment");

        Action discount = () => _ = new AllUnitsDiscountFunction<Flt64>(
            x: Variable(),
            breakpoints: new[] { new Flt64(100.0), new Flt64(0.0) },
            rates: new[] { new Flt64(10.0) },
            converter: IIntoValue<Flt64>.Identity);
        discount.Should().Throw<ArgumentException>("a reversed breakpoint pair is not a tier table");
    }

    /// <summary>**费率数必须等于区间数** / One rate per interval.</summary>
    [Fact]
    public void BothPricings_ShouldRequireOneRatePerInterval()
    {
        // 两个断点 = 一个区间，所以只要一个费率。
        IncrementalTariff.Create<Flt64>(
            x: Variable(),
            breakpoints: new[] { new Flt64(0.0), new Flt64(100.0) },
            marginalRates: new[] { new Flt64(10.0), new Flt64(5.0) },
            converter: IIntoValue<Flt64>.Identity)
            .IsOk.Should().BeFalse("two rates for one interval is a mismatch");

        Action discount = () => _ = new AllUnitsDiscountFunction<Flt64>(
            x: Variable(),
            breakpoints: new[] { new Flt64(0.0), new Flt64(100.0) },
            rates: new[] { new Flt64(10.0), new Flt64(5.0) },
            converter: IIntoValue<Flt64>.Identity);
        discount.Should().Throw<ArgumentException>();
    }

    /// <summary>**负费率被拒绝** / Negative rates are rejected.</summary>
    /// <remarks>
    /// **负费率让"买得越多花得越少"，那不是折扣而是套利**——
    /// **而分段函数照样构造成功、求解器照样给出"更优"的解。**
    ///
    /// **A negative rate makes buying more cost less — arbitrage rather than a discount** —
    /// **while the piecewise function still builds and the solver reports a "better" objective.**
    /// </remarks>
    [Fact]
    public void BothPricings_ShouldRejectNegativeRates()
    {
        IncrementalTariff.Create<Flt64>(
            x: Variable(),
            breakpoints: new[] { new Flt64(0.0), new Flt64(100.0) },
            marginalRates: new[] { new Flt64(-1.0) },
            converter: IIntoValue<Flt64>.Identity)
            .IsOk.Should().BeFalse("a negative marginal rate is arbitrage");

        Action discount = () => _ = new AllUnitsDiscountFunction<Flt64>(
            x: Variable(),
            breakpoints: new[] { new Flt64(0.0), new Flt64(100.0) },
            rates: new[] { new Flt64(-1.0) },
            converter: IIntoValue<Flt64>.Identity);
        discount.Should().Throw<ArgumentException>();
    }

    /// <summary>**低于第一个断点是越界，不是"按第一档"** / Below the first breakpoint is out of range.</summary>
    [Fact]
    public void TheAllUnitsCost_ShouldRejectBelowTheFirstBreakpoint()
    {
        AllUnitsDiscountFunction<Flt64> discount = BuildDiscount();

        discount.Evaluate(Values(100.0)).Should().NotBeNull("100 is exactly the first breakpoint");
        discount.Evaluate(Values(-1.0)).Should().BeNull("negative quantity is outside the tariff table");
    }

    /// <summary>**`baseCost` 是起点，不是每档的附加费** / `baseCost` is the starting point, not a per-tier fee.</summary>
    /// <remarks>
    /// **加错位置会让第一档偏一个常数、而后续档位的增量仍然正确**——
    /// **于是"每段增量对不对"的判据全绿，只有绝对量才看得出错。**
    ///
    /// **Placing it wrongly shifts the first tier by a constant while the later increments stay correct** —
    /// **so "are the increments right" stays green and only the absolute values show the error.**
    /// </remarks>
    [Fact]
    public void TheBaseCost_ShouldShiftEveryPointByTheSameAmount()
    {
        double withBase = IncrementalWithBase(50.0, baseCost: 1000.0);
        double withoutBase = Incremental(50.0);

        withBase.Should().BeApproximately(withoutBase + 1000.0, 1e-9,
            "a base cost of one thousand shifts the whole curve by one thousand");
    }

    private static double Incremental(double quantity) => IncrementalWithBase(quantity, baseCost: null);

    private static double IncrementalWithBase(double quantity, double? baseCost) {
        Result<UnivariateLinearPiecewiseFunction<Flt64>, ErrorCode, Error<ErrorCode>> created =
            IncrementalTariff.Create<Flt64>(
            x: Variable(),
            breakpoints: new[] { new Flt64(0.0), new Flt64(100.0), new Flt64(200.0), new Flt64(300.0) },
            marginalRates: new[] { new Flt64(10.0), new Flt64(5.0), new Flt64(2.0) },
            converter: IIntoValue<Flt64>.Identity,
            baseCost: baseCost is { } cost ? new Flt64(cost) : null);

        if (!created.IsOk) {
            throw new InvalidOperationException(
                $"IncrementalTariff.Create failed at quantity {quantity} (see the error code)");
        }

        UnivariateLinearPiecewiseFunction<Flt64> function = created.Value;

        if (function.Evaluate(Values(quantity)) is not { } value) {
            throw new InvalidOperationException($"the incremental tariff returned null at quantity {quantity}");
        }

        return value.ToDouble();
    }

    private static double Discount(double quantity) {
        AllUnitsDiscountFunction<Flt64> discount = BuildDiscount();
        return discount.Evaluate(Values(quantity))!.Value.ToDouble();
    }

    private static AllUnitsDiscountFunction<Flt64> BuildDiscount() =>
        new(
            x: Variable(),
            breakpoints: new[] { new Flt64(0.0), new Flt64(100.0), new Flt64(200.0), new Flt64(300.0) },
            rates: new[] { new Flt64(10.0), new Flt64(5.0), new Flt64(2.0) },
            converter: IIntoValue<Flt64>.Identity,
            name: "discount");

    /// <summary>
    /// 数量变量，带范围 / The quantity variable, with bounds.
    /// </summary>
    /// <remarks>
    /// **必须设范围**：`FromPoints` 会根据输入多项式的界推导结果区间，
    /// **而一个无界的输入会让它拒绝整次构造**——这也是它"失败却没有堆栈"的原因。
    ///
    /// **Bounds are required**: `FromPoints` derives the result interval from the input polynomial's bounds, and an
    /// unbounded input makes it reject the whole construction, which is why it failed without a stack.
    /// </remarks>
    private static RealVar _variable = BoundedVariable();

    private static RealVar BoundedVariable() {
        var variable = new RealVar("tariff-x");
        variable.Range.Geq(Flt64.Zero);
        variable.Range.Leq(new Flt64(300.0));
        return variable;
    }

    private static LinearPolynomial<Flt64> Variable() =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, _variable) }, Flt64.Zero);

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(double quantity) =>
        new Dictionary<ISymbol, Flt64> { [_variable] = new Flt64(quantity) };
}
