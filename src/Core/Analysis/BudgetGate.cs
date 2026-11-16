#nullable enable

using System;
using System.Diagnostics;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 预算门控 / The budget gate.
/// </summary>
/// <remarks>
/// **这是 的 `budgetExceeded` 的实现。**
///
/// 有一行：`budget != null &amp;&amp; System.nanoTime - startedAt &gt;= budget.inWholeNanoseconds`。
/// **它短到看起来不值得单独成文件，而它有一个容易写反的语义：没有预算就是永不超支。**
///
/// It is one line, **short enough to look unworthy of its own file, and it has a semantics that is easy to invert: no budget means never exceeded.**
///
/// | 情况 | 结果 | 为什么 |
/// | --- | --- | --- |
/// | 没有预算（`null`） | **`false`** | 不设上限就是没有上限，**不是"立刻超支"** |
/// | 恰好等于预算 | **`true`** | 的是 `>=`，不是 `>` |
/// | 超过预算 | `true` | — |
/// | 还没到预算 | `false` | — |
///
/// **前两行是本文件的全部价值。** 第一行写反会让"没设预算"的调用**一次都不跑**；
/// 第二行写反会让"刚好用完预算"的那一次**多跑一次求解**——而它正是最后那一次。
///
/// **The first two rows are the whole point of this file.** Inverting the first makes every unbudgeted call **do nothing at all**; inverting the second lets the one solve that exactly spends the
/// budget **run again** — and it is precisely the last one.
/// </remarks>
public static class BudgetGate {
    /// <summary>
    /// 已用时间是否达到预算 / Whether the elapsed time has reached the budget.
    /// </summary>
    /// <param name="elapsed">已用时间 / The elapsed time.</param>
    /// <param name="budget">预算；<c>null</c> 表示不设上限 / The budget, or <c>null</c> for no limit.</param>
    /// <returns>达到或超过预算时为 <c>true</c> / <c>true</c> when the budget is reached or exceeded.</returns>
    // **没有预算 = 永不超支。** `budget != null && …`，不是 `budget == null || …`。
 // **No budget means never exceeded.** This implementation is `budget != null && …`, not `budget == null || …`.
    public static bool Exceeded(TimeSpan elapsed, TimeSpan? budget) =>
        budget is { } limit && elapsed >= limit;

    /// <summary>
    /// 用起点的单调时钟判定 / Judge against a monotonic clock started earlier.
    /// </summary>
    /// <param name="startedAt">起点时间戳（来自 <c>Stopwatch.GetTimestamp</c>） / The start timestamp.</param>
    /// <param name="budget">预算；<c>null</c> 表示不设上限 / The budget, or <c>null</c> for no limit.</param>
    /// <returns>达到或超过预算时为 <c>true</c> / <c>true</c> when the budget is reached or exceeded.</returns>
    /// <remarks>
    /// **用单调时钟而不是墙钟**：墙钟会在校时、夏令时、系统休眠时跳变，
    /// **而"跑了多久"这个问题用墙钟回答会得到一个负数或一个巨大的数。**
    ///
    /// **A monotonic clock, not the wall clock**: the wall clock jumps on time sync, daylight saving and resume, **and "how long has this been running" answered by a wall clock comes back negative
    /// or enormous.**
    /// </remarks>
    public static bool ExceededSince(long startedAt, TimeSpan? budget) =>
        Exceeded(System.Diagnostics.Stopwatch.GetElapsedTime(startedAt), budget);
}
