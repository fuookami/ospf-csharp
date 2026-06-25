#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>
/// 产能调度场景的产品产量管理抽象基类
/// Abstract base class for produce management in capacity scheduling scenarios
/// </summary>
/// <remarks>
/// 提供产能调度场景下产品产量计算的通用框架。
/// Provides a common framework for product quantity calculation in capacity scheduling scenarios.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
/// <typeparam name="P">产品类型 / Product type</typeparam>
public abstract class CapacitySchedulingProduce<A, P> : AbstractProduce<P>
    where A : IProductionAction
    where P : IMaterial {
    protected readonly IReadOnlyList<A> Actions;
    protected readonly IReadOnlyList<TimeRange> Slots;
    protected readonly TimeWindow<Flt64> TimeWindow;
    private readonly object _quantity = new();

    /// <summary>
    /// 产能调度生产构造 / Capacity scheduling produce constructor
    /// </summary>
    /// <param name="products">产品与需求列表 / List of products and demands</param>
    /// <param name="actions">生产动作列表 / List of production actions</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    protected CapacitySchedulingProduce(
        IReadOnlyList<(P Product, MaterialDemand? Demand)> products,
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow)
        : base(products) {
        Actions = actions;
        Slots = slots;
        TimeWindow = timeWindow;
    }

    /// <inheritdoc/>
    public override object Quantity => _quantity;

    /// <inheritdoc/>
    public override bool OverEnabled => true;

    /// <inheritdoc/>
    public override bool LessEnabled => true;

    /// <summary>
    /// 将 quantity 变量添加到模型 / Add quantity variables to model
    /// </summary>
    protected Try AddQuantityToModel(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
