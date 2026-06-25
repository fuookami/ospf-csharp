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
/// 生产数量最大化 / Produce quantity maximization
/// </summary>
/// <typeparam name="P">产品类型 / Product type</typeparam>
public sealed class ProduceQuantityMaximization<P> : IPipeline<object>
    where P : IMaterial {
    private readonly IReadOnlyList<P> _products;
    private readonly IProduce _produce;
    private readonly Func<P, Flt64> _threshold;
    private readonly Func<P, Flt64> _coefficient;

    /// <summary>
    /// 生产数量最大化构造 / Produce quantity maximization constructor
    /// </summary>
    /// <param name="products">产品列表 / List of products</param>
    /// <param name="produce">生产对象 / Produce object</param>
    /// <param name="threshold">阈值函数 / Threshold function</param>
    /// <param name="coefficient">成本系数函数 / Cost coefficient function</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public ProduceQuantityMaximization(
        IReadOnlyList<P> products,
        IProduce produce,
        Func<P, Flt64>? threshold = null,
        Func<P, Flt64>? coefficient = null,
        string name = "produce_quantity_maximization") {
        _products = products;
        _produce = produce;
        _threshold = threshold ?? (_ => Flt64.Zero);
        _coefficient = coefficient ?? (_ => Flt64.One);
        Name = name;
    }

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
