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
/// Bunch 模式产能调度生产（支持列生成）
/// Bunch-mode produce management for capacity scheduling (with column generation)
/// </summary>
/// <remarks>
/// 用于列生成场景，通过 CapacityColumn 追加产量贡献。
/// Used for column generation scenarios, adds produce contribution through CapacityColumn.
/// </remarks>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
/// <typeparam name="P">产品类型 / Product type</typeparam>
public sealed class BunchCapacitySchedulingProduce<E, A, P> : CapacitySchedulingProduce<A, P>
    where E : Executor
    where A : IProductionAction
    where P : IMaterial {
    /// <summary>
    /// Bunch 模式产能调度生产构造 / Bunch-mode capacity scheduling produce constructor
    /// </summary>
    /// <param name="products">产品与需求列表 / List of products and demands</param>
    /// <param name="actions">生产动作列表 / List of production actions</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    public BunchCapacitySchedulingProduce(
        IReadOnlyList<(P Product, MaterialDemand? Demand)> products,
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow)
        : base(products, actions, slots, timeWindow) { }

    /// <inheritdoc/>
    public override Try Register(object model) => AddQuantityToModel(model);

    /// <summary>
    /// 从 IterativeCapacityCompilation 添加列贡献
    /// Add column contribution from IterativeCapacityCompilation
    /// </summary>
    /// <remarks>
    /// 用于列生成场景，在每次迭代中添加新列的产量贡献。
    /// Used for column generation, adds produce contribution from new columns in each iteration.
    /// </remarks>
    /// <param name="iteration">当前迭代 / Current iteration</param>
    /// <param name="columns">产能列列表 / Capacity columns</param>
    /// <param name="compilation">迭代编译对象 / Iterative compilation object</param>
    /// <returns>操作结果 / Operation result</returns>
    public Try AddColumns(ulong iteration, IReadOnlyList<CapacityColumn<E, A>> columns, IterativeCapacityCompilation<E, A> compilation) =>
        // Column produce contribution is rebuilt from operationTime to keep consistency
        // when iterative x variables are reshaped.
        Results.Ok<Success>(Results.SuccessInstance);
}
