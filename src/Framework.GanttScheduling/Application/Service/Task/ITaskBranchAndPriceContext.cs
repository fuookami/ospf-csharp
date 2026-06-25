#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Application.Service.Task;
/// <summary>
/// 任务分支定价编译上下文接口 / Task branch-and-price compilation context interface.
///
/// 提供任务级分支定价算法所需的所有编译操作。
/// Provides all compilation operations required by task-level branch-and-price algorithm.
/// </summary>
/// <typeparam name="Args">影子价格参数类型 / Shadow price arguments type</typeparam>
/// <typeparam name="IT">迭代任务类型 / Iterative task type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public interface ITaskBranchAndPriceContext<Args, IT, T, E, A>
    where Args : class, IGanttSchedulingShadowPriceArguments<E, A>
    where IT : IIterativeAbstractTask<E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>列数 / Column amount</summary>
    ulong ColumnAmount { get; }

    /// <summary>注册到模型 / Register to model</summary>
    Try Register(object model);

    /// <summary>添加列 / Add columns</summary>
    Result<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<IT> newTasks,
        object model);

    /// <summary>移除列 / Remove columns</summary>
    Result<Flt64, ErrorCode, Error<ErrorCode>> RemoveColumns(
        Flt64 maximumReducedCost,
        ulong maximumColumnAmount,
        Func<IT, Flt64> reducedCost,
        ISet<IT> fixedTasks,
        ISet<IT> keptTasks,
        object model);

    /// <summary>提取影子价格 / Extract shadow price</summary>
    Try ExtractShadowPrice(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices);

    /// <summary>提取固定任务 / Extract fixed tasks</summary>
    Result<ISet<IT>, ErrorCode, Error<ErrorCode>> ExtractFixedTasks(ulong iteration, object model);

    /// <summary>提取保留任务 / Extract kept tasks</summary>
    Result<ISet<IT>, ErrorCode, Error<ErrorCode>> ExtractKeptTasks(ulong iteration, object model);

    /// <summary>提取隐藏执行器 / Extract hidden executors</summary>
    Result<ISet<E>, ErrorCode, Error<ErrorCode>> ExtractHiddenExecutors(IReadOnlyList<E> executors, object model);

    /// <summary>选择空闲执行器 / Select free executors</summary>
    Result<ISet<E>, ErrorCode, Error<ErrorCode>> SelectFreeExecutors(
        ISet<IT> fixedTasks,
        ISet<E> hiddenExecutors,
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model);

    /// <summary>全局固定 / Globally fix</summary>
    Try GloballyFix(ISet<IT> fixedTasks);

    /// <summary>局部固定 / Locally fix</summary>
    Result<ISet<IT>, ErrorCode, Error<ErrorCode>> LocallyFix(
        ulong iteration,
        Flt64 bar,
        ISet<IT> fixedTasks,
        object model);

    /// <summary>分析任务解 / Analyze task solution</summary>
    Result<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> AnalyzeSolution(
        ulong iteration,
        IReadOnlyList<T> tasks,
        object model);

    /// <summary>记录结果 / Log result</summary>
    Try LogResult(ulong iteration, object model);

    /// <summary>记录任务成本 / Log task cost</summary>
    Try LogTaskCost(ulong iteration, object model);

    /// <summary>刷新变量范围 / Flush variable ranges</summary>
    Try Flush(ulong iteration);
}
