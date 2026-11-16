// 关系查询执行统计 / Relational query execution statistics.
// 与固定 Kotlin 对应。本文件只保存不可变审计数据，不执行数据库。
// The pinned Kotlin: . This file stores immutable audit data
// only and never executes a database.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Persistence.Query;

/// <summary>
/// 一次关系查询执行的统计 / Statistics for one relational query execution.
/// </summary>
/// <remarks>
/// 耗时用**纳秒**保存，便于调用方保留高精度计时；失败执行同样记录计划信息。
///
/// **与一处语言差异**：基线另有 `typealias RelationalQueryStatsRecorder`。C# 没有可导出的类型别名，
/// 因此不提供该简写——调用方用完整类型名，或在自己的文件里写 `using` 别名。这不是缺口。
///
/// The duration is kept in **nanoseconds** so callers retain timing precision; failed executions retain plan
/// information too.
///
/// **One language difference**: the code also has `typealias RelationalQueryStatsRecorder`. C# has no
/// exportable type alias, so the shorthand is not provided — callers use the full name or a `using` alias in
/// their own file. That is not a gap.
/// </remarks>
/// <param name="PlanHash">计划规范化哈希 / Canonical plan hash.</param>
/// <param name="Canonical">计划规范化表示 / Canonical plan representation.</param>
/// <param name="Duration">执行耗时（纳秒）/ Execution duration in nanoseconds.</param>
/// <param name="RowCount">返回行数；失败时通常为 null / Returned row count, usually null on failure.</param>
/// <param name="Failed">是否执行失败 / Whether execution failed.</param>
/// <param name="Failure">可选失败描述 / Optional failure description.</param>
public sealed record RelationalQueryExecutionStats(
    string PlanHash,
    string Canonical,
    long Duration,
    long? RowCount = null,
    bool Failed = false,
    string? Failure = null) {
    /// <summary>执行耗时（毫秒）/ Execution duration in milliseconds.</summary>
    public long DurationMillis => Duration / 1_000_000L;

    /// <summary>兼容审计字段名 / Compatibility audit field name.</summary>
    public string CanonicalHash => PlanHash;
}

/// <summary>
/// 线程安全的关系查询统计记录器 / Thread-safe relational query statistics recorder.
/// </summary>
/// <remarks>
/// 记录顺序与调用顺序一致；<see cref="Snapshot"/> 返回独立的只读列表。
/// Record order follows insertion order; <see cref="Snapshot"/> returns an independent read-only list.
/// </remarks>
public sealed class RelationalQueryExecutionStatsRecorder {
    private readonly ConcurrentQueue<RelationalQueryExecutionStats> _entries = new();

    /// <summary>
    /// 记录成功执行 / Record a successful execution.
    /// </summary>
    /// <param name="plan">查询计划 / Query plan.</param>
    /// <param name="rowCount">返回行数 / Returned row count.</param>
    /// <param name="duration">执行耗时（纳秒）/ Execution duration in nanoseconds.</param>
    /// <returns>新增统计 / Recorded statistics.</returns>
    public RelationalQueryExecutionStats RecordSuccess(
        RelationalQueryPlan plan,
        long rowCount,
        long duration = 0L) =>
        Record(plan, duration, rowCount, failed: false, failure: null);

    /// <summary>
    /// 记录失败执行 / Record a failed execution.
    /// </summary>
    /// <param name="plan">查询计划 / Query plan.</param>
    /// <param name="duration">执行耗时（纳秒）/ Execution duration in nanoseconds.</param>
    /// <param name="failure">失败描述 / Failure description.</param>
    /// <returns>新增统计 / Recorded statistics.</returns>
    public RelationalQueryExecutionStats RecordFailure(
        RelationalQueryPlan plan,
        long duration = 0L,
        string? failure = null) =>
        Record(plan, duration, rowCount: null, failed: true, failure);

    /// <summary>
    /// 返回统计快照 / Return a statistics snapshot.
    /// </summary>
    /// <returns>当前记录的独立列表 / Independent list of current records.</returns>
    public IReadOnlyList<RelationalQueryExecutionStats> Snapshot() =>
        _entries.ToArray();

    private RelationalQueryExecutionStats Record(
        RelationalQueryPlan plan,
        long duration,
        long? rowCount,
        bool failed,
        string? failure) {
        ArgumentNullException.ThrowIfNull(plan);

        var stats = new RelationalQueryExecutionStats(
            PlanHash: plan.Hash(),
            Canonical: plan.Canonical(),
            // 负耗时是计时器背压或时钟回拨的痕迹，不是"很快"；夹到 0 以免污染统计。
            // A negative duration betrays timer backpressure or a clock rollback, not speed; clamp it to 0 so
            // it cannot pollute the statistics.
            Duration: System.Math.Max(duration, 0L),
            RowCount: rowCount,
            Failed: failed,
            Failure: failure);
        _entries.Enqueue(stats);
        return stats;
    }
}
