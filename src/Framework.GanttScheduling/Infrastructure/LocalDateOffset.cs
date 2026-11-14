// 本地日期偏移 / Local date offset.
// 与固定 Kotlin 基线 `gantt-scheduling-infrastructure/.../LocalDateOffset.kt` 对应。
// Mirrors the pinned Kotlin baseline's `LocalDateOffset.kt`.

using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;

/// <summary>
/// 本地日期偏移：早于偏移量的时刻归入前一天 / A local date offset: instants before the offset belong to the previous day.
/// </summary>
/// <param name="Offset">日偏移时间 / The daily offset time.</param>
/// <remarks>
/// 这是**班次归属**问题，不是时区问题。夜班从 `22:00` 开始时，`22:30` 发生的事归入**当天开工的那个班次**，
/// 而 `02:30` 发生的事仍属于前一天开始的那个班次。若按自然日切分，同一个夜班会被拦腰切断，
/// 于是"今天这个班做了多少"永远算不对。
///
/// This is a **shift attribution** question, not a time-zone one. When the night shift starts at `22:00`, an event at `22:30` belongs to **the shift that started that day**,
/// while an event at `02:30` still belongs to the shift that started the previous day. Splitting by the natural day cuts the same night shift in half, so "how much did
/// today's shift produce" can never be computed correctly.
/// </remarks>
public readonly record struct LocalDateOffset(TimeOnly Offset) {
    /// <summary>
    /// 计算给定时刻所属的日期 / Compute the date to which a given instant belongs.
    /// </summary>
    /// <param name="time">本地日期时间 / The local date and time.</param>
    /// <returns>所属日期 / The date it belongs to.</returns>
    /// <remarks>
    /// **严格小于才回退**：恰好等于偏移量的时刻属于**当天**。这不是吹毛求疵——偏移量通常正是班次开始时刻，
    /// 而班次开始的第一件事就发生在这一秒上；把它划到前一天等于把班次开头的一批任务记到上一班。
    ///
    /// **Only a strictly earlier instant falls back**: one exactly at the offset belongs to **that day**. This is not pedantry — the offset is normally exactly the shift
    /// start, and the first thing a shift does happens at that instant; moving it to the previous day books the shift's opening tasks onto the previous shift.
    /// </remarks>
    public DateOnly Date(DateTime time) {
        var timeOfDay = TimeOnly.FromDateTime(time);
        return timeOfDay < Offset ? DateOnly.FromDateTime(time).AddDays(-1) : DateOnly.FromDateTime(time);
    }
}
