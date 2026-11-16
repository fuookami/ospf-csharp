// 任务束成本目标的判据 / Criteria for the bunch cost objective.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **任务束成本目标** / The bunch cost objective.
/// </summary>
/// <remarks>
/// `BunchCostMinimization.Invoke` 原来是 `Results.Ok(...)`：**什么都不加，也什么都不报**。于是"目标里包含束成本"这件事只存在于注释里——
/// 求解器照样给出一个解，代价是最小化的是**别的**东西。
///
/// 这里把它做成真的：把 `bunch_cost`（`Σ 束成本 × x`）作为 `Minimum` 子目标加进模型。构造参数也从具体泛型
/// `BunchCompilation&lt;AbstractTaskBunch&lt;T,E,A&gt;,…&gt;` 换成 `ITaskCost`——泛型不变性让前者根本接不上
/// `BunchCompilation&lt;FlightTaskBunch,…&gt;`，Demo4 当初正是因此把它跳过的。
///
/// `BunchCostMinimization.Invoke` used to be `Results.Ok(...)`: **adds nothing and reports nothing**. So "the objective includes bunch cost" existed only in comments — the solver
/// still produced a solution, except it minimised something else. makes it real: `bunch_cost` (`Σ bunch cost × x`) goes in as a `Minimum` sub-objective. The constructor
/// parameter also moved from the concrete `BunchCompilation&lt;AbstractTaskBunch&lt;T,E,A&gt;,…&gt;` to `ITaskCost` — invariance made the former unusable with
/// `BunchCompilation&lt;FlightTaskBunch,…&gt;`, which is exactly why Demo4 skipped it.
/// </remarks>
public class BunchCostMinimizationTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (LinearMetaModel<Flt64> Model, List<TestTask> Tasks, TestExecutor Executor,
        BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> Compilation) Prepare() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(Origin.AddHours(2), Origin.AddHours(4))),
        };
        foreach (TestTask task in tasks) {
            task.EnabledExecutorSet.Add(executors[0]);
        }
        var compilation = new BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors);
        return (new LinearMetaModel<Flt64>(), tasks, executors[0], compilation);
    }

    /// <summary>**注册后：目标带上束成本，系数是束自己的成本** / After registration the objective carries the bunch's own cost.</summary>
    [Fact]
    public void Invoke_ShouldAddTheBunchCostObjective() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, TestExecutor executor,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        compilation.Register(model).IsOk.Should().BeTrue();
        var bunch = new TestTaskBunch(
            executor,
            new ExecutorInitialUsability<TestTask, TestExecutor, TestAssignmentPolicy>(null, Origin),
            tasks,
            new ImmutableCost<Flt64>(Array.Empty<CostItem<Flt64>>(), new Flt64(7.5)));
        compilation.AddColumns(0UL, new List<TestTaskBunch> { bunch }, model).IsOk.Should().BeTrue();

        new BunchCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(compilation)
            .Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().ContainSingle();
        MetaSubObject<Flt64> objective = model.MetaSubObjects[0];
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("bunch cost");
        objective.Polynomial.Monomials.Should().ContainSingle();
        objective.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(7.5, 1e-9,
            "the objective must price the column at the bunch's own cost, not at 1");
        objective.Polynomial.Monomials[0].Symbol.Should().BeSameAs(compilation.Columns(0UL)![0]);
    }

    /// <summary>**没有列时不加空目标** / No columns, no empty objective.</summary>
    /// <remarks>
    /// 一列都没有时 `bunch_cost` 恒为零。加一个空子目标不会改变解，却会让"目标里有束成本"这句话在模型里**看起来成立**——
    /// 而这正是这里要消灭的那种假象。
    ///
    /// With no columns `bunch_cost` is identically zero. An empty sub-objective does not change the solution but makes "the objective includes bunch cost" **look** true in the
    /// model — precisely the illusion.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddNothingWhileThereAreNoColumns() {
        (LinearMetaModel<Flt64> model, List<TestTask> _, TestExecutor __,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        compilation.Register(model).IsOk.Should().BeTrue();

        new BunchCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(compilation)
            .Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().BeEmpty("an identically-zero cost is not an objective");
    }

    /// <summary>**未注册就失败** / Unregistered fails loudly.</summary>
    /// <remarks>
    /// `bunch_cost` 由 `Register` 建出来。取不到说明**流程顺序错了**，不是"成本是零"——这两种情况在解上看起来一样（都是没成本），
    /// 所以必须靠返回值分开。
    ///
    /// `bunch_cost` comes from `Register`. Not finding it means the **flow is out of order**, not that the cost is zero — the two look identical in the solution (no cost either
    /// way), so only the return value can tell them apart.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailWhenTheCompilationIsNotRegistered() {
        (LinearMetaModel<Flt64> model, List<TestTask> _, TestExecutor __,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            new BunchCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(compilation).Invoke(model);

        result.IsOk.Should().BeFalse("the cost symbol does not exist before registration");
        model.MetaSubObjects.Should().BeEmpty();
    }

    /// <summary>**非模型被拒** / A non-model is rejected.</summary>
    [Fact]
    public void Invoke_ShouldRejectANonModel() {
        (LinearMetaModel<Flt64> model, List<TestTask> _, TestExecutor __,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        compilation.Register(model).IsOk.Should().BeTrue();

        new BunchCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(compilation)
            .Invoke(new object()).IsOk.Should().BeFalse();
    }
}
