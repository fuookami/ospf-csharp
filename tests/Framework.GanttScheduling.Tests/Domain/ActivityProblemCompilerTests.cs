#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Domain;

/// <summary>
/// **`ActivityProblemCompiler.Prepare` 的四条拒绝规则** / The four refusal rules of `ActivityProblemCompiler.Prepare`.
/// </summary>
/// <remarks>
/// **`ActivityProblemCompiler.cs` 有 577 行，而全仓测试里一次都没提到它的名字。**
/// 它做的是"把问题编译成模型"**之前**的校验：时域非空、任务/资源/执行三类身份各自不重复、且任务与执行不许撞名。
///
/// **这些校验一旦失效，症状不是报错而是"编译出一个错的模型"**：
/// 重复的任务 ID 会让两个活动悄悄合并成一个，**而目标函数、约束、解全都算得出来，只是算的是另一个问题。**
///
/// **`ActivityProblemCompiler.cs` is 577 lines and appears nowhere in the test tree.**
/// It validates **before** compiling the problem into a model: a non-empty horizon, no duplicate identity within
/// tasks, resources or executions, and no task colliding with an execution.
///
/// **When these checks fail the symptom is not an error but "a wrong model compiles fine"**:
/// a duplicate task id silently merges two activities, **and the objective, the constraints and the solution all come out —
/// of a different problem.**
/// </remarks>
public class ActivityProblemCompilerTests
{
    /// <summary>**合法问题必须编译成功** / A legal problem must compile.</summary>
    /// <remarks>
    /// **对照组先立住**，否则后面四条"被拒"证明不了什么——**一个永远失败的编译器也能通过它们全部。**
    ///
    /// **The control comes first**, otherwise the four refusals below prove nothing —
    /// **a compiler that always fails would pass every one of them.**
    /// </remarks>
    [Fact]
    public void TheLegalProblem_ShouldCompile()
    {
        ActivityProblemCompiler.Prepare(Problem())
            .Should().BeOfType<Ok<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>>>(
                "the fixture is legal, or the refusals below prove nothing");
    }

    /// <summary>**空时域必须被拒** / An empty horizon must be refused.</summary>
    /// <remarks>
    /// **时域是全部时间约束的定义域**：空的时域让"活动落在时域内"变成恒假，
    /// **而求解器会把它读成"不可行"——于是真实原因是参数写错，报出来却是"排不下"。**
    ///
    /// **The horizon is the domain of every time constraint**: an empty horizon makes "the activity falls inside it" vacuously false,
    /// **and the solver reports infeasible — so the real cause is a bad parameter while the report says "does not fit".**
    /// </remarks>
    [Fact]
    public void TheEmptyHorizon_ShouldBeRefused()
    {
        ActivitySchedulingProblem<Flt64> problem = Problem() with { Horizon = Range(8, 8) };

        ActivityProblemCompiler.Prepare(problem)
            .Should().BeOfType<Failed<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>>>(
                "a zero-length horizon schedules nothing");
    }

    /// <summary>**反向时域必须被拒** / A reversed horizon must be refused.</summary>
    /// <remarks>
    /// **比空时域更隐蔽**：`End &lt; Start` 不会让任何比较变假，
    /// **它让"落在时域内"这条约束永远无法满足，而报错看起来像排程失败。**
    ///
    /// **Sneakier than an empty one**: `End &lt; Start` does not make any comparison false,
    /// **it makes "inside the horizon" unsatisfiable while the error looks like a scheduling failure.**
    /// </remarks>
    [Fact]
    public void TheReversedHorizon_ShouldBeRefused()
    {
        ActivitySchedulingProblem<Flt64> problem = Problem() with { Horizon = Range(20, 4) };

        ActivityProblemCompiler.Prepare(problem)
            .Should().BeOfType<Failed<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>>>(
                "an end before its start is not a horizon");
    }

