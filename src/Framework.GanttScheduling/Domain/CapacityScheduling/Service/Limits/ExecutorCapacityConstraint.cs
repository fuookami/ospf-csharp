#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits;

/// <summary>
/// 设备产能约束 / Executor capacity constraint
/// </summary>
/// <remarks>
/// 每台设备在每个时隙的总产能不超过可用时长。
/// Total capacity per executor per slot should not exceed available duration.
/// </remarks>
/// <remarks>
/// **第一版是 `Invoke =&gt; Ok`**：一条约束都没建，却对每个模型都报成功。空 `Ok` 是这里最坏的一种缺陷——上层无法区分
/// "约束成立"和"根本没有约束"，于是产能超卖的解会静默通过。
///
/// 第二版（B725）改成真的建行，但压的是 `CapacityUsageVariables` 那组**自由变量**——它们与数量之间没有任何行相连，
/// 所以对数量毫无约束力。B735 起改为压 `CapacityLoadExpressions`：那里的容量是 `Σ 该执行器动作的数量`，
/// **约束才真的落在数量上**。
///
/// **The first version was `Invoke =&gt; Ok`**: not one row was built yet every model was reported as fine. An empty `Ok` is the worst defect here — the caller cannot tell
/// "the constraint holds" from "there is no constraint", so an oversubscribed solution passes silently. B725 made it build real rows, but they pressed on `CapacityUsageVariables`,
/// **free variables** with no row tying them to the amounts, so they constrained nothing. Since B735 they press on `CapacityLoadExpressions`, where capacity is
/// `the sum of that executor's amounts` — **the constraint now really lands on the amounts**.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class ExecutorCapacityConstraint<A> : IMetaConstraintGroup
    where A : IProductionAction {
    private readonly CapacityLoadExpressions<A> _load;

    private readonly TimeWindow<Flt64> _timeWindow;

    /// <summary>
    /// 设备产能约束构造 / Executor capacity constraint constructor
    /// </summary>
    /// <param name="load">占用表达式层 / The load expression layer</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="name">约束名称 / Constraint name</param>
    public ExecutorCapacityConstraint(
        CapacityLoadExpressions<A> load,
        TimeWindow<Flt64> timeWindow,
        string name = "executor_capacity") {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
        _timeWindow = timeWindow;
        Name = name;
    }

    /// <inheritdoc/>
    public bool Lazy => false;

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>
    /// 应用约束到模型 / Apply constraint to model
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>成功或失败 / Success or failure</returns>
    public Try Invoke(object model) {
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "设备产能约束需要线性元模型 / the executor capacity constraint needs a linear meta model");
        }

        for (int executor = 0; executor < _load.Executors.Count; ++executor) {
            for (int slot = 0; slot < _load.Slots.Count; ++slot) {
                // **右端项是"窗口单位"下的可用时长**，不是秒：`TimeWindow.ValueOf(TimeSpan)` 返回 `duration / Interval`，
                // 8 小时时隙在 8 小时刻度下就是 1。占用表达式与它同单位。
                // **The right-hand side is the available duration in window units, not seconds**: `TimeWindow.ValueOf(TimeSpan)` returns `duration / Interval`, so an 8-hour slot on
                // an 8-hour scale is 1. The occupancy expression shares that unit.
                double available = _timeWindow.ValueOf(_load.Slots[slot].Duration).ToDouble();

                var lhs = new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, _load.CapacityOf(executor, slot)) },
                    Flt64.Zero);
                var rhs = new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    new Flt64(available));

                Try added = linear.AddConstraint(
                    new LinearInequality<Flt64>(lhs, rhs, Comparison.LE),
                    group: null,
                    name: $"{Name}_{_load.Executors[executor].Id}_{slot}",
                    args: null);
                if (!added.IsOk) {
                    return added;
                }
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
