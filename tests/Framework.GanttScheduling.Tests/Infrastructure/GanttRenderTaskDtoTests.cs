// 甘特渲染 DTO（B678）的判据 / Criteria for the Gantt render DTOs (B678).

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.Dto;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Infrastructure;

/// <summary>
/// **甘特渲染 DTO（B678）** / The Gantt render DTOs (B678).
/// </summary>
/// <remarks>
/// 判据的重点：**计划时刻与实际时刻分开**（只给必填项时计划为 null，不拿实际冒充）、
/// **集合空默认返回空而不是 null**、**子任务类别是字符串且默认 `Normal`**。
///
/// The criteria focus on **scheduled and actual instants staying apart** (with only the mandatory fields, the scheduled one is null rather than standing in for the actual), on **empty defaults
/// returning empty rather than null**, and on **the sub-task category being a string defaulting to `Normal`**.
/// </remarks>
public class GanttRenderTaskDtoTests {
    private static DateTimeOffset At(int day) => new(2026, 11, day, 8, 0, 0, TimeSpan.FromHours(8.0));

    /// <summary>
    /// **计划时刻与实际时刻分开** / Scheduled and actual instants stay apart.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 只给必填项时，**计划起止都是 null**，实际起止有值——不能拿实际冒充计划。
    /// 2. 类别与执行者原样保留（`Testing` / `m1`），不被默认值顶替。
    /// 3. 可选项 `Order` / `Produce` 为 null。
    ///
    /// Three paired points: with only the mandatory fields, **both scheduled instants are null** while the actual range is set, so the actual one never stands in for the scheduled; the category
    /// and executor survive verbatim (`Testing` and `m1`) rather than being replaced by defaults; and the optional `Order` and `Produce` are null.
    /// </remarks>
    [Fact]
    public void Create_ShouldKeepScheduledApartFromActual() {
        var task = GanttRenderTaskDTO.Create(
            "task-a", GanttRenderTaskCategory.Testing, "m1", At(1), At(2));

        task.ScheduledStartTime.Should().BeNull("an unscheduled task has no scheduled start");
        task.ScheduledEndTime.Should().BeNull();
        task.StartTime.Should().Be(At(1));
        task.EndTime.Should().Be(At(2));
        task.Category.Should().Be(GanttRenderTaskCategory.Testing);
        task.Executor.Should().Be("m1");
        task.Order.Should().BeNull();
        task.Produce.Should().BeNull();
    }

    /// <summary>
    /// **集合空默认返回空，子任务类别默认 `Normal`** / Empty defaults return empty, and the sub-task category defaults to `Normal`.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. `Create` 出来的任务，四个集合（消耗/资源/信息/子任务）**都是空而不是 null**。
    /// 2. 子任务便捷构造的类别是 **`Normal`**（字符串，不是枚举）。
    /// 3. 子任务的信息默认为空。
    /// 4. `GanttRenderSchemaDTO` 的任务列表默认也是空。
    ///
    /// Four paired points: a task from `Create` has **all four collections empty rather than null** (consumption, resources, info, sub-tasks); the sub-task convenience constructor's category is
    /// **`Normal`** as a string rather than an enum; the sub-task's info defaults to empty; and `GanttRenderSchemaDTO`'s task list also defaults to empty.
    /// </remarks>
    [Fact]
    public void Defaults_ShouldBeEmptyNotNull() {
        var task = GanttRenderTaskDTO.Create(
            "task-b", GanttRenderTaskCategory.Normal, "m2", At(3), At(4));

        task.ConsumptionValue.Should().BeEmpty();
        task.ResourcesValue.Should().BeEmpty();
        task.InfoValue.Should().BeEmpty();
        task.SubTasksValue.Should().BeEmpty();
        task.Products.Should().BeNull("an optional collection may stay null; the accessor is what fills in");

        var sub = GanttRenderSubTaskDTO.Create("sub-a", At(5), At(6));
        sub.Category.Should().Be("Normal", "the default category is the enum's Normal name");
        sub.InfoValue.Should().BeEmpty();
        sub.StartTime.Should().Be(At(5));

        new GanttRenderSchemaDTO().TasksValue.Should().BeEmpty();
    }
}
