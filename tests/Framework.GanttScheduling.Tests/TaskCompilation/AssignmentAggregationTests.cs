// 分配/编译聚合重写规则的判据 / Criteria for the assignment/compilation rewrite rules.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **守卫不能省；两张表的分组键不同** / The guard cannot be dropped, and the two tables group by different keys.
/// </summary>
/// <remarks>
/// 守卫为假时不动聚合；若写成无条件重写，每个原始任务每轮都会被 `flush()` 成空——
/// 所有指派关系消失，而**没有任何错误或警告**：之后的换型、冲突、成本都读到零，模型只是"什么都没做"。
///
/// When the guard is false the aggregate is left alone; an unconditional rewrite would `flush()` every origin task into an empty sum every round — all assignment relations vanish with
/// **no error or warning**: switching, conflicts and cost then read zero, and the model merely "does nothing".
/// </remarks>
public class AssignmentAggregationTests {
    /// **没有新列就不重写** / No new column means no rewrite.
    [Fact]
    public void ShouldRewrite_ShouldBeFalseWithoutNewColumns() =>
        AssignmentAggregation.ShouldRewrite(0).Should().BeFalse(
            "an unconditional rewrite would flush the aggregate into an empty sum");

    /// **有新列才重写** / A rewrite happens only with new columns.
    [Theory]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public void ShouldRewrite_ShouldHoldWithNewColumns(int count, bool expected) =>
        AssignmentAggregation.ShouldRewrite(count).Should().Be(expected);

    /// **负数列数被拒绝** / A negative count is rejected.
    [Fact]
    public void ShouldRewrite_ShouldRejectNegativeCounts() =>
        ((Action)(() => AssignmentAggregation.ShouldRewrite(-1)))
            .Should().Throw<ArgumentOutOfRangeException>();

    /// **分配聚合要求标识与执行器都相同** / The assignment aggregate needs both identity and executor.
    [Fact]
    public void MatchesAssignment_ShouldRequireBothParts() {
        AssignmentAggregation.MatchesAssignment("T1", "E1", "T1", "E1", Same, Same)
            .Should().BeTrue();
        AssignmentAggregation.MatchesAssignment("T1", "E1", "T1", "E2", Same, Same)
            .Should().BeFalse("another executor's column is not this executor's assignment");
    }

    /// **标识不同就不计入，即使执行器相同** / A different identity is excluded even on the same executor.
    [Fact]
    public void MatchesAssignment_ShouldRejectADifferentIdentity() =>
        AssignmentAggregation.MatchesAssignment("T1", "E1", "T2", "E1", Same, Same).Should().BeFalse();

    /// <summary>
    /// **编译聚合不看执行器** / The compilation aggregate ignores the executor.
    /// </summary>
    /// <remarks>
    /// 把执行器也算进来会让"该任务达成"只统计某一个执行器的列，达成量偏低——
    /// 而偏低是安静的：它只是一个更小的数。
    ///
    /// Including the executor would count only one executor's columns towards "this task is achieved", understating it — and understating is quiet: it is merely a smaller number.
    /// </remarks>
    [Fact]
    public void MatchesCompilation_ShouldIgnoreTheExecutor() =>
        AssignmentAggregation.MatchesCompilation("T1", "T1", Same).Should().BeTrue(
            "the compilation aggregate spans every executor");

    /// **编译聚合仍然按标识匹配** / The compilation aggregate still matches on identity.
    [Fact]
    public void MatchesCompilation_ShouldMatchOnIdentity() =>
        AssignmentAggregation.MatchesCompilation("T1", "T2", Same).Should().BeFalse();

    /// <summary>返回布尔判定的替身 / A stand-in returning a boolean verdict.</summary>
    /// <typeparam name="T">比较类型 / The compared type.</typeparam>
    /// <param name="left">左侧 / Left.</param>
    /// <param name="right">右侧 / Right.</param>
    /// <returns>是否相等 / Whether equal.</returns>
    private static bool Same<T>(T left, T right) => EqualityComparer<T>.Default.Equals(left, right);
}
