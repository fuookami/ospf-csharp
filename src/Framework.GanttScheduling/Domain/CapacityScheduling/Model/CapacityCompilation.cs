#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
/// <summary>
/// 产能编译决策对象（无顺序）/ Capacity compilation decision object (no order)
/// </summary>
/// <remarks>
/// 二维整型变量：x[action, slot] -> 数量。
/// Two-dimensional integer variable: x[action, slot] -> amount.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class CapacityCompilation<A> : ICapacity<A>
    where A : IProductionAction {
    private readonly IReadOnlyList<A> _actions;
    private readonly IReadOnlyList<TimeRange> _slots;
    private readonly TimeWindow<Flt64> _timeWindow;
    private readonly IReadOnlyList<Executor> _executors;

    /// <summary>
    /// 产能编译构造 / Capacity compilation constructor
    /// </summary>
    /// <param name="actions">生产动作列表 / List of production actions</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    public CapacityCompilation(
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow) {
        _actions = actions;
        _slots = slots;
        _timeWindow = timeWindow;
        _executors = actions.Select(a => a.Executor).Distinct().ToList();

        // Index actions if they implement ManualIndexed
        foreach (ManualIndexed action in actions.OfType<ManualIndexed>()) {
            if (!action.Indexed) {
                action.SetIndexed();
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Executor> Executors => _executors;

    /// <summary>生产动作列表 / List of production actions</summary>
    public IReadOnlyList<A> Actions => _actions;

    /// <summary>时隙列表 / List of time slots</summary>
    public IReadOnlyList<TimeRange> Slots => _slots;

    /// <summary>时间窗口 / Time window</summary>
    public TimeWindow<Flt64> TimeWindow => _timeWindow;

    /// <inheritdoc/>
    public Result<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>> ExtractSolution(object model) {
        var actionAllocations = new List<ActionAllocation<A>>();
        var executorCapacities = new List<ExecutorCapacityResult>();

        return Results.Ok<CapacitySchedulingSolution<A>>(new CapacitySchedulingSolution<A>(
            _actions, actionAllocations, executorCapacities));
    }
}
