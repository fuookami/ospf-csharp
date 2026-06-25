#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
/// <summary>
/// 任务聚合 / Task aggregation
/// </summary>
/// <typeparam name="T">迭代任务类型 / Iterative task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskAggregation<T, E, A>
    where T : IIterativeAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly List<IReadOnlyList<T>> _tasksIteration = new();
    private readonly List<T> _tasks = new();
    private readonly HashSet<T> _removedTasks = new();

    /// <summary>任务迭代列表 / Task iteration list</summary>
    public IReadOnlyList<IReadOnlyList<T>> TasksIteration => _tasksIteration;

    /// <summary>任务列表 / Task list</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>已移除任务集合 / Removed tasks set</summary>
    public IReadOnlySet<T> RemovedTasks => _removedTasks;

    /// <summary>最后迭代任务列表 / Last iteration tasks</summary>
    public IReadOnlyList<T> LastIterationTasks =>
        _tasksIteration.LastOrDefault(t => t.Count > 0) ?? Array.Empty<T>();

    /// <summary>
    /// 添加列 / Add columns
    /// </summary>
    /// <param name="newTasks">新任务列表 / List of new tasks</param>
    /// <returns>去重后的新任务列表 / Deduplicated list of new tasks</returns>
    public IReadOnlyList<T> AddColumns(IReadOnlyList<T> newTasks) {
        var unduplicatedNewTasks = new List<T>();
        foreach (T task in newTasks) {
            if (unduplicatedNewTasks.All(t => !EqualityComparer<T>.Default.Equals(task, t))) {
                unduplicatedNewTasks.Add(task);
            }
        }

        var unduplicatedTasks = new List<T>();
        foreach (T task in unduplicatedNewTasks) {
            if (_tasks.All(t => !EqualityComparer<T>.Default.Equals(task, t))) {
                unduplicatedTasks.Add(task);
            }
        }

        _tasksIteration.Add(unduplicatedTasks);
        _tasks.AddRange(unduplicatedTasks);

        return unduplicatedTasks;
    }

    /// <summary>
    /// 移除列 / Remove column
    /// </summary>
    /// <param name="task">要移除的任务 / Task to remove</param>
    public void RemoveColumn(T task) {
        if (!_removedTasks.Contains(task)) {
            _removedTasks.Add(task);
            _tasks.Remove(task);
        }
    }

    /// <summary>清空所有数据 / Clear all data</summary>
    public void Clear() {
        _tasksIteration.Clear();
        _tasks.Clear();
        _removedTasks.Clear();
    }
}
