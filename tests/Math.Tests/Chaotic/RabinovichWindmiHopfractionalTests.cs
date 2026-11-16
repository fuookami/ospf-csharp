#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Chaotic;
using Fuookami.Ospf.Math.Geometry;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Chaotic;

/// <summary>
/// 两条方程的不对称是定义，不是笔误 / The asymmetry between the two equations is the definition.
/// </summary>
/// <remarks>
/// 补 `RabinovichFabrikantAttractor` / `WindmiAttractor` / `HopfractionalMap` 与迭代器。
///
/// **三处坑都不会报错**：
/// ① Rabinovich–Fabrikant 的 `γ` 只乘第一条方程，**而两条都带 `xz` 项**；
/// ② Windmi 的 `dz` 里 `e` 乘的是 **`x²`**，不是 `x·z` 也不是 `x·y`；
/// ③ Hopfractional 的 `(k·x)^q` 在负底数配分数指数时是 NaN。
///
/// **两个失效参数**：Windmi 的 `g`、Hopfractional 迭代器的 `iterations`。
///
/// **Three traps, none of which throws**:
/// ① Rabinovich–Fabrikant's `γ` multiplies only the first equation, **while both carry an `xz` term**;
/// ② Windmi's `dz` has `e` multiplying **`x²`**, not `x·z` and not `x·y`;
/// ③ Hopfractional's `(k·x)^q` is NaN for a negative base with a fractional exponent.
///
/// **Two dead parameters**: Windmi's `g` and the Hopfractional iterator's `iterations`.
/// </remarks>
public class RabinovichWindmiHopfractionalTests
{
    /// <summary>**`γ` 不进第二条方程** / `γ` does not enter the second equation.</summary>
    /// <remarks>
    /// **把 `γ` 设成 0，`dy` 里仍然必须有 `xz` 项**——
    /// **如果实现者把 `γ` 也乘到了 `dy` 上，`γ = 0` 会让整个 `dy` 退化，这条立刻红。**
    /// **而"轨迹呈混沌"对两种写法都成立。**
    ///
    /// **With `γ` set to zero, `dy` must still carry the `xz` term** —
    /// **had `γ` been multiplied into `dy` too, `γ = 0` would flatten `dy` entirely, and this goes red at once.**
    /// **"Chaotic-looking" holds under both spellings.**
    /// </remarks>
    [Fact]
    public void TheGamma_ShouldNotEnterTheSecondEquation()
    {
        // γ = 0 → dx 恒为 0；dy = αx − y + xz 仍含 xz。
        // Gamma = 0 means dx is identically zero, while dy = alpha*x - y + x*z still carries x*z.
        var attractor = new RabinovichFabrikantAttractor<Flt64>(
            new Flt64(1.1), new Flt64(0.0), new Flt64(0.01));

        Point<Dim3, Flt64> next = attractor.Invoke(
            PointFactory.point3(new Flt64(0.5), new Flt64(0.2), new Flt64(3.0)));

        // dx = 0*(y - x + xz) = 0 → x 不变。
        next[0].Value.Should().BeApproximately(0.5, 1e-15, "with gamma zero the first equation is flat");

        // dy = 1.1*0.5 - 0.2 + 0.5*3.0 = 1.85，不是 1.1*0.5 - 0.2 = 0.35。
        double expectedDy = 1.1 * 0.5 - 0.2 + 0.5 * 3.0;
        next[1].Value.Should().BeApproximately(0.2 + 0.01 * expectedDy, 1e-12,
            "the xz term in dy must survive gamma being zero");
    }

    /// <summary>**Rabinovich–Fabrikant 一步与手算一致** / One step matches the hand computation.</summary>
    [Fact]
    public void TheRabinovichFabrikantStep_ShouldMatchTheEquations()
    {
        const double x = -1.0;
        const double y = 0.0;
        const double z = 0.5;

        double dx = 0.9 * (y - x + x * z);
        double dy = 1.1 * x - y + x * z;
        double dz = -x * y - z;

        Point<Dim3, Flt64> next = RabinovichFabrikantAttractor<Flt64>.Create()
            .Invoke(PointFactory.point3(new Flt64(x), new Flt64(y), new Flt64(z)));

        next[0].Value.Should().BeApproximately(x + 0.01 * dx, 1e-12);
        next[1].Value.Should().BeApproximately(y + 0.01 * dy, 1e-12);
        next[2].Value.Should().BeApproximately(z + 0.01 * dz, 1e-12);
    }

