#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.Dto;

/// <summary>
/// 甘特渲染任务的类别 / The category of a Gantt render task.
/// </summary>
public enum GanttRenderTaskCategory {
    /// <summary>普通任务 / A normal task</summary>
    Normal,
    /// <summary>测试任务 / A testing task</summary>
    Testing,
    /// <summary>不可用时段 / An unavailable slot</summary>
    Unavailable,
    /// <summary>未知 / Unknown</summary>
    Unknown,
}

/// <summary>
/// 甘特渲染的子任务 / A sub-task in a Gantt render.
/// </summary>
/// <param name="Name">名称 / The name.</param>
/// <param name="Category">类别名 / The category name.</param>
/// <param name="StartTime">开始时刻 / The start instant.</param>
/// <param name="EndTime">结束时刻 / The end instant.</param>
/// <param name="Info">附加信息 / Extra information.</param>
/// <remarks>
/// **类别是字符串而不是枚举**——子任务的类别由报表方定义，
/// 强制成枚举会把"用户自定义类别"这条路堵死；顶层任务的类别才是枚举。
///
/// **The category is a string rather than an enum**: a sub-task's category is the reporting side's to define, and forcing an enum would block
/// user-defined categories. Only the top-level task's category is an enum.
/// </remarks>
public sealed record GanttRenderSubTaskDTO(
    string Name,
    string Category,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyDictionary<string, string>? Info = null) {
    /// <summary>类别默认值 / The category default</summary>
    public const string DefaultCategory = nameof(GanttRenderTaskCategory.Normal);

    /// <summary>附加信息（未给则空）/ The extra information, empty when unset</summary>
    public IReadOnlyDictionary<string, string> InfoValue =>
        Info ?? new Dictionary<string, string>();

    /// <summary>
    /// 便捷构造：用默认类别 / Convenience constructor using the default category.
    /// </summary>
    /// <param name="name">名称 / The name.</param>
    /// <param name="startTime">开始时刻 / The start instant.</param>
    /// <param name="endTime">结束时刻 / The end instant.</param>
    /// <returns>子任务 / The sub-task.</returns>
    public static GanttRenderSubTaskDTO Create(string name, DateTimeOffset startTime, DateTimeOffset endTime) =>
        new(name, DefaultCategory, startTime, endTime);
}

/// <summary>
/// 甘特渲染的任务 / A task in a Gantt render.
/// </summary>
/// <param name="Name">名称 / The name.</param>
/// <param name="Category">类别 / The category.</param>
/// <param name="Executor">执行者 / The executor.</param>
/// <param name="Order">订单号 / The order number.</param>
/// <param name="Produce">产出物 / What is produced.</param>
/// <param name="Products">产品集合 / The products.</param>
/// <param name="Consumption">消耗 / The consumption.</param>
/// <param name="ScheduledStartTime">计划开始 / The scheduled start.</param>
/// <param name="ScheduledEndTime">计划结束 / The scheduled end.</param>
/// <param name="StartTime">实际开始 / The actual start.</param>
/// <param name="EndTime">实际结束 / The actual end.</param>
/// <param name="Resources">资源 / The resources.</param>
/// <param name="Info">附加信息 / Extra information.</param>
/// <param name="SubTasks">子任务 / The sub-tasks.</param>
/// <remarks>
/// 三处保留：**计划时刻与实际时刻分开**（两个可空、两个必填——
/// 排程没排出来时计划时刻就是 null，不能拿实际时刻冒充）；**必填的只有名称/类别/执行者/实际起止**；
/// **其余集合一律可空且给出空默认**，序列化时不必区分"没给"与"空"。
///
/// Three points are kept: **scheduled and actual instants are kept apart** (two nullable, two mandatory — an unscheduled task has no scheduled
/// start, and the actual one must not stand in for it); **only name, category, executor and the actual range are mandatory**; and **every other collection is nullable with an empty default**,
/// so serialisation need not distinguish "unset" from "empty".
/// </remarks>
public sealed record GanttRenderTaskDTO(
    string Name,
    GanttRenderTaskCategory Category,
    string Executor,
    string? Order,
    string? Produce,
    IReadOnlyDictionary<string, string>? Products,
    IReadOnlyDictionary<string, string>? Consumption,
    DateTimeOffset? ScheduledStartTime,
    DateTimeOffset? ScheduledEndTime,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyDictionary<string, string>? Resources,
    IReadOnlyDictionary<string, string>? Info,
    IReadOnlyList<GanttRenderSubTaskDTO>? SubTasks) {
    /// <summary>消耗（未给则空）/ The consumption, empty when unset</summary>
    public IReadOnlyDictionary<string, string> ConsumptionValue =>
        Consumption ?? new Dictionary<string, string>();

    /// <summary>资源（未给则空）/ The resources, empty when unset</summary>
    public IReadOnlyDictionary<string, string> ResourcesValue =>
        Resources ?? new Dictionary<string, string>();

    /// <summary>附加信息（未给则空）/ The extra information, empty when unset</summary>
    public IReadOnlyDictionary<string, string> InfoValue => Info ?? new Dictionary<string, string>();

    /// <summary>子任务（未给则空）/ The sub-tasks, empty when unset</summary>
    public IReadOnlyList<GanttRenderSubTaskDTO> SubTasksValue =>
        SubTasks ?? Array.Empty<GanttRenderSubTaskDTO>();

    /// <summary>
    /// 便捷构造：只给必填项 / Convenience constructor taking only the mandatory fields.
    /// </summary>
    /// <param name="name">名称 / The name.</param>
    /// <param name="category">类别 / The category.</param>
    /// <param name="executor">执行者 / The executor.</param>
    /// <param name="startTime">实际开始 / The actual start.</param>
    /// <param name="endTime">实际结束 / The actual end.</param>
    /// <returns>任务 / The task.</returns>
    public static GanttRenderTaskDTO Create(
        string name, GanttRenderTaskCategory category, string executor,
        DateTimeOffset startTime, DateTimeOffset endTime) =>
        new(name, category, executor, null, null, null, null, null, null, startTime, endTime,
            null, null, null);
}

/// <summary>
/// 甘特渲染的整体结构 / The overall Gantt render schema.
/// </summary>
/// <param name="Tasks">任务列表 / The task list.</param>
public sealed record GanttRenderSchemaDTO(IReadOnlyList<GanttRenderTaskDTO>? Tasks = null) {
    /// <summary>任务列表（未给则空）/ The task list, empty when unset</summary>
    public IReadOnlyList<GanttRenderTaskDTO> TasksValue => Tasks ?? Array.Empty<GanttRenderTaskDTO>();
}
