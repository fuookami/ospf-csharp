// 分段线性最大装载重量的判据 / Criteria for the piecewise-linear maximum load weight.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **分段线性最大装载重量** / Piecewise-linear maximum load weight.
/// </summary>
/// <remarks>
/// `PositionMaximumLoadWeight` 支持"按零油重分段"：同一舱位在飞机轻时允许装得多、重时允许装得少，为别的舱位留余量。
/// 它有三处容易被"顺手写得更合理"的地方：
///
/// 1. **阶跃，不是插值**。那条平滑的 `UnivariateLinearPiecewiseFunction` 路径**是注释掉的**，生效的是分段查找。
/// 把它"补全"成插值是很自然的冲动，但那会把跨语言行为改掉。
/// 2. **零油重超过最后一段时取最后一段**，不是"无限制"。取无限制会让已经超重的飞机允许再装任意重量。
/// 3. **`mlw` 是各段最大值**，不是第一段或最后一段——`y` 的变量上界用的就是它。
///
/// The implementation supports "segments by zero-fuel weight": a position takes more when the aircraft is light and less when it is heavy, leaving room for other positions. Three places
/// invite a "helpful" rewrite: (1) it is a **step, not an interpolation** — its smooth path is **commented out** and the segment lookup is what runs; (2) a zero-fuel
/// weight past the last segment takes the last segment, not "no limit", which would let an already-heavy aircraft load anything; (3) `mlw` is the **maximum across segments**,
/// not the first or last, and that is what bounds `y`.
/// </remarks>
public class Demo2MaxLoadWeightTests {
    private static MaxLoadWeightLimit Piecewise(params (double Mlw, double MinZfw, double? MaxZfw)[] segments) {
        var list = new List<MaxLoadWeightLimit.Segment>();
        foreach ((double mlw, double minZfw, double? maxZfw) in segments) {
            list.Add(new MaxLoadWeightLimit.Segment(mlw, minZfw, maxZfw));
        }
        return new MaxLoadWeightLimit(list);
    }

    /// <summary>
    /// **`mlw` 是各段最大值，不是第一段** / `mlw` is the maximum across segments, not the first.
    /// </summary>
    /// <remarks>
    /// 成对：三段分别是 800/600/400，`mlw` 必须是 800。取第一段碰巧也对（这里第一段最大），所以**故意把最大值放在中间**
    /// 才能区分"取最大值"与"取第一段"与"取最后一段"。
    ///
    /// Paired: with segments 800/600/400, `mlw` must be 800. Taking the first would coincidentally agree here, so the **maximum is deliberately in the middle** to distinguish
    /// "take the max" from "take the first" and "take the last".
    /// </remarks>
    [Fact]
    public void Mlw_ShouldBeTheMaximumAcrossSegments() {
        MaxLoadWeightLimit limit = Piecewise(
            (600.0, 0.0, 50.0),
            (800.0, 50.0, 100.0),
            (400.0, 100.0, null));

        limit.Mlw.Should().BeApproximately(800.0, 1e-9, "the maximum is in the middle segment");
        limit.Mlw.Should().NotBeApproximately(600.0, 1e-9, "not the first segment");
        limit.Mlw.Should().NotBeApproximately(400.0, 1e-9, "not the last segment");
        limit.Segments.Should().HaveCount(3);
        limit.Piecewise.Should().BeTrue();
    }

    /// <summary>
    /// **段按最小零油重升序排列** / Segments are ordered by minimum zero-fuel weight.
    /// </summary>
    /// <remarks>
    /// 与"取值不受输入顺序影响"成对：乱序传入后 `At` 的结果必须与顺序传入一致。不排序时 `At` 的线性查找会先命中错误的那一段，
    /// 而 `Segments[0]`／最后一段这两条边界分支也会用错段。
    ///
    /// Paired with "the value does not depend on input order": passing them shuffled must give the same `At` results as passing them sorted. Without sorting, `At`'s linear scan
    /// hits the wrong segment first, and both boundary branches (`Segments[0]`, last) use the wrong one.
    /// </remarks>
    [Fact]
    public void Segments_ShouldBeSortedAndOrderIndependent() {
        MaxLoadWeightLimit shuffled = Piecewise(
            (400.0, 100.0, null),
            (800.0, 50.0, 100.0),
            (600.0, 0.0, 50.0));
        MaxLoadWeightLimit sorted = Piecewise(
            (600.0, 0.0, 50.0),
            (800.0, 50.0, 100.0),
            (400.0, 100.0, null));

        shuffled.Segments[0].MinZfw.Should().BeApproximately(0.0, 1e-9);
        shuffled.Segments[2].MinZfw.Should().BeApproximately(100.0, 1e-9);
        shuffled.Mlw.Should().BeApproximately(sorted.Mlw, 1e-9);

        foreach (double zfw in new[] { 0.0, 49.0, 50.0, 99.0, 100.0, 200.0 }) {
            shuffled.At(zfw).Should().BeApproximately(sorted.At(zfw), 1e-9,
                "the value must not depend on the order the segments were given in");
        }
    }

