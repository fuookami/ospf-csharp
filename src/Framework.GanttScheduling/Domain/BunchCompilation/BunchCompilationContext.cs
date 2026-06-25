#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
/// <summary>
/// 任务束编译上下文接口 / Bunch compilation context interface
/// </summary>
/// <typeparam name="Args">影子价格参数类型 / Shadow price arguments type</typeparam>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public interface IBunchCompilationContext<Args, B, T, E, A>
    where Args : class, IGanttSchedulingShadowPriceArguments<E, A>
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>任务束编译聚合 / Bunch compilation aggregation</summary>
    BunchCompilationAggregation<B, T, E, A> Aggregation { get; }

    /// <summary>列生成管线列表 / CG pipeline list</summary>
    IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>> PipelineList { get; }

    /// <summary>列数 / Column amount</summary>
    ulong ColumnAmount => (ulong)Aggregation.Bunches.Count;

    /// <summary>
    /// 注册到模型 / Register to model
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>操作结果 / Operation result</returns>
    Try Register(object model);

    /// <summary>
    /// 添加列 / Add columns
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="newBunches">新任务束列表 / List of new bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>去重后的任务束列表 / Deduplicated bunch list</returns>
    Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<B> newBunches,
        object model);

    /// <summary>
    /// 移除列 / Remove columns
    /// </summary>
    Result<Flt64, ErrorCode, Error<ErrorCode>> RemoveColumns(
        Flt64 maximumReducedCost,
        ulong maximumColumnAmount,
        Func<B, Flt64> reducedCost,
        ISet<B> fixedBunches,
        ISet<B> keptBunches,
        object model);

    /// <summary>
    /// 提取影子价格 / Extract shadow price
    /// </summary>
    Try ExtractShadowPrice(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices);

    /// <summary>
    /// 提取固定任务束 / Extract fixed bunches
    /// </summary>
    Result<ISet<B>, ErrorCode, Error<ErrorCode>> ExtractFixedBunches(ulong iteration, object model);

    /// <summary>
    /// 提取保留任务束 / Extract kept bunches
    /// </summary>
    Result<ISet<B>, ErrorCode, Error<ErrorCode>> ExtractKeptBunches(ulong iteration, object model);

    /// <summary>
    /// 提取保留任务束及比率 / Extract kept bunches with ratio
    /// </summary>
    Result<IReadOnlyDictionary<B, Flt64>, ErrorCode, Error<ErrorCode>> ExtractKeptBunchesWithRatio(
        ulong iteration, object model);

    /// <summary>
    /// 提取隐藏执行器 / Extract hidden executors
    /// </summary>
    Result<ISet<E>, ErrorCode, Error<ErrorCode>> ExtractHiddenExecutors(IReadOnlyList<E> executors, object model);

    /// <summary>
    /// 全局固定 / Globally fix
    /// </summary>
    Try GloballyFix(ISet<B> fixedBunches);

    /// <summary>
    /// 局部固定 / Locally fix
    /// </summary>
    Result<ISet<B>, ErrorCode, Error<ErrorCode>> LocallyFix(
        ulong iteration,
        Flt64 bar,
        ISet<B> fixedBunches,
        object model);

    /// <summary>
    /// 分析任务解 / Analyze task solution
    /// </summary>
    Result<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> AnalyzeTaskSolution(
        ulong iteration,
        IReadOnlyList<T> tasks,
        object model,
        IReadOnlyList<Flt64>? solution = null);

    /// <summary>
    /// 分析任务束解 / Analyze bunch solution
    /// </summary>
    Result<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>> AnalyzeBunchSolution(
        ulong iteration,
        IReadOnlyList<T> tasks,
        object model,
        IReadOnlyList<Flt64>? solution = null);
}

/// <summary>
/// 分时隙任务束编译上下文接口
/// Slot-based bunch compilation context interface
/// </summary>
/// <remarks>
/// 扩展 BunchCompilationContext，增加时隙相关功能。提供产能预求解功能，获取时隙级中间值。
/// Extends BunchCompilationContext with slot-related functionality.
/// </remarks>
/// <typeparam name="Args">影子价格参数类型 / Shadow price arguments type</typeparam>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
/// <typeparam name="Action">生产动作类型 / Production action type</typeparam>
public interface ISlotBasedBunchCompilationContext<Args, B, T, E, A, Action>
    : IBunchCompilationContext<Args, B, T, E, A>
    where Args : class, IGanttSchedulingShadowPriceArguments<E, A>
    where B : AbstractTaskBunch<T, E, A>, ISlotBasedBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E>
    where Action : IProductionAction {
    /// <summary>时隙列表 / List of time slots</summary>
    IReadOnlyList<TimeRange> Slots { get; }

    /// <summary>产能中间值（预求解后填充）/ Capacity intermediate values (populated after pre-solving)</summary>
    CapacityIntermediateValues<Action>? IntermediateValues { get; }

    /// <summary>
    /// 获取指定时隙的约束 / Get constraints for specified slot
    /// </summary>
    /// <param name="slot">时隙 / The time slot</param>
    /// <returns>时隙约束 / Slot constraints</returns>
    SlotConstraints? SlotConstraints(TimeRange slot);

    /// <summary>
    /// 按时隙添加列 / Add columns by slot
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="newBunches">新任务束列表 / List of new bunches</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>按时隙分组的已添加 bunch / Added bunches grouped by slot</returns>
    Result<IReadOnlyDictionary<TimeRange, IReadOnlyList<B>>, ErrorCode, Error<ErrorCode>> AddColumnsBySlot(
        ulong iteration,
        IReadOnlyList<B> newBunches,
        object model);

    /// <summary>
    /// 获取指定时隙的所有 bunch / Get all bunches for specified slot
    /// </summary>
    /// <param name="slot">时隙 / The time slot</param>
    /// <returns>该时隙的 bunch 列表 / List of bunches in this slot</returns>
    IReadOnlyList<B> BunchesInSlot(TimeRange slot);
}

/// <summary>
/// 提取任务束编译上下文接口 / Extract bunch compilation context interface
/// </summary>
/// <typeparam name="Args">影子价格参数类型 / Shadow price arguments type</typeparam>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public interface IExtractBunchCompilationContext<Args, B, T, E, A>
    where Args : class, IGanttSchedulingShadowPriceArguments<E, A>
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>基础上下文 / Base context</summary>
    IBunchCompilationContext<Args, B, T, E, A> BaseContext { get; }

    /// <summary>
    /// 注册到模型 / Register to model
    /// </summary>
    Try Register(object model);

    /// <summary>
    /// 添加列 / Add columns
    /// </summary>
    Try AddColumns(ulong iteration, IReadOnlyList<B> newBunches, object model);

    /// <summary>
    /// 提取影子价格 / Extract shadow price
    /// </summary>
    Try ExtractShadowPrice(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices);

    /// <summary>
    /// 记录结果 / Log result
    /// </summary>
    Try LogResult(ulong iteration, object model) => Results.Ok<Success>(Results.SuccessInstance);
}
