#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Service;
/// <summary>
/// 已计划任务束生成器（桩类型，Kotlin 源码中已完全注释）
/// Planned task bunch generator (stub type, entirely commented out in Kotlin source)
/// </summary>
/// <remarks>
/// 此类在 Kotlin 源码中已被完全注释掉，保留为占位符以备将来实现。
/// 包含拓扑排序和标签设置算法的骨架结构。
/// This class was entirely commented out in the Kotlin source, kept as a placeholder for future implementation.
/// Contains topological sort and label-setting algorithm skeleton structure.
/// </remarks>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class PlannedTaskBunchGenerator<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly Graph _graph;
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;

    /// <summary>
    /// 已计划任务束生成器构造 / Planned task bunch generator constructor
    /// </summary>
    /// <param name="graph">任务图 / Task graph</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    public PlannedTaskBunchGenerator(
        Graph graph,
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors) {
        _graph = graph;
        _tasks = tasks;
        _executors = executors;
    }

    /// <summary>任务图 / Task graph</summary>
    public Graph Graph => _graph;

    /// <summary>任务列表 / List of tasks</summary>
    public IReadOnlyList<T> Tasks => _tasks;

    /// <summary>执行器列表 / List of executors</summary>
    public IReadOnlyList<E> Executors => _executors;
}
