#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Core.Solver.Value;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// **截断函数不是新原语，是把 max 与 min 拼起来** / Clamp is not a primitive; it is max and min composed.
/// </summary>
/// <remarks>
/// 结论是"`core` 剩下的缺口不是少几个类，是少几层"。**先验证这个结论对不对**，
/// 结果发现**函数层已经存在**：`IMathFunctionSymbol`、`IHasResultPolynomial`、`FunctionSymbol.cs`
/// 与 **52 个函数类**都在，**缺的是具体函数，不是那一层。**
///
/// **所以补一个函数是小增量**——**`ClampFunction` 直接用已有的 `MaxFunction` 与 `MinFunction` 拼。**
///
/// A survey concluded "what `core` lacks is layers, not classes". **That conclusion was then checked first** and found
/// **the function layer already exists**: `IMathFunctionSymbol`, `IHasResultPolynomial`, `FunctionSymbol.cs`
/// and **52 function classes** are all there — **what is missing is individual functions, not the layer.**
///
/// **So adding one is a small increment** — **`ClampFunction` composes the existing `MaxFunction` and `MinFunction`.**
/// </remarks>
public class ClampFunctionTests
{
    /// <summary>**夹在区间内的值原样返回** / A value inside the interval comes back unchanged.</summary>
    [Fact]
    public void TheValueInsideTheInterval_ShouldPassThrough()
    {
        ClampFunction<Flt64> clamp = Make(-10.0, 10.0);

        clamp.Evaluate(Values(3.0)).Should().Be(new Flt64(3.0));
    }

    /// <summary>**低于下界的值被抬到下界** / A value below the lower bound is raised to it.</summary>
    [Fact]
    public void TheValueBelowTheLowerBound_ShouldBecomeTheLowerBound()
    {
        ClampFunction<Flt64> clamp = Make(-10.0, 10.0);

        clamp.Evaluate(Values(-42.0)).Should().Be(new Flt64(-10.0));
    }

    /// <summary>**高于上界的值被压到上界** / A value above the upper bound is lowered to it.</summary>
    [Fact]
    public void TheValueAboveTheUpperBound_ShouldBecomeTheUpperBound()
    {
        ClampFunction<Flt64> clamp = Make(-10.0, 10.0);

        clamp.Evaluate(Values(42.0)).Should().Be(new Flt64(10.0));
    }

    /// <summary>**边界值本身不被改动** / The bounds themselves are unchanged.</summary>
    /// <remarks>
    /// **这是"`<` 与 `<=` 写反了也照样过"的那类判据**：只测"区间内"与"区间外"，
    /// **边界上写反比较符不会有任何测试发现。**
    ///
    /// **This is the kind of criterion where `<` versus `<=` passes either way**: test only "inside" and "outside"
    /// and a reversed comparator at the boundary goes unnoticed.
    /// </remarks>
    [Fact]
    public void TheBoundsThemselves_ShouldBeUnchanged()
    {
        ClampFunction<Flt64> clamp = Make(-10.0, 10.0);

        clamp.Evaluate(Values(-10.0)).Should().Be(new Flt64(-10.0));
        clamp.Evaluate(Values(10.0)).Should().Be(new Flt64(10.0));
    }

    /// <summary>**下界大于上界要当场拒绝** / A lower bound above the upper one is rejected outright.</summary>
    /// <remarks>
    /// **空区间会让整个模型不可行，而报错会指向求解器而不是这里**——
    /// **所以拒绝要发生在构造时，让错误落在能看懂的地方。**
    ///
    /// **An empty interval makes the whole model infeasible and the error points at the solver instead of here** —
    /// **so the rejection belongs at construction, where the message makes sense.**
    /// </remarks>
    [Fact]
    public void TheInvertedInterval_ShouldBeRejectedAtConstruction()
    {
        Action inverted = () => Make(10.0, -10.0);

        inverted.Should().Throw<ArgumentException>(
            "an empty interval is a caller mistake, not a degenerate clamp");
    }

    /// <summary>**下界等于上界是合法的** / A degenerate interval is legal.</summary>
    /// <remarks>
    /// **`lower == upper` 是"常数函数"，不是错误**——
    /// **把它也拒绝掉会让"夹到一个定值"这种正当用法无法表达。**
    ///
    /// **`lower == upper` is a constant function, not an error** —
    /// **rejecting it too would make "clamp to a fixed value" inexpressible.**
    /// </remarks>
    [Fact]
    public void TheDegenerateInterval_ShouldBeAccepted()
    {
        ClampFunction<Flt64> clamp = Make(5.0, 5.0);

        clamp.Evaluate(Values(-100.0)).Should().Be(new Flt64(5.0));
        clamp.Evaluate(Values(100.0)).Should().Be(new Flt64(5.0));
    }

    /// <summary>造一个 `[lower, upper]` 的截断函数，输入是单个变量 / Build a clamp over one variable.</summary>
    /// <remarks>
    /// **变量实例必须与 `Values` 里用的那个是同一个**——
    /// **第一版在 `Make` 里 `new RealVar("x")`，而 `Values` 用静态 `Variable`，
    /// 于是赋值表里根本没有输入多项式引用的那个符号，`Evaluate` 会返回 `null` 而判据"看起来对"。**
    ///
    /// **The variable instance must be the same one `Values` uses** —
    /// **the first version created `new RealVar("x")` inside `Make` while `Values` used a static `Variable`,
    /// so the binding table did not contain the symbol the polynomial referenced and `Evaluate` returned `null`,
    /// with the criteria looking plausible.**
    /// </remarks>
    private static ClampFunction<Flt64> Make(double lower, double upper)
    {
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(1.0), Variable) },
            new Flt64(0.0));

        return new ClampFunction<Flt64>(
            input: polynomial,
            lower: new Flt64(lower),
            upper: new Flt64(upper),
            converter: IIntoValue<Flt64>.Identity);
    }

    /// <summary>给变量赋值 / Bind the variable.</summary>
    private static Dictionary<ISymbol, Flt64> Values(double value) =>
        new() { [Variable] = new Flt64(value) };

    private static readonly RealVar Variable = new("x");
}
