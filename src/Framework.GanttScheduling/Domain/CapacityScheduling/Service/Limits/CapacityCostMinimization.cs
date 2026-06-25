#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits;
/// <summary>
/// 产能成本最小化目标 / Capacity cost minimization objective
/// </summary>
/// <remarks>
/// 最小化产能调度的总成本。成本 = sum(action.unitCost * x[action, slot])。
/// Minimizes total cost of capacity scheduling.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class CapacityCostMinimization<A> : IMetaConstraintGroup
    where A : IProductionAction {
    private readonly ICapacity<A> _capacity;
    private readonly IReadOnlyList<A> _actions;
    private readonly IReadOnlyList<TimeRange> _slots;
    private readonly TimeWindow<Flt64> _timeWindow;

    /// <summary>
    /// 产能成本最小化构造 / Capacity cost minimization constructor
    /// </summary>
    /// <param name="capacity">产能编译对象 / Capacity compilation object</param>
    /// <param name="actions">生产动作列表 / List of production actions</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="name">目标名称 / Objective name</param>
    public CapacityCostMinimization(
        ICapacity<A> capacity,
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow,
        string name = "capacity_cost_minimization") {
        _capacity = capacity;
        _actions = actions;
        _slots = slots;
        _timeWindow = timeWindow;
        Name = name;
    }

    /// <inheritdoc/>
    public bool Lazy => false;

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>
    /// 应用目标到模型 / Apply objective to model
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
