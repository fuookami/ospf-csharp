// 执行器占用与编译量的判据 / Criteria for executor usage and compilation amount.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **`xor` 只有下界；带空闲时编译量能到 2** / `xor` has a lower bound only, and with leisure the compiled amount reaches 2.
/// </summary>
/// <remarks>
/// 两处容易想歪：把 `≥` 当成 `=`（于是在未最小化时错误推断"执行器未使用"），
/// 以及把编译量当成 0/1 指示量（于是带空闲时低估一半成本）。
///
/// Two things are easy to get wrong: reading `≥` as `=` (and so wrongly inferring "the executor is unused" when nothing is minimised), and treating the compiled amount as a 0/1
/// indicator (underestimating half the cost with leisure).
/// </remarks>
public class ExecutorUsageTests {
    /// **有一列被选中时下界为真** / The lower bound holds once any column is selected.
    [Fact]
    public void LowerBound_ShouldHoldWithAnySelectedColumn() {
        ExecutorUsage.LowerBound(1).Should().BeTrue();
        ExecutorUsage.LowerBound(5).Should().BeTrue();
    }

    /// **一列都没有时下界为假（不排除取 1）** / No selected column means the bound is false (it does not force zero).
    [Fact]
    public void LowerBound_ShouldBeFalseWithNoSelectedColumn() =>
        ExecutorUsage.LowerBound(0).Should().BeFalse(
            "the bound is 0, so xor may still be 1 — only minimisation drives it down");

    /// **负数被拒绝** / A negative count is rejected.
    [Fact]
    public void LowerBound_ShouldRejectNegativeCounts() =>
        ((Action)(() => ExecutorUsage.LowerBound(-1)))
            .Should().Throw<ArgumentOutOfRangeException>();

    /// **不带空闲时编译量就是占用指示量** / Without leisure the amount is the usage indicator.
    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(1.0, 0.0, 1.0)]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(1.0, 1.0, 1.0)]
    public void Compilation_ShouldIgnoreLeisureWithoutTheSwitch(double xor, double leisure, double expected) =>
        ExecutorUsage.Compilation(withLeisure: false, xor, leisure).Should().BeApproximately(expected, 1e-12);

    /// <summary>
    /// **带空闲时编译量是两者之和，可以到 2** / With leisure the amount is their sum and can reach 2.
    /// </summary>
    /// <remarks>
    /// 判据必须包含 `(1, 1) → 2` 这一项：只测 `(1, 0)` 与 `(0, 1)` 的话，
    /// 一个"取两者较大值"的错误实现也能通过，而它在满员+空闲时会把 2 算成 1。
    ///
    /// The criterion must include `(1, 1) → 2`: testing only `(1, 0)` and `(0, 1)` would let a wrong "take the larger" implementation pass, and that one reports 1 where 2 belongs
    /// when both are set.
    /// </remarks>
    [Theory]
    [InlineData(1.0, 1.0, 2.0)]
    [InlineData(1.0, 0.0, 1.0)]
    [InlineData(0.0, 1.0, 1.0)]
    [InlineData(0.0, 0.0, 0.0)]
    public void Compilation_ShouldSumXorAndLeisure(double xor, double leisure, double expected) =>
        ExecutorUsage.Compilation(withLeisure: true, xor, leisure).Should().BeApproximately(expected, 1e-12);

    /// **上界随配置变化，且带空闲时是 2** / The bound follows the configuration, and is 2 with leisure.
    [Theory]
    [InlineData(true, 2.0)]
    [InlineData(false, 1.0)]
    public void CompilationUpperBound_ShouldDoubleWithLeisure(bool withLeisure, double expected) =>
        ExecutorUsage.CompilationUpperBound(withLeisure).Should().BeApproximately(expected, 1e-12,
            "a cost coefficient multiplies this bound, so the two configurations differ twofold");
}
