// 活动排程聚合的判据 / Criteria for the activity scheduling aggregation.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Domain;

/// <summary>
/// **活动排程聚合** / The activity scheduling aggregation.
/// </summary>
/// <remarks>
/// 判据的重点：**只能注册一次**、**`minimizeCost` 为真要求最小化模型**、
/// **`minimizeCost` 为假时不注册成本目标**、**取解要求模型是注册时那个实例**。
///
/// The criteria focus on **registration happening once**, on **`minimizeCost` requiring a minimisation model when true**, on **no cost objective being registered when false**, and on **extraction
/// requiring the very instance that was registered**.
/// </remarks>
public class ActivitySchedulingAggregationTests {
    private static Quantity<Flt64> Q(double value) => new(new Flt64(value), SIBaseUnits.Meter);
    private static DateTimeOffset At(int hour) =>
        new(2026, 11, 9, hour, 0, 0, TimeSpan.FromHours(8.0));
    private static TimeRange Range(int startHour, int endHour) => new(At(startHour), At(endHour));

    private static ActivitySchedulingAggregation BuildAggregation(bool withOrphanActivity = false) {
        var choices = new List<CompiledActivityChoice<Flt64>> {
            new("a1",
                new ActivityPlacement<Flt64>(
                    new ActivityChoiceKey("a1", "m", At(8)), Range(8, 10),
                    Array.Empty<ActivityReservation<Flt64>>()),
                Array.Empty<CompiledActivityReservation>(),
                new Flt64(10.0)),
            new("a1", null, Array.Empty<CompiledActivityReservation>(), new Flt64(3.0)),
        };
        var activities = new List<Activity<Flt64>> { new("a1", new List<ActivityMode<Flt64>>()) };
        if (withOrphanActivity) {
            activities.Add(new Activity<Flt64>("a2", new List<ActivityMode<Flt64>>()));
        }
        var problem = new ActivitySchedulingProblem<Flt64>(
            Horizon: Range(0, 23),
            Activities: activities,
            Resources: new List<ActivityResource<Flt64>>(),
            Dependencies: null, Baselines: null, Executions: null, Rows: null,
            CostUnit: SIBaseUnits.Meter);
        var data = new CompiledActivityProblem<Flt64>(
            problem, choices,
            new Dictionary<string, CompiledActivityResource<Flt64>>(),
            Array.Empty<CompiledActivityReservation>(),
            Array.Empty<CompiledActivityRow>(),
            new Dictionary<string, CompiledActivityMaterial<Flt64>>(),
            Array.Empty<CompiledActivityMaterialEvent>());
        return new ActivitySchedulingAggregation(data, "act");
    }