    /// <summary>**重复的任务 ID 必须被拒** / A duplicate task id must be refused.</summary>
    /// <remarks>
    /// **这是四条里最危险的一条**：两个活动同 ID 时，**编译出来的模型只有一个活动**，
    /// **而排程结果看起来完全正常**——它只是漏掉了一整项工作。
    ///
    /// **This is the most dangerous of the four**: two activities sharing an id **compile into a single activity**,
    /// **and the schedule looks entirely normal** — it simply dropped a whole piece of work.
    /// </remarks>
    [Fact]
    public void TheDuplicateTask_ShouldBeRefused()
    {
        ActivitySchedulingProblem<Flt64> problem = Problem() with {
            Activities = [Activity("a1"), Activity("a1")],
        };

        ActivityProblemCompiler.Prepare(problem)
            .Should().BeOfType<Failed<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>>>(
                "two activities with one id is one activity too few");
    }

    /// <summary>**重复的资源 ID 必须被拒** / A duplicate resource id must be refused.</summary>
    /// <remarks>
    /// **资源重复会让容量被"后一个覆盖前一个"**：一个 8 小时的资源和一个 2 小时的资源同 ID 时，
    /// **模型只看到其中一个，而另一个的容量凭空消失或凭空出现。**
    ///
    /// **A duplicate resource means one capacity silently replaces the other**: an 8-hour and a 2-hour resource sharing an id
    /// **leave the model seeing one of them, with the other's capacity vanishing or appearing out of nowhere.**
    /// </remarks>
    [Fact]
    public void TheDuplicateResource_ShouldBeRefused()
    {
        ActivitySchedulingProblem<Flt64> problem = Problem() with {
            Resources = [new ActivityResource<Flt64>("m1", Q(8.0)), new ActivityResource<Flt64>("m1", Q(2.0))],
        };

        ActivityProblemCompiler.Prepare(problem)
            .Should().BeOfType<Failed<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>>>(
                "two capacities under one id is an ambiguity, not a merge");
    }

    /// <summary>**任务与执行撞名必须被拒** / A task colliding with an execution must be refused.</summary>
    /// <remarks>
    /// **这两类是不同层次的标识**（"要做什么"与"实际怎么排"），
    /// **撞名之后按 ID 查找会随机命中其中一个**——**而两个方向的错法都说得通，于是没人会怀疑。**
    ///
    /// **These are identities at different levels** ("what has to happen" versus "how it was actually placed"),
    /// **and a collision makes lookup-by-id hit one of them at random** — **both outcomes look plausible, so nobody suspects it.**
    /// </remarks>
    [Fact]
    public void TheTaskCollidingWithAnExecution_ShouldBeRefused()
    {
        ActivitySchedulingProblem<Flt64> problem = Problem() with {
            Executions = [Execution("a1")],
        };

        ActivityProblemCompiler.Prepare(problem)
            .Should().BeOfType<Failed<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>>>(
                "one id cannot name both a task and an execution");
    }

    private static DateTimeOffset At(int hour) =>
        new(2026, 11, 9, hour, 0, 0, TimeSpan.FromHours(8.0));

    private static TimeRange Range(int startHour, int endHour) => new(At(startHour), At(endHour));

    private static Quantity<Flt64> Q(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    private static Activity<Flt64> Activity(string id) =>
        new(id, [Mode()], CancellationCost: Q(3.0));

    private static ActivityMode<Flt64> Mode() =>
        new("m", TimeSpan.FromHours(2.0), [At(8)],
            [new ActivityResourceUse<Flt64>("m1", Q(1.0))], Q(10.0));

    /// <summary>造一个执行 / Build one execution.</summary>
    /// <param name="task">任务标识 / The task id.</param>
    /// <returns>执行 / The execution.</returns>
    private static ActivityExecution<Flt64> Execution(string task) =>
        new(new ActivityPlacement<Flt64>(
                new ActivityChoiceKey(task, "m", At(8)),
                new TimeRange(At(8), At(10)),
                Array.Empty<ActivityReservation<Flt64>>()),
            ActivityExecutionStatus.Completed);

    private static ActivitySchedulingProblem<Flt64> Problem() =>
        new(
            Horizon: Range(0, 23),
            Activities: [Activity("a1")],
            Resources: [new ActivityResource<Flt64>("m1", Q(8.0))],
            Dependencies: null, Baselines: null, Executions: null, Rows: null,
            CostUnit: SIBaseUnits.Meter);
}
