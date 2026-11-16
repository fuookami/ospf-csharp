#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Chaotic;
using Fuookami.Ospf.Math.Geometry;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Chaotic;

/// <summary>
/// 环面用取小数部分，不是取绝对值 / The torus uses the fractional part, not the absolute value.
/// </summary>
/// <remarks>
/// 覆盖 `SinaiMap` / `SprottAttractor` / `WangChenAttractor` 与它们的迭代器。
///
/// **三个映射各有自己的坑，而三处都不会报错**：
/// ① Sinai 的 `mod 1` 是取小数部分——**绝对值会把环面的左侧折到右侧**；
/// ② Sprott 声明了参数 `b` 但公式里从未用它；
/// ③ Wang–Chen 的 `dw` 里没有 `w`。
///
/// **Each map has its own trap, and none throws**.
/// ① Sinai's `mod 1` is the fractional part — **an absolute value folds the torus's left side onto its right**;
/// ② Sprott declares a `b` the equations never use;
/// ③ Wang–Chen's `dw` carries no `w`.
///
/// **这个文件刻意少用 `var`**：**本项目两条样式规则（IDE0007 与 IDE0008）对
/// "类型可从右侧推断"的局部声明给出相反建议，同时启用时无法两全。**
/// **判据直接对表达式与显式基本类型断言，绕开这个矛盾，而不是去调分析器配置。**
///
/// **This file keeps `var` to a minimum**: **two style rules here (IDE0007 and IDE0008) give opposite advice on a local
/// whose type is inferable from the right-hand side, and both cannot be satisfied at once.**
/// **The criteria assert on expressions and on explicit primitives, sidestepping the conflict instead of touching
/// analyzer config.**
/// </remarks>
public class SinaiSprottWangChenTests
{
    /// <summary>**Sinai 映射把负坐标映到 [0,1)，而不是折返** / Sinai maps negatives into [0,1).</summary>
    /// <remarks>
    /// **坐标取 -0.2 时，`mod 1` 给 0.8，`Abs` 给 0.2——环面上那是两个不同的点。**
    /// **两种写法都会让结果"看起来正常"，只有把具体值钉住才分得开。**
    ///
    /// **At -0.2, `mod 1` gives 0.8 and `Abs` gives 0.2 — different points on the torus.**
    /// **Both look fine, so only pinning the actual value separates them.**
    /// </remarks>
    [Fact]
    public void TheSinaiMap_ShouldWrapNegativesIntoTheUnitInterval()
    {
        // r = 0 时映射退化为 x' = (x + y) mod 1、y' = y mod 1，可以手算。
        // With r = 0 the map degenerates to x' = (x + y) mod 1 and y' = y mod 1, which is hand-checkable.
        double newX = SinaiMap<Flt64>.Create(r: new Flt64(0.0))
            .Invoke(PointFactory.point2(new Flt64(-0.2), new Flt64(0.0)))[0].Value;
        double newY = SinaiMap<Flt64>.Create(r: new Flt64(0.0))
            .Invoke(PointFactory.point2(new Flt64(-0.2), new Flt64(0.0)))[1].Value;

        newX.Should().BeApproximately(0.8, 1e-12,
            "the fractional part of -0.2 is 0.8, while an absolute value would give 0.2");
        newY.Should().BeApproximately(0.0, 1e-12, "the fractional part of zero is zero");
    }

    /// <summary>**Sinai 的两坐标都留在 [0,1] 内** / Sinai keeps both coordinates inside the unit interval.</summary>
    [Fact]
    public void TheSinaiMap_ShouldStayInsideTheUnitSquare()
    {
        SinaiMapGenerator generator = new(
            map: SinaiMap<Flt64>.Create(),
            x: PointFactory.point2(new Flt64(0.37), new Flt64(0.91)));

        for (int step = 0; step < 200; ++step) {
            Point<Dim2, Flt64> point = generator.Invoke();
            point[0].Value.Should().BeInRange(0.0, 1.0, $"step {step} keeps x on the torus");
            point[1].Value.Should().BeInRange(0.0, 1.0, $"step {step} keeps y on the torus");
        }
    }

    /// <summary>**Sprott 的参数 `b` 不影响轨迹** / The Sprott `b` does not move the trajectory.</summary>
    /// <remarks>
    /// **`b` 被声明而公式里没有它。保留它是因为那是签名，但"它无效"必须可测**——
    /// **否则下一个人会去调 `b` 并以为在调系统。**
    ///
    /// **The code declares `b` and the equations ignore it. It stays because it is the signature, but "it does
    /// nothing" has to be measurable** — **or the next person tunes `b` believing it tunes the system.**
    /// </remarks>
    [Fact]
    public void TheSprottParameterB_ShouldNotAffectTheTrajectory()
    {
        Point<Dim3, Flt64> start = PointFactory.point3(
            new Flt64(0.1), new Flt64(0.2), new Flt64(0.3));

        Point<Dim3, Flt64> withDefault = SprottAttractor<Flt64>.Create(b: new Flt64(28.0)).Invoke(start);
        Point<Dim3, Flt64> withOther = SprottAttractor<Flt64>.Create(b: new Flt64(-999.0)).Invoke(start);

        for (int axis = 0; axis < 3; ++axis) {
            withOther[axis].Value.Should().BeApproximately(withDefault[axis].Value, 1e-15,
                $"axis {axis} must not depend on b, which the equations never mention");
        }
    }

