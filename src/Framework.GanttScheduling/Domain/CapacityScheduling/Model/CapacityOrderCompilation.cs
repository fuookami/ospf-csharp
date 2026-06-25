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

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
/// <summary>
/// 产能编译决策对象（带顺序）/ Capacity compilation decision object (with order)
/// </summary>
/// <remarks>
/// 三维变量：x[action, slot, order] -> 数量（整型），b[action, slot, order] -> 是否选中（二进制）。
/// Three-dimensional variables: x[action, slot, order] -> amount (integer), b[action, slot, order] -> is_selected (binary).
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class CapacityOrderCompilation<A> : ICapacity<A>
    where A : IProductionAction {
    private readonly IReadOnlyList<A> _actions;
    private readonly IReadOnlyList<TimeRange> _slots;
    private readonly TimeWindow<Flt64> _timeWindow;
    private readonly ulong _maxOrderPerSlot;
    private readonly IReadOnlyList<Executor> _executors;

    /// <summary>
    /// 产能编译构造（带顺序）/ Capacity order compilation constructor
    /// </summary>
    /// <param name="actions">生产动作列表 / List of production actions</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="maxOrderPerSlot">每时隙最大顺序数 / Maximum order per slot</param>
    public CapacityOrderCompilation(
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow,
        ulong maxOrderPerSlot) {
        _actions = actions;
        _slots = slots;
        _timeWindow = timeWindow;
        _maxOrderPerSlot = maxOrderPerSlot;
        _executors = actions.Select(a => a.Executor).Distinct().ToList();

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

    /// <summary>每时隙最大顺序数 / Maximum order per slot</summary>
    public ulong MaxOrderPerSlot => _maxOrderPerSlot;

    /// <inheritdoc/>
    public Result<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>> ExtractSolution(object model) {
        var actionAllocations = new List<ActionAllocation<A>>();
        var executorCapacities = new List<ExecutorCapacityResult>();

        return Results.Ok<CapacitySchedulingSolution<A>>(new CapacitySchedulingSolution<A>(
            _actions, actionAllocations, executorCapacities));
    }
}
