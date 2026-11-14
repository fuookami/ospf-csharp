// 四种时间偏移目标的门槛判据 / Criteria for the four time-deviation objectives' reference quantities.
// 对应固定 Kotlin 基线 `task_compilation/service/limits/` 里的四件：
//   TaskAdvanceTimeMinimiaztion.kt        advanceEnabled + scheduledTime  != null
//   TaskAdvanceEarliestEndTimeMinimization advanceEnabled + earliestEndTime != null
//   TaskDelayTimeMinimization             delayEnabled   + scheduledTime  != null
//   TaskDelayLastEndTimeMinimization      delayEnabled   + lastEndTime    != null
// Mirrors the four classes in the pinned baseline's `task_compilation/service/limits/`, whose filters are exactly the table above.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **四件目标有四种门槛，用错一个就静默算在错误的任务上** / The four objectives have four reference quantities, and using one for another silently charges the wrong tasks.
/// </summary>
/// <remarks>
/// 四件的筛选结构完全相同（全局开关 + 单任务方向开关 + **门槛量非空**），差别只在门槛量是哪一个：
///
/// | 目标 | 方向开关 | 门槛量 |
/// | --- | --- | --- |
/// | 提前 | `advanceEnabled` | 排定时间 |
/// | 提前（最早结束） | `advanceEnabled` | **最早结束时间** |
/// | 延迟 | `delayEnabled` | 排定时间 |
/// | 延迟（最晚结束） | `delayEnabled` | **最晚结束时间** |
///
/// 用错门槛量的后果是**任务集变了而没有任何报错**：某个任务有排定时间却没有最早结束时间，
/// 于是它在该进的目标里被漏掉，偏移量偏小而看起来完全正常。
///
/// The four share the same filter structure (global switch + per-task direction switch + **non-null reference**) and differ only in which reference:
///
/// | Objective | Direction switch | Reference |
/// | --- | --- | --- |
/// | advance | `advanceEnabled` | scheduled time |
/// | advance (earliest end) | `advanceEnabled` | **earliest end time** |
/// | delay | `delayEnabled` | scheduled time |
/// | delay (latest end) | `delayEnabled` | **latest end time** |
///
/// Using the wrong reference changes the task set **with no error at all**: a task may have a scheduled time but no earliest end, so it is dropped from the objective it belongs
/// to, the deviation reads small, and everything looks normal.
/// </remarks>
public class TimeDeviationReferenceTests {
    /// <summary>
    /// **"有排定时间"与"有最早结束时间"是两个不同的门槛** / "Has a scheduled time" and "has an earliest end" are different references.
    /// </summary>
    /// <remarks>
    /// 判据让同一个任务**只有其中之一**：`scheduled-only` 有排定时间、`earliest-only` 有最早结束时间。
    /// 两个目标各自筛出不同的集合；若实现复用了同一个门槛量，两边会得到同一个集合。
    ///
    /// The criterion gives one task **only the first** and another **only the second**: `scheduled-only` has a scheduled time, `earliest-only` has an earliest end. Each objective
    /// selects a different set; an implementation reusing one reference would return the same set twice.
    /// </remarks>
    [Fact]
    public void Select_ShouldUseTheReferenceEachObjectiveNames() {
        var tasks = new List<string> { "both", "scheduled-only", "earliest-only" };
        Func<string, bool> hasScheduled = task => task != "earliest-only";
        Func<string, bool> hasEarliestEnd = task => task != "scheduled-only";

        IReadOnlyList<string> advance = TimeDeviationSelection.Select(true, tasks, _ => true, hasScheduled);
        IReadOnlyList<string> advanceEarliest =
            TimeDeviationSelection.Select(true, tasks, _ => true, hasEarliestEnd);

        advance.Should().BeEquivalentTo(new[] { "both", "scheduled-only" });
        advanceEarliest.Should().BeEquivalentTo(new[] { "both", "earliest-only" });
        advance.Should().NotBeEquivalentTo(advanceEarliest,
            "the scheduled time and the earliest end are different quantities");
    }

    /// **"最晚结束时间"与"最早结束时间"不可互换** / The latest and earliest end times are not interchangeable.
    [Fact]
    public void Select_ShouldNotConfuseEarliestAndLatestEnd() {
        var tasks = new List<string> { "both", "earliest-only", "latest-only" };
        Func<string, bool> hasEarliestEnd = task => task != "latest-only";
        Func<string, bool> hasLatestEnd = task => task != "earliest-only";

        TimeDeviationSelection.Select(true, tasks, _ => true, hasEarliestEnd)
            .Should().BeEquivalentTo(new[] { "both", "earliest-only" });
        TimeDeviationSelection.Select(true, tasks, _ => true, hasLatestEnd)
            .Should().BeEquivalentTo(new[] { "both", "latest-only" });
    }

    /// **四件共用同一套三条件** / All four share the same three conditions.
    [Fact]
    public void Select_ShouldApplyTheSameThreeConditionsToEveryObjective() {
        var tasks = new List<string> { "ok", "no-reference", "no-direction" };
        Func<string, bool> direction = task => task != "no-direction";
        Func<string, bool> reference = task => task != "no-reference";

        TimeDeviationSelection.Select(true, tasks, direction, reference)
            .Should().ContainSingle().Which.Should().Be("ok", "all three conditions hold only for `ok`");
        TimeDeviationSelection.Select(false, tasks, direction, reference)
            .Should().BeEmpty("the global gate closes the whole objective");
    }

    /// **方向开关决定方向，门槛量决定资格** / The direction switch picks the direction, the reference picks eligibility.
    [Fact]
    public void Select_ShouldKeepDirectionAndReferenceIndependent() {
        var tasks = new List<string> { "advance", "delay" };
        Func<string, bool> allowsAdvance = task => task == "advance";
        Func<string, bool> allowsDelay = task => task == "delay";

        TimeDeviationSelection.Select(true, tasks, allowsAdvance, _ => true)
            .Should().BeEquivalentTo(new[] { "advance" });
        TimeDeviationSelection.Select(true, tasks, allowsDelay, _ => true)
            .Should().BeEquivalentTo(new[] { "delay" });
    }
}