    /// <summary>**Windmi 的 `e` 乘的是 `x²`，不是 `xz`** / Windmi's `e` multiplies `x²`, not `xz`.</summary>
    /// <remarks>
    /// **取 `z = 0` 时 `x·z = 0` 而 `x² = 4`**——**两种写法在这个输入下给出完全不同的 `dz`，**
    /// **而"轨迹有界"两边都成立。**
    ///
    /// **With `z = 0`, `x·z` is zero while `x²` is four** — **the two spellings give completely different `dz` on this
    /// input**, **while "bounded" holds for both.**
    /// </remarks>
    [Fact]
    public void TheWindmiE_ShouldMultiplyXSquared()
    {
        // 取 x=2, y=0, z=0：dz = c*0 + 0 - e*4 = -0.8（若误写成 x*z 则为 0）。
        // With x=2, y=0, z=0: dz = -e*4 = -0.8, while a mistaken x*z would give 0.
        Point<Dim3, Flt64> next = WindmiAttractor<Flt64>.Create()
            .Invoke(PointFactory.point3(new Flt64(2.0), new Flt64(0.0), new Flt64(0.0)));

        next[2].Value.Should().BeApproximately(0.0 + 0.01 * (-0.2 * 4.0), 1e-12,
            "e multiplies x squared, and x*z would be zero here");
    }

    /// <summary>**Windmi 的参数 `g` 不影响轨迹** / Windmi's `g` does not move the trajectory.</summary>
    [Fact]
    public void TheWindmiParameterG_ShouldNotAffectTheTrajectory()
    {
        Point<Dim3, Flt64> start = PointFactory.point3(
            new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));

        Point<Dim3, Flt64> withDefault = WindmiAttractor<Flt64>.Create(g: new Flt64(0.5)).Invoke(start);
        Point<Dim3, Flt64> withOther = WindmiAttractor<Flt64>.Create(g: new Flt64(-999.0)).Invoke(start);

