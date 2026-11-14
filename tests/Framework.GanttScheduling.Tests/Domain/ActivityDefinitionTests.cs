// 活动定义（B685）的判据 / Criteria for the activity definitions (B685).

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Domain;

/// <summary>
/// **活动定义（B685）** / The activity definitions (B685).
/// </summary>
/// <remarks>
/// 判据的重点：**null 访问器返回空而不是 null**、**`Until` 为 null 表示"持续到结束"而不是零长度**、
/// **取消成本为 null 表示不允许取消**、**时长与成本挂在模式上而不是活动上**。
///
/// The criteria focus on **null accessors returning empty rather than null**, on **a null `Until` meaning "through to the finish" rather than zero length**, on **a null cancellation cost meaning
/// cancellation is not allowed**, and on **duration and cost hanging off the mode rather than the activity**.
/// </remarks>
public class ActivityDefinitionTests {
    private static Quantity<Flt64> Q(double value) => new(new Flt64(value), SIBaseUnits.Meter);
    private static TimeRange Range(int startHour, int endHour) =>
        new(new DateTimeOffset(2026, 11, 9, startHour, 0, 0, TimeSpan.FromHours(8.0)),
            new DateTimeOffset(2026, 11, 9, endHour, 0, 0, TimeSpan.FromHours(8.0)));

    /// <summary>
    /// **null 访问器返回空，取消成本为 null 表示不可取消** / Null accessors return empty, and a null cancellation cost means cancellation is not allowed.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. `ActivityResource` 两个列表都不给 → **两个空列表**，不是 null。
    /// 2. `ActivityMode.MaterialFlowsValue` 同样为空。
    /// 3. `Activity.CancellationCost` **为 null**（保持原始 null，不兜底成 0）。
    /// 4. `ActivityResourceUse.Until` **为 null**，而 `FromValue` 兜底成零——两者处理不同。
    ///
    /// Four paired points: an `ActivityResource` with neither list yields **two empty lists** rather than null; `ActivityMode.MaterialFlowsValue` is likewise empty; `Activity.CancellationCost` **stays
    /// null** (not defaulted to zero); and `ActivityResourceUse.Until` **is null** while `FromValue` defaults to zero — the two being handled differently.
    /// </remarks>
    [Fact]
    public void Defaults_ShouldBeEmptyButKeepTheSemanticNulls() {
        var resource = new ActivityResource<Flt64>("m1", Q(8.0));
        resource.UnavailableTimesValue.Should().BeEmpty();
        resource.CapacitiesValue.Should().BeEmpty();

        var use = new ActivityResourceUse<Flt64>("m1", Q(1.0));
        use.FromValue.Should().Be(TimeSpan.Zero, "an absent offset means from the activity's start");
        use.Until.Should().BeNull("null means through to the finish, which is not a zero length");

        var mode = new ActivityMode<Flt64>("fast", TimeSpan.FromHours(2.0),
            new List<DateTimeOffset> { Range(8, 10).Start },
            new List<ActivityResourceUse<Flt64>> { use }, Q(100.0));
        mode.MaterialFlowsValue.Should().BeEmpty();

        var activity = new Activity<Flt64>("a1", new List<ActivityMode<Flt64>> { mode });
        activity.CancellationCost.Should().BeNull("a null cancellation cost means cancellation is not allowed");
    }

    /// <summary>
    /// **时长与成本挂在模式上** / Duration and cost hang off the mode.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 同一活动的两个模式可以**时长不同**（2 小时与 5 小时）。
    /// 2. 成本也各自不同（100 与 250）——换模式就是换一整套参数。
    /// 3. `Activity` 本身**没有** `Duration` 或 `Cost` 属性：这类数只能从模式读，
    ///    否则"活动的时长"会出现第二个来源。
    ///
    /// Three paired points: two modes of the same activity may **differ in duration** (2 hours and 5); their costs differ too (100 and 250), since switching mode swaps the whole parameter set; and
    /// `Activity` itself carries **no** `Duration` or `Cost` property, so those numbers have exactly one source rather than a second one on the activity.
    /// </remarks>
    [Fact]
    public void Modes_ShouldCarryTheirOwnDurationAndCost() {
        var fast = new ActivityMode<Flt64>("fast", TimeSpan.FromHours(2.0),
            new List<DateTimeOffset> { Range(8, 10).Start },
            new List<ActivityResourceUse<Flt64>>(), Q(100.0));
        var cheap = new ActivityMode<Flt64>("cheap", TimeSpan.FromHours(5.0),
            new List<DateTimeOffset> { Range(8, 13).Start },
            new List<ActivityResourceUse<Flt64>>(), Q(250.0));

        var activity = new Activity<Flt64>("a1", new List<ActivityMode<Flt64>> { fast, cheap });

        activity.Modes.Should().HaveCount(2);
        activity.Modes[0].Duration.Should().Be(TimeSpan.FromHours(2.0));
        activity.Modes[1].Duration.Should().Be(TimeSpan.FromHours(5.0));
        activity.Modes[0].Cost.Value.ToFlt64().ToDouble().Should().BeApproximately(100.0, 1e-9);
        activity.Modes[1].Cost.Value.ToFlt64().ToDouble().Should().BeApproximately(250.0, 1e-9);

        typeof(Activity<Flt64>).GetProperty("Duration").Should().BeNull(
            "duration belongs to the mode; a second source on the activity would let them disagree");
        typeof(Activity<Flt64>).GetProperty("Cost").Should().BeNull();
    }
}
