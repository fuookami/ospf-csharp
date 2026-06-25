#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 可反转任务对 / Reversible task pair
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
/// <param name="PrevTask">前一个任务 / Previous task</param>
/// <param name="SuccTask">后一个任务 / Successor task</param>
/// <param name="Symmetrical">是否对称 / Whether symmetrical</param>
public sealed record ReversiblePair<T, E, A>(
    T PrevTask,
    T SuccTask,
    bool Symmetrical)
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E>;

/// <summary>
/// 任务反转 / Task reverse
/// </summary>
/// <remarks>
/// 管理可反转的任务对及其查找映射。
/// Manages reversible task pairs and their lookup mappings.
/// </remarks>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskReverse<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<ReversiblePair<T, E, A>> _symmetricalPairs;
    private readonly IReadOnlyDictionary<TaskKey, IReadOnlyList<ReversiblePair<T, E, A>>> _leftMapper;
    private readonly IReadOnlyDictionary<TaskKey, IReadOnlyList<ReversiblePair<T, E, A>>> _rightMapper;

    internal TaskReverse(
        IReadOnlyList<ReversiblePair<T, E, A>> symmetricalPairs,
        IReadOnlyDictionary<TaskKey, IReadOnlyList<ReversiblePair<T, E, A>>> leftMapper,
        IReadOnlyDictionary<TaskKey, IReadOnlyList<ReversiblePair<T, E, A>>> rightMapper) {
        _symmetricalPairs = symmetricalPairs;
        _leftMapper = leftMapper;
        _rightMapper = rightMapper;
    }

    /// <summary>
    /// 对称任务对列表 / List of symmetrical pairs
    /// </summary>
    public IReadOnlyList<ReversiblePair<T, E, A>> SymmetricalPairs => _symmetricalPairs;

    /// <summary>
    /// 检查是否包含任务对 / Check if contains task pair
    /// </summary>
    /// <param name="prevTask">前一个任务 / Previous task</param>
    /// <param name="succTask">后一个任务 / Successor task</param>
    /// <returns>是否包含 / Whether contains</returns>
    public bool Contains(T prevTask, T succTask) {
        return _leftMapper.TryGetValue(prevTask.Key, out IReadOnlyList<ReversiblePair<T, E, A>>? pairs)
            && pairs.Any(p => Equals(p.SuccTask, succTask));
    }

    /// <summary>
    /// 检查任务对是否对称 / Check if task pair is symmetrical
    /// </summary>
    /// <param name="prevTask">前一个任务 / Previous task</param>
    /// <param name="succTask">后一个任务 / Successor task</param>
    /// <returns>是否对称 / Whether symmetrical</returns>
    public bool Symmetrical(T prevTask, T succTask) {
        return _leftMapper.TryGetValue(prevTask.Key, out IReadOnlyList<ReversiblePair<T, E, A>>? pairs)
            && pairs.FirstOrDefault(p => Equals(p.SuccTask, succTask))?.Symmetrical == true;
    }

    /// <summary>
    /// 左查找 / Left find
    /// </summary>
    /// <param name="task">任务 / Task</param>
    /// <returns>可反转任务对列表 / List of reversible pairs</returns>
    public IReadOnlyList<ReversiblePair<T, E, A>> LeftFind(T task) => _leftMapper.TryGetValue(task.Key, out IReadOnlyList<ReversiblePair<T, E, A>>? pairs) ? pairs : Array.Empty<ReversiblePair<T, E, A>>();

    /// <summary>
    /// 右查找 / Right find
    /// </summary>
    /// <param name="task">任务 / Task</param>
    /// <returns>可反转任务对列表 / List of reversible pairs</returns>
    public IReadOnlyList<ReversiblePair<T, E, A>> RightFind(T task) => _rightMapper.TryGetValue(task.Key, out IReadOnlyList<ReversiblePair<T, E, A>>? pairs) ? pairs : Array.Empty<ReversiblePair<T, E, A>>();
}

