#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
/// <summary>
/// 任务调度解汇总 / Task scheduling solution summary
/// </summary>
/// <param name="AssignedTaskCount">已分配任务数 / Assigned task count</param>
/// <param name="CanceledTaskCount">已取消任务数 / Canceled task count</param>
/// <param name="TotalTaskCount">总任务数 / Total task count</param>
public sealed record TaskSolutionSummary(
    ulong AssignedTaskCount,
    ulong CanceledTaskCount,
    ulong TotalTaskCount);

/// <summary>
/// 任务调度解 / Task scheduling solution
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public sealed record TaskSolution<T, E, A>(
    IReadOnlyList<T> AssignedTasks,
    IReadOnlyList<T> CanceledTasks
)
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>解汇总 / Solution summary</summary>
    public TaskSolutionSummary Summary => new(
        AssignedTaskCount: (ulong)AssignedTasks.Count,
        CanceledTaskCount: (ulong)CanceledTasks.Count,
        TotalTaskCount: (ulong)(AssignedTasks.Count + CanceledTasks.Count));
}
