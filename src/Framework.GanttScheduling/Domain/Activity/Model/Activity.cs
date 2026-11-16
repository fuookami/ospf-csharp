// 活动排程模型 / Activity scheduling model.
//
// `ActivityCompilation`（472 行）尚未实现，见文末说明。
// `ActivityCompilation` (472 lines) is not yet implemented — see the tail note.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;

/// <summary>活动端点 / An activity endpoint.</summary>
public enum ActivityEndpoint {
    /// <summary>开始 / Start.</summary>
    Start,

    /// <summary>结束 / Finish.</summary>
    Finish,
}

/// <summary>
/// 存在性规则，与纯时间关系显式分离 / Presence semantics, explicitly separated from the pure timing relation.
/// </summary>
/// <remarks>
/// **为什么存在性要独立于时间**：一条依赖既说"后驱不早于前驱结束"，也说"前驱不在时该条是否作废"。
/// 把两者混在一个枚举里，"前驱缺失"就只能表达成某种特殊的时间值，而任何时间值都会被当成真实时刻参与比较——
/// 于是"前驱不存在"变成了"前驱在某个时刻完成"，排程结果看着正常，实际是凭空多了一个任务。
///
/// **Why presence is independent of timing**: a dependency says both "the successor starts no earlier than the predecessor finishes" and "what happens to this rule when
/// the predecessor is absent". Folding the two into one enum forces "the predecessor is missing" to be expressed as some special time value, and any time value participates
/// in comparisons as a real instant — so "the predecessor does not exist" becomes "the predecessor finishes at some instant", and the schedule looks fine while containing
/// a task that was never there.
/// </remarks>
public enum ActivityDependencyPresence {
    /// <summary>两侧都存在时才约束 / Constrain only when both are present.</summary>
    WhenBothPresent,

    /// <summary>要求前驱存在 / Require the predecessor.</summary>
    RequirePredecessor,

    /// <summary>要求两侧都存在 / Require both.</summary>
    RequireBoth,
}

/// <summary>执行事实类型 / Execution fact kind.</summary>
public enum ActivityExecutionStatus {
    /// <summary>已完成 / Completed.</summary>
    Completed,

    /// <summary>执行中 / Running.</summary>
    Running,
}

/// <summary>
/// 候选身份：任务、模式与起点 / A choice identity: task, mode and start.
/// </summary>
/// <param name="Task">任务标识 / The task identity.</param>
/// <param name="Mode">活动内模式标识 / The stable mode identity within the activity.</param>
/// <param name="Start">起点 / The start instant.</param>
/// <remarks>
/// **三点全等才算同一候选**：只比任务会合并同一任务的不同模式，只比任务+模式会合并不同起点。
/// 前一种错在把"换个做法"当成"同一个做法"，后一种错在把"挪个时间"当成"没动"——
/// 而基准线冻结、改期费用、改模式费用全靠这个身份区分。
///
/// **All three parts must match for the same choice**: comparing only the task merges different modes of one task; comparing task plus mode merges different starts. The first
/// wrongly treats "do it differently" as "the same plan", the second treats "moved in time" as "unchanged" — while baseline freezing, re-timing cost and mode-change cost
/// all rely on this identity.
/// </remarks>
/// <remarks>
/// 任务与资源身份在这里用 `string`：C# 的 `Executor.Id` 是 `string`，而`ExecutorId` 是接口。
/// 把接口继承体系搬过来会牵动整个 `Task` 领域，而这里只需要一个可用于分组与比较的键。
/// `ponytail:` 天花板——等到活动与任务领域真正需要共享身份时，再引入 `ExecutorId` 接口。
///
/// **The task and resource identities are `string` here**: C#'s `Executor.Id` is a `string` while its `ExecutorId` is an interface. Porting that interface
/// hierarchy would drag in the whole `Task` domain, and only needs a key to group and compare by.
/// **`ponytail:` ceiling** — introduce an `ExecutorId` interface when the activity and task domains genuinely need to share identity.
/// </remarks>
public sealed record ActivityChoiceKey(string Task, string Mode, DateTimeOffset Start);

/// <summary>
/// 通用端点时间关系，涵盖 FS/SS/FF/SF / A general endpoint timing relation covering FS, SS, FF and SF.
/// </summary>
/// <param name="Predecessor">前驱任务 / The predecessor task.</param>
/// <param name="Successor">后继任务 / The successor task.</param>
/// <param name="From">前驱端点 / The predecessor endpoint.</param>
/// <param name="To">后继端点 / The successor endpoint.</param>
/// <param name="Minimum">最小间隔，可为负 / The minimum lag, possibly negative.</param>
/// <param name="Maximum">可选最大间隔 / The optional maximum lag.</param>
/// <param name="Presence">存在性语义 / The presence semantics.</param>
public sealed record ActivityDependency(
    string Predecessor,
    string Successor,
    ActivityEndpoint From = ActivityEndpoint.Finish,
    ActivityEndpoint To = ActivityEndpoint.Start,
    TimeSpan? Minimum = null,
    TimeSpan? Maximum = null,
    ActivityDependencyPresence Presence = ActivityDependencyPresence.RequirePredecessor) {
    /// <summary>解析后的最小间隔，未给出时为零 / The resolved minimum lag, zero when omitted.</summary>
    public TimeSpan MinimumLag => Minimum ?? TimeSpan.Zero;
}

/// <summary>
/// 绝对时间的资源占用段 / A resource reservation in absolute time.
/// </summary>
/// <param name="Resource">资源身份 / The resource identity.</param>
/// <param name="Time">半开占用区间 / The half-open reservation interval.</param>
/// <param name="Quantity">占用量 / The reserved amount.</param>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed record ActivityReservation<V>(string Resource, TimeRange Time, Quantity<V> Quantity)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 完整活动分配，保留内部资源占用段 / A complete allocation, retaining its internal resource phases.
/// </summary>
/// <param name="Key">候选身份 / The choice identity.</param>
/// <param name="Time">实际活动区间 / The activity interval.</param>
/// <param name="Reservations">内部资源占用 / The internal reservations.</param>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed record ActivityPlacement<V>(
    ActivityChoiceKey Key,
    TimeRange Time,
    IReadOnlyList<ActivityReservation<V>> Reservations)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 排程校验结果 / A schedule validation result.
/// </summary>
/// <param name="Violations">违规项 / The violations.</param>
/// <remarks>
/// **`IsValid` 是派生值，不是可写字段**：一个可写的"是否通过"会和违规列表各自演化，
/// 于是出现"列表非空但标记为通过"的状态，而读哪一边决定了后续行为。
///
/// **`IsValid` is derived, not a writable field**: a writable "passed" flag would evolve independently of the violation list, producing states like "the list is non-empty but
/// the flag says passed", where which one you read decides what happens next.
/// </remarks>
public sealed record ActivityScheduleValidation(IReadOnlyList<string> Violations) {
    /// <summary>是否无违规 / Whether there are no violations.</summary>
    public bool IsValid => Violations.Count == 0;
}