    /// <summary>
    /// **只能注册一次；最小化要求按需检查** / Registration happens once, and the minimisation requirement is checked only when asked.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 第一次注册成功 → `IsUsable` 为真。
    /// 2. **第二次注册失败**，且消息点名"只能一次"——静默成功会让调用方以为新模型被建好了。
    /// 3. `minimizeCost: true` 加到**最大化**模型上 → 失败，消息点名最小化要求。
    /// 4. `minimizeCost: false` 加到最大化模型上 → **成功**（不注册成本目标，因此不需要最小化）。
    ///
    /// Four paired points: the first registration succeeds and `IsUsable` becomes true; **a second registration fails** with a message naming the once-only rule, since a silent success would leave
    /// the caller believing a new model had been built; `minimizeCost: true` against a **maximisation** model fails with a message naming the requirement; and `minimizeCost: false` against the same
    /// model **succeeds**, because no cost objective is registered and therefore no minimisation is needed.
    /// </remarks>
    [Fact]
    public void Register_ShouldRunOnceAndCheckMinimisationOnlyWhenAsked() {
        ActivitySchedulingAggregation aggregation = BuildAggregation();
        using var minimum = new LinearMetaModel<Flt64>("m", ObjectCategory.Minimum);

        aggregation.Register(minimum).IsOk.Should().BeTrue();
        aggregation.IsUsable.Should().BeTrue();

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> second = aggregation.Register(minimum);
        second.IsOk.Should().BeFalse();
        ((Fuookami.Ospf.Utils.Functional.Failed<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)second).Error.Message
            .Should().Contain("only once",
                "a silent success would leave the caller believing a fresh model had been built");

        ActivitySchedulingAggregation onMaximum = BuildAggregation();
        using var maximum = new LinearMetaModel<Flt64>("m", ObjectCategory.Maximum);
        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> rejected = onMaximum.Register(maximum);
        rejected.IsOk.Should().BeFalse();
        ((Fuookami.Ospf.Utils.Functional.Failed<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)rejected).Error.Message
            .Should().Contain("minimisation");

        ActivitySchedulingAggregation withoutCost = BuildAggregation();
        using var maximumAgain = new LinearMetaModel<Flt64>("m", ObjectCategory.Maximum);
        withoutCost.Register(maximumAgain, minimizeCost: false).IsOk.Should().BeTrue(
            "no cost objective means no minimisation requirement");

        // **成本目标真的注册了 / 真的没注册**：这是 `minimizeCost` 的实质差别，不能只看注册成败。
        // **The cost objective is really registered, or really is not**: that is what `minimizeCost` means, not merely whether registration succeeded.
        minimum.MetaSubObjects.Should().ContainSingle("minimizeCost: true registers one cost objective");
        maximumAgain.MetaSubObjects.Should().BeEmpty("minimizeCost: false registers no objective at all");
    }

    /// <summary>
    /// **取解要求模型是注册时那个实例** / Extraction requires the very instance that was registered.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 未注册就取解 → 失败。
    /// 2. 注册后用**另一个**模型取解 → 失败（引用相等，不是名字相等）。
    /// 3. 用注册的那个模型取解、但解值未赋值 → 失败并点名"缺少解值"。
    /// 4. 两个选择的解值为 `1 / 0` → 成功，成本取第一个（`10`），取消集合为空。
    ///
    /// Four paired points: extracting before registering fails; extracting with **another** model after registering fails (reference equality, not name equality); extracting with the registered model
    /// but unassigned values fails naming the missing solution value; and values of `1` and `0` succeed, with the cost taken from the first choice (`10`) and an empty cancellation set.
    /// </remarks>
    [Fact]
    public void Extract_ShouldRequireTheRegisteredInstance() {
        ActivitySchedulingAggregation aggregation = BuildAggregation();
        using var model = new LinearMetaModel<Flt64>("m", ObjectCategory.Minimum);

        aggregation.ExtractSolution(model).IsOk.Should().BeFalse("nothing was registered yet");

        aggregation.Register(model).IsOk.Should().BeTrue();

        using var other = new LinearMetaModel<Flt64>("m", ObjectCategory.Minimum);
        aggregation.ExtractSolution(other).IsOk.Should().BeFalse("a different instance is not this context's model");
        Message(aggregation.ExtractSolution(other)).Should().Contain("does not belong to this context",
            "the owner guard must fire, not the missing-value guard behind it");

        aggregation.ExtractSolution(model).IsOk.Should().BeFalse("the values are still unassigned");
        Message(aggregation.ExtractSolution(model)).Should().Contain("missing activity-choice solution value");

        model.Tokens.Find(aggregation.Compilation.Variables[0])!.SetResult(new Flt64(1.0));
        model.Tokens.Find(aggregation.Compilation.Variables[1])!.SetResult(new Flt64(0.0));
        Fuookami.Ospf.Utils.Functional.Result<ActivitySchedule<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> extracted =
            aggregation.ExtractSolution(model);

        extracted.IsOk.Should().BeTrue();
        ActivitySchedule<Flt64> schedule = extracted.Value;
        schedule.Placements.Should().ContainSingle("only the first choice is selected");
        schedule.Cancelled.Should().BeEmpty();
        schedule.Cost.Value.ToFlt64().ToDouble().Should().BeApproximately(10.0, 1e-9);
        schedule.CandidateScope.Should().ContainSingle("only the choice with a placement is in scope");
    }

