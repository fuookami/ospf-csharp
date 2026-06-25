#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
/// <summary>
/// 总成本计算器委托 / Total cost calculator delegate
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <param name="executor">执行器 / The executor</param>
/// <param name="lastTask">上一个任务 / The last task</param>
/// <param name="tasks">任务列表 / List of tasks</param>
/// <returns>成本值，若无效则为 null / Cost value, or null if invalid</returns>
public delegate ICost<Flt64>? TotalCostCalculator<T, E>(
    E executor,
    T? lastTask,
    IReadOnlyList<T> tasks)
    where E : Executor
    where T : class;

/// <summary>
/// 标签模型 / Label model
/// </summary>
/// <remarks>
/// 列生成中的标签传播模型。用于最短路径算法生成任务束。
/// Label propagation model for column generation. Used in shortest-path algorithms to generate task bunches.
/// </remarks>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class Label<T, E, A>
    where T : class, IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 标签构造 / Label constructor
    /// </summary>
    /// <param name="cost">成本 / Cost</param>
    /// <param name="shadowPrice">影子价格 / Shadow price</param>
    /// <param name="prevLabel">前一个标签 / Previous label</param>
    /// <param name="node">节点 / Node</param>
    /// <param name="task">任务 / Task</param>
    public Label(
        ICost<Flt64> cost,
        Flt64 shadowPrice,
        Label<T, E, A>? prevLabel = default,
        Node? node = default,
        T? task = default) {
        Cost = cost;
        ShadowPrice = shadowPrice;
        PrevLabel = prevLabel;
        Node = node;
        Task = task;

        // Build trace
        List<ulong> traceList = prevLabel?.Trace?.ToList() ?? new List<ulong>();
        if (node is TaskNode<T, E, A> taskNode) {
            traceList.Add(taskNode.Index);
        }
        Trace = traceList;
    }

    /// <summary>成本 / Cost</summary>
    public ICost<Flt64> Cost { get; }

    /// <summary>影子价格 / Shadow price</summary>
    public Flt64 ShadowPrice { get; }

    /// <summary>前一个标签 / Previous label</summary>
    public Label<T, E, A>? PrevLabel { get; }

    /// <summary>节点 / Node</summary>
    public Node? Node { get; }

    /// <summary>任务 / Task</summary>
    public T? Task { get; }

    /// <summary>约简成本 / Reduced cost</summary>
    public Flt64 ReducedCost => (Cost.CostSum ?? Flt64.Zero) - ShadowPrice;

    /// <summary>执行器变更次数 / Executor change count</summary>
    public ulong ExecutorChange => Task?.ExecutorChanged == true ? 1UL : 0UL;

    /// <summary>路径节点索引列表 / Trace of node indices</summary>
    public IReadOnlyList<ulong> Trace { get; }

    /// <summary>是否为更优任务束 / Whether this is a better bunch</summary>
    public bool IsBetterBunch => ReducedCost.ToDouble() < 0.0;

    /// <summary>
    /// 检查节点是否已访问 / Check if node has been visited
    /// </summary>
    /// <param name="node">节点 / The node</param>
    /// <returns>是否已访问 / Whether visited</returns>
    public bool Visited(Node node) {
        return node switch {
            RootNode or EndNode => false,
            TaskNode<T, E, A> taskNode => Trace.Contains(taskNode.Index),
            _ => false
        };
    }

    /// <summary>
    /// 从标签生成任务束 / Generate bunch from label
    /// </summary>
    /// <param name="iteration">迭代次数 / Iteration count</param>
    /// <param name="executor">执行器 / The executor</param>
    /// <param name="executorUsability">执行器初始可用性 / Executor initial usability</param>
    /// <param name="totalCostCalculator">总成本计算器 / Total cost calculator</param>
    /// <returns>任务束，若无效则为 null / Task bunch, or null if invalid</returns>
    public AbstractTaskBunch<T, E, A>? GenerateBunch(
        long iteration,
        E executor,
        ExecutorInitialUsability<T, E, A> executorUsability,
        TotalCostCalculator<T, E> totalCostCalculator) {
        if (Node is not EndNode) {
            return null;
        }

        var labels = new List<Label<T, E, A>>();
        Label<T, E, A>? currLabel = PrevLabel;
        while (currLabel?.Node is not RootNode && currLabel is not null) {
            labels.Add(currLabel);
            currLabel = currLabel.PrevLabel;
        }

        var tasks = new List<T>();
        for (int i = labels.Count - 1; i >= 0; i--) {
            if (labels[i].Task is T task) {
                tasks.Add(task);
            }
        }

        ICost<Flt64>? totalCost = totalCostCalculator(executor, executorUsability.LastTask, tasks);
        if (totalCost is null) {
            return null;
        }

        return new AbstractTaskBunch<T, E, A>(
            executor,
            executorUsability,
            tasks,
            totalCost,
            iteration);
    }
}
