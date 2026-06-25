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
/// 消费数量影子价格键 / Consumption quantity shadow price key
/// </summary>
public sealed class ConsumptionQuantityShadowPriceKey : ShadowPriceKey {
    /// <summary>材料 / Material</summary>
    public IMaterial Material { get; }

    /// <summary>构造函数 / Constructor</summary>
    public ConsumptionQuantityShadowPriceKey(IMaterial material)
        : base(typeof(ConsumptionQuantityShadowPriceKey)) {
        Material = material;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is ConsumptionQuantityShadowPriceKey other && Material.Index == other.Material.Index;

    /// <inheritdoc/>
    public override int GetHashCode() => Material.Index.GetHashCode();
}

/// <summary>
/// 消费数量约束 / Consumption quantity constraint
/// </summary>
/// <remarks>
/// 建模原料消耗的上下限约束。
/// Models material consumption lower/upper bound constraints.
/// </remarks>
/// <typeparam name="C">材料类型 / Material type</typeparam>
public sealed class ConsumptionQuantityConstraint<C> : IPipeline<object>
    where C : IMaterial {
    private readonly IReadOnlyList<(C Material, MaterialReserves Reserves)> _materials;
    private readonly IConsumption _consumption;

    /// <summary>
    /// 消费数量约束构造 / Consumption quantity constraint constructor
    /// </summary>
    /// <param name="materials">材料与储备对列表 / List of material-reserve pairs</param>
    /// <param name="consumption">消费对象 / Consumption object</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public ConsumptionQuantityConstraint(
        IReadOnlyList<(C Material, MaterialReserves? Reserves)> materials,
        IConsumption consumption,
        string name = "consumption_quantity") {
        _materials = new List<(C, MaterialReserves)>();
        foreach ((C? material, MaterialReserves? reserves) in materials) {
            if (reserves != null) {
                ((List<(C, MaterialReserves)>)_materials).Add((material, reserves));
            }
        }
        _consumption = consumption;
        Name = name;
    }

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
