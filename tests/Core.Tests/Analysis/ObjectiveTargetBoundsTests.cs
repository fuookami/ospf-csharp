#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using System;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 目标值到 CP 整数边界的映射 / The mapping from an objective target to a CP integer bound.
/// </summary>
/// <remarks>
/// **舍入方向是全部价值所在。** 方向反了不会报错，
/// **只会让可行域悄悄变宽或变窄。**
///
/// **The rounding direction is the whole value here.** Getting it wrong raises nothing; **it quietly widens or narrows the feasible region.**
/// </remarks>
public class ObjectiveTargetBoundsTests {
    private static ObjectiveTarget AtLeast(double value) =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(value));

    private static ObjectiveTarget AtMost(double value) =>
        ObjectiveTarget.CreateAtMost(new ObjectiveId("objective:main"), new Flt64(value));

    /// <summary>取一个可表达目标的边界，并断言它确实可表达 / Read a representable bound, asserting it is representable.</summary>
    /// <remarks>
    /// **`Int64?` 是可空值类型**，直接 `.Value` 会在编译期告警（CS8629），
    /// 而 `FluentAssertions` 的断言不会让编译器相信它非空。**这里只封装一次。**
    ///
    /// **`Int64?` is a nullable value type**, so `.Value` warns at compile time (CS8629), and a `FluentAssertions` assertion does not convince the compiler. **Wrap it once.**
    /// </remarks>
    private static long Bound(ObjectiveTarget target) {
        Int64? bound = ObjectiveTargetBounds.IntegerBound(target);
        bound.Should().NotBeNull("this target is representable as an integer bound");
        return bound.GetValueOrDefault().ToLong();
    }

    /// <summary>**`AtLeast` 向上取整** / AtLeast rounds up.</summary>
    /// <remarks>
    /// "≥ 2.7" 需要**最小的**整数 ≥ 2.7，也就是 **3**。
    /// **用向下取整会得到 2——于是一个目标值 2 的不可行解会被判为可行。**
    ///
    /// "≥ 2.7" needs **the smallest** integer ≥ 2.7, which is **3**. **With floor it becomes 2 — making an infeasible solution with objective 2 pass as feasible.**
    /// </remarks>
    [Fact]
    public void TheAtLeastTarget_ShouldRoundUp() {
        Bound(AtLeast(2.7))
            .Should().Be(3, "2.7 is not an integer, and ≥ 2.7 needs the next one up");
    }

    /// <summary>**`AtMost` 向下取整** / AtMost rounds down.</summary>
    /// <remarks>
    /// **反向对照**：只测 `AtLeast` 会让"两个都用向上取整"通过。
    /// "≤ 2.7" 需要**最大的**整数 ≤ 2.7，也就是 **2**。
    ///
    /// **The reverse control**: testing only `AtLeast` lets "ceil for both" pass. "≤ 2.7" needs **the largest** integer ≤ 2.7, which is **2**.
    /// </remarks>
    [Fact]
    public void TheAtMostTarget_ShouldRoundDown() {
        Bound(AtMost(2.7))
            .Should().Be(2, "2.7 is not an integer, and ≤ 2.7 needs the next one down");
    }

    /// <summary>**整数值原样通过** / An integer value passes through unchanged.</summary>
    /// <remarks>
    /// **两侧都要**：舍入只该在没有小数时"什么都不做"。
    /// **A rounding rule that moves an exact integer is broken even when both sides happen to agree.**
    /// </remarks>
    [Fact]
    public void TheExactInteger_ShouldPassThroughBothWays() {
        Bound(AtLeast(3.0)).Should().Be(3);
        Bound(AtMost(3.0)).Should().Be(3);
    }

    /// <summary>**两个方向在同一小数上必须不同** / The two directions must disagree on a fractional value.</summary>
    /// <remarks>
    /// **这是一条"差异"判据**：如果某天有人把两个分支写成同一个舍入，
    /// 上面两条各自仍能通过各自的用例，**而这一条会立刻红。**
    ///
    /// **This is a difference criterion**: if someone ever collapses the two branches into one rounding, the two cases above still pass individually, **and this one goes red immediately.**
    /// </remarks>
    [Fact]
    public void TheTwoDirections_ShouldDisagreeOnAFraction() {
        long atLeast = Bound(AtLeast(2.7));
        long atMost = Bound(AtMost(2.7));

        atLeast.Should().NotBe(atMost, "the same 2.7 must map to 3 upward and 2 downward");
    }

    /// <summary>**越界返回 `null`，不抛异常** / Out of range returns null instead of throwing.</summary>
    /// <remarks>
    /// 那是"这个目标无法用整数边界表达"，**是一个合法的分析结论，不是程序错误。**
    /// **抛异常会让调用方在正常的分析路径上崩溃。**
    ///
    /// **This means "the target cannot be expressed as an integer bound" — a legitimate analysis result, not a program error.** **Throwing would crash the caller on a normal analysis path.**
    /// </remarks>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e30)]
    public void TheUnrepresentableTarget_ShouldBeNull(double value) {
        Action call = () => ObjectiveTargetBounds.IntegerBound(AtLeast(value));

        call.Should().NotThrow("an unrepresentable target is a result, not an exception");
        ObjectiveTargetBounds.IntegerBound(AtLeast(value)).Should().BeNull();
    }

    /// <summary>**无法映射时状态是 `Unsupported`，不是 `Unknown`** / The status is Unsupported, not Unknown.</summary>
    /// <remarks>
    /// "这个目标表达不出来"与求解器无关，**换个求解器也不会变；但它也不是"算错了"。**
    ///
    /// **"This target cannot be expressed" has nothing to do with the solver, so swapping solvers cannot help; but it is also not "computed wrong".**
    /// </remarks>
    [Fact]
    public void TheUnsupportedReport_ShouldSayUnsupported() {
        TargetFeasibilityReport report = ObjectiveTargetBounds.UnsupportedReport(AtLeast(2.7));

        report.Status.Should().Be(AnalysisStatus.Unsupported);
        report.Target.Should().NotBeNull("the report has to name the target it could not map");
        report.Message.Should().Contain("CP Int64", "the caller needs to know what the target failed to become");
    }

    /// <summary>**`TryIntegerBound` 与 `IntegerBound` 必须是同一个答案** / Both entry points must agree.</summary>
    /// <remarks>
    /// **`SnapshotModelRebuilder` 里原本有一份独立的舍入实现**（`9.2e18` 窗口、
    /// 非 `AtLeast` 一律向下取整）。**两份相同的规则只要改一份就再也不相同**
    /// （与 B1045 的 `ShouldStop` 同一个形状），所以改为转发。
    ///
    /// **There used to be an independent rounding implementation inside `SnapshotModelRebuilder`** (a `9.2e18` window, floor for anything not `AtLeast`). **Two copies of one rule stop agreeing as soon as one is
    /// edited** (the same shape as `ShouldStop` in B1045), so it now forwards.
    /// </remarks>
    [Theory]
    [InlineData(2.7, true)]
    [InlineData(-2.7, true)]
    [InlineData(0.0, true)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void TheTwoEntryPoints_ShouldAgree(double value, bool representable) {
        foreach (ObjectiveTarget target in new[] { AtLeast(value), AtMost(value) }) {
            bool ok = SnapshotModelRebuilder.TryIntegerBound(target, out Int64 viaRebuilder);
            Int64? viaBounds = ObjectiveTargetBounds.IntegerBound(target);

            ok.Should().Be(representable, $"{value} representability has one answer");
            if (ok) {
                viaRebuilder.Should().Be(viaBounds!.Value,
                    "the rebuilder must not round by its own rules");
            }
        }
    }

    /// <summary>**`long` 上界处不得溢出** / No overflow at the upper long edge.</summary>
    /// <remarks>
    /// `long.MaxValue` 转成 `double` 恰好是 2^63，**而 2^63 已超出 `long` 一位**。
    /// 写成 `rounded > long.MaxValue` 会放过它，**于是 `(long)rounded` 溢出成 `long.MinValue`**——
    /// **一个巨大的正目标变成一个巨大的负目标，而两者都像合法边界。**
    ///
    /// `long.MaxValue` as a `double` is exactly 2^63, **one past `long`**. Writing `rounded > long.MaxValue` lets it through, **so `(long)rounded` overflows to `long.MinValue`** — **a huge positive target becomes a huge
    /// negative one, and both look like legal bounds.**
    /// </remarks>
    [Fact]
    public void TheUpperLongEdge_ShouldBeRefusedNotWrapped() {
        ObjectiveTargetBounds.IntegerBound(AtLeast(9.2233720368547758e18))
            .Should().BeNull("2^63 is one past long, so it is not an expressible bound");

        // **反向对照**：刚好能表达的下面一格必须仍然通过。
        // **The reverse control**: the representable step below it must still pass.
        ObjectiveTargetBounds.IntegerBound(AtLeast(-9.2233720368547758e18))
            .Should().NotBeNull("long.MinValue itself is exactly representable");
    }
}
