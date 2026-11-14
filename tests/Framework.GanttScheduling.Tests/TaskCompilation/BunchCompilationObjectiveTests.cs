// 编译无关目标在任务束编译上的判据 / Criteria for the compilation-agnostic objectives on a bunch compilation.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **取消/空闲这两条目标本来与编译种类无关（B569）** / The cancellation/leisure objectives are compilation-agnostic (B569).
/// </summary>
/// <remarks>
/// 两条目标都只要求 `ICompilation` + `ICompilationColumns`，而任务束编译从 B563 起就实现了后者。
/// 之前 Demo4 的注释写"这两条未接线，注册进来只会让管线失败"——真实原因是 `BunchCompilation` **当时没有实现那些接口**，
/// 与目标本身无关。现在补判据，把"能用在任务束编译上"这件事**实测**一遍，而不是靠注释断言。
///
/// Both objectives only need `ICompilation` + `ICompilationColumns`, and the bunch compilation has implemented the latter since B563. Demo4 used to say "these two are unwired;
/// registering them would only make the pipeline fail" — the real reason was that `BunchCompilation` **did not implement those interfaces** at the time, nothing to do with the
/// objectives. These criteria measure "usable on a bunch compilation" instead of asserting it in a comment.
/// </remarks>
public class BunchCompilationObjectiveTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (LinearMetaModel<Flt64> Model, List<TestTask> Tasks, List<TestExecutor> Executors,
        BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> Compilation) Prepare(
        bool withExecutorLeisure = true) {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var tasks = new List<TestTask> {
            new("T1", "T1", new TimeRange(Origin.AddHours(2), Origin.AddHours(4))),
            new("T2", "T2", new TimeRange(Origin.AddHours(3), Origin.AddHours(5))),
        };
        foreach (TestTask task in tasks) {
            // 取消要**逐任务**打开：`TestTask.CancelEnabled` 默认是 false，而取消最小化只遍历可取消的任务。
            // Cancellation is per task: `TestTask.CancelEnabled` defaults to false, and cancel minimisation only walks cancellable tasks.
            task.CancelEnabled = true;
            foreach (TestExecutor executor in executors) {
                task.EnabledExecutorSet.Add(executor);
            }
        }
        var compilation = new BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, withExecutorLeisure: withExecutorLeisure);
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        return (model, tasks, executors, compilation);
    }

    /// <summary>**取消最小化：每个可取消任务一项，系数来自提取器** / Cancel minimisation: one term per cancellable task, coefficients from the extractor.</summary>
    /// <remarks>
    /// 判据查**系数互不相同**（3 和 5）：只查项数会让"所有系数都是 1"通过，而那样两条任务的取消代价就一样了。
    ///
    /// The criterion pins **distinct coefficients** (3 and 5): a count alone would pass if every coefficient were 1, which would price two cancellations the same.
    /// </remarks>
    [Fact]
    public void TaskCancelMinimization_ShouldWorkOnABunchCompilation() {
        (LinearMetaModel<Flt64> model, List<TestTask> tasks, List<TestExecutor> _,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();

        new TaskCancelMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, compilation, coefficient: task => new Flt64(task.Id == "T1" ? 3 : 5))
            .Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().ContainSingle();
        MetaSubObject<Flt64> objective = model.MetaSubObjects[0];
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Polynomial.Monomials.Should().HaveCount(2);
        objective.Polynomial.Monomials.Select(monomial => monomial.Coefficient.ToDouble())
            .Should().BeEquivalentTo(new[] { 3.0, 5.0 });
        foreach (TestTask task in tasks) {
            objective.Polynomial.Monomials.Select(monomial => monomial.Symbol)
                .Should().Contain((Fuookami.Ospf.Math.Symbol.ISymbol)compilation.Cancellation[
                    task]);
        }
    }

    /// <summary>**空闲最小化：给了系数才有目标** / Leisure minimisation: an objective only with a coefficient.</summary>
    /// <remarks>
    /// 基线是 `coefficient?.let { … }`，所以必须**成对**：给了系数 → 每个执行器一项；没给 → 模型里没有子目标。
    ///
    /// The baseline is `coefficient?.let { … }`, so this must be **paired**: with a coefficient → one term per executor; without one → no sub-objective at all.
    /// </remarks>
    [Fact]
    public void ExecutorLeisureMinimization_ShouldWorkOnABunchCompilationOnlyWithACoefficient() {
        (LinearMetaModel<Flt64> givenModel, List<TestTask> _, List<TestExecutor> executors,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) = Prepare();
        new ExecutorLeisureMinimization<TestExecutor, TestAssignmentPolicy>(
            executors, compilation, coefficient: executor => new Flt64(executor.Id == "E1" ? 2 : 4))
            .Invoke(givenModel).IsOk.Should().BeTrue();

        givenModel.MetaSubObjects.Should().ContainSingle();
        MetaSubObject<Flt64> objective = givenModel.MetaSubObjects[0];
        objective.Polynomial.Monomials.Should().HaveCount(2, "one term per executor");
        objective.Polynomial.Monomials.Select(monomial => monomial.Coefficient.ToDouble())
            .Should().BeEquivalentTo(new[] { 2.0, 4.0 });

        (LinearMetaModel<Flt64> noneModel, List<TestTask> __, List<TestExecutor> executors2,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation2) = Prepare();
        new ExecutorLeisureMinimization<TestExecutor, TestAssignmentPolicy>(executors2, compilation2)
            .Invoke(noneModel).IsOk.Should().BeTrue();
        noneModel.MetaSubObjects.Should().BeEmpty("no coefficient means the caller did not ask for it");
    }

    /// <summary>**没有空闲列时是"没有这项成本"，不是失败** / Without leisure columns there is no cost, and that is not a failure.</summary>
    /// <remarks>
    /// 任务束编译可以在没有执行器空闲的情况下工作（`withExecutorLeisure: false`）。那时"空闲成本"这个词没有所指，
    /// 目标应当什么都不做——而不是报"该编译不提供空闲列"。这两种情形在解上看起来一样，但一个是正常配置、一个是接线错误。
    ///
    /// A bunch compilation can work without executor leisure (`withExecutorLeisure: false`). Then "leisure cost" refers to nothing and the objective should do nothing — not report
    /// "this compilation exposes no leisure columns". The two look the same in the solution, but one is a normal configuration and the other a wiring bug.
    /// </remarks>
    [Fact]
    public void ExecutorLeisureMinimization_ShouldStaySilentWithoutLeisureColumns() {
        (LinearMetaModel<Flt64> model, List<TestTask> _, List<TestExecutor> executors,
            BunchCompilation<TestTaskBunch, TestTask, TestExecutor, TestAssignmentPolicy> compilation) =
            Prepare(withExecutorLeisure: false);

        new ExecutorLeisureMinimization<TestExecutor, TestAssignmentPolicy>(
            executors, compilation, coefficient: _ => new Flt64(1))
            .Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().BeEmpty();
    }
}
