// 关系查询执行统计记录器测试 / Tests for the relational query execution statistics recorder.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Query;
using Xunit;
using QueryPlan = Fuookami.Ospf.Framework.Persistence.Query.RelationalQueryPlan;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// 关系查询执行统计记录器 / Relational query execution statistics recorder.
/// </summary>
public sealed class RelationalQueryExecutionStatsTest {
    /// <summary>
    /// 成功记录必须带上计划哈希与规范化表示，行数原样保留——审计数据缺了哪怕一项都无法回溯到具体计划。
    /// A success record must carry the plan hash and canonical form with the row count preserved: audit data
    /// missing even one of these cannot be traced back to the plan.
    /// </summary>
    [Fact]
    public void RecordSuccess_ShouldCapturePlanIdentity() {
        var recorder = new RelationalQueryExecutionStatsRecorder();
        QueryPlan plan = Plan();

        RelationalQueryExecutionStats stats = recorder.RecordSuccess(
            plan: plan,
            rowCount: 42,
            duration: 1_500_000L);

        stats.PlanHash.Should().Be(plan.Hash());
        stats.Canonical.Should().Be(plan.Canonical());
        stats.RowCount.Should().Be(42);
        stats.Failed.Should().BeFalse();
        stats.Failure.Should().BeNull();
        stats.Duration.Should().Be(1_500_000L);
    }

    /// <summary>
    /// 失败记录**仍然**要保存计划信息——这正是基线注释强调的点：失败也要能回溯到计划。
    /// A failure record **still** keeps plan information, which is exactly what the code comment stresses.
    /// </summary>
    [Fact]
    public void RecordFailure_ShouldStillCapturePlan() {
        var recorder = new RelationalQueryExecutionStatsRecorder();
        QueryPlan plan = Plan();

        RelationalQueryExecutionStats stats = recorder.RecordFailure(
            plan: plan,
            duration: 10L,
            failure: "timeout");

        stats.PlanHash.Should().Be(plan.Hash());
        stats.Canonical.Should().Be(plan.Canonical());
        stats.RowCount.Should().BeNull("失败没有行数 / a failure has no row count");
        stats.Failed.Should().BeTrue();
        stats.Failure.Should().Be("timeout");
    }

    /// <summary>
    /// 负耗时必须夹到 0：负值是计时器背压或时钟回拨的痕迹，混进统计会让平均值失真。
    /// A negative duration must be clamped to 0: it betrays timer backpressure or a clock rollback and would
    /// distort averages.
    /// </summary>
    [Fact]
    public void Record_ShouldClampNegativeDuration() {
        var recorder = new RelationalQueryExecutionStatsRecorder();

        recorder.RecordSuccess(plan: Plan(), rowCount: 1, duration: -5L)
            .Duration.Should().Be(0L);
        recorder.RecordFailure(plan: Plan(), duration: -1L)
            .Duration.Should().Be(0L);
    }

    /// <summary>
    /// 毫秒换算必须是整数除法（纳秒 → 毫秒），不能四舍五入：审计里的毫秒是"至少这么多"，取整才不会谎报更快。
    /// The millisecond conversion must be integer division (nanoseconds to milliseconds), not rounding: an audit
    /// millisecond value means "at least this much", so truncation never overstates speed.
    /// </summary>
    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(999_999L, 0L)]
    [InlineData(1_000_000L, 1L)]
    [InlineData(2_500_000L, 2L)]
    public void DurationMillis_ShouldTruncate(long nanoseconds, long expectedMillis) {
        var stats = new RelationalQueryExecutionStats(
            PlanHash: "hash",
            Canonical: "canonical",
            Duration: nanoseconds);

        stats.DurationMillis.Should().Be(expectedMillis);
    }

    /// <summary>
    /// 快照必须与内部状态解耦：之后再记录不得改变已经返回的列表。
    /// A snapshot must be decoupled from internal state: later records must not mutate a list already returned.
    /// </summary>
    [Fact]
    public void Snapshot_ShouldBeIndependent() {
        var recorder = new RelationalQueryExecutionStatsRecorder();
        recorder.RecordSuccess(plan: Plan(), rowCount: 1);

        IReadOnlyList<RelationalQueryExecutionStats> first = recorder.Snapshot();
        recorder.RecordSuccess(plan: Plan(), rowCount: 2);
        IReadOnlyList<RelationalQueryExecutionStats> second = recorder.Snapshot();

        first.Should().HaveCount(1);
        second.Should().HaveCount(2);
    }

    /// <summary>
    /// 顺序必须与调用顺序一致——审计靠顺序还原执行过程，乱序等于丢失信息。
    /// Order must follow insertion order: the audit reconstructs the execution from that order, and shuffling
    /// loses information.
    /// </summary>
    [Fact]
    public void Snapshot_ShouldPreserveInsertionOrder() {
        var recorder = new RelationalQueryExecutionStatsRecorder();
        for (int index = 0; index < 5; index++) {
            recorder.RecordSuccess(plan: Plan(), rowCount: index);
        }

        recorder.Snapshot().Select(stats => stats.RowCount)
            .Should().ContainInOrder(0L, 1L, 2L, 3L, 4L);
    }

    /// <summary>
    /// 并发记录必须一条不丢、且不抛异常（并发队列的理由就在这里）。
    /// Concurrent recording must lose nothing and never throw — that is precisely why the code uses a
    /// concurrent queue.
    /// </summary>
    [Fact]
    public void Record_ShouldBeSafeUnderConcurrency() {
        var recorder = new RelationalQueryExecutionStatsRecorder();
        QueryPlan plan = Plan();

        Parallel.For(0, 200, index => {
            if (index % 2 == 0) {
                recorder.RecordSuccess(plan: plan, rowCount: index, duration: index);
            }
            else {
                recorder.RecordFailure(plan: plan, duration: index);
            }
        });

        IReadOnlyList<RelationalQueryExecutionStats> snapshot = recorder.Snapshot();
        snapshot.Should().HaveCount(200);
        snapshot.Count(stats => stats.Failed).Should().Be(100);
    }

    /// <summary>兼容审计字段名必须与计划哈希同值 / The compatibility audit field must alias the plan hash.</summary>
    [Fact]
    public void CanonicalHash_ShouldAliasPlanHash() {
        var stats = new RelationalQueryExecutionStats(
            PlanHash: "abc",
            Canonical: "canonical",
            Duration: 0L);

        stats.CanonicalHash.Should().Be("abc");
    }

    /// <summary>计划为 null 必须直接拒绝 / A null plan must be rejected outright.</summary>
    [Fact]
    public void Record_ShouldRejectNullPlan() {
        var recorder = new RelationalQueryExecutionStatsRecorder();

        Action act = () => recorder.RecordSuccess(plan: null!, rowCount: 1);

        act.Should().Throw<ArgumentNullException>();
    }

    private static QueryPlan Plan() => new(
        root: new QuerySource("job"),
        groupBy: new[] { new ColumnRef("job", "id") });
}
