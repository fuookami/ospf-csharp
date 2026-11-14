// 活动排程复核的判据 / Criteria for activity schedule validation.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Activity;

/// <summary>
/// **复核从外部输入重推，每条违规都带定位信息** / Validation re-derives from external input, and every violation carries a location.
/// </summary>
/// <remarks>
/// 校验器若去读求解器写出的行，建模错误会被同一份数据确认，"复核"退化成"再跑一遍相同推理"。
/// 而只报"有冲突"等于让人自己在几百个活动里找。
///
/// A validator that read the solver's rows would confirm a modelling error from the same data, degenerating into "run the same reasoning again". Reporting only "there is a
/// conflict" leaves the user to search hundreds of activities by hand.
/// </remarks>
public class ActivityScheduleValidatorTests {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// **全部排上、依赖满足时无违规** / No violations when everything is scheduled and dependencies hold.
    [Fact]
    public void Validate_ShouldPassAWellFormedSchedule() {
        ActivityScheduleInput input = Input(
            activities: new[] { "A", "B" },
            dependencies: new[] { FinishToStart("A", "B") });

        ActivityScheduleValidation result = ActivityScheduleValidator.Validate(
            input, new[] { Placement("A", 0, 1), Placement("B", 1, 2) });

        result.IsValid.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    /// **同一候选重复出现被报出** / A duplicated choice is reported.
    [Fact]
    public void Validate_ShouldReportADuplicatedChoice() {
        ActivityScheduleInput input = Input(activities: new[] { "A" });

        ActivityScheduleValidation result = ActivityScheduleValidator.Validate(
            input, new[] { Placement("A", 0, 1), Placement("A", 0, 1) });

        result.Violations.Should().Contain("duplicate_choice");
    }

    /// <summary>
    /// **不可取消的活动缺排被报出，可取消的不报** / A missing mandatory activity is reported, a missing cancellable one is not.
    /// </summary>
    /// <remarks>
    /// 判据必须让两个活动**都缺排**，否则测的就不是"可取消与否"，而是"有没有排"：
    /// 若只缺一个可取消的活动，一个从不报缺失的实现照样通过。
    ///
    /// Both activities must be **missing**, otherwise this measures "was it scheduled" rather than "is it cancellable":
    /// with only a cancellable one missing, an implementation that never reports absences would also pass.
    /// </remarks>
    [Fact]
    public void Validate_ShouldDistinguishMandatoryFromCancellableMissing() {
        ActivityScheduleInput input = Input(
            activities: new[] { "A", "B" },
            cancellable: new HashSet<string> { "B" });

        ActivityScheduleValidation result = ActivityScheduleValidator.Validate(
            input, Array.Empty<ActivityPlacement<Flt64>>());

        result.Violations.Should().Contain("missing_required:A", "A is mandatory and was not scheduled");
        result.Violations.Should().NotContain("missing_required:B", "B is cancellable, so its absence is legitimate");
    }

    /// <summary>
    /// **一个任务被排了两次算 multiple_modes** / Scheduling one task twice is reported as multiple_modes.
    /// </summary>
    /// <remarks>
    /// 这与 `duplicate_choice` 是**两件不同的事**：同一候选重复是"抄了两遍"，不同模式并存是"两种做法都排上了"——
    /// 后者在时间上不重叠、也未必触发容量违规，只有这条规则能抓住。
    ///
    /// This is **a different thing** from `duplicate_choice`: repeating the same choice is "copied twice", while coexisting modes are "both plans were scheduled" — the latter
    /// need not overlap in time nor trip a capacity rule, and only this check catches it.
    /// </remarks>
    [Fact]
    public void Validate_ShouldReportTwoModesOfOneTask() {
        ActivityScheduleInput input = Input(activities: new[] { "A" });

        ActivityScheduleValidation result = ActivityScheduleValidator.Validate(
            input, new[] { Placement("A", 0, 1), Placement("A", 5, 6, mode: "alt") });

        result.Violations.Should().Contain("multiple_modes:A");
        result.Violations.Should().NotContain("duplicate_choice", "the two choices differ in mode and start");
    }

    /// **依赖时间违规带下标，能定位到具体那条** / A timing violation carries the index so the exact rule can be found.
    [Fact]
    public void Validate_ShouldReportADependencyTimeViolationWithItsIndex() {
        // B 在 A 结束之前开始，违反 Finish-to-Start。
        // B starts before A finishes, violating finish-to-start.
        ActivityScheduleInput input = Input(
            activities: new[] { "A", "B" },
            dependencies: new[] { FinishToStart("A", "B") });

        ActivityScheduleValidation result = ActivityScheduleValidator.Validate(
            input, new[] { Placement("A", 5, 6), Placement("B", 0, 1) });

        result.Violations.Should().Contain("dependency_time:0",
            "the index is what lets the user find the offending rule among many");
    }

    /// <summary>
    /// **存在性规则与时间规则分开判** / Presence is judged separately from timing.
    /// </summary>
    /// <remarks>
    /// 前驱缺失时，`RequirePredecessor` 必须报 `dependency_presence`，而**不能**去比时间（没有时间可比）。
    /// 把两者混在一起时，"前驱不存在"会被当成"前驱在某个时刻完成"，排程看着正常却凭空多出一个任务。
    ///
    /// When the predecessor is absent, `RequirePredecessor` must report `dependency_presence` and must **not** compare times (there is nothing to compare). Folding the two
    /// together turns "the predecessor does not exist" into "it finishes at some instant", and the schedule looks fine with a task that was never there.
    /// </remarks>
    [Fact]
    public void Validate_ShouldJudgePresenceSeparatelyFromTiming() {
        ActivityScheduleInput input = Input(
            activities: new[] { "A", "B" },
            dependencies: new[]
            {
                FinishToStart("A", "B") with { Presence = ActivityDependencyPresence.RequirePredecessor },
            });

        ActivityScheduleValidation result = ActivityScheduleValidator.Validate(
            input, new[] { Placement("B", 0, 1) });

        result.Violations.Should().Contain("dependency_presence:0");
        result.Violations.Should().NotContain("dependency_time:0",
            "there is no predecessor interval, so timing cannot be judged");
    }

    /// <summary>构造 Finish-to-Start 依赖 / Build a finish-to-start dependency.</summary>
    /// <param name="predecessor">前驱 / The predecessor.</param>
    /// <param name="successor">后继 / The successor.</param>
    /// <returns>依赖 / The dependency.</returns>
    private static ActivityDependency FinishToStart(string predecessor, string successor) =>
        new(predecessor, successor, ActivityEndpoint.Finish, ActivityEndpoint.Start, TimeSpan.Zero);

    /// <summary>构造静态输入 / Build the static input.</summary>
    /// <param name="activities">活动 / The activities.</param>
    /// <param name="dependencies">依赖 / The dependencies.</param>
    /// <param name="cancellable">可取消的活动 / The cancellable activities.</param>
    /// <returns>输入 / The input.</returns>
    private static ActivityScheduleInput Input(
        IReadOnlyList<string> activities,
        IReadOnlyList<ActivityDependency>? dependencies = null,
        IReadOnlySet<string>? cancellable = null) =>
        new(activities,
            cancellable ?? new HashSet<string>(),
            dependencies ?? Array.Empty<ActivityDependency>(),
            new HashSet<string>());

    /// <summary>构造一次放置 / Build a placement.</summary>
    /// <param name="task">任务 / The task.</param>
    /// <param name="startHour">开始小时 / The start hour.</param>
    /// <param name="endHour">结束小时 / The end hour.</param>
    /// <param name="mode">模式 / The mode.</param>
    /// <returns>放置 / The placement.</returns>
    private static ActivityPlacement<Flt64> Placement(
        string task, int startHour, int endHour, string mode = "default") =>
        new(new ActivityChoiceKey(task, mode, Origin.AddHours(startHour)),
            new TimeRange(Origin.AddHours(startHour), Origin.AddHours(endHour)),
            Array.Empty<ActivityReservation<Flt64>>());
}
