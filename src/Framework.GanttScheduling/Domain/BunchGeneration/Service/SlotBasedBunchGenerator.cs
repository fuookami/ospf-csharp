#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Service;
/// <summary>
/// 分时隙任务束生成器接口
/// Slot-based bunch generator interface
/// </summary>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
/// <typeparam name="Action">生产动作类型 / Production action type</typeparam>
public interface ISlotBasedBunchGenerator<B, T, E, A, Action>
    where B : AbstractTaskBunch<T, E, A>, ISlotBasedBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E>
    where Action : IProductionAction {
    /// <summary>支持的执行器列表 / List of supported executors</summary>
    IReadOnlyList<E> Executors { get; }

    /// <summary>
    /// 为指定时隙生成任务束 / Generate bunches for specified slot
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="slot">时隙 / Time slot</param>
    /// <param name="constraints">时隙约束 / Slot constraints</param>
    /// <param name="shadowPrices">影子价格映射 / Shadow prices</param>
    /// <returns>生成的任务束列表 / List of generated bunches</returns>
    Task<Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>> Generate(
        ulong iteration,
        TimeRange slot,
        SlotConstraints constraints,
        IReadOnlyDictionary<T, Flt64> shadowPrices);

    /// <summary>
    /// 为所有时隙生成任务束 / Generate bunches for all slots
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="intermediateValues">产能中间值 / Capacity intermediate values</param>
    /// <param name="shadowPrices">影子价格映射 / Shadow prices</param>
    /// <returns>所有生成的任务束列表 / List of all generated bunches</returns>
    async Task<Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>> GenerateAll(
        ulong iteration,
        CapacityIntermediateValues<Action> intermediateValues,
        IReadOnlyDictionary<T, Flt64> shadowPrices) {
        var allBunches = new List<B>();
        foreach (TimeRange slot in intermediateValues.Slots) {
            SlotConstraints? constraints = intermediateValues.GetSlotConstraints(slot);
            if (constraints is not null) {
                Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> result = await Generate(iteration, slot, constraints, shadowPrices);
                if (result.IsFailed) {
                    return result;
                }

                if (result is Ok<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> ok) {
                    allBunches.AddRange(ok.Value);
                }
            }
        }
        return Results.Ok<IReadOnlyList<B>>(allBunches);
    }

    /// <summary>
    /// 检查是否支持指定执行器 / Check if supports specified executor
    /// </summary>
    /// <param name="executor">执行器 / The executor</param>
    /// <returns>是否支持 / Whether supported</returns>
    bool SupportsExecutor(E executor) => Executors.Any(e => Equals(e, executor));
}

/// <summary>
/// 分时隙任务束生成器工厂接口
/// Slot-based bunch generator factory interface
/// </summary>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
/// <typeparam name="Action">生产动作类型 / Production action type</typeparam>
public interface ISlotBasedBunchGeneratorFactory<B, T, E, A, Action>
    where B : AbstractTaskBunch<T, E, A>, ISlotBasedBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E>
    where Action : IProductionAction {
    /// <summary>
    /// 为指定执行器创建生成器 / Create generator for specified executor
    /// </summary>
    /// <param name="executor">执行器 / The executor</param>
    /// <returns>生成器，若不支持则为 null / Generator, or null if not supported</returns>
    ISlotBasedBunchGenerator<B, T, E, A, Action>? Create(E executor);
}
