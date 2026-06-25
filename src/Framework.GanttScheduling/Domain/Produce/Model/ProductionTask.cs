#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Ret = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Math.Algebra.Number.Flt64, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>材料接口 / Material interface</summary>
public interface IMaterial : IIndexed {
    /// <summary>材料自身引用 / Material self reference</summary>
    IMaterial Material => this;
}

/// <summary>产品接口 / Product interface</summary>
public interface IProduct : IMaterial { }

/// <summary>半产品接口 / Semi-product interface</summary>
public interface ISemiProduct : IMaterial { }

/// <summary>原材料接口 / Raw material interface</summary>
public interface IRawMaterial : IMaterial { }

/// <summary>
/// 材料需求 / Material demand
/// </summary>
/// <param name="LowerBound">数量下界 / Quantity lower bound</param>
/// <param name="UpperBound">数量上界 / Quantity upper bound</param>
/// <param name="LessQuantityValue">不足数量 / Less quantity value</param>
/// <param name="OverQuantityValue">超量数量 / Over quantity value</param>
public sealed record MaterialDemand(
    Flt64 LowerBound,
    Flt64 UpperBound,
    Flt64? LessQuantityValue = null,
    Flt64? OverQuantityValue = null) {
    /// <summary>是否启用不足 / Whether less quantity is enabled</summary>
    public bool LessEnabled => LessQuantityValue.HasValue;

    /// <summary>是否启用超量 / Whether over quantity is enabled</summary>
    public bool OverEnabled => OverQuantityValue.HasValue;

    /// <summary>求解器下界 / Solver lower bound</summary>
    public Flt64 SolverLowerBound() => LowerBound;

    /// <summary>求解器上界 / Solver upper bound</summary>
    public Flt64 SolverUpperBound() => UpperBound;

    /// <summary>求解器不足量 / Solver less quantity</summary>
    public Flt64 SolverLessQuantity() => LessQuantityValue ?? Flt64.Zero;

    /// <summary>求解器超限量 / Solver over quantity</summary>
    public Flt64 SolverOverQuantity() => OverQuantityValue ?? Flt64.Zero;

    /// <summary>求解器数值范围下界 / Solver value range lower bound</summary>
    public Flt64 SolverRangeLowerBound() => SolverLowerBound() - SolverLessQuantity();

    /// <summary>求解器数值范围上界 / Solver value range upper bound</summary>
    public Flt64 SolverRangeUpperBound() => SolverUpperBound() + SolverOverQuantity();
}

/// <summary>
/// 材料储备 / Material reserves
/// </summary>
/// <param name="LowerBound">数量下界 / Quantity lower bound</param>
/// <param name="UpperBound">数量上界 / Quantity upper bound</param>
/// <param name="LessQuantityValue">不足数量 / Less quantity value</param>
/// <param name="OverQuantityValue">超量数量 / Over quantity value</param>
public sealed record MaterialReserves(
    Flt64 LowerBound,
    Flt64 UpperBound,
    Flt64? LessQuantityValue = null,
    Flt64? OverQuantityValue = null) {
    /// <summary>是否启用不足 / Whether less quantity is enabled</summary>
    public bool LessEnabled => LessQuantityValue.HasValue;

    /// <summary>是否启用超量 / Whether over quantity is enabled</summary>
    public bool OverEnabled => OverQuantityValue.HasValue;

    /// <summary>求解器下界 / Solver lower bound</summary>
    public Flt64 SolverLowerBound() => LowerBound;

    /// <summary>求解器上界 / Solver upper bound</summary>
    public Flt64 SolverUpperBound() => UpperBound;

    /// <summary>求解器不足量 / Solver less quantity</summary>
    public Flt64 SolverLessQuantity() => LessQuantityValue ?? Flt64.Zero;

    /// <summary>求解器超限量 / Solver over quantity</summary>
    public Flt64 SolverOverQuantity() => OverQuantityValue ?? Flt64.Zero;

    /// <summary>求解器数值范围下界 / Solver value range lower bound</summary>
    public Flt64 SolverRangeLowerBound() => SolverLowerBound() - SolverLessQuantity();

    /// <summary>求解器数值范围上界 / Solver value range upper bound</summary>
    public Flt64 SolverRangeUpperBound() => SolverUpperBound() + SolverOverQuantity();
}

/// <summary>
/// 生产任务接口 / Production task interface
/// </summary>
/// <remarks>
/// 继承 IAbstractTask，添加产品产量和原料消耗映射。
/// Extends IAbstractTask with produce quantity and consumption quantity maps.
/// </remarks>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public interface IProductionTask<E, A> : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>生产量映射 / Produce quantity map</summary>
    IReadOnlyDictionary<IMaterial, Flt64> ProduceQuantityByProduct { get; }

    /// <summary>消耗量映射 / Consumption quantity map</summary>
    IReadOnlyDictionary<IMaterial, Flt64> ConsumptionQuantityByMaterial { get; }

    /// <summary>生产量 / Produce quantity</summary>
    Flt64? ProduceQuantity(IMaterial product)
        => ProduceQuantityByProduct.TryGetValue(product, out Flt64 q) ? q : null;

    /// <summary>消耗量 / Consumption quantity</summary>
    Flt64? ConsumptionQuantity(IMaterial material)
        => ConsumptionQuantityByMaterial.TryGetValue(material, out Flt64 q) ? q : null;
}

/// <summary>
/// 生产任务扩展方法 / Production task extension methods
/// </summary>
public static class ProductionTaskExtensions {
    /// <summary>读取非零生产物料 / Read non-zero produce materials</summary>
    public static IReadOnlyList<IMaterial> NonZeroProduceMaterials<E, A>(this IProductionTask<E, A> task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => task.ProduceQuantityByProduct
            .Where(kv => kv.Value != Flt64.Zero)
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>读取非零消耗物料 / Read non-zero consumption materials</summary>
    public static IReadOnlyList<IMaterial> NonZeroConsumptionMaterials<E, A>(this IProductionTask<E, A> task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => task.ConsumptionQuantityByMaterial
            .Where(kv => kv.Value != Flt64.Zero)
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>计算任务束的生产量 / Calculate bunch produce quantity</summary>
    public static Ret Produce<T, E, A>(this AbstractTaskBunch<T, E, A> bunch, IMaterial product)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var quantities = bunch.Tasks
            .OfType<IProductionTask<E, A>>()
            .Select(t => t.ProduceQuantity(product))
            .Where(q => q.HasValue)
            .Select(q => q!.Value)
            .ToList();

        return Results.Ok(quantities.Aggregate(Flt64.Zero, (acc, v) => acc + v));
    }

    /// <summary>计算任务束的消耗量 / Calculate bunch consumption quantity</summary>
    public static Ret Consumption<T, E, A>(this AbstractTaskBunch<T, E, A> bunch, IMaterial material)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var quantities = bunch.Tasks
            .OfType<IProductionTask<E, A>>()
            .Select(t => t.ConsumptionQuantity(material))
            .Where(q => q.HasValue)
            .Select(q => q!.Value)
            .ToList();

        return Results.Ok(quantities.Aggregate(Flt64.Zero, (acc, v) => acc + v));
    }
}
