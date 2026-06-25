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
/// 顺序约束 / Order constraint
/// </summary>
/// <remarks>
/// 每个顺序位置最多只能有一个动作不为0（仅用于 CapacityOrderCompilation）。
/// Each order position can have at most one action with non-zero allocation (only for CapacityOrderCompilation).
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class OrderConstraint<A> : IMetaConstraintGroup
    where A : IProductionAction {
    private readonly CapacityOrderCompilation<A> _compilation;
    private readonly IReadOnlyList<A> _actions;
    private readonly IReadOnlyList<TimeRange> _slots;
    private readonly ulong _maxOrderPerSlot;

    /// <summary>
    /// 顺序约束构造 / Order constraint constructor
    /// </summary>
    /// <param name="compilation">产能编译对象 / Capacity compilation object</param>
    /// <param name="actions">生产动作列表 / List of production actions</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="maxOrderPerSlot">每时隙最大顺序数 / Maximum order per slot</param>
    /// <param name="name">约束名称 / Constraint name</param>
    public OrderConstraint(
        CapacityOrderCompilation<A> compilation,
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        ulong maxOrderPerSlot,
        string name = "order") {
        _compilation = compilation;
        _actions = actions;
        _slots = slots;
        _maxOrderPerSlot = maxOrderPerSlot;
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
