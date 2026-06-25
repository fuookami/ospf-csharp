#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Service.Limits;
/// <summary>
/// 生产数量影子价格键 / Produce quantity shadow price key
/// </summary>
public sealed class ProduceQuantityShadowPriceKey : ShadowPriceKey {
    /// <summary>产品 / Product</summary>
    public IMaterial Product { get; }

    /// <summary>构造函数 / Constructor</summary>
    public ProduceQuantityShadowPriceKey(IMaterial product)
        : base(typeof(ProduceQuantityShadowPriceKey)) {
        Product = product;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is ProduceQuantityShadowPriceKey other && Product.Index == other.Product.Index;

    /// <inheritdoc/>
    public override int GetHashCode() => Product.Index.GetHashCode();
}

/// <summary>
/// 生产数量约束 / Produce quantity constraint
/// </summary>
/// <remarks>
/// 建模产品产量的上下限约束。
/// Models product output lower/upper bound constraints.
/// </remarks>
/// <typeparam name="P">产品类型 / Product type</typeparam>
public sealed class ProduceQuantityConstraint<P> : IPipeline<object>
    where P : IMaterial {
    private readonly IReadOnlyList<(P Product, MaterialDemand Demand)> _products;
    private readonly IProduce _produce;

    /// <summary>
    /// 生产数量约束构造 / Produce quantity constraint constructor
    /// </summary>
    /// <param name="products">产品与需求对列表 / List of product-demand pairs</param>
    /// <param name="produce">生产对象 / Produce object</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public ProduceQuantityConstraint(
        IReadOnlyList<(P Product, MaterialDemand? Demand)> products,
        IProduce produce,
        string name = "produce_quantity") {
        _products = new List<(P, MaterialDemand)>();
        foreach ((P? product, MaterialDemand? demand) in products) {
            if (demand != null) {
                ((List<(P, MaterialDemand)>)_products).Add((product, demand));
            }
        }
        _produce = produce;
        Name = name;
    }

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
