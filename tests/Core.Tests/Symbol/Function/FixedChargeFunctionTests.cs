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
/// 不启用是免费的，但只有“启用”这一步才收固定成本 / Inactive is free, and only activation pays the fixed cost.
/// </summary>
/// <remarks>
/// 补 FixedChargeFunction（基线 Tariff.kt）。形状是
/// cost = fixedCost · active + unitCost · activity，active 是 0/1。
///
/// 它和普通线性成本只差一个激活项，而少了那一项模型会说“不启用也免费”——
/// 目标函数看起来完全正常，只是每个设施都不开。
///
/// Adds cost = fixedCost · active + unitCost · activity with a binary active.
///
/// It differs from an ordinary linear cost by the activation term alone, and without that term the model says
/// “not activating is free” — with an objective that looks perfectly ordinary while every facility stays shut.
/// </remarks>
public class FixedChargeFunctionTests
{
    /// <summary>不启用时成本为零 / Inactive costs nothing.</summary>
    [Fact]
    public void TheInactiveCase_ShouldCostNothing()
    {
        FixedChargeFunction<Flt64> charge = Make(fixedCost: 100.0, unitCost: 3.0, minimum: 0.0, maximum: 10.0);

        charge.Evaluate(Values(active: 0.0, activity: 0.0)).Should().Be(Flt64.Zero);
    }

    /// <summary>启用就要付固定成本，即使活动量为零 / Activating pays the fixed cost even when idle.</summary>
    /// <remarks>
    /// 这一条是固定费用模型的核心：**启用与活动是两件事**。
    /// 把固定成本乘上活动量（而不是启用变量），会让“开着但不生产”免费，
    /// 而调度里“开着待机”恰恰是最常见的状态。
    ///
    /// This is the heart of a fixed-charge model: **activation and activity are two different things**.
    /// Multiplying the fixed cost by the activity instead of the activation makes “on but idle” free,
    /// while “on and idle” is the most common state in scheduling.
    /// </remarks>
    [Fact]
    public void TheActiveIdleCase_ShouldStillPayTheFixedCost()
    {
        FixedChargeFunction<Flt64> charge = Make(fixedCost: 100.0, unitCost: 3.0, minimum: 0.0, maximum: 10.0);

        charge.Evaluate(Values(active: 1.0, activity: 0.0)).Should().Be(new Flt64(100.0));
    }

    /// <summary>启用且有活动量时，固定成本与单位成本相加 / Active and busy pays both parts.</summary>
    [Fact]
    public void TheActiveBusyCase_ShouldPayBothParts()
    {
        FixedChargeFunction<Flt64> charge = Make(fixedCost: 100.0, unitCost: 3.0, minimum: 0.0, maximum: 10.0);

        // 100 固定 + 3 × 4 = 112。
        charge.Evaluate(Values(active: 1.0, activity: 4.0)).Should().Be(new Flt64(112.0));
    }

    /// <summary>启用变量的取值不是 0/1 时无法求值 / A fractional activation cannot be evaluated.</summary>
    /// <remarks>
    /// 返回 `null` 而不是“按 0.5 算”：**0.5 不是“部分启用”，是这个模型还不该被求值。**
    /// 给个近似值会让求解器在松弛解上拿到一个看似合理的成本，而那个成本没有任何意义。
    ///
    /// Returns null rather than “computing at 0.5”: **0.5 is not “partly activated”, it means this model is not
    /// ready to evaluate.** Approximating would hand the solver a plausible-looking cost for a relaxed solution,
    /// and that cost means nothing.
    /// </remarks>
    [Fact]
    public void TheFractionalActivation_ShouldNotEvaluate()
    {
        FixedChargeFunction<Flt64> charge = Make(fixedCost: 100.0, unitCost: 3.0, minimum: 0.0, maximum: 10.0);

        charge.Evaluate(Values(active: 0.5, activity: 4.0)).Should().BeNull();
    }

    /// <summary>没启用却活动，是模型不一致 / Busy while inactive is an inconsistent model.</summary>
    [Fact]
    public void TheBusyInactiveCase_ShouldNotEvaluate()
    {
        FixedChargeFunction<Flt64> charge = Make(fixedCost: 100.0, unitCost: 3.0, minimum: 0.0, maximum: 10.0);

        charge.Evaluate(Values(active: 0.0, activity: 4.0)).Should().BeNull();
    }

    /// <summary>启用却超出活动区间，是模型不一致 / Active but outside the interval is inconsistent.</summary>
    [Fact]
    public void TheOutOfRangeCase_ShouldNotEvaluate()
    {
        FixedChargeFunction<Flt64> charge = Make(fixedCost: 100.0, unitCost: 3.0, minimum: 2.0, maximum: 10.0);

        charge.Evaluate(Values(active: 1.0, activity: 1.0)).Should().BeNull("below the minimum batch");
        charge.Evaluate(Values(active: 1.0, activity: 11.0)).Should().BeNull("above the maximum");
    }

    /// <summary>负的固定成本要当场拒绝 / A negative fixed cost is rejected outright.</summary>
    /// <remarks>
    /// 负的固定成本会变成“启用反而省钱”，于是求解器把每个变量都打开——
    /// 这不是“更优解”，是模型写错了。
    ///
    /// A negative fixed cost means “activating pays you”, so the solver opens everything —
    /// not a better solution, a broken model.
    /// </remarks>
    [Fact]
    public void TheNegativeFixedCost_ShouldBeRejected()
    {
        Action negative = () => Make(fixedCost: -1.0, unitCost: 3.0, minimum: 0.0, maximum: 10.0);

        negative.Should().Throw<ArgumentException>(
            "a negative fixed cost turns activation into a reward and opens every facility");
    }

    /// <summary>活动量只给一个界要拒绝 / Supplying only one bound for the activity is rejected.</summary>
    [Fact]
    public void TheHalfBoundedActivity_ShouldBeRejected()
    {
        LinearPolynomial<Flt64> activity = Activity();
        Action halfBounded = () => new FixedChargeFunction<Flt64>(
            activation: Activation,
            fixedCost: new Flt64(100.0),
            converter: IIntoValue<Flt64>.Identity,
            activity: activity,
            unitCost: new Flt64(3.0),
            minimumActive: new Flt64(0.0),
            maximumActivity: null);

        halfBounded.Should().Throw<ArgumentException>(
            "one missing bound leaves the activity link unconstrained on that side");
    }

    /// <summary>造一个固定费用函数 / Build one.</summary>
    private static FixedChargeFunction<Flt64> Make(double fixedCost, double unitCost, double minimum, double maximum) =>
        new(
            activation: Activation,
            fixedCost: new Flt64(fixedCost),
            converter: IIntoValue<Flt64>.Identity,
            activity: Activity(),
            unitCost: new Flt64(unitCost),
            minimumActive: new Flt64(minimum),
            maximumActivity: new Flt64(maximum));

    private static LinearPolynomial<Flt64> Activity() => new(
        new[] { new LinearMonomial<Flt64>(new Flt64(1.0), ActivityVar) },
        Flt64.Zero);

    private static Dictionary<ISymbol, Flt64> Values(double active, double activity) => new() {
        [Activation] = new Flt64(active),
        [ActivityVar] = new Flt64(activity),
    };

    private static readonly IntVar Activation = new("y");
    private static readonly RealVar ActivityVar = new("x");
}
