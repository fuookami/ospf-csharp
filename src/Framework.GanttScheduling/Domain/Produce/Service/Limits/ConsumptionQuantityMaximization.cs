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
/// 消费数量最大化 / Consumption quantity maximization
/// </summary>
/// <typeparam name="C">材料类型 / Material type</typeparam>
public sealed class ConsumptionQuantityMaximization<C> : IPipeline<object>
    where C : IMaterial {
    private readonly IReadOnlyList<C> _materials;
    private readonly IConsumption _consumption;
    private readonly Func<C, Flt64> _threshold;
    private readonly Func<C, Flt64> _coefficient;

    /// <summary>
    /// 消费数量最大化构造 / Consumption quantity maximization constructor
    /// </summary>
    /// <param name="materials">材料列表 / List of materials</param>
    /// <param name="consumption">消费对象 / Consumption object</param>
    /// <param name="threshold">阈值函数 / Threshold function</param>
    /// <param name="coefficient">成本系数函数 / Cost coefficient function</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public ConsumptionQuantityMaximization(
        IReadOnlyList<C> materials,
        IConsumption consumption,
        Func<C, Flt64>? threshold = null,
        Func<C, Flt64>? coefficient = null,
        string name = "consumption_quantity_maximization") {
        _materials = materials;
        _consumption = consumption;
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
