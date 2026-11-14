// 最大完工时间取值的判据 / Criteria for the makespan value.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **`extra` 不改变取值，只加选择变量；最大值就是最大值** / `extra` does not change the value and only adds selectors; the maximum is the maximum.
/// </summary>
/// <remarks>
/// 读 `MinMax.kt` 的文档：`MinMaxFunction` 的结果也是 `max(...)`，名字里的 "MinMax" 指"在优化里最小化这个最大值"。
/// 把它理解成"最大减最小"会让目标从"尽早全部做完"变成"把排程压紧"，**滞后与否完全不再被计入**——
/// 而数值仍然叫"完工时间"，没人会怀疑。
///
/// Reading `MinMax.kt`: `MinMaxFunction` also evaluates to `max(...)`; the name refers to "minimising that maximum in the optimisation". Reading it as "max minus min" changes the
/// objective from "finish everything early" to "squeeze the schedule together", and **lateness stops counting** — while the number is still called "the makespan" and nobody
/// suspects a thing.
/// </remarks>
public class MakespanValueTests {
    /// **取最大值** / The maximum.
    [Fact]
    public void Of_ShouldTakeTheMaximum() =>
        MakespanValue.Of(new List<double> { 3.0, 8.0, 5.0 }).Should().BeApproximately(8.0, 1e-12);

    /// **单个任务取自身** / A single task yields itself.
    [Fact]
    public void Of_ShouldHandleASingleTask() =>
        MakespanValue.Of(new List<double> { 4.5 }).Should().BeApproximately(4.5, 1e-12);

    /// **最大值在末尾时也要取到** / The maximum is found even when it is last.
    [Fact]
    public void Of_ShouldFindTheMaximumAtTheEnd() =>
        MakespanValue.Of(new List<double> { 1.0, 2.0, 9.0 })
            .Should().BeApproximately(9.0, 1e-12, "scanning only the first element would return 1");

    /// **空列表取零** / An empty list yields zero.
    [Fact]
    public void Of_ShouldBeZeroForAnEmptyList() =>
        MakespanValue.Of(new List<double>()).Should().BeApproximately(0.0, 1e-12,
            "no tasks really does finish at zero");

    /// <summary>
    /// **`extra` 分支的取值与默认分支相同** / The `extra` branch yields the same value as the default one.
    /// </summary>
    /// <remarks>
    /// 判据用**数值互不相同**的输入（`2 / 7 / 4`）：最大值是 7 而极差是 5，
    /// 于是"把 `extra` 理解成跨度"的实现会返回 5 并被检出。输入若全相同，两种理解都是 0，分不开。
    ///
    /// The criterion uses **distinct** values (`2 / 7 / 4`): the maximum is 7 while the spread is 5, so an implementation reading `extra` as the spread returns 5 and is caught.
    /// With all-equal input both interpretations give 0 and nothing is separated.
    /// </remarks>
    [Fact]
    public void OfBranch_ShouldIgnoreExtra() {
        var endTimes = new List<double> { 2.0, 7.0, 4.0 };

        MakespanValue.OfBranch(endTimes, extra: true).Should().BeApproximately(7.0, 1e-12,
            "MinMax also evaluates to the maximum, not to the spread");
        MakespanValue.OfBranch(endTimes, extra: true)
            .Should().BeApproximately(MakespanValue.OfBranch(endTimes, extra: false), 1e-12);
    }

    /// **两个分支都保住空列表取零** / Both branches keep the empty case at zero.
    [Fact]
    public void OfBranch_ShouldBeZeroForAnEmptyList() {
        MakespanValue.OfBranch(new List<double>(), extra: true).Should().BeApproximately(0.0, 1e-12);
        MakespanValue.OfBranch(new List<double>(), extra: false).Should().BeApproximately(0.0, 1e-12);
    }

    /// **只有 `extra` 分支需要选择变量** / Only the `extra` branch needs selector variables.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void RequiresSelectors_ShouldFollowExtra(bool extra, bool expected) =>
        MakespanValue.RequiresSelectors(extra).Should().Be(expected);
}
