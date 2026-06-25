#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 任务束调度解汇总 / Bunch scheduling solution summary
/// </summary>
/// <param name="BunchCount">任务束数 / Bunch count</param>
/// <param name="AssignedTaskCount">已分配任务数 / Assigned task count</param>
/// <param name="CanceledTaskCount">已取消任务数 / Canceled task count</param>
/// <param name="TotalTaskCount">总任务数 / Total task count</param>
public sealed record BunchSolutionSummary(
    ulong BunchCount,
    ulong AssignedTaskCount,
    ulong CanceledTaskCount,
    ulong TotalTaskCount);

/// <summary>
/// 任务束调度解 / Bunch scheduling solution
/// </summary>
/// <typeparam name="B">任务束类型 / Task bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
/// <param name="Bunches">任务束列表 / List of bunches</param>
/// <param name="CanceledTasks">已取消任务列表 / List of canceled tasks</param>
public sealed record BunchSolution<B, T, E, A>(
    IReadOnlyList<B> Bunches,
    IReadOnlyList<T> CanceledTasks)
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>解汇总 / Solution summary</summary>
    public BunchSolutionSummary Summary => new(
        BunchCount: (ulong)Bunches.Count,
        AssignedTaskCount: (ulong)Bunches.Sum(b => b.Tasks.Count),
        CanceledTaskCount: (ulong)CanceledTasks.Count,
        TotalTaskCount: (ulong)(Bunches.Sum(b => b.Tasks.Count) + CanceledTasks.Count));
}

/// <summary>
/// 任务束解扩展方法 / Bunch solution extension methods
/// </summary>
public static class BunchSolutionExtensions {
    /// <summary>
    /// 从任务束解创建任务解 / Create task solution from bunch solution
    /// </summary>
    /// <typeparam name="B">任务束类型 / Task bunch type</typeparam>
    /// <typeparam name="T">任务类型 / Task type</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type</typeparam>
    /// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
    /// <param name="bunchSolution">任务束解 / Bunch solution</param>
    /// <returns>任务解 / Task solution</returns>
    public static TaskCompilation.Model.TaskSolution<T, E, A> ToTaskSolution<B, T, E, A>(
        this BunchSolution<B, T, E, A> bunchSolution)
        where B : AbstractTaskBunch<T, E, A>
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var assignedTasks = new List<T>();
        foreach (B bunch in bunchSolution.Bunches) {
            foreach (T task in bunch.Tasks) {
                A? policy = task.AssignmentPolicy;
                if (policy is not null && !policy.Empty) {
                    assignedTasks.Add(task);
                }
            }
        }
        return new TaskCompilation.Model.TaskSolution<T, E, A>(assignedTasks, bunchSolution.CanceledTasks);
    }
}
