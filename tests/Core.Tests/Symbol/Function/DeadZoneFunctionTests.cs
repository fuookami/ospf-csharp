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
/// 死区之内没有反应，之外按超出量增长 / No response inside the band, linear in the excess outside it.
/// </summary>
/// <remarks>
/// 补 DeadZoneFunction（基线 CompositeFunctions.kt），形状是 max(|x| - delta, 0)。
///
/// 这个形状有三处容易写错，而三处都不会报错：
/// ① 死区之内返回的是 |x| 而不是 0；② 负的一侧忘了取绝对值；③ delta 为负时死区“消失”而没人发现。
///
/// Three things are easy to get wrong here, and none of them throws:
/// ① returning |x| instead of 0 inside the band; ② forgetting the absolute value on the negative side;
/// ③ a negative delta making the band vanish unnoticed.
/// </remarks>
public class DeadZoneFunctionTests
{
    /// <summary>死区之内返回 0 / Inside the band it returns zero.</summary>
    [Fact]
    public void TheValueInsideTheBand_ShouldBeZero()
    {
        DeadZoneFunction<Flt64> dead = Make(delta: 2.0);

        dead.Evaluate(Values(0.0)).Should().Be(Flt64.Zero);
        dead.Evaluate(Values(1.5)).Should().Be(Flt64.Zero);
        dead.Evaluate(Values(-1.5)).Should().Be(Flt64.Zero);
    }

    /// <summary>边界上恰好是 0，不是 delta / At the boundary it is exactly zero, not delta.</summary>
    /// <remarks>
    /// 这一条挡的是“把死区写成 x 减 delta 再取大”：
    /// 那样在 x = delta 处会返回 0（碰巧对），但在 x = 0 处会返回 max(-delta, 0) = 0（也碰巧对），
    /// 而在 x = 2*delta 处会返回 delta 而不是 delta —— 三个点都对，所以抽查不出来。
    ///
    /// This blocks “max(x - delta, 0)”: at x = delta it returns 0 (right by accident), at x = 0 it returns
    /// max(-delta, 0) = 0 (also right by accident), and at x = 2*delta it returns delta instead of delta — three
    /// points all agree, so spot checks cannot reveal it.
    /// </remarks>
    [Fact]
    public void TheBoundary_ShouldBeZero()
    {
        DeadZoneFunction<Flt64> dead = Make(delta: 2.0);

        dead.Evaluate(Values(2.0)).Should().Be(Flt64.Zero);
        dead.Evaluate(Values(-2.0)).Should().Be(Flt64.Zero);
    }

    /// <summary>超出之后返回超出量 / Outside it returns the excess.</summary>
    [Fact]
    public void TheValueOutsideTheBand_ShouldBeTheExcess()
    {
        DeadZoneFunction<Flt64> dead = Make(delta: 2.0);

        dead.Evaluate(Values(5.0)).Should().Be(new Flt64(3.0));
        dead.Evaluate(Values(-5.0)).Should().Be(new Flt64(3.0),
            "the negative side takes the absolute value, so it grows the same way");
    }

    /// <summary>函数关于 0 对称 / The function is symmetric about zero.</summary>
    /// <remarks>
    /// 这一条是“负的一侧忘了取绝对值”的直接判据：
    /// 少了绝对值，负的一侧会恒为 0，而正的一侧完全正常——只测正数永远发现不了。
    ///
    /// This is the direct criterion for “forgot the absolute value on the negative side”:
    /// without it the negative side is identically zero while the positive side looks perfect,
    /// and testing only positive inputs never reveals it.
    /// </remarks>
    [Fact]
    public void TheFunction_ShouldBeSymmetric()
    {
        DeadZoneFunction<Flt64> dead = Make(delta: 2.0);

        foreach (double magnitude in new[] { 0.0, 1.0, 2.0, 3.5, 10.0 }) {
            dead.Evaluate(Values(magnitude)).Should().Be(
                dead.Evaluate(Values(-magnitude)),
                $"a dead zone is symmetric, and it fails to be at |x| = {magnitude}");
        }
    }

    /// <summary>负的 delta 要当场拒绝 / A negative delta is rejected outright.</summary>
    [Fact]
    public void TheNegativeDelta_ShouldBeRejected()
    {
        Action negative = () => Make(delta: -1.0);

        negative.Should().Throw<ArgumentException>(
            "a negative half-width silently removes the dead zone while the caller believes a tolerance is set");
    }

    /// <summary>零 delta 是合法的（退化成 |x|）/ A zero delta is legal and degenerates to |x|.</summary>
    [Fact]
    public void TheZeroDelta_ShouldBehaveLikeAbsoluteValue()
    {
        DeadZoneFunction<Flt64> dead = Make(delta: 0.0);

        dead.Evaluate(Values(3.0)).Should().Be(new Flt64(3.0));
        dead.Evaluate(Values(-3.0)).Should().Be(new Flt64(3.0));
        dead.Evaluate(Values(0.0)).Should().Be(Flt64.Zero);
    }

    /// <summary>造一个死区函数 / Build one.</summary>
    private static DeadZoneFunction<Flt64> Make(double delta) => new(
        input: new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(1.0), Variable) },
            Flt64.Zero),
        delta: new Flt64(delta),
        converter: IIntoValue<Flt64>.Identity);

    private static Dictionary<ISymbol, Flt64> Values(double value) =>
        new() { [Variable] = new Flt64(value) };

    private static readonly RealVar Variable = new("x");
}
