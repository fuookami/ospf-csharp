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
using System.Linq;
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
    /// <inheritdoc/>
    /// <remarks>
    /// 每个有生产的产品建两条行：上界 `超量扣除后的产量 至多 上界`、下界 `不足补足后的产量 至少 下界`。
    /// **有超量 / 不足松弛时行加在 `polyX`（`x - pos` / `x + neg`）上，不是数量本身**——加在数量上会让松弛失去意义，
    /// 超出的部分照样违反上界。**方向不能反**：两者求值都非负，反了照样能解，只是约束变成"上界至少"。
    ///
    /// Each produce gets two rows: an upper bound on 超量扣除后的产量 and a lower bound on 不足补足后的产量. **With an over/less slack the rows apply to `polyX` (`x - pos` / `x + neg`), not to the quantity itself**,
    /// since applying them to the quantity makes the slack pointless as the excess would still violate the bound. **The directions must not be swapped**: both evaluate non-negative and a swap still
    /// solves, merely asserting "at least the upper bound".
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "生产约束只能施加到线性元模型 / a produce constraint can only be applied to a linear meta model"));
        }
        if (_produce.Quantity is not IReadOnlyDictionary<P, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> quantity) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "生产尚未注册，取不到产量表 / the produce has not been registered, so its quantity table is unavailable"));
        }

        var over = _produce.OverQuantity as IReadOnlyDictionary<P, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>;
        var less = _produce.LessQuantity as IReadOnlyDictionary<P, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>;

        foreach ((P Product, MaterialDemand demand) in _products) {
            if (!quantity.TryGetValue(Product, out Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>? symbol)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "产量表缺项：" + Product + " / the quantity table is missing an entry: " + Product));
            }
            Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>? overSlack =
                _produce.OverEnabled && demand.OverEnabled && over is not null && over.TryGetValue(Product, out Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>? o)
                    ? o as Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>
                    : null;
            Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>? lessSlack =
                _produce.LessEnabled && demand.LessEnabled && less is not null && less.TryGetValue(Product, out Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>? l)
                    ? l as Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>
                    : null;

            Try added = ProduceSlackHelper.BuildQuantityRows(
                linear, symbol, overSlack, lessSlack,
                demand.SolverLowerBound(), demand.SolverUpperBound(),
                Name,
                Product.Index.ToString(),
                new ProduceQuantityShadowPriceKey(Product));
            if (!added.IsOk) {
                return added;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
