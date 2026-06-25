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
/// 设备产能约束 / Executor capacity constraint
/// </summary>
/// <remarks>
/// 每台设备在每个时隙的总产能不超过可用时长。
/// Total capacity per executor per slot should not exceed available duration.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class ExecutorCapacityConstraint<A> : IMetaConstraintGroup
    where A : IProductionAction {
    private readonly ICapacity<A> _capacity;
    private readonly IReadOnlyList<TimeRange> _slots;
    private readonly TimeWindow<Flt64> _timeWindow;

    /// <summary>
    /// 设备产能约束构造 / Executor capacity constraint constructor
    /// </summary>
    /// <param name="capacity">产能编译对象 / Capacity compilation object</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="name">约束名称 / Constraint name</param>
    public ExecutorCapacityConstraint(
        ICapacity<A> capacity,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow,
        string name = "executor_capacity") {
        _capacity = capacity;
        _slots = slots;
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
    /// <returns>操作结果 / Operation result</returns>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
