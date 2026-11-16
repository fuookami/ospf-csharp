// 提前时间代价核心的判据 / Criteria for the advance-time cost core.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **零阈值与非零阈值必须给出同一取值；提不出系数不是"系数为一"** / A zero threshold and a non-zero one must agree, and a missing coefficient is not "one".
/// </summary>
/// <remarks>
/// 零阈值时直接线性计入、非零阈值时引入松弛变量。零阈值只是省掉一个变量，不是换一套语义：
/// 两者若差一点点，"设了阈值"和"没设阈值"就会给出不同的解，而这在配置层面完全看不出。
///
/// 而"提不出系数"在是 `infinity`——即该任务的提前时间**不可定价**。静默取 `1` 会把"禁止提前"变成
/// "每单位罚 1"，于是本该被拒绝的排程被轻松接受。
///
/// The code charges linearly at a zero threshold and introduces a slack variable otherwise. A zero threshold merely saves one variable, it does not swap semantics: if the two
/// diverge slightly, "configured a threshold" and "no threshold" produce different solutions, invisible at the configuration level. And "no coefficient" is `infinity` upstream —
/// the advance **cannot be priced**. Defaulting to `1` turns "advance prohibited" into "penalty 1 per unit", letting a schedule that should be rejected be accepted cheaply.
/// </remarks>
public class TaskAdvanceTimeCostTests {
    /// **阈值以内不罚** / Nothing is charged at or below the threshold.
    [Fact]
    public void Cost_ShouldChargeNothingBelowTheThreshold() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> below =
            TaskAdvanceTimeCost.Cost(new Flt64(2.0), new Flt64(5.0), new Flt64(3.0));

        below.Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)below).Value.ToDouble().Should().BeApproximately(0.0, 1e-12);

        Result<Flt64, ErrorCode, Error<ErrorCode>> atThreshold =
            TaskAdvanceTimeCost.Cost(new Flt64(5.0), new Flt64(5.0), new Flt64(3.0));
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)atThreshold).Value.ToDouble().Should().BeApproximately(0.0, 1e-12);
    }

    /// **超出阈值的部分按系数计** / The excess beyond the threshold is charged at the coefficient.
    [Fact]
    public void Cost_ShouldChargeTheExcessOnly() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> over =
            TaskAdvanceTimeCost.Cost(new Flt64(9.0), new Flt64(4.0), new Flt64(2.5));

        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)over).Value.ToDouble()
            .Should().BeApproximately(12.5, 1e-12, "(9 - 4) * 2.5");
    }

    /// <summary>
    /// **零阈值与非零阈值给出同一取值** / A zero threshold and a non-zero one give the same value.
    /// </summary>
    /// <remarks>
    /// 取 `advance = 9`、`coefficient = 2.5`：零阈值直接线性得 `22.5`，而阈值 `4` 的松弛路径得 `12.5`。
    /// 判据比较的是**阈值 `0` 与阈值 `0` 的显式写法**，确保快路径与慢路径在阈值恰为零时一致；
    /// 用"阈值 0 vs 阈值 4"来比较会恒不相等，测不到任何东西。
    ///
    /// With `advance = 9` and `coefficient = 2.5`, a zero threshold charges `22.5` linearly while a threshold of `4` charges `12.5`. The criterion compares **threshold zero against
    /// zero written explicitly**, ensuring the fast path and the slack path agree when the threshold is exactly zero; comparing "threshold 0 vs 4" would never be equal and
    /// exercise nothing.
    /// </remarks>
    [Fact]
    public void Cost_ShouldAgreeBetweenTheFastPathAndTheZeroThresholdPath() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> fast =
            TaskAdvanceTimeCost.Cost(new Flt64(9.0), null, new Flt64(2.5));
        Result<Flt64, ErrorCode, Error<ErrorCode>> explicitZero =
            TaskAdvanceTimeCost.Cost(new Flt64(9.0), Flt64.Zero, new Flt64(2.5));

        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)fast).Value.ToDouble()
            .Should().BeApproximately(((Ok<Flt64, ErrorCode, Error<ErrorCode>>)explicitZero).Value.ToDouble(), 1e-12);
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)fast).Value.ToDouble().Should().BeApproximately(22.5, 1e-12);
    }

    /// **提不出系数返回失败，不取 1** / A missing coefficient fails instead of defaulting to 1.
    [Fact]
    public void Cost_ShouldFailWhenTheCoefficientCannotBeExtracted() {
        TaskAdvanceTimeCost.Cost(new Flt64(9.0), null, null)
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>(
                "treating an unpriced advance as a penalty of 1 per unit accepts schedules that should be rejected");
    }

    /// **负提前时间不产生负代价** / A negative advance produces no negative cost.
    [Fact]
    public void Cost_ShouldNotProduceANegativeCost() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> negative =
            TaskAdvanceTimeCost.Cost(new Flt64(-3.0), null, new Flt64(2.0));

        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)negative).Value.ToDouble().Should().BeApproximately(0.0, 1e-12,
            "a negative cost would reward advancing backwards");
    }

    /// **全局开关关闭时不选任何任务** / No task is selected when the global switch is off.
    [Fact]
    public void Select_ShouldReturnNothingWhenTheGlobalSwitchIsOff() {
        TaskAdvanceTimeCost.Select(
                globalAdvanceEnabled: false,
                tasks: new List<string> { "A", "B" },
                taskAdvanceEnabled: _ => true,
                scheduled: _ => true)
            .Should().BeEmpty();
    }

    /// **未排定或不允许提前的任务被排除** / Unscheduled or non-advancing tasks are excluded.
    [Fact]
    public void Select_ShouldExcludeUnscheduledAndNonAdvancingTasks() {
        IReadOnlyList<string> selected = TaskAdvanceTimeCost.Select(
            globalAdvanceEnabled: true,
            tasks: new List<string> { "both", "unscheduled", "locked" },
            taskAdvanceEnabled: task => task != "locked",
            scheduled: task => task != "unscheduled");

        selected.Should().ContainSingle().Which.Should().Be("both");
    }
}