/// <summary>
/// 任务反转构建器 / Task reverse builder
/// </summary>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskReverseBuilder<B, T, E, A>
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 构建任务反转 / Build task reverse
    /// </summary>
    /// <param name="pairs">任务对列表 / List of task pairs</param>
    /// <param name="originBunches">原始任务束列表 / List of origin bunches</param>
    /// <param name="timeLockedTasks">时间锁定任务集合 / Set of time-locked tasks</param>
    /// <param name="timeDifferenceLimit">时间差限制 / Time difference limit</param>
    /// <returns>任务反转对象 / Task reverse object</returns>
    public TaskReverse<T, E, A> Invoke(
        IReadOnlyList<(T First, T Second)> pairs,
        IReadOnlyList<B> originBunches,
        ISet<T>? timeLockedTasks = null,
        TimeSpan? timeDifferenceLimit = null) {
        timeLockedTasks ??= new HashSet<T>();
        timeDifferenceLimit ??= TimeSpan.Zero;

        var symmetricalPairs = new List<ReversiblePair<T, E, A>>();
        var leftMapper = new Dictionary<TaskKey, List<ReversiblePair<T, E, A>>>();
        var rightMapper = new Dictionary<TaskKey, List<ReversiblePair<T, E, A>>>();

        foreach ((T? prevTask, T? succTask) in pairs) {
            bool isSymmetrical = Symmetrical(originBunches, prevTask, succTask, timeLockedTasks, timeDifferenceLimit.Value);
            var reversiblePair = new ReversiblePair<T, E, A>(prevTask, succTask, isSymmetrical);

            if (!leftMapper.TryGetValue(prevTask.Key, out List<ReversiblePair<T, E, A>>? leftList)) {
                leftList = new List<ReversiblePair<T, E, A>>();
                leftMapper[prevTask.Key] = leftList;
            }
            leftList.Add(reversiblePair);

            if (!rightMapper.TryGetValue(succTask.Key, out List<ReversiblePair<T, E, A>>? rightList)) {
                rightList = new List<ReversiblePair<T, E, A>>();
                rightMapper[succTask.Key] = rightList;
            }
            rightList.Add(reversiblePair);

            if (isSymmetrical) {
                symmetricalPairs.Add(reversiblePair);
            }
        }

        return new TaskReverse<T, E, A>(
            symmetricalPairs,
            leftMapper.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ReversiblePair<T, E, A>>)kv.Value),
            rightMapper.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ReversiblePair<T, E, A>>)kv.Value));
    }

    /// <summary>
    /// 检查反转是否启用 / Check if reverse is enabled
    /// </summary>
    /// <param name="prevTask">前一个任务 / Previous task</param>
    /// <param name="succTask">后一个任务 / Successor task</param>
    /// <param name="timeLockedTasks">时间锁定任务集合 / Set of time-locked tasks</param>
    /// <param name="timeDifferenceLimit">时间差限制 / Time difference limit</param>
    /// <returns>是否启用反转 / Whether reverse is enabled</returns>
    public virtual bool ReverseEnabled(
        T prevTask,
        T succTask,
        ISet<T>? timeLockedTasks = null,
        TimeSpan? timeDifferenceLimit = null) {
        if (!prevTask.DelayEnabled && !prevTask.AdvanceEnabled) {
            return false;
        }

        if (timeLockedTasks is not null && !timeLockedTasks.Contains(prevTask)) {
            return false;
        }

        TimeSpan limit = timeDifferenceLimit ?? TimeSpan.Zero;
        TimeRange? prevScheduled = prevTask.ScheduledTime;
        TimeRange? succScheduled = succTask.ScheduledTime;
        if (prevScheduled is not null && succScheduled is not null
            && prevScheduled.Start < succScheduled.Start
            && (succScheduled.Start - prevScheduled.Start) <= limit) {
            return true;
        }

        TimeRange? prevWindow = prevTask.TimeWindow;
        TimeRange? succWindow = succTask.TimeWindow;
        if (prevWindow is not null && succScheduled is not null
            && prevWindow.Start < succScheduled.Start
            && succScheduled.Start < prevWindow.End) {
            return true;
        }
        if (prevScheduled is not null && succWindow is not null
            && prevScheduled.Start < succWindow.Start
            && (succWindow.Start - prevScheduled.Start) <= limit) {
            return true;
        }

        TimeSpan? prevDuration = prevTask.Duration;
        TimeSpan? succDuration = succTask.Duration;
        return prevWindow is not null && succWindow is not null
            && prevDuration.HasValue && succDuration.HasValue
            && (prevWindow.End - prevDuration.Value) <= (succWindow.End - succDuration.Value);
    }

    /// <summary>
    /// 检查是否对称 / Check if symmetrical
    /// </summary>
    protected virtual bool Symmetrical(
        IReadOnlyList<B> originBunches,
        T prevTask,
        T succTask,
        ISet<T> timeLockedTasks,
        TimeSpan timeDifferenceLimit) => originBunches.Any(b => b.Contains(prevTask, succTask));
}
