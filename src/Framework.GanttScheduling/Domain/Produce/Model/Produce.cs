#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>生产接口 / Produce interface</summary>
public interface IProduce {
    /// <summary>产量变量 / Quantity variables (indexed by product)</summary>
    object Quantity { get; }

    /// <summary>超量变量 / Over quantity variables</summary>
    object? OverQuantity { get; }

    /// <summary>不足量变量 / Less quantity variables</summary>
    object? LessQuantity { get; }

    /// <summary>是否启用超量 / Whether over quantity is enabled</summary>
    bool OverEnabled { get; }

    /// <summary>是否启用不足 / Whether less quantity is enabled</summary>
    bool LessEnabled { get; }

    /// <summary>注册到模型 / Register to model</summary>
    Try Register(object model);

    /// <summary>刷新影子价格 / Refresh shadow prices</summary>
    Try Refresh(object shadowPriceMap, object shadowPrices);
}

/// <summary>
/// 抽象生产 / Abstract produce
/// </summary>
/// <remarks>
/// 提供生产量管理的通用框架，包括超量和不足量的松弛变量注册及影子价格提取。
/// Provides a common framework for produce quantity management, including slack variable registration and shadow price extraction.
/// </remarks>
/// <typeparam name="P">产品类型 / Product type</typeparam>
/// <param name="Products">产品与需求列表 / List of products and demands</param>
public abstract class AbstractProduce<P>(
    IReadOnlyList<(P Product, MaterialDemand? Demand)> Products
) : IProduce where P : IMaterial {
    /// <summary>产品与需求列表（按索引排序）/ Products and demands sorted by index</summary>
    protected IReadOnlyList<(P Product, MaterialDemand? Demand)> SortedProducts
        = Products.OrderBy(p => p.Product.Index).ToList();

    /// <inheritdoc/>
    public abstract object Quantity { get; }

    /// <inheritdoc/>
    public object? OverQuantity { get; protected set; }

    /// <inheritdoc/>
    public object? LessQuantity { get; protected set; }

    /// <inheritdoc/>
    public abstract bool OverEnabled { get; }

    /// <inheritdoc/>
    public abstract bool LessEnabled { get; }

    /// <inheritdoc/>
    public virtual Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <inheritdoc/>
    public virtual Try Refresh(object shadowPriceMap, object shadowPrices) => Results.Ok<Success>(Results.SuccessInstance);
}

/// <summary>
/// 任务调度生产 / Task scheduling produce
/// </summary>
/// <remarks>
/// 暂未实现，请使用 BunchSchedulingProduce。
/// Not yet implemented, please use BunchSchedulingProduce.
/// </remarks>
/// <typeparam name="P">产品类型 / Product type</typeparam>
public sealed class TaskSchedulingProduce<P>(
    IReadOnlyList<(P Product, MaterialDemand? Demand)> Products,
    bool OverEnabledFlag = false,
    bool LessEnabledFlag = false
) : AbstractProduce<P>(Products) where P : IMaterial {
    private readonly object _quantity = new();

    /// <inheritdoc/>
    public override object Quantity => _quantity;

    /// <inheritdoc/>
    public override bool OverEnabled => OverEnabledFlag;

    /// <inheritdoc/>
    public override bool LessEnabled => LessEnabledFlag;

    /// <inheritdoc/>
    public override Try Register(object model) {
        return new Failed<Success, ErrorCode, Error<ErrorCode>>(
            new Err<ErrorCode>(ErrorCode.ApplicationFailed,
                "TaskSchedulingProduce.Register is not implemented. Use BunchSchedulingProduce instead."));
    }
}

/// <summary>
/// 任务束调度生产 / Bunch scheduling produce
/// </summary>
/// <remarks>
/// 用于列生成场景，支持通过 addColumns 追加产量贡献。
/// Used for column generation scenarios, supports adding produce contribution through addColumns.
/// </remarks>
/// <typeparam name="P">产品类型 / Product type</typeparam>
public sealed class BunchSchedulingProduce<P> : AbstractProduce<P>
    where P : IMaterial {
    private readonly object _quantity = new();

    /// <summary>
    /// 任务束调度生产构造 / Bunch scheduling produce constructor
    /// </summary>
    /// <param name="products">产品与需求列表 / List of products and demands</param>
    public BunchSchedulingProduce(IReadOnlyList<(P Product, MaterialDemand? Demand)> products)
        : base(products) { }

    /// <inheritdoc/>
    public override object Quantity => _quantity;

    /// <inheritdoc/>
    public override bool OverEnabled => true;

    /// <inheritdoc/>
    public override bool LessEnabled => true;

    /// <inheritdoc/>
    public override Try Register(object model) => base.Register(model);
}
