// 产能成本目标真的建起来/ The capacity cost objective is really built.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.CapacityScheduling;

/// <summary>
/// **产能成本目标真的建起来** / The capacity cost objective is really built.
/// </summary>
/// <remarks>
/// 第一版 `Invoke` 是 `=&gt; Ok`：什么目标都没设却报成功。没有目标的模型会被当成"随便找一个可行解"，
/// 而调用方以为它在最小化成本——**解是可行的，数字是错的**。
///
/// 这里建 `Σ unitCost(action, 时隙起点) · x[a, s, o]`，按 `ObjectCategory.Minimum` 挂到模型上。
///
/// The first `Invoke` was `=&gt; Ok`: no objective was set while success was reported. A model without an objective is understood as "find any feasible solution" while the caller believes
/// cost is being minimised — **the solution is feasible and the number is wrong**. builds `sum of unitCost(action, slot start) · x[a, s, o]` and attaches it with
/// `ObjectCategory.Minimum`.
/// </remarks>
public class CapacityCostMinimizationTests {
    private sealed record TestCostAction(string Id, Executor Executor, double Cost) : IProductionAction {
        public string Name => Id;

        public bool Discrete => true;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => fromDouble(Cost);

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => 9UL;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() { new TimeRange(Origin, Origin.AddHours(8)) };

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static List<TestCostAction> Actions(double first = 2.0, double second = 5.0) {
        var executor = new TestExecutor("E1", "E1");
        return new List<TestCostAction> { new("A1", executor, first), new("A2", executor, second) };
    }

    private static (CapacityOrderVariables<TestCostAction>, LinearMetaModel<Flt64>) Registered(
        IReadOnlyList<TestCostAction>? actions = null) {
        var variables = new CapacityOrderVariables<TestCostAction>(actions ?? Actions(), Slots(), Window(), 2UL);
        var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();
        return (variables, model);
    }

    /// <summary>
    /// **目标真的挂上去了，且方向是最小化** / The objective is really attached, and it minimises.
    /// </summary>
    /// <remarks>
    /// 三点成对：调用后模型里**有一个**目标（第一版这里是零个）/ 方向是 `Minimum`（最大化的解会挑最贵的）/ 名字能对上。
    ///
    /// Three paired points: after the call the model holds **one** objective (the first version held none); the sense is `Minimum` (maximising would pick the most expensive
    /// solution); and the name matches.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAttachAMinimumObjective() {
        (CapacityOrderVariables<TestCostAction> variables, LinearMetaModel<Flt64> model) = Registered();
        var minimization = new CapacityCostMinimization<TestCostAction>(variables);

        minimization.Invoke(model).IsOk.Should().BeTrue();

        minimization.Name.Should().Be("capacity_cost_minimization");
        minimization.Lazy.Should().BeFalse();
        model.MetaSubObjects.Should().ContainSingle("the first version attached nothing at all");
        model.MetaSubObjects[0].Category.Should().Be(ObjectCategory.Minimum);
    }

    /// <summary>
    /// **系数是各动作自己的单位成本，零成本动作不进目标** / The coefficients are each action's own unit cost, and zero-cost actions are left out.
    /// </summary>
    /// <remarks>
    /// 四点成对：目标是 2 与 5 两组系数（每个动作 × 每个顺序位）/ 系数集合正好是 `{2, 5}`，没有混进 1 或 0 /
    /// 把两个动作都设成 0 成本后目标**为空**（不是"一堆系数 0 的项"）/ 名字仍然挂上去——空目标也是一种目标，不该静默消失。
    ///
    /// Four paired points: the objective carries the 2 and 5 coefficients (per action per position); the coefficient set is exactly `{2, 5}`, nothing mixed in at 1 or 0; setting both
    /// actions to zero cost leaves the objective **empty** rather than full of zero terms; and the objective is still attached — an empty objective is still an objective and must not
    /// vanish silently.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldUseEachActionsUnitCost() {
        (CapacityOrderVariables<TestCostAction> variables, LinearMetaModel<Flt64> model) = Registered();
        new CapacityCostMinimization<TestCostAction>(variables).Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<double> coefficients = ObjectiveCoefficients(model);
        coefficients.Should().NotBeEmpty();
        coefficients.Distinct().OrderBy(value => value).Should().Equal(new[] { 2.0, 5.0 },
            "each action contributes its own unit cost, nothing else");

        IReadOnlyList<TestCostAction> free = Actions(0.0, 0.0);
        (CapacityOrderVariables<TestCostAction> freeVariables, LinearMetaModel<Flt64> freeModel) = Registered(free);
        new CapacityCostMinimization<TestCostAction>(freeVariables).Invoke(freeModel).IsOk.Should().BeTrue();
        freeModel.MetaSubObjects.Should().ContainSingle("a zero-cost objective still exists, it is just empty");
        ObjectiveCoefficients(freeModel).Should().BeEmpty("zero-cost terms are not written into the objective");
    }

    /// <summary>
    /// **未注册与非法入参显式失败** / Unregistered variables and invalid inputs fail explicitly.
    /// </summary>
    /// <remarks>
    /// 三点成对：未注册 → `Failed` 且模型里没有目标（第一版返回 `Ok` 且也没有目标——**这就是那个静默空洞**）/
    /// 传入 `new object` → `Failed` / 注册之后同一个调用 → `Ok` 且目标存在。
    ///
    /// Three paired points: unregistered yields `Failed` with no objective on the model (the first version returned `Ok` with no objective either — **that is the silent hole**);
    /// `new object` yields `Failed`; and after registration the same call yields `Ok` with an objective.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailInsteadOfSkipping() {
        var unregistered = new CapacityOrderVariables<TestCostAction>(Actions(), Slots(), Window(), 2UL);
        using var model = new LinearMetaModel<Flt64>();
        new CapacityCostMinimization<TestCostAction>(unregistered).Invoke(model).IsFailed.Should().BeTrue();
        model.MetaSubObjects.Should().BeEmpty();

        (CapacityOrderVariables<TestCostAction> variables, LinearMetaModel<Flt64> registered) = Registered();
        var minimization = new CapacityCostMinimization<TestCostAction>(variables);
        minimization.Invoke(new object()).IsFailed.Should().BeTrue("a non-model argument must not be reported as success");
        minimization.Invoke(registered).IsOk.Should().BeTrue();
        registered.MetaSubObjects.Should().ContainSingle();
    }

    /// <summary>读取目标里各单项式的系数 / Read the objective's monomial coefficients.</summary>
    /// <param name="model">模型 / The model.</param>
    /// <returns>系数 / The coefficients.</returns>
    private static IReadOnlyList<double> ObjectiveCoefficients(LinearMetaModel<Flt64> model) =>
        model.MetaSubObjects
            .SelectMany(subObject => subObject.Polynomial.Monomials)
            .Select(monomial => monomial.Coefficient.ToDouble())
            .ToList();
}