    private static string Message(
        Fuookami.Ospf.Utils.Functional.Result<ActivitySchedule<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result) =>
        result is Fuookami.Ospf.Utils.Functional.Failed<ActivitySchedule<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> failed
            ? failed.Error.Message
            : string.Empty;

    /// <summary>
    /// **每个活动必须恰好一个选择** / Each activity must select exactly one choice.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 两个选择都是 `1` → 失败，消息点名"恰好一个"。
    /// 2. 两个选择都是 `0` → **成功**：第二个选择是**取消列**，它不是"不选"，而是"显式取消"。
    /// 3. 取消成功时 `Cancelled` 含该活动、`Placements` 为空、成本取取消列的成本（`3`）。
    ///
    /// 第二点是我原先写错的：我以为"零个选择"该被拒，但取消列让"零个执行"成为合法解。
    /// **判据写错会让人把正确行为当成 bug 去"修"。**
    ///
    /// Three paired points: both choices at `1` fail with a message naming the exactly-one rule; both at `0` **succeed**, because the second choice is a **cancellation column** and stands for an
    /// explicit cancellation rather than "nothing chosen"; and a cancellation yields that activity in `Cancelled`, an empty `Placements` and the cancellation column's cost (`3`).
    ///
    /// That second point is where I was wrong first: I assumed "zero selections" must be rejected, but the cancellation column makes "zero executions" a legal solution. **A wrong criterion gets
    /// correct behaviour reported as a bug.**
    /// </remarks>
    [Fact]
    public void Extract_ShouldRejectAnythingButExactlyOnePerActivity() {
        ActivitySchedulingAggregation aggregation = BuildAggregation();
        using var model = new LinearMetaModel<Flt64>("m", ObjectCategory.Minimum);
        aggregation.Register(model).IsOk.Should().BeTrue();

        model.Tokens.Find(aggregation.Compilation.Variables[0])!.SetResult(new Flt64(1.0));
        model.Tokens.Find(aggregation.Compilation.Variables[1])!.SetResult(new Flt64(1.0));
        Message(aggregation.ExtractSolution(model)).Should().Contain("exactly one");

        model.Tokens.Find(aggregation.Compilation.Variables[0])!.SetResult(new Flt64(0.0));
        Fuookami.Ospf.Utils.Functional.Result<ActivitySchedule<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> cancelled =
            aggregation.ExtractSolution(model);
        cancelled.IsOk.Should().BeTrue("zero executions is legal when the cancellation column is taken");
        cancelled.Value.Cancelled.Should().Contain("a1");
        cancelled.Value.Placements.Should().BeEmpty();
        cancelled.Value.Cost.Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9,
            "the cost comes from the cancellation column");

        // **没有任何选择的活动必须被拒**：它既没有执行列也没有取消列，凑不出"恰好一个"。
        // 这一条也是 `count != 1` 与 `count > 1` 的唯一分界——只看"两个都选"分不开它们。
        // **An activity with no choice at all must be rejected**: it has neither an execution column nor a cancellation one, so "exactly one" cannot be met.
        // This is also the only thing separating `count != 1` from `count > 1`; "both chosen" alone cannot tell them apart.
        ActivitySchedulingAggregation withOrphan = BuildAggregation(withOrphanActivity: true);
        using var orphanModel = new LinearMetaModel<Flt64>("m", ObjectCategory.Minimum);
        withOrphan.Register(orphanModel).IsOk.Should().BeTrue();
        orphanModel.Tokens.Find(withOrphan.Compilation.Variables[0])!.SetResult(new Flt64(1.0));
        orphanModel.Tokens.Find(withOrphan.Compilation.Variables[1])!.SetResult(new Flt64(0.0));
        Message(withOrphan.ExtractSolution(orphanModel)).Should().Contain("exactly one",
            "a2 has no column at all, so zero selections must be refused");
    }
}
