// 任务成本最小化接线的判据 / Criteria for the wired task-cost minimisation.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **`TaskCostMinimization` 从空壳变成真目标** / `TaskCostMinimization` goes from an empty stub to a real objective.
/// </summary>
/// <remarks>
/// 把它翻成"未接线就失败"。这里接线：它现在把 `bunch_cost` 多项式作为 `Minimum` 子目标加进模型。
///
/// 两条判据分工明确：**非模型必须失败**（守卫还在），**已注册的迭代编译必须真的加进目标**（不是返回 `Ok` 了事）。
/// 后者若只看 `IsOk` 就完全看不到差别——空壳也返回过 `Ok`。
///
/// flipped it to "fail while unwired". wires it: it now adds the `bunch_cost` polynomial as a `Minimum` sub-objective. The two criteria split the job:
/// **a non-model must fail** (the guard is still there) and **a registered iterative compilation must really enter the objective** (not merely return `Ok`). Looking only at
/// `IsOk` would not see the difference — the empty stub returned `Ok` too.
/// </remarks>
public class TaskCostMinimizationTests {
    private static (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> Compilation,
        LinearMetaModel<Flt64> Model,
        List<TestExecutor> Executors) MakeRegistered() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var origin = new List<TestTask> { new("T1", "T1") };
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            origin, executors);
        var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();
        return (compilation, model, executors);
    }

    /// <summary>**注册后 `TaskCost` 就是那个 `bunch_cost` 符号** / After registration `TaskCost` is that `bunch_cost` symbol.</summary>
    /// <remarks>
    /// 名字照抄基线：字段叫 `taskCost`、符号叫 `bunch_cost`。认错符号的后果是目标里加进了别的东西——
    /// 照样求解、照样出解，只是最小化的不是成本。
    ///
    /// The name is copied from the baseline: the field is `taskCost`, the symbol is `bunch_cost`. Picking the wrong symbol puts something else in the objective — it still solves and
    /// still returns a solution, it simply is not minimising cost.
    /// </remarks>
    [Fact]
    public void TaskCost_ShouldBeTheBunchCostSymbol() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation, _, _) =
            MakeRegistered();

        compilation.TaskCost.Should().NotBeNull();
        var cost = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)compilation.TaskCost!;
        cost.Name.Should().Be("bunch_cost");
    }

    /// <summary>**注册前没有代价项** / Before registration there is no cost term.</summary>
    /// <remarks>
    /// 没有这一条，`TaskCost => new LinearExpressionSymbol(...)` 这种"每次都新建"的写法也能让上一条判据通过，
    /// 而那个符号**根本不在模型里**——目标加进去之后引用的是一张空符号，解出来的成本恒为 0。
    ///
    /// Without this one, a "create a fresh symbol every call" implementation would satisfy the criterion above while that symbol is **not in the model at all** — the objective then
    /// references an empty symbol and the evaluated cost is always 0.
    /// </remarks>
    [Fact]
    public void TaskCost_ShouldBeNullBeforeRegistration() {
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1") }, new List<TestExecutor> { new("E1", "E1") });

        compilation.TaskCost.Should().BeNull("nothing has been registered into a model yet");
    }

    /// <summary>**接线后真的加进目标，而且引用的是编译的代价多项式** / Once wired it really enters the objective, referencing the compilation's cost polynomial.</summary>
    /// <remarks>
    /// 判据要落到**目标里的多项式**，不能只看 `IsOk`： 之前的空壳也返回 `Ok`。
    ///
    /// The criterion lands on **the polynomial in the objective**, not on `IsOk`: the pre- stub returned `Ok` as well.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddTheCostPolynomialAsAMinimumObjective() {
        (IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy> compilation,
            LinearMetaModel<Flt64> model,
            List<TestExecutor> executors) = MakeRegistered();
        compilation.AddColumns(
            0,
            new[] { new TestIterativeTask(new TestTask("T1", "T1") { Executor = executors[0] }, 0) },
            model,
            _ => new Flt64(7.0),
            (_, _) => false).IsOk.Should().BeTrue();
        int objectivesBefore = model.MetaSubObjects.Count;

        var minimization = new TaskCostMinimization<TestExecutor, TestAssignmentPolicy>(compilation);
        Try result = minimization.Invoke(model);

        result.IsOk.Should().BeTrue();
        model.MetaSubObjects.Should().HaveCount(objectivesBefore + 1);
        MetaSubObject<Flt64> objective = model.MetaSubObjects.Last();
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Name.Should().Be("task cost");

        var cost = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)compilation.TaskCost!;
        objective.Polynomial.Monomials.Should().NotBeEmpty("the round's cost was added to `bunch_cost`");
        objective.Polynomial.Monomials.Select(monomial => monomial.ToString())
            .Should().Equal(cost.Polynomial.Monomials.Select(monomial => monomial.ToString()));
    }

    /// <summary>**普通编译没有代价项，必须失败** / A plain compilation has no cost term and must fail.</summary>
    /// <remarks>
    /// 普通编译把代价写在列系数里，没有 `bunch_cost`。此时静默成功等于宣称"成本已最小化"，
    /// 而目标里根本没有这一项——解照样出来，只是不看成本。
    ///
    /// A plain compilation writes its cost into the column coefficients and has no `bunch_cost`. Succeeding silently there claims "the cost was minimised" while the objective
    /// has no such term — a solution still comes out, simply ignoring cost.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailForAPlainCompilation() {
        var compilation = new TaskCompilation<TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1") },
            new List<TestExecutor> { new("E1", "E1") });
        using var model = new LinearMetaModel<Flt64>();
        compilation.Register(model).IsOk.Should().BeTrue();

        var minimization = new TaskCostMinimization<TestExecutor, TestAssignmentPolicy>(compilation);

        minimization.Invoke(model).IsOk.Should().BeFalse(
            "a plain compilation carries no cost term, so there is nothing to minimise");
    }

    /// <summary>**迭代编译但还没注册，也必须失败** / An iterative compilation that has not been registered must fail too.</summary>
    /// <remarks>
    /// 这一条钉住"有代价项"和"代价项已进模型"是两件事：没注册时 `TaskCost` 是 `null`，
    /// 若把它当成"没有代价"而返回 `Ok`，就又是一个静默空操作。
    ///
    /// This pins "has a cost term" and "the cost term is in the model" as two different things: before registration `TaskCost` is `null`, and treating that as "no cost" and returning
    /// `Ok` would be another silent no-op.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailBeforeRegistration() {
        var compilation = new IterativeTaskCompilation<TestIterativeTask, TestTask, TestExecutor, TestAssignmentPolicy>(
            new List<TestTask> { new("T1", "T1") },
            new List<TestExecutor> { new("E1", "E1") });
        using var model = new LinearMetaModel<Flt64>();

        var minimization = new TaskCostMinimization<TestExecutor, TestAssignmentPolicy>(compilation);

        minimization.Invoke(model).IsOk.Should().BeFalse(
            "the cost symbol does not exist until the compilation is registered");
    }
}
