// 取消成本与空闲成本最小化的判据 / Criteria for cancellation- and leisure-cost minimisation.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **取消成本与空闲成本从空壳变成真目标** / Cancellation and leisure costs go from stubs to real objectives.
/// </summary>
/// <remarks>
/// 两条目标各有一个**容易看漏的"什么都不做"**，而且理由完全不同：
/// <list type="bullet">
/// <item>取消成本：编译未启用取消 → 这条目标根本不存在；</item>
/// <item>空闲成本：**没给系数** → 基线的 `coefficient?.let { … }` 直接跳过。**空闲最小化默认关闭**，
/// 不是"默认按 1 计价"。</item>
/// </list>
/// 把第二个误当成"没接线"去改，就会让每个默认调用方凭空多出一笔空闲成本。
///
/// Each objective has an **easy-to-miss "do nothing"**, for different reasons: cancellation cost — cancellation is disabled in the compilation, so the objective does not exist;
/// leisure cost — **no coefficient was given**, and the baseline's `coefficient?.let { … }` skips it. **Leisure minimisation is off by default**, not "costs 1 by default". Mistaking
/// the second for "unwired" would give every default caller a leisure cost out of nowhere.
/// </remarks>
public class CancelAndLeisureMinimizationTests {
    private static TestTask CancelableTask(string id) => new(id, id) { CancelEnabled = true };