    /// <summary>
    /// **阶跃，不是插值** / A step, not an interpolation.
    /// </summary>
    /// <remarks>
    /// 段内取值必须**恒等于该段的 `mlw`**，不随零油重变化。插值的话 60 与 90 会给出两个不同的数；阶跃下两者都是 800。
    /// 那条插值路径是注释掉的——把它"补全"是很自然的冲动，这条判据就是拦这个的。
    ///
    /// Inside a segment the value must be **exactly that segment's `mlw`**, independent of the zero-fuel weight: an interpolation would give two different numbers at 60 and 90 while
 /// a step gives 800 for both. Its interpolation path is commented out — "completing" it is the natural impulse this criterion blocks.
    /// </remarks>
    [Fact]
    public void At_ShouldBeAStepNotAnInterpolation() {
        MaxLoadWeightLimit limit = Piecewise(
            (600.0, 0.0, 50.0),
            (800.0, 50.0, 100.0),
            (400.0, 100.0, null));

        limit.At(60.0).Should().BeApproximately(800.0, 1e-9);
        limit.At(90.0).Should().BeApproximately(800.0, 1e-9,
            "inside a segment the value does not vary — an interpolation would differ from At(60)");
        limit.At(60.0).Should().BeApproximately(limit.At(90.0), 1e-9);
        limit.At(10.0).Should().BeApproximately(600.0, 1e-9);
        limit.At(150.0).Should().BeApproximately(400.0, 1e-9);
    }

    /// <summary>
    /// **低于第一段用第一段，高于最后一段用最后一段** / Below the first uses the first; above the last uses the last.
    /// </summary>
    /// <remarks>
    /// 两条边界成对。第二条尤其重要：超过最后一段说明飞机已经超重，允许装载量应当**最小**而不是无限制。写成"无限制"会让超重飞机
    /// 继续装载而不被这一处拦下。
    ///
    /// Both boundaries paired. The second matters most: past the last segment the aircraft is already heavy and the allowed load should be the **smallest**, not unlimited. Returning
    /// "no limit" would let an overweight aircraft keep loading past this check.
    /// </remarks>
    [Fact]
    public void At_ShouldClampAtBothEnds() {
        MaxLoadWeightLimit limit = Piecewise(
            (600.0, 50.0, 100.0),
            (400.0, 100.0, null));

        limit.At(0.0).Should().BeApproximately(600.0, 1e-9, "below the first bound uses the first segment");
        limit.At(10000.0).Should().BeApproximately(400.0, 1e-9,
            "past the last bound uses the last segment, not an unlimited value");
        limit.At(10000.0).Should().NotBeApproximately(double.PositiveInfinity, 1e-9);
    }

    /// <summary>
    /// **最后一段带上界时，超出仍取最后一段** / With a bounded last segment, overshooting still takes the last segment.
    /// </summary>
    /// <remarks>
    /// 与"最后一段无上界"成对，而且**这一条才是唯一能走到兜底分支的形态**：最后一段 `MaxZfw = null` 时，任何大的零油重都会在循环里
    /// 被最后一段接住，兜底那行**不可达**——所以用无上界的 fixture 测"超出取最后一段"是空判据，改成"取最大值"也照样绿。
    /// 最后一段带上界时，超出循环就落空，兜底才是唯一出口。
    ///
    /// Paired with the unbounded-last-segment case, and **this is the only shape that reaches the fallback**: with `MaxZfw = null` on the last segment any large zero-fuel weight is
    /// caught inside the loop, so the fallback line is **unreachable** — testing "overshoot takes the last segment" on an unbounded fixture is vacuous, and swapping it for
    /// "take the maximum" stays green. With a bounded last segment the loop falls through and the fallback is the only exit.
    /// </remarks>
    [Fact]
    public void At_ShouldClampToTheLastSegmentWhenItHasAnUpperBound() {
        MaxLoadWeightLimit limit = Piecewise(
            (600.0, 50.0, 100.0),
            (400.0, 100.0, 500.0));

        limit.At(150.0).Should().BeApproximately(400.0, 1e-9, "inside the last segment");
        limit.At(10000.0).Should().BeApproximately(400.0, 1e-9,
            "past a bounded last segment still takes that segment's value");
        limit.At(10000.0).Should().NotBeApproximately(limit.Mlw, 1e-9,
            "the fallback is the last segment's own value, not the maximum across segments");
    }

    /// <summary>
    /// **无分段时 `At` 就是那个常数** / With no segments, `At` is the constant.
    /// </summary>
    /// <remarks>
    /// 与分段形态成对。把"无分段"当成"没有上界"会让最常见的形态（单一常数）退化成无限制，而它恰恰是所有老调用方在用的那种。
    ///
    /// Paired with the piecewise form. Treating "no segments" as "no bound" would degrade the most common form — a single constant, which is what every pre-existing caller uses —
    /// into an unlimited one.
    /// </remarks>
    [Fact]
    public void At_ShouldBeTheConstantWithoutSegments() {
        var constant = new MaxLoadWeightLimit(750.0);

        constant.Piecewise.Should().BeFalse();
        constant.Mlw.Should().BeApproximately(750.0, 1e-9);
        constant.At(0.0).Should().BeApproximately(750.0, 1e-9);
        constant.At(99999.0).Should().BeApproximately(750.0, 1e-9,
            "a constant specification does not vary with the zero-fuel weight");
    }

    /// <summary>
    /// **空分段是错误** / An empty segment list is an error.
    /// </summary>
    /// <remarks>
    /// `assert(piecewise.isNotEmpty)` 在 Kotlin 里默认关闭；C# 的异常在 Release 下也生效。静默接受空分段会得到一个
    /// "永远返回 0"的规格——它不会崩，只会让每个舱位都不可用。
    ///
    /// `assert` is off by default in Kotlin while C#'s exception fires in Release too. Silently accepting an empty list yields a specification that returns zero
    /// forever — nothing crashes, every position just becomes unusable.
    /// </remarks>
    [Fact]
    public void Constructor_ShouldRejectAnEmptySegmentList() {
        Action act = () => _ = new MaxLoadWeightLimit(Array.Empty<MaxLoadWeightLimit.Segment>());
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
