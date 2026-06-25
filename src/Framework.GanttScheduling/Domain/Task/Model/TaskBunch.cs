#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
/// <summary>
/// 抽象任务束，表示分配给同一执行者的一组有序任务 / Abstract task bunch representing an ordered group of tasks assigned to the same executor.
/// </summary>
/// <typeparam name="T">任务类型 / The task type.</typeparam>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
/// <typeparam name="A">分配策略类型 / The assignment policy type.</typeparam>
public class AbstractTaskBunch<T, E, A> : ManualIndexed, IEq<AbstractTaskBunch<T, E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="executor">执行者 / The executor.</param>
    /// <param name="time">时间范围 / The time range.</param>
    /// <param name="tasks">任务列表 / The list of tasks.</param>
    /// <param name="cost">成本 / The cost.</param>
    /// <param name="initialUsability">初始可用性 / The initial usability.</param>
    /// <param name="iteration">迭代次数 / The iteration number.</param>
    public AbstractTaskBunch(
        E executor,
        TimeRange time,
        IReadOnlyList<T> tasks,
        ICost<Flt64> cost,
        ExecutorInitialUsability<T, E, A> initialUsability,
        long iteration = -1) {
        Executor = executor;
        Time = time;
        Tasks = tasks;
        Cost = cost;
        InitialUsability = initialUsability;
        Iteration = iteration;
    }

    /// <summary>
    /// 从执行者和初始可用性构造空任务束 / Construct empty bunch from executor and initial usability.
    /// </summary>
    public AbstractTaskBunch(
        E executor,
        DateTimeOffset time,
        ExecutorInitialUsability<T, E, A> initialUsability,
        long iteration = -1)
        : this(executor, new TimeRange(time, DateTimeOffset.MaxValue), Array.Empty<T>(),
            new ImmutableCost<Flt64>(Array.Empty<CostItem<Flt64>>()), initialUsability, iteration) { }

    /// <summary>
    /// 从执行者、初始可用性和任务列表构造 / Construct from executor, initial usability, and task list.
    /// </summary>
    public AbstractTaskBunch(
        E executor,
        ExecutorInitialUsability<T, E, A> initialUsability,
        IReadOnlyList<T> tasks,
        ICost<Flt64>? cost = null,
        long iteration = -1)
        : this(executor,
            new TimeRange(tasks[0].Time!.Start, tasks[^1].Time!.End),
            tasks,
            cost ?? new ImmutableCost<Flt64>(Array.Empty<CostItem<Flt64>>()),
            initialUsability, iteration) { }

    /// <summary>执行者 / The executor.</summary>
    public E Executor { get; }

    /// <summary>时间范围 / The time range.</summary>
    public TimeRange Time { get; }

    /// <summary>任务列表 / The list of tasks.</summary>
    public IReadOnlyList<T> Tasks { get; }

    /// <summary>成本 / The cost.</summary>
    public ICost<Flt64> Cost { get; }

    /// <summary>初始可用性 / The initial usability.</summary>
    public ExecutorInitialUsability<T, E, A> InitialUsability { get; }

    /// <summary>迭代次数 / The iteration number.</summary>
    public long Iteration { get; }

    /// <summary>任务数量 / Number of tasks.</summary>
    public int Size => Tasks.Count;

    /// <summary>是否为空 / Whether the bunch is empty.</summary>
    public bool Empty => Tasks.Count == 0;

    /// <summary>上一个任务 / The last task.</summary>
    public T? LastTask => InitialUsability.LastTask;

    /// <summary>成本密度（成本/任务数）/ Cost density (cost/number of tasks).</summary>
    public Flt64 CostDensity => (Cost.SolverCostOrNull(Flt64.Zero) ?? Flt64.Zero) / new Flt64(Size);

    /// <summary>完工时间 / Makespan.</summary>
    public DateTimeOffset Makespan => Tasks
        .Select(t => t.Time?.End)
        .Where(e => e.HasValue)
        .Select(e => e!.Value)
        .DefaultIfEmpty(InitialUsability.EnabledTime)
        .Max();

    /// <summary>任务键到索引的映射 / Mapping from task key to index.</summary>
    public IReadOnlyDictionary<TaskKey, int> Keys => Tasks
        .Select((t, i) => (t.Key, i))
        .ToDictionary(x => x.Key, x => x.i);

    /// <summary>执行者变更次数 / Number of executor changes.</summary>
    public int ExecutorChangeCount => Tasks.Count(t => t.ExecutorChanged);

    /// <summary>总延迟时间 / Total delay time.</summary>
    public TimeSpan TotalDelay => TimeSpan.FromTicks(Tasks.Sum(t => t.Delay.Ticks));

    /// <summary>总提前时间 / Total advance time.</summary>
    public TimeSpan TotalAdvance => TimeSpan.FromTicks(Tasks.Sum(t => t.Advance.Ticks));

    /// <summary>任务连接对列表 / List of task connection pairs.</summary>
    public IReadOnlyList<(T? Prev, T? Next)> Connections {
        get {
            var result = new List<(T?, T?)>();
            for (int i = 0; i < Tasks.Count; i++) {
                T? prev = i > 0 ? Tasks[i - 1] : LastTask;
                T? next = i < Tasks.Count - 1 ? Tasks[i + 1] : default;
                result.Add((prev, next));
            }
            return result;
        }
    }

    /// <summary>
    /// 判断是否包含指定任务 / Check whether the bunch contains the specified task.
    /// </summary>
    /// <param name="task">任务 / The task.</param>
    /// <returns>是否包含 / Whether contained.</returns>
    public bool Contains(IAbstractTask<E, A> task) => Keys.ContainsKey(task.Key);

    /// <summary>
    /// 判断是否包含指定任务对 / Check whether the bunch contains the specified task pair.
    /// </summary>
    /// <param name="prev">前驱任务 / The previous task.</param>
    /// <param name="succ">后继任务 / The successor task.</param>
    /// <returns>是否包含 / Whether contained.</returns>
    public bool Contains(IAbstractTask<E, A> prev, IAbstractTask<E, A> succ) {
        if (!Keys.TryGetValue(prev.Key, out int prevIdx)) {
            return false;
        }

        if (!Keys.TryGetValue(succ.Key, out int succIdx)) {
            return false;
        }

        return succIdx - prevIdx == 1;
    }

    /// <inheritdoc/>
    public bool? PartialEq(AbstractTaskBunch<T, E, A> rhs) {
        if (ReferenceEquals(this, rhs)) {
            return true;
        }

        if (Executor.Id != rhs.Executor.Id) {
            return false;
        }

        if (Tasks.Count != rhs.Tasks.Count) {
            return false;
        }

        for (int i = 0; i < Tasks.Count; i++) {
            if (!Tasks[i].Eq(rhs.Tasks[i])) {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public bool Eq(AbstractTaskBunch<T, E, A> rhs) => PartialEq(rhs) ?? false;

    /// <inheritdoc/>
    public bool Neq(AbstractTaskBunch<T, E, A> rhs) => !Eq(rhs);
}
