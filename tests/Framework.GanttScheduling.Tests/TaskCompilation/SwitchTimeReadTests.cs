// 换型时间读取的判据 / Criteria for reading a switch time back.

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **求不出值时退回常数项，不是退回零** / When evaluation fails the constant term is used, not zero.
/// </summary>
/// <remarks>
/// 无 `taskTime` 的分支里 `switchTime` 是一个常量表达式（`distance(t1.end → t2.end)`），
/// 它根本没有 token 可求值。若兜底给 0，**所有固定时段排布的换型时间都会读成 0**——
/// 换型代价在报告里凭空消失，而求解本身是对的。
///
/// In the branch without `taskTime`, `switchTime` is a constant expression (`distance(t1.end → t2.end)`) with no token to evaluate at all. Defaulting to 0 there means **every
/// fixed-time arrangement reports a switch time of zero** — the switch cost silently vanishes from the report while the solve was correct.
/// </remarks>
public class SwitchTimeReadTests {
    /// **求得出值时用求值结果** / The evaluated value wins when available.
    [Fact]
    public void Value_ShouldPreferTheEvaluatedValue() =>
        SwitchTimeRead.Value(evaluated: 7.5, constant: 2.0)
            .Should().BeApproximately(7.5, 1e-12, "the solved value is the truth after solving");

    /// <summary>
    /// **求不出值时用常数项** / The constant term is used when evaluation fails.
    /// </summary>
    /// <remarks>
    /// 判据里的常数是 **2.0 而不是 0**：兜底给 0 的实现会在这里失败，而常数恰好为 0 的输入分不开这两种行为。
    ///
    /// The criterion's constant is **2.0, not 0**: a zero default fails here, while a constant that happens to be 0 would not separate the two behaviours.
    /// </remarks>
    [Fact]
    public void Value_ShouldFallBackToTheConstant() =>
        SwitchTimeRead.Value(evaluated: null, constant: 2.0)
            .Should().BeApproximately(2.0, 1e-12, "a constant switch time must not be read as zero");

    /// **求出的零是合法的零** / An evaluated zero is a legitimate zero.
    [Fact]
    public void Value_ShouldKeepAnEvaluatedZero() =>
        SwitchTimeRead.Value(evaluated: 0.0, constant: 2.0)
            .Should().BeApproximately(0.0, 1e-12, "the solution says there was no switch, so the constant must not override it");

    /// **负的常数项也原样返回** / A negative constant comes back unchanged.
    [Fact]
    public void Value_ShouldKeepANegativeConstant() =>
        SwitchTimeRead.Value(evaluated: null, constant: -3.5).Should().BeApproximately(-3.5, 1e-12);

    /// **是否走兜底可被判定** / Whether the fallback applies can be decided.
    [Theory]
    [InlineData(null, true)]
    [InlineData(0.0, false)]
    public void UsesConstant_ShouldOnlyApplyWithoutAnEvaluation(double? evaluated, bool expected) =>
        SwitchTimeRead.UsesConstant(evaluated).Should().Be(expected);
}
