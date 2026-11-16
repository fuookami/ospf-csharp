// 约简成本截断值的判据 / Criteria for the reduced-cost cutoff.
//
// 私有函数 `NextReducedCostCutoff` 无法直接调用，因此经 public 的 `RemoveColumns` 间接覆盖：
// 当剩余列数超过上限时，它返回的正是那个截断值。
// The private `NextReducedCostCutoff` cannot be called directly, so it is exercised through the public `RemoveColumns`,
// which returns exactly that cutoff when the remaining columns exceed the limit.

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;

/// <summary>
/// **截断值是先向下取整再整数乘除，不是浮点乘除后截断** / The cutoff floors first and then multiplies and divides in integers, rather than truncating a floating result.
/// </summary>
/// <remarks>
/// 两者在非整数输入上差 1，而**一次差 1 的截断会换掉一批列**：截断值决定哪些列被判为值得保留。
/// 判据用 `7.9` 这类输入，两种算法给出 `4` 与 `5` —— 整数输入（如 `9`）下两者都得 `6`，判据无法区分。
///
/// The two differ by one on non-integral input, and **being off by one swaps a batch of columns**: the cutoff decides which columns count as worth keeping.
/// The criteria use inputs like `7.9`, where the two algorithms give `4` and `5`; on integral input such as `9` both give `6` and the criterion cannot discriminate.
/// </remarks>
public class ReducedCostCutoffTests {
    /// <summary>
    /// **非整数输入下路径向下取整** / Non-integral input follows its floor-first path.
    /// </summary>
    /// <remarks>
    /// 输入要选**两条路径都高于 5 下限**的，否则下限钳制会把差异抹平：
    /// `7.9` 走基线得 `floor(7.9)=7 → 14/3=4`，被钳到 `5`；浮点路径得 `(long)5.266=5`，也是 `5`——两条路径不可区分。
    /// `10.9` 才是判别点：基线 `floor → 10 → 20/3=6`，浮点 `(long)7.266=7`，差 1 且都在下限之上。
    ///
    /// The input must keep **both paths above the floor of 5**, or the clamp erases the difference:
    /// `7.9` gives `floor(7.9)=7 → 14/3=4` on the code path, clamped to `5`, and `(long)5.266=5` on the floating path — also `5`, indistinguishable.
    /// `10.9` is the discriminating point: baseline `floor → 10 → 20/3=6` versus floating `(long)7.266=7`, off by one and both above the floor.
    /// </remarks>
    [Fact]
    public void RemoveColumns_ShouldFloorBeforeScaling()
        => CutoffFor(10.9).ToDouble().Should().BeApproximately(6.0, 1e-12);

    /// **截断值有 5 的下限** / The cutoff has a lower bound of 5.
    [Fact]
    public void RemoveColumns_ShouldClampToFive() {
        CutoffFor(1.0).ToDouble().Should().BeApproximately(5.0, 1e-12);
        CutoffFor(7.0).ToDouble().Should().BeApproximately(5.0, 1e-12, "7 * 2 / 3 = 4, below the floor");
        CutoffFor(9.0).ToDouble().Should().BeApproximately(6.0, 1e-12, "9 * 2 / 3 = 6, above the floor");
    }

    /// **未超上限时原样返回输入** / Below the column limit the input is returned unchanged.
    [Fact]
    public void RemoveColumns_ShouldReturnTheInputWhenTheLimitIsNotExceeded() {
        Fixture fixture = Build(columnCount: 2);

        Flt64 result = ValueOf(fixture.Aggregation.RemoveColumns(
            new Flt64(7.9), 5UL, _ => new Flt64(0.0), new HashSet<TestTaskBunch>(), new HashSet<TestTaskBunch>(), new object()));

        result.ToDouble().Should().BeApproximately(7.9, 1e-12,
            "nothing had to be trimmed, so no cutoff is applied");
    }

    /// **恰好等于上限时不裁剪** / Exactly at the limit nothing is trimmed.
    [Fact]
    public void RemoveColumns_ShouldNotTrimAtExactlyTheLimit() {
        Fixture fixture = Build(columnCount: 3);

        Flt64 result = ValueOf(fixture.Aggregation.RemoveColumns(
            new Flt64(7.9), 3UL, _ => new Flt64(0.0), new HashSet<TestTaskBunch>(), new HashSet<TestTaskBunch>(), new object()));

        result.ToDouble().Should().BeApproximately(7.9, 1e-12, "the comparison is strictly greater-than");
    }

    /// <summary>构造若干列并返回其截断值 / Build several columns and return the resulting cutoff.</summary>
    /// <param name="maximumReducedCost">输入的最大约简成本 / The input maximum reduced cost.</param>
    /// <returns>截断值 / The cutoff.</returns>
    private static Flt64 CutoffFor(double maximumReducedCost) {
        Fixture fixture = Build(columnCount: 4);

        return ValueOf(fixture.Aggregation.RemoveColumns(
            new Flt64(maximumReducedCost),
            maximumColumnAmount: 1UL,
            // 约简成本 0 小于截断值，于是没有列被移除，剩余列数恒为 4 > 1，必然走截断分支。
            // A reduced cost of 0 is below any cutoff, so no column is removed, the remaining count stays 4 > 1 and the cutoff branch is always taken.
            _ => new Flt64(0.0),
            new HashSet<TestTaskBunch>(),
            new HashSet<TestTaskBunch>(),
            new object()));
    }

    /// <summary>构造带若干列的聚合 / Build an aggregation holding several columns.</summary>
    /// <param name="columnCount">列数 / The number of columns.</param>
    /// <returns>夹具 / The fixture.</returns>
    private static Fixture Build(int columnCount) {
        var executor = new TestExecutor("E1", "executor");
        var usability = new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, DateTimeOffset.Now);
        // 任务束构造会用 tasks[0].Time!.Start 推算跨度，因此任务必须带时间。
        // The bunch constructor derives its span from tasks[0].Time!.Start, so the task must carry a time.
        var taskTime = new TimeRange(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero));
        // 每列带不同数量的任务，否则会被 `Eq` 判为同一列而去重，剩余列数恒为 1，
        // 于是永远走不到"超上限"的截断分支。
        // Each column carries a different task count, otherwise `Eq` treats them as the same column and dedups them, leaving a single column and never reaching the
        // over-limit cutoff branch.
        var tasks = new List<TestTask>();
        var bunches = new List<TestTaskBunch>();
        for (int index = 0; index < columnCount; index++) {
            var columnTasks = new List<TestTask>();
            for (int inner = 0; inner <= index; inner++) {
                columnTasks.Add(new TestTask("T" + index + "_" + inner, "task", taskTime));
            }
            tasks.AddRange(columnTasks);
            bunches.Add(new TestTaskBunch(executor, usability, columnTasks, iteration: index));
        }

        var aggregation = new BunchCompilationAggregation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, new List<TestExecutor> { executor });
        IReadOnlyList<TestTaskBunch> added = aggregation.Compilation.Aggregation.AddColumns(bunches);
        if (added.Count != columnCount) {
            throw new InvalidOperationException(
                "the fixture must keep " + columnCount + " distinct columns, but " + added.Count + " survived dedup");
        }
        return new Fixture(aggregation);
    }

    /// <summary>夹具 / The fixture.</summary>
    /// <param name="Aggregation">聚合 / The aggregation.</param>
    private sealed record Fixture(
        BunchCompilationAggregation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> Aggregation);

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    private static Flt64 ValueOf(Result<Flt64, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<Flt64, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("the cutoff computation must succeed");
}
