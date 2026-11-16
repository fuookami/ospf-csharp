// 两个成本目标的判据 / Criteria for the two cost objectives.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **两个成本目标从"返回 `Failed`"变成真的目标** / The two cost objectives go from "returns `Failed`" to real objectives.
/// </summary>
/// <remarks>
/// 三种静默失效：
/// <list type="number">
/// <item>**只遍历任务自己启用的执行器**：那些不可用组合本来被钉成 0，漏掉它们无害；但反过来"只遍历启用的"会让另一些组合**完全不计成本**，
/// 解依旧可行、目标依旧是个正常数；</item>
/// <item>**成本提取失败静默取 0**：那个任务对目标不产生任何压力，看起来像"它很便宜"；</item>
/// <item>**执行器成本在没有提取器时照样加目标**：本应"没给提取器就什么都不做"，多出来的目标会改变解。</item>
/// </list>
///
/// Three silent failures: (1) **iterating only the executors a task enables** — unavailable pairs are pinned to 0 so missing them is harmless, but some pairs then end up
/// **entirely uncharged** while the solution stays feasible and the objective stays a respectable number; (2) **silently zeroing a failed extraction** — that task puts no
/// pressure on the objective and looks cheap; (3) **adding the executor objective without an extractor** — the code explicitly does nothing then, and the extra objective
/// changes the solution.
/// </remarks>
public class CostObjectiveTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeWindow<Flt64> Window() => new(
        new TimeRange(Origin, Origin.AddHours(24)),
        true,
        TimeSpan.FromHours(1));

    private sealed record Setup(
        LinearMetaModel<Flt64> Model,
        List<TestTask> Tasks,
        List<TestExecutor> Executors,
        TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> Compilation);

    private static Setup Prepare(int taskCount = 2, int executorCount = 2) {
        TimeWindow<Flt64> window = Window();
        var executors = Enumerable.Range(1, executorCount).Select(i => new TestExecutor("E" + i, "E" + i)).ToList();
        var tasks = Enumerable.Range(1, taskCount)
            .Select(i => new TestTask("T" + i, "T" + i, new TimeRange(Origin, Origin.AddHours(2))))
            .ToList();
        foreach (TestTask task in tasks) {
            foreach (TestExecutor executor in executors) {
                task.EnabledExecutorSet.Add(executor);
            }
        }
        var model = new LinearMetaModel<Flt64>();
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, executors);
        compilation.Register(model).IsOk.Should().BeTrue();
        return new Setup(model, tasks, executors, compilation);
    }

    /// <summary>**任务执行器成本覆盖整张分配表** / The task-executor cost covers the whole assignment table.</summary>
    /// <remarks>
    /// 2 任务 × 2 执行器 = **4** 项。判据查项数而不只是"非空"：只遍历"启用"的执行器时这里会漏。
    /// 再查每个系数**按对**不同——只查项数会让"所有系数都取同一个值"通过。
    ///
    /// 2 tasks × 2 executors = **4** terms. The criterion pins the count, not merely "non-empty": iterating only enabled executors would drop some. It also pins that the
    /// coefficients **differ per pair** — a count alone would pass if one value were used everywhere.
    /// </remarks>
    [Fact]
    public void TaskExecutorCost_ShouldCoverEveryPairWithItsOwnCoefficient() {
        Setup setup = Prepare();
        new TaskExecutorCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            setup.Tasks, setup.Executors, setup.Compilation,
            costCalculator: pair => new Flt64(pair.Task.Id == "T1" ? (pair.Executor.Id == "E1" ? 5 : 7) : (pair.Executor.Id == "E1" ? 11 : 13)))
            .Invoke(setup.Model).IsOk.Should().BeTrue();

        setup.Model.MetaSubObjects.Should().ContainSingle();
        MetaSubObject<Flt64> objective = setup.Model.MetaSubObjects[0];
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("task executor");
        objective.Polynomial.Monomials.Should().HaveCount(4, "2 tasks × 2 executors");
        objective.Polynomial.Monomials.Select(m => m.Coefficient.ToDouble()).Distinct()
            .Should().BeEquivalentTo(new[] { 5.0, 7.0, 11.0, 13.0 });
    }

    /// <summary>**执行器成本：没给提取器就不加目标** / The executor cost: no extractor, no objective.</summary>
    /// <remarks>
    /// 该分支只在提取器非 null 时才加目标。这一支必须**成对**测：只测"给了提取器有目标"会让"总是加目标"通过，
    /// 只测"没给提取器没目标"会让"永远不加"通过。
    ///
    /// The branch exists only when the extractor is non-null. It must be tested **as a pair**: "with an extractor there is an objective" alone passes if one is always added, and
    /// "without one there is none" alone passes if none is ever added.
    /// </remarks>
    [Fact]
    public void ExecutorCost_ShouldOnlyExistWhenAnExtractorIsGiven() {
        Setup none = Prepare();
        new ExecutorCostMinimization<TestExecutor, TestAssignmentPolicy>(
            none.Executors, none.Compilation).Invoke(none.Model).IsOk.Should().BeTrue();
        none.Model.MetaSubObjects.Should().BeEmpty("no extractor means no objective");

        Setup given = Prepare();
        new ExecutorCostMinimization<TestExecutor, TestAssignmentPolicy>(
            given.Executors, given.Compilation, coefficient: executor => new Flt64(executor.Id == "E1" ? 3 : 4))
            .Invoke(given.Model).IsOk.Should().BeTrue();
        given.Model.MetaSubObjects.Should().ContainSingle();
        given.Model.MetaSubObjects[0].Category.Should().Be(ObjectCategory.Minimum);
        given.Model.MetaSubObjects[0].Name.Should().Be("executor");
        given.Model.MetaSubObjects[0].Polynomial.Monomials.Should().HaveCount(2, "one term per executor");
        given.Model.MetaSubObjects[0].Polynomial.Monomials.Select(m => m.Coefficient.ToDouble()).Distinct()
            .Should().BeEquivalentTo(new[] { 3.0, 4.0 });
    }

    /// <summary>**成本提取失败就失败，不静默取 0** / A failed extraction fails instead of zeroing.</summary>
    [Fact]
    public void Cost_ShouldFailWhenTheExtractionFails() {
        Setup task = Prepare();
        new TaskExecutorCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            task.Tasks, task.Executors, task.Compilation, costCalculator: _ => null)
            .Invoke(task.Model).IsOk.Should().BeFalse();

        Setup executor = Prepare();
        new ExecutorCostMinimization<TestExecutor, TestAssignmentPolicy>(
            executor.Executors, executor.Compilation, coefficient: _ => null)
            .Invoke(executor.Model).IsOk.Should().BeFalse();
    }

    /// <summary>**非模型被拒** / A non-model is rejected.</summary>
    [Fact]
    public void Cost_ShouldRejectANonModel() {
        Setup setup = Prepare();
        new TaskExecutorCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            setup.Tasks, setup.Executors, setup.Compilation).Invoke(new object()).IsOk.Should().BeFalse();
        new ExecutorCostMinimization<TestExecutor, TestAssignmentPolicy>(
            setup.Executors, setup.Compilation, coefficient: _ => Flt64.One).Invoke(new object()).IsOk.Should().BeFalse();
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实 SCIP：选便宜的执行器，目标值就是那个便宜价** / Real SCIP: the cheaper executor is chosen and the objective equals that cheaper price.
    /// </summary>
    /// <remarks>
    /// 两个执行器、一个任务，成本 5 与 1。目标最小值只能是 **1**：等于 5 说明选了贵的（没最小化），等于 0 说明分配变量根本没进目标。
    /// 三个候选值互不相同，单点断言就能分辨——这里终于不需要差值技巧。
    ///
    /// Two executors, one task, costs 5 and 1. The minimum is **1**: 5 would mean the expensive one was chosen (nothing minimised) and 0 would mean the assignment variable never
    /// reached the objective. The three candidates are distinct, so a single assertion suffices here.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task TaskExecutorCost_ShouldPickTheCheaperExecutorUnderRealScip() {
        Setup setup = Prepare(taskCount: 1, executorCount: 2);
        new TaskExecutorCostMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            setup.Tasks, setup.Executors, setup.Compilation,
            costCalculator: pair => new Flt64(pair.Executor.Id == "E1" ? 5 : 1))
            .Invoke(setup.Model).IsOk.Should().BeTrue();

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            await new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("gantt-cost", setup.Model);

        result.IsOk.Should().BeTrue("the model must stay feasible: " + result);
        result.Value.Obj.ToDouble().Should().BeApproximately(1.0, 1e-6, "the cheaper executor costs 1 and the other 5");
    }
#endif
}
