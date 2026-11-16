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
    /// <inheritdoc/>
    /// <remarks>
    /// 每个有消耗的材料建两条行：上界 `超量扣除后的消耗量至多上界`、下界 `不足补足后的消耗量至少下界`。
    /// **有超量 / 不足松弛时行加在 `polyX`（`x - pos` / `x + neg`）上，不是数量本身**——加在数量上会让松弛失去意义，
    /// 超出的部分照样违反上界。**方向不能反**：两者求值都非负，反了照样能解，只是约束变成"上界至少"。
    ///
    /// Each consumption gets two rows: an upper bound on 超量扣除后的消耗量 and a lower bound on 不足补足后的消耗量. **With an over/less slack the rows apply to `polyX` (`x - pos` / `x + neg`), not to the quantity itself**,
    /// since applying them to the quantity makes the slack pointless as the excess would still violate the bound. **The directions must not be swapped**: both evaluate non-negative and a swap still
    /// solves, merely asserting "at least the upper bound".
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "消耗约束只能施加到线性元模型 / a consumption constraint can only be applied to a linear meta model"));
        }
        if (_consumption.Quantity is not IReadOnlyDictionary<C, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> quantity) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "消耗尚未注册，取不到消耗量表 / the consumption has not been registered, so its quantity table is unavailable"));
        }

        var over = _consumption.OverQuantity as IReadOnlyDictionary<C, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>;
        var less = _consumption.LessQuantity as IReadOnlyDictionary<C, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>;

        foreach ((C Material, MaterialReserves reserves) in _materials) {
            if (!quantity.TryGetValue(Material, out Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>? symbol)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "消耗量表缺项：" + Material + " / the quantity table is missing an entry: " + Material));
            }
            Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>? overSlack =
                _consumption.OverEnabled && reserves.OverEnabled && over is not null && over.TryGetValue(Material, out Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>? o)
                    ? o as Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>
                    : null;
            Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>? lessSlack =
                _consumption.LessEnabled && reserves.LessEnabled && less is not null && less.TryGetValue(Material, out Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>? l)
                    ? l as Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>
                    : null;

            Try added = ProduceSlackHelper.BuildQuantityRows(
                linear, symbol, overSlack, lessSlack,
                reserves.SolverLowerBound(), reserves.SolverUpperBound(),
                Name,
                Material.Index.ToString(),
                new ConsumptionQuantityShadowPriceKey(Material));
            if (!added.IsOk) {
                return added;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
