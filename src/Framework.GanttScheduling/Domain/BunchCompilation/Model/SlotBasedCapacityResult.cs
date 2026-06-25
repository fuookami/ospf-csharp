#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 分时隙产能结果
/// Slot-based capacity result
/// </summary>
/// <remarks>
/// 存储单个时隙的产能分配结果和中间数值。
/// Stores capacity allocation results and intermediate values for a single time slot.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
/// <param name="Slot">所属时隙 / The time slot</param>
/// <param name="SlotIndex">时隙索引 / Slot index</param>
/// <param name="ActionAllocations">该时隙内的动作分配 / Action allocations in this slot</param>
/// <param name="TotalCostValue">该时隙的总成本值 / Total cost value for this slot</param>
/// <param name="ProduceQuantityByProduct">该时隙的产品产量 / Product production quantities in this slot</param>
/// <param name="ConsumptionQuantityByMaterial">该时隙的原料消耗 / Material consumption quantities in this slot</param>
/// <param name="ResourceUsageQuantityByResource">该时隙的资源使用量 / Resource usage quantities in this slot</param>
public sealed record SlotBasedCapacityResult<A>(
    TimeRange Slot,
    int SlotIndex,
    IReadOnlyList<ActionAllocation<A>> ActionAllocations,
    Flt64 TotalCostValue,
    IReadOnlyDictionary<object, Flt64> ProduceQuantityByProduct,
    IReadOnlyDictionary<object, Flt64> ConsumptionQuantityByMaterial,
    IReadOnlyDictionary<object, Flt64> ResourceUsageQuantityByResource)
    where A : IProductionAction;

/// <summary>
/// 产能中间值集合
/// Capacity intermediate values collection
/// </summary>
/// <remarks>
/// 聚合所有时隙的产能结果，提供查询接口。
/// Aggregates capacity results for all slots, provides query interface.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
/// <param name="Slots">时隙列表 / List of time slots</param>
/// <param name="Results">各时隙的产能结果 / Capacity results by slot</param>
public sealed record CapacityIntermediateValues<A>(
    IReadOnlyList<TimeRange> Slots,
    IReadOnlyDictionary<TimeRange, SlotBasedCapacityResult<A>> Results)
    where A : IProductionAction {
    /// <summary>
    /// 获取指定时隙的产品产量 / Get product production quantity for specified slot
    /// </summary>
    /// <param name="slot">时隙 / The time slot</param>
    /// <param name="product">产品 / The product</param>
    /// <returns>产量值 / Production quantity value</returns>
    public Flt64? ProduceQuantity(TimeRange slot, object product) {
        return Results.TryGetValue(slot, out SlotBasedCapacityResult<A>? result)
            && result.ProduceQuantityByProduct.TryGetValue(product, out Flt64 qty)
            ? qty : null;
    }

    /// <summary>
    /// 获取指定时隙的原料消耗 / Get material consumption quantity for specified slot
    /// </summary>
    /// <param name="slot">时隙 / The time slot</param>
    /// <param name="material">原料 / The material</param>
    /// <returns>消耗值 / Consumption quantity value</returns>
    public Flt64? ConsumptionQuantity(TimeRange slot, object material) {
        return Results.TryGetValue(slot, out SlotBasedCapacityResult<A>? result)
            && result.ConsumptionQuantityByMaterial.TryGetValue(material, out Flt64 qty)
            ? qty : null;
    }

    /// <summary>
    /// 获取指定时隙的资源使用量 / Get resource usage quantity for specified slot
    /// </summary>
    /// <param name="slot">时隙 / The time slot</param>
    /// <param name="resource">资源容量 / The resource capacity</param>
    /// <returns>使用量值 / Resource usage quantity value</returns>
    public Flt64? ResourceUsageQuantity(TimeRange slot, object resource) {
        return Results.TryGetValue(slot, out SlotBasedCapacityResult<A>? result)
            && result.ResourceUsageQuantityByResource.TryGetValue(resource, out Flt64 qty)
            ? qty : null;
    }

    /// <summary>
    /// 获取指定时隙的所有约束 / Get all constraints for specified slot
    /// </summary>
    /// <param name="slot">时隙 / The time slot</param>
    /// <returns>时隙约束 / Slot constraints</returns>
    public SlotConstraints? GetSlotConstraints(TimeRange slot) {
        return Results.TryGetValue(slot, out SlotBasedCapacityResult<A>? result)
            ? new SlotConstraints(result.Slot, result.SlotIndex,
                result.ProduceQuantityByProduct, result.ConsumptionQuantityByMaterial,
                result.ResourceUsageQuantityByResource)
            : null;
    }
}

/// <summary>
/// 时隙约束
/// Slot constraints
/// </summary>
/// <remarks>
/// 描述单个时隙的资源、产量、消耗约束边界。
/// Describes resource, produce, consumption constraint boundaries for a single slot.
/// </remarks>
/// <param name="Slot">所属时隙 / The time slot</param>
/// <param name="SlotIndex">时隙索引 / Slot index</param>
/// <param name="ProduceQuantity">产品产量 / Production quantities by product</param>
/// <param name="ConsumptionQuantity">原料消耗 / Consumption quantities by material</param>
/// <param name="ResourceUsageQuantity">资源使用量 / Resource usage quantities by resource</param>
public sealed record SlotConstraints(
    TimeRange Slot,
    int SlotIndex,
    IReadOnlyDictionary<object, Flt64> ProduceQuantity,
    IReadOnlyDictionary<object, Flt64> ConsumptionQuantity,
    IReadOnlyDictionary<object, Flt64> ResourceUsageQuantity);
