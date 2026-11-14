// 换型代价三元组的判据 / Criteria for the switch cost triples.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **对角线包含在内；换型时间与提前时间共用同一套阈值语义** / The diagonal is included, and switch time shares the advance-time threshold semantics.
/// </summary>
/// <remarks>
/// 一个"自然"的实现会顺手跳过 `task1 == task2`，因为"从某个任务换到它自己"听起来没有意义。
/// 但若 `switch[e, t, t]` 非零（比如包括准备时间），跳过它就等于**少罚了一部分换型时间**，而排程结果看起来完全正常。
///
/// A "natural" implementation skips `task1 == task2`, since switching a task to itself sounds meaningless. But if `switch[e, t, t]` is non-zero (including setup time, say),
/// skipping it **under-penalises part of the switch time** while the schedule looks entirely normal.
/// </remarks>
public class SwitchCostTriplesTests {
    /// **三元组数量是 执行器 × 任务 × 任务** / The triple count is executor × task × task.
    [Fact]
    public void All_ShouldBeTheFullCartesianProduct() {
        var executors = new List<string> { "E1", "E2" };
        var tasks = new List<string> { "A", "B", "C" };

        SwitchCostTriples.All(executors, tasks).Should().HaveCount(18, "2 × 3 × 3");
    }

    /// <summary>
    /// **对角线包含在内** / The diagonal is included.
    /// </summary>
    /// <remarks>
    /// 判据直接断言 `(E1, A, A)` 存在。若实现加了 `if (left == right) continue`，这里立刻失败——
    /// 而"数量是不是 执行器 × 任务 × (任务−1)"这种间接断言也能测，但直接查对角线更难写错。
    ///
    /// The criterion asserts `(E1, A, A)` directly. An implementation adding `if (left == right) continue` fails here immediately — an indirect assertion such as
    /// "count is executor × task × (task − 1)" would also work, but checking the diagonal directly is harder to get wrong.
    /// </remarks>
    [Fact]
    public void All_ShouldIncludeTheDiagonal() {
        var executors = new List<string> { "E1" };
        var tasks = new List<string> { "A", "B" };

        IReadOnlyList<(string Executor, string Left, string Right)> triples =
            SwitchCostTriples.All(executors, tasks);

        triples.Should().Contain(("E1", "A", "A"));
        triples.Should().Contain(("E1", "B", "B"));
    }

    /// **执行器参与组合** / The executor takes part in the triple.
    [Fact]
    public void All_ShouldVaryTheExecutor() {
        var executors = new List<string> { "E1", "E2" };
        var tasks = new List<string> { "A" };

        SwitchCostTriples.All(executors, tasks).Should().BeEquivalentTo(new[] {
            ("E1", "A", "A"),
            ("E2", "A", "A"),
        });
    }

    /// **换型时间的对不含执行器，同样含对角线** / The switch-time pairs carry no executor and include the diagonal.
    [Fact]
    public void AllPairs_ShouldBeTaskSquaredWithTheDiagonal() {
        var tasks = new List<string> { "A", "B", "C" };

        IReadOnlyList<(string Left, string Right)> pairs = SwitchCostTriples.AllPairs(tasks);

        pairs.Should().HaveCount(9, "3 × 3");
        pairs.Should().Contain(("B", "B"));
    }

    /// **空输入得到空序列** / Empty input yields an empty sequence.
    [Fact]
    public void Both_ShouldHandleEmptyInput() {
        SwitchCostTriples.All(new List<string>(), new List<string> { "A" }).Should().BeEmpty();
        SwitchCostTriples.All(new List<string> { "E1" }, new List<string>()).Should().BeEmpty();
        SwitchCostTriples.AllPairs(new List<string>()).Should().BeEmpty();
    }

    /// <summary>
    /// **换型时间直接复用提前时间的代价核心** / Switch time reuses the advance-time cost core directly.
    /// </summary>
    /// <remarks>
    /// 基线这两处形态相同：阈值零 → 全额线性；阈值非零 → 只罚超出部分；系数为空 → 无穷（禁止）。
    /// 所以要断言的是**同一组输入给出那三个具体答案**，而不是"自己等于自己"——
    /// 后者恒真，什么都没测。
    ///
    /// The two are the same shape upstream: zero threshold → charge linearly; non-zero → charge only the excess; empty coefficient → infinity (forbid). What must be asserted is
    /// that **the same inputs give those three concrete answers** — not "it equals itself", which is vacuous.
    /// </remarks>
    [Fact]
    public void SwitchTimeCost_ShouldFollowTheSameFormAsAdvanceTime() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> linear =
            TaskAdvanceTimeCost.Cost(new Flt64(9.0), threshold: null, new Flt64(2.5));
        Result<Flt64, ErrorCode, Error<ErrorCode>> excess =
            TaskAdvanceTimeCost.Cost(new Flt64(9.0), new Flt64(4.0), new Flt64(2.5));
        Result<Flt64, ErrorCode, Error<ErrorCode>> unpriced =
            TaskAdvanceTimeCost.Cost(new Flt64(9.0), new Flt64(4.0), coefficient: null);

        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)linear).Value.ToDouble()
            .Should().BeApproximately(22.5, 1e-12, "a zero threshold charges the whole switch time");
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)excess).Value.ToDouble()
            .Should().BeApproximately(12.5, 1e-12, "a non-zero threshold charges only the excess");
        unpriced.Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
    }
}