    private static (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> Compilation,
        LinearMetaModel<Flt64> Model, List<TestExecutor> Executors) MakeRegistered(
        IReadOnlyList<TestTask> tasks,
        bool taskCancelEnabled,
        bool withExecutorLeisure) {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, executors, taskCancelEnabled: taskCancelEnabled, withExecutorLeisure: withExecutorLeisure);
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        return (compilation, model, executors);
    }

    // ===== 取消成本 =====

    /// <summary>**每个可取消任务都进目标，系数按提取器** / Every cancellable task enters the objective, weighted by the extractor.</summary>
    /// <remarks>
    /// 三个任务里两个可取消、系数分别 5 和 3，所以目标的多项式必须**恰好两条**，且系数就是那两个数。
    /// 只数条数会让"系数全为 1"通过；只看系数会让"漏一个任务"通过。
    ///
    /// Two of three tasks may be cancelled with coefficients 5 and 3, so the objective must have **exactly two** monomials carrying those numbers. Counting alone would let
    /// "all coefficients are 1" through; reading coefficients alone would let "one task missing" through.
    /// </remarks>
    [Fact]
    public void TaskCancel_ShouldWeightEachCancellableTask() {
        var tasks = new List<TestTask> {
            CancelableTask("T1"), CancelableTask("T2"), new("T3", "T3") { CancelEnabled = false },
        };
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model, _) = MakeRegistered(tasks, taskCancelEnabled: true, withExecutorLeisure: false);
        int before = model.MetaSubObjects.Count;
        var coefficients = new Dictionary<string, double> { ["T1"] = 5.0, ["T2"] = 3.0 };

        var minimization = new TaskCancelMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, compilation, task => new Flt64(coefficients[task.Id]));

        minimization.Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Should().HaveCount(before + 1);
        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("task cancel");
        objective.Polynomial.Monomials.Should().HaveCount(2, "T3 cannot be cancelled and must not be charged");
        objective.Polynomial.Monomials.Select(m => m.Coefficient.ToDouble()).OrderBy(v => v)
            .Should().Equal(new[] { 3.0, 5.0 });
        objective.Polynomial.Monomials.Select(m => m.Symbol.Name).OrderBy(n => n, System.StringComparer.Ordinal)
            .Should().Equal(new[] { "y_0", "y_1" });
    }

    /// <summary>**取消未启用 → 不加目标，但也不是失败** / Cancellation disabled adds no objective, and is not a failure.</summary>
    /// <remarks>
    /// 这里的 `Ok` 与空壳的 `Ok` 形状一样，含义完全不同：目标**本就不存在**。
    /// 判据必须同时确认"没有新增子目标"——否则"加了条空目标再返回 Ok"也能过。
    ///
    /// This `Ok` looks like the stub's but means something else: the objective **does not exist**. The criterion must also confirm "no new sub-objective", or "add an empty objective
    /// then return Ok" would pass too.
    /// </remarks>
    [Fact]
    public void TaskCancel_ShouldAddNothingWhenCancellationIsDisabled() {
        var tasks = new List<TestTask> { CancelableTask("T1") };
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model, _) = MakeRegistered(tasks, taskCancelEnabled: false, withExecutorLeisure: false);
        int before = model.MetaSubObjects.Count;

        var minimization = new TaskCancelMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(tasks, compilation);

        minimization.Invoke(model).IsOk.Should().BeTrue("a disabled cost is not a wiring failure");
        model.MetaSubObjects.Should().HaveCount(before, "nothing to minimise, so nothing is added");
    }

    /// <summary>**系数提取失败必须失败，不能悄悄跳过那个任务** / A failed extraction must fail, not silently skip the task.</summary>
    /// <remarks>
    /// 跳过那个任务等于"取消它不花钱"——求解器于是**专挑它取消**，解照样成立，成本还更低。
    /// 这是本类里最隐蔽的一种：结果不是错一个数，是错一整个方向。
    ///
    /// Skipping the task means "cancelling it is free", so the solver **cancels exactly that one**: the solution still stands and is even cheaper. This is the subtlest kind here —
    /// the result is not one wrong number, it is a wrong direction.
    /// </remarks>
    [Fact]
    public void TaskCancel_ShouldFailWhenTheCoefficientIsMissing() {
        var tasks = new List<TestTask> { CancelableTask("T1") };
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model, _) = MakeRegistered(tasks, taskCancelEnabled: true, withExecutorLeisure: false);
        int before = model.MetaSubObjects.Count;

        var minimization = new TaskCancelMinimization<TestTask, TestExecutor, TestAssignmentPolicy>(
            tasks, compilation, _ => null);

        minimization.Invoke(model).IsOk.Should().BeFalse("an unpriced cancellation would be a free one");
        model.MetaSubObjects.Should().HaveCount(before, "a failed invocation leaves no half-built objective");
    }

    // ===== 空闲成本 =====

    /// <summary>**每个执行器都进目标，系数按提取器** / Every executor enters the objective, weighted by the extractor.</summary>
    [Fact]
    public void ExecutorLeisure_ShouldWeightEachExecutor() {
        var tasks = new List<TestTask> { CancelableTask("T1") };
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model, List<TestExecutor> executors) =
            MakeRegistered(tasks, taskCancelEnabled: false, withExecutorLeisure: true);
        int before = model.MetaSubObjects.Count;
        var penalties = new Dictionary<string, double> { ["E1"] = 4.0, ["E2"] = 6.0 };

        var minimization = new ExecutorLeisureMinimization<TestExecutor, TestAssignmentPolicy>(
            executors, compilation, executor => new Flt64(penalties[executor.Id]));

        minimization.Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Should().HaveCount(before + 1);
        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("executor leisure");
        objective.Polynomial.Monomials.Select(m => m.Coefficient.ToDouble()).OrderBy(v => v)
            .Should().Equal(new[] { 4.0, 6.0 });
        objective.Polynomial.Monomials.Select(m => m.Symbol.Name).OrderBy(n => n, System.StringComparer.Ordinal)
            .Should().Equal(new[] { "z_0", "z_1" });
    }

    /// <summary>**没给系数 → 什么都不加（基线默认关闭）** / No coefficient adds nothing — off by default in the baseline.</summary>
    /// <remarks>
    /// 这条判据钉的是**默认行为**：空闲最小化不生效。第一版如果照直觉写成"没给系数就按 1 计价"，
    /// 这里会红。基线是 `coefficient?.let { … }`，跳过。
    ///
    /// This pins the **default**: leisure minimisation is off. A first version that follows intuition ("no coefficient → cost 1") turns this red. The baseline is
    /// `coefficient?.let { … }`: skip.
    /// </remarks>
    [Fact]
    public void ExecutorLeisure_ShouldAddNothingWithoutACoefficient() {
        var tasks = new List<TestTask> { CancelableTask("T1") };
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model, List<TestExecutor> executors) =
            MakeRegistered(tasks, taskCancelEnabled: false, withExecutorLeisure: true);
        int before = model.MetaSubObjects.Count;

        var minimization = new ExecutorLeisureMinimization<TestExecutor, TestAssignmentPolicy>(executors, compilation);

        minimization.Invoke(model).IsOk.Should().BeTrue("leisure cost is opt-in");
        model.MetaSubObjects.Should().HaveCount(before, "no coefficient means no leisure objective at all");
    }

    /// <summary>**空闲未启用 → 不加目标** / Leisure disabled adds no objective.</summary>
    [Fact]
    public void ExecutorLeisure_ShouldAddNothingWhenLeisureIsDisabled() {
        var tasks = new List<TestTask> { CancelableTask("T1") };
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model, List<TestExecutor> executors) =
            MakeRegistered(tasks, taskCancelEnabled: false, withExecutorLeisure: false);
        int before = model.MetaSubObjects.Count;

        var minimization = new ExecutorLeisureMinimization<TestExecutor, TestAssignmentPolicy>(
            executors, compilation, _ => new Flt64(1.0));

        minimization.Invoke(model).IsOk.Should().BeTrue("a disabled cost is not a wiring failure");
        model.MetaSubObjects.Should().HaveCount(before, "there is no `z` column to charge");
    }

    /// <summary>**系数提取失败必须失败** / A failed extraction must fail.</summary>
    [Fact]
    public void ExecutorLeisure_ShouldFailWhenTheCoefficientIsMissing() {
        var tasks = new List<TestTask> { CancelableTask("T1") };
        (TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model, List<TestExecutor> executors) =
            MakeRegistered(tasks, taskCancelEnabled: false, withExecutorLeisure: true);
        int before = model.MetaSubObjects.Count;

        var minimization = new ExecutorLeisureMinimization<TestExecutor, TestAssignmentPolicy>(
            executors, compilation, _ => null);

        minimization.Invoke(model).IsOk.Should().BeFalse("an unpriced idle hour would be a free one");
        model.MetaSubObjects.Should().HaveCount(before);
    }
}
