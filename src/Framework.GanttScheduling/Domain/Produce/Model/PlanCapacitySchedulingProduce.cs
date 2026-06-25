#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>
/// Plan 模式产能调度生产 / Plan-mode capacity scheduling produce
/// </summary>
/// <remarks>
/// 用于非列生成场景，在构造时绑定 Capacity 编译对象。
/// Used for non-column generation scenarios, binds to Capacity compilation object at construction.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
/// <typeparam name="P">产品类型 / Product type</typeparam>
public sealed class PlanCapacitySchedulingProduce<A, P> : CapacitySchedulingProduce<A, P>
    where A : IProductionAction
    where P : IMaterial {
    private readonly ICapacity<A> _compilation;

    /// <summary>
    /// Plan 模式产能调度生产构造 / Plan-mode capacity scheduling produce constructor
    /// </summary>
    /// <param name="products">产品列表及其需求 / Product list with demands</param>
    /// <param name="compilation">Capacity 编译对象 / Capacity compilation object</param>
    /// <param name="actions">生产动作列表 / List of production actions</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    public PlanCapacitySchedulingProduce(
        IReadOnlyList<(P Product, MaterialDemand? Demand)> products,
        ICapacity<A> compilation,
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow)
        : base(products, actions, slots, timeWindow) {
        _compilation = compilation;
    }

    /// <inheritdoc/>
    public override Try Register(object model) => AddQuantityToModel(model);
}
