#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>
/// 产能动作生产接口 / Capacity action produce interface
/// </summary>
/// <remarks>
/// 定义生产动作与产品产量、原料消耗之间的关系。
/// Defines the relationship between production actions and product output/material consumption.
/// </remarks>
public interface ICapacityActionProduce {
    /// <summary>生产动作对应的产品产量（单位操作时间的产量）/ Product produce per unit operation time</summary>
    IReadOnlyDictionary<IMaterial, Flt64> Produce { get; }

    /// <summary>生产动作对应的原料消耗（单位操作时间的消耗）/ Material consumption per unit operation time</summary>
    IReadOnlyDictionary<IMaterial, Flt64> Consumption { get; }
}

/// <summary>
/// 产能动作生产扩展方法 / Capacity action produce extension methods
/// </summary>
public static class CapacityActionProduceExtensions {
    /// <summary>按产品方向读取生产动作的单位产量映射 / Read unit produce map from a production action</summary>
    public static IReadOnlyDictionary<IMaterial, Flt64>? UnitProduceMap(this IProductionAction action)
        => (action as ICapacityActionProduce)?.Produce;

    /// <summary>按消耗方向读取生产动作的单位消耗映射 / Read unit consumption map from a production action</summary>
    public static IReadOnlyDictionary<IMaterial, Flt64>? UnitConsumptionMap(this IProductionAction action)
        => (action as ICapacityActionProduce)?.Consumption;

    /// <summary>按 CapacityColumn 计算产量 / Calculate produce from a CapacityColumn</summary>
    public static Flt64 Produce<E, A>(this CapacityColumn<E, A> column, IMaterial product)
        where A : IProductionAction {
        Flt64 result = Flt64.Zero;
        foreach ((A? action, ulong amount) in column.Allocations) {
            Flt64 unitProduce = action.UnitProduceMap()?.TryGetValue(product, out Flt64 p) == true ? p : Flt64.Zero;
            result += unitProduce * new Flt64(Convert.ToDouble(amount));
        }
        return result;
    }

    /// <summary>按 CapacityColumn 计算消耗 / Calculate consumption from a CapacityColumn</summary>
    public static Flt64 Consumption<E, A>(this CapacityColumn<E, A> column, IMaterial material)
        where A : IProductionAction {
        Flt64 result = Flt64.Zero;
        foreach ((A? action, ulong amount) in column.Allocations) {
            Flt64 unitConsumption = action.UnitConsumptionMap()?.TryGetValue(material, out Flt64 c) == true ? c : Flt64.Zero;
            result += unitConsumption * new Flt64(Convert.ToDouble(amount));
        }
        return result;
    }
}