        for (int axis = 0; axis < 3; ++axis) {
            withOther[axis].Value.Should().BeApproximately(withDefault[axis].Value, 1e-15,
                $"axis {axis} must not depend on g, which the equations never mention");
        }
    }

    /// <summary>**Windmi 一步与手算一致** / One Windmi step matches the equations.</summary>
    [Fact]
    public void TheWindmiStep_ShouldMatchTheEquations()
    {
        const double x = 0.3;
        const double y = -0.2;
        const double z = 0.7;

        double dx = 0.2 * (y - x) + 0.5 * x * z;
        double dy = 0.2 * x - x * z + 5.7 * y;
        double dz = 2.2 * z + x * y - 0.2 * x * x;

        Point<Dim3, Flt64> next = WindmiAttractor<Flt64>.Create()
            .Invoke(PointFactory.point3(new Flt64(x), new Flt64(y), new Flt64(z)));

        next[0].Value.Should().BeApproximately(x + 0.01 * dx, 1e-12);
        next[1].Value.Should().BeApproximately(y + 0.01 * dy, 1e-12);
        next[2].Value.Should().BeApproximately(z + 0.01 * dz, 1e-12);
    }

    /// <summary>**Hopfractional 与公式一致，且在 q=2 时负值可算** / Hopfractional matches, and q=2 tolerates negatives.</summary>
    /// <remarks>
    /// **`q = 2` 时 `(k·x)²` 对负底数有定义**，所以整条映射在负值上也成立——
    /// **这正是把"分数阶"限制写进文档的原因：只有非整数 `q` 才对负底数失效。**
    ///
    /// **With `q = 2`, `(k·x)²` is defined for a negative base**, so the map is fine there —
    /// **which is why the limit is documented precisely: only a non-integer `q` fails on a negative base.**
    /// </remarks>
    [Fact]
    public void TheHopfractionalMap_ShouldMatchTheFormula()
    {
        // p=0.5, q=2, k=1, s=1, x=-2 → 分子 1+4=5，分母 1+4=5，结果 0.5*(-2)*1 = -1。
        HopfractionalMap<Flt64>.Create().Invoke(new Flt64(-2.0)).Value
            .Should().BeApproximately(-1.0, 1e-12,
                "with k = s the fraction cancels, leaving p times x");

        // x = 3, k=1, s=1 → 同样抵消，得 1.5。
        HopfractionalMap<Flt64>.Create().Invoke(new Flt64(3.0)).Value
            .Should().BeApproximately(1.5, 1e-12);
    }

    /// <summary>**`k` 与 `s` 不同时结果真的不同** / With k different from s the fraction does not cancel.</summary>
    /// <remarks>
    /// **上一条在 `k = s` 时分子分母相同，任何"忽略 k、s"的实现都会通过**——
    /// **所以必须另有一条让 `k ≠ s`。**
    ///
    /// **The previous criterion has `k = s`, so numerator and denominator coincide and any implementation that ignores
    /// `k` and `s` passes** — **so a second one with `k ≠ s` is required.**
    /// </remarks>
    [Fact]
    public void TheHopfractionalFraction_ShouldDependOnKAndS()
    {
        // p=1, q=2, k=2, s=1, x=1 → 分子 1+4=5，分母 1+1=2 → 1*1*5/2 = 2.5。
        HopfractionalMap<Flt64>.Create(
                p: new Flt64(1.0), q: new Flt64(2.0), k: new Flt64(2.0), s: new Flt64(1.0))
            .Invoke(new Flt64(1.0)).Value
            .Should().BeApproximately(2.5, 1e-12,
                "numerator 1 + (2*1)^2 = 5 and denominator 1 + (1*1)^2 = 2");
    }

    /// <summary>**Hopfractional 迭代器的 `iterations` 不影响输出** / `iterations` changes no output.</summary>
    [Fact]
    public void TheHopfractionalIterations_ShouldNotAffectTheOutput()
    {
        double firstWithThousand = new HopfractionalMapGenerator(
            map: HopfractionalMap<Flt64>.Create(),
            iterations: 1000UL,
            x: new Flt64(0.25)).Invoke().Value;
        double firstWithOne = new HopfractionalMapGenerator(
            map: HopfractionalMap<Flt64>.Create(),
            iterations: 1UL,
            x: new Flt64(0.25)).Invoke().Value;

        firstWithThousand.Should().Be(firstWithOne,
            "iterations is stored and never used, so it cannot change a single step");
    }

    /// <summary>**三个迭代器都先返回当前点再推进** / All three iterators yield the current point first.</summary>
    [Fact]
    public void TheGenerators_ShouldYieldBeforeAdvancing()
    {
        double rabinovich = new RabinovichFabrikantAttractorGenerator(
            attractor: RabinovichFabrikantAttractor<Flt64>.Create(),
            x: PointFactory.point3(new Flt64(0.4), new Flt64(0.4), new Flt64(0.4))).Invoke()[0].Value;
        double windmi = new WindmiAttractorGenerator(
            attractor: WindmiAttractor<Flt64>.Create(),
            x: PointFactory.point3(new Flt64(0.4), new Flt64(0.4), new Flt64(0.4))).Invoke()[0].Value;
        double hopfractional = new HopfractionalMapGenerator(
            map: HopfractionalMap<Flt64>.Create(), x: new Flt64(0.4)).Invoke().Value;

        rabinovich.Should().BeApproximately(0.4, 1e-15,
            "the first call yields the seed, not the image of the seed");
        windmi.Should().BeApproximately(0.4, 1e-15);
        hopfractional.Should().BeApproximately(0.4, 1e-15);
    }
}
