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
/// 生产数量最小化 / Produce quantity minimization
/// </summary>
/// <typeparam name="P">产品类型 / Product type</typeparam>
public sealed class ProduceQuantityMinimization<P> : IPipeline<object>
    where P : IMaterial {
    private readonly IReadOnlyList<P> _products;
    private readonly IProduce _produce;
    private readonly Func<P, Flt64> _threshold;
    private readonly Func<P, Flt64> _coefficient;

    /// <summary>
    /// 生产数量最小化构造 / Produce quantity minimization constructor
    /// </summary>
    /// <param name="products">产品列表 / List of products</param>
    /// <param name="produce">生产对象 / Produce object</param>
    /// <param name="threshold">阈值函数 / Threshold function</param>
    /// <param name="coefficient">成本系数函数 / Cost coefficient function</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public ProduceQuantityMinimization(
        IReadOnlyList<P> products,
        IProduce produce,
        Func<P, Flt64>? threshold = null,
        Func<P, Flt64>? coefficient = null,
        string name = "produce_quantity_minimization") {
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
    /// <inheritdoc/>
    /// <remarks>
    /// 阈值 0 时直接计费数量符号，非零时建一条**自带约束**的阈值松弛再计费它的正部；系数在松弛外面乘。
    /// 列表为空时**不变加一个空目标**（四个"数量"目标都如此）。
    ///
    /// A zero threshold charges the quantity symbol itself; a non-zero one builds a threshold slack that **carries its own rows** and charges its positive part, with the coefficient multiplying
    /// outside the slack. An empty list still adds an empty objective, as the four "quantity" objectives do.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "数量目标只能施加到线性元模型 / a quantity objective can only be applied to a linear meta model"));
        }
        // **表是 `IReadOnlyDictionary<T, ILinearIntermediateSymbol<Flt64>>`，不是 `<T, object>`**：
        // 值类型不协变，按 `<T, object>` 匹配永远失败——那样目标会一直报"未注册"，而它其实已注册。
        // **The table is `IReadOnlyDictionary<T, ILinearIntermediateSymbol<Flt64>>`, not `<T, object>`**: value types do not covary, so matching on `<T, object>` never succeeds — the objective
        // would then always report "unregistered" while it is in fact registered.
        if (_produce.Quantity is not IReadOnlyDictionary<P, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> table) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "生产尚未注册，取不到产量表 / the produce has not been registered, so its produce-quantity table is unavailable"));
        }

        return ProduceSlackHelper.BuildObjective(
            linear, table, _products,
            _threshold, _coefficient,
            "produce_quantity_minimization_threshold",
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            "produce quantity",
            skipWhenEmpty: false);
    }
}