    /// <summary>**Sprott 的一步与手算一致** / One Sprott step matches the hand computation.</summary>
    [Fact]
    public void TheSprottStep_ShouldMatchTheEquations()
    {
        const double x = 0.1;
        const double y = 0.2;
        const double z = 0.3;
        const double c = 8.0 / 3.0;

        double dx = 10.0 * (y - x);
        double dy = y * (c - x) - y;
        double dz = x * y - c * z;

        Point<Dim3, Flt64> next = new SprottAttractor<Flt64>(
            new Flt64(10.0), new Flt64(28.0), new Flt64(c), new Flt64(0.01))
            .Invoke(PointFactory.point3(new Flt64(x), new Flt64(y), new Flt64(z)));

        next[0].Value.Should().BeApproximately(x + 0.01 * dx, 1e-12);
        next[1].Value.Should().BeApproximately(y + 0.01 * dy, 1e-12);
        next[2].Value.Should().BeApproximately(z + 0.01 * dz, 1e-12);
    }

    /// <summary>**Wang–Chen 的 `dw` 不带 `w`** / Wang–Chen's `dw` carries no `w`.</summary>
    /// <remarks>
    /// **两个初值只差 `w`，一步之后 `w` 的变化量必须相同**——**因为 `dw = −yz` 与 `w` 无关。**
    /// **写成 `dw = −δw` 时这条会红：那时 `w` 越大，变化量越大。**
    ///
    /// **Two starts differing only in `w` must show the same change in `w` after one step** —
    /// **because `dw = −yz` does not involve `w`.**
    /// **With `dw = −δw` this goes red: the larger `w` then changes faster.**
    /// </remarks>
    [Fact]
    public void TheWangChenStep_ShouldNotDependOnW()
    {
        double movedHigh = WangChenAttractor<Flt64>.Create()
            .Invoke(PointFactory.point4(
                new Flt64(0.1), new Flt64(0.2), new Flt64(0.3), new Flt64(5.0)))[3].Value - 5.0;
        double movedLow = WangChenAttractor<Flt64>.Create()
            .Invoke(PointFactory.point4(
                new Flt64(0.1), new Flt64(0.2), new Flt64(0.3), new Flt64(0.0)))[3].Value;

        movedHigh.Should().BeApproximately(movedLow, 1e-12,
            "the change in w is -h*y*z, which knows nothing about w itself");
    }

    /// <summary>**Wang–Chen 的四个分量都按公式动** / All four Wang–Chen components follow the equations.</summary>
    [Fact]
    public void TheWangChenStep_ShouldMatchTheEquations()
    {
        const double x = 0.1;
        const double y = 0.2;
        const double z = 0.3;
        const double w = 0.4;
        const double h = 0.01;

        double dx = 10.0 * (y - x);
        double dy = 40.0 * x - x * z + w;
        double dz = x * y - 2.5 * z;
        double dw = -y * z;

        Point<Dim4, Flt64> next = new WangChenAttractor<Flt64>(
            new Flt64(10.0), new Flt64(40.0), new Flt64(1.0), new Flt64(2.5), new Flt64(h))
            .Invoke(PointFactory.point4(
                new Flt64(x), new Flt64(y), new Flt64(z), new Flt64(w)));

        next[0].Value.Should().BeApproximately(x + h * dx, 1e-12);
        next[1].Value.Should().BeApproximately(y + h * dy, 1e-12);
        next[2].Value.Should().BeApproximately(z + h * dz, 1e-12);
        next[3].Value.Should().BeApproximately(w + h * dw, 1e-12);
    }

    /// <summary>**三个迭代器都先返回当前点再推进** / All three iterators yield the current point, then advance.</summary>
    /// <remarks>
    /// **顺序反了会让整条轨迹偏移一步**——**而"轨迹有界"与"轨迹呈混沌"都不会因此变红。**
    ///
    /// **Reversing the order shifts the whole trajectory by one step** — **while "bounded" and "chaotic-looking" both
    /// stay green.**
    /// </remarks>
    [Fact]
    public void TheGenerators_ShouldYieldBeforeAdvancing()
    {
        double sinai = new SinaiMapGenerator(
            map: SinaiMap<Flt64>.Create(r: new Flt64(0.0)),
            x: PointFactory.point2(new Flt64(0.1), new Flt64(0.0))).Invoke()[0].Value;
        double sprott = new SprottAttractorGenerator(
            attractor: SprottAttractor<Flt64>.Create(),
            x: PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1))).Invoke()[0].Value;
        double wangChen = new WangChenAttractorGenerator(
            attractor: WangChenAttractor<Flt64>.Create(),
            x: PointFactory.point4(
                new Flt64(0.1), new Flt64(0.1), new Flt64(0.1), new Flt64(0.1))).Invoke()[0].Value;

        sinai.Should().BeApproximately(0.1, 1e-12,
            "the first call yields the seed, not the image of the seed");
        sprott.Should().BeApproximately(0.1, 1e-12);
        wangChen.Should().BeApproximately(0.1, 1e-12);
    }
}
