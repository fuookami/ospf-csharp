// 时间偏移量任务筛选的判据 / Criteria for time-deviation task selection.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **"允许提前"不等于"提前量可知"：没有排定时间就没有基准** / "Advancing allowed" is not "the advance is knowable": no scheduled time means no reference.
/// </summary>
/// <remarks>
/// 第三个条件最容易被漏掉：它看起来像冗余校验，其实是这个量的**定义前提**。
/// 基线把这种情况**排除**而不是当成零——当成零会让"提前了但没有基准"安静地通过。
///
/// The third condition is the easiest to skip: it looks like a redundant validation but is really the **precondition for the definition**. The baseline **excludes** the case
/// rather than treating it as zero — zero would let "advanced, but there is no reference" through quietly.
/// </remarks>
public class TimeDeviationSelectionTests {
    private static readonly List<string> Tasks = new() { "qualifies", "no-time", "no-advance" };

    private static IReadOnlyList<string> Select(bool globalEnabled) =>
        TimeDeviationSelection.Select(
            globalEnabled,
            Tasks,
            directionEnabled: task => task != "no-advance",
            hasScheduledTime: task => task != "no-time");

    /// **三个条件都满足才入选** / The task enters only when all three hold.
    [Fact]
    public void Select_ShouldKeepOnlyFullyQualifiedTasks() =>
        Select(globalEnabled: true).Should().ContainSingle().Which.Should().Be("qualifies");

    /// **没有排定时间的任务被排除** / A task without a scheduled time is excluded.
    [Fact]
    public void Select_ShouldExcludeTasksWithoutAScheduledTime() =>
        Select(globalEnabled: true).Should().NotContain("no-time",
            "advance has no reference without a scheduled time");

    /// **不允许提前的任务被排除** / A task that forbids advancing is excluded.
    [Fact]
    public void Select_ShouldExcludeTasksThatForbidTheDirection() =>
        Select(globalEnabled: true).Should().NotContain("no-advance");

    /// **全局关闭时一个都不选** / Nothing is selected with the global switch off.
    [Fact]
    public void Select_ShouldReturnNothingWhenTheGlobalSwitchIsOff() =>
        Select(globalEnabled: false).Should().BeEmpty();

    /// **全空任务列表得到空结果** / An empty candidate list yields nothing.
    [Fact]
    public void Select_ShouldHandleAnEmptyCandidateList() =>
        TimeDeviationSelection.Select(
                true, new List<string>(), _ => true, _ => true)
            .Should().BeEmpty();

    /// **全部不满足时得到空结果** / An all-failing candidate list yields nothing.
    [Fact]
    public void Select_ShouldReturnNothingWhenNoTaskQualifies() =>
        TimeDeviationSelection.Select(
                true, new List<string> { "A", "B" }, _ => false, _ => true)
            .Should().BeEmpty();

    /// <summary>
    /// **提前与延迟的开关互不影响** / The advance and delay switches are independent.
    /// </summary>
    /// <remarks>
    /// 判据让 `advance-only` 只允许提前、`delay-only` 只允许延迟。两个方向各自筛出**不同**的任务集。
    /// 若延迟那边误用了 `advanceEnabled`，两边会得到同一个集合——而它看起来同样合理，
    /// 只是偏移量算在了错误的任务上。
    ///
    /// The criterion makes `advance-only` allow only advancing and `delay-only` only delaying, so each direction selects a **different** set. If the delay side used
    /// `advanceEnabled`, both would return the same set — a set that looks equally plausible, with the deviation charged to the wrong tasks.
    /// </remarks>
    [Fact]
    public void Select_ShouldKeepTheTwoDirectionsApart() {
        var tasks = new List<string> { "both", "advance-only", "delay-only" };
        Func<string, bool> allowsAdvance = task => task != "delay-only";
        Func<string, bool> allowsDelay = task => task != "advance-only";

        IReadOnlyList<string> advance = TimeDeviationSelection.Select(true, tasks, allowsAdvance, _ => true);
        IReadOnlyList<string> delay = TimeDeviationSelection.Select(true, tasks, allowsDelay, _ => true);

        advance.Should().BeEquivalentTo(new[] { "both", "advance-only" });
        delay.Should().BeEquivalentTo(new[] { "both", "delay-only" });
        advance.Should().NotBeEquivalentTo(delay, "the two directions must not collapse into one set");
    }

    /// **一个任务可以同时进入两个方向** / One task may enter both directions.
    [Fact]
    public void Select_ShouldAllowATaskInBothDirections() {
        var tasks = new List<string> { "both" };

        TimeDeviationSelection.Select(true, tasks, _ => true, _ => true)
            .Should().ContainSingle().Which.Should().Be("both");
    }

    /// <summary>
    /// **入选任务的代价复用 B497 的语义** / The selected tasks' cost reuses B497's semantics.
    /// </summary>
    /// <remarks>
    /// 每任务的代价与 B497 的 `TaskAdvanceTimeCost` 相同：阈值零 → 全额线性；阈值非零 → 只罚超出；系数空 → 无穷（禁止）。
    /// 判据把三个答案各算一遍，证明本批没有引入第二套代价语义。
    ///
    /// Each task's cost matches B497's `TaskAdvanceTimeCost`: zero threshold → charge linearly; non-zero → only the excess; empty coefficient → infinity (forbid). The criterion
    /// computes all three answers, proving this batch introduced no second cost semantics.
    /// </remarks>
    [Fact]
    public void AdvanceCost_ShouldReuseTheAdvanceTimeCostSemantics() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> linear =
            TaskAdvanceTimeCost.Cost(new Flt64(6.0), threshold: null, new Flt64(1.5));
        Result<Flt64, ErrorCode, Error<ErrorCode>> excess =
            TaskAdvanceTimeCost.Cost(new Flt64(6.0), new Flt64(4.0), new Flt64(1.5));
        Result<Flt64, ErrorCode, Error<ErrorCode>> unpriced =
            TaskAdvanceTimeCost.Cost(new Flt64(6.0), new Flt64(4.0), coefficient: null);

        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)linear).Value.ToDouble().Should().BeApproximately(9.0, 1e-12);
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)excess).Value.ToDouble().Should().BeApproximately(3.0, 1e-12);
        unpriced.Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
    }
}
