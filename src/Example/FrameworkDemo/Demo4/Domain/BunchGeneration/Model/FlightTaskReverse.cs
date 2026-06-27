#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Model;

/// <summary>
/// 管理用于顺序变更操作的可反转航班任务对。
/// Manages reversible flight task pairs for order change operations.
/// </summary>
public sealed class FlightTaskReverse {
    /// <summary>默认时间差限制 / Default time difference limit.</summary>
    public static readonly TimeSpan DefaultTimeDifferenceLimit = TimeSpan.FromHours(5);

    /// <summary>临界大小 / Critical size.</summary>
    public const ulong CriticalSize = 200UL;

    private readonly IReadOnlyList<ReversiblePair> _symmetricalPairs;
    private readonly IReadOnlyDictionary<TaskKey, IReadOnlyList<ReversiblePair>> _leftMapper;
    private readonly IReadOnlyDictionary<TaskKey, IReadOnlyList<ReversiblePair>> _rightMapper;

    private FlightTaskReverse(
        IReadOnlyList<ReversiblePair> symmetricalPairs,
        IReadOnlyDictionary<TaskKey, IReadOnlyList<ReversiblePair>> leftMapper,
        IReadOnlyDictionary<TaskKey, IReadOnlyList<ReversiblePair>> rightMapper) {
        _symmetricalPairs = symmetricalPairs;
        _leftMapper = leftMapper;
        _rightMapper = rightMapper;
    }

    /// <summary>
    /// 可以反转的一对任务。
    /// A pair of tasks that can be reversed.
    /// </summary>
    /// <param name="PrevTask">前驱任务 / Previous task.</param>
    /// <param name="SuccTask">后继任务 / Successor task.</param>
    /// <param name="Symmetrical">是否对称 / Whether symmetrical.</param>
    public sealed record ReversiblePair(FlightTask PrevTask, FlightTask SuccTask, bool Symmetrical);

    /// <summary>
    /// 从任务对列表创建 FlightTaskReverse。
    /// Creates a FlightTaskReverse from a list of task pairs.
    /// </summary>
    public static FlightTaskReverse Create(
        IReadOnlyList<(FlightTask, FlightTask)> pairs,
        IReadOnlyList<FlightTaskBunch> originBunches,
        Lock lockObj,
        TimeSpan timeDifferenceLimit) {
        var symmetricalPairs = new List<ReversiblePair>();
        var leftMapper = new Dictionary<TaskKey, List<ReversiblePair>>();
        var rightMapper = new Dictionary<TaskKey, List<ReversiblePair>>();

        foreach (var (first, second) in pairs) {
            var symmetrical = Symmetrical(originBunches, first, second, lockObj, timeDifferenceLimit);
            var pair = new ReversiblePair(first, second, symmetrical);

            if (!leftMapper.TryGetValue(first.Key, out var leftList)) { leftList = new(); leftMapper[first.Key] = leftList; }
            leftList.Add(pair);

            if (!rightMapper.TryGetValue(second.Key, out var rightList)) { rightList = new(); rightMapper[second.Key] = rightList; }
            rightList.Add(pair);

            if (symmetrical) symmetricalPairs.Add(pair);
        }

        return new FlightTaskReverse(symmetricalPairs,
            leftMapper.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ReversiblePair>)kv.Value),
            rightMapper.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ReversiblePair>)kv.Value));
    }

    /// <summary>
    /// 检查两个任务是否可以反转。
    /// Checks if two tasks can be reversed.
    /// </summary>
    public static bool ReverseEnabled(FlightTask prevTask, FlightTask succTask, Lock lockObj, TimeSpan timeDifferenceLimit) {
        if (prevTask.Dep != succTask.Arr) return false;
        if (!prevTask.DelayEnabled && !succTask.AdvanceEnabled) return false;
        if (lockObj.LockedTime(prevTask) is not null) return false;

        var prevScheduled = prevTask.ScheduledTime;
        var succScheduled = succTask.ScheduledTime;
        if (prevScheduled is not null && succScheduled is not null
            && prevScheduled.Start < succScheduled.Start
            && (succScheduled.Start - prevScheduled.Start) <= timeDifferenceLimit) return true;

        var prevTW = prevTask.TimeWindow;
        var succTW = succTask.TimeWindow;
        if (prevTW is not null && succScheduled is not null
            && prevTW.Start < succScheduled.Start
            && succScheduled.Start < prevTW.End) return true;
        if (prevScheduled is not null && succTW is not null
            && prevScheduled.Start < succTW.Start
            && (succTW.Start - prevScheduled.Start) <= timeDifferenceLimit) return true;
        if (prevTW is not null && succTW is not null
            && prevTask.Duration is not null && succTask.Duration is not null
            && (prevTW.End - prevTask.Duration.Value) <= (succTW.End - succTask.Duration.Value)) return true;

        return false;
    }

    /// <summary>
    /// 检查两个任务是否对称。
    /// Checks if two tasks are symmetrical.
    /// </summary>
    public static bool Symmetrical(FlightTask prevTask, FlightTask succTask, Lock lockObj, TimeSpan timeDifferenceLimit)
        => ReverseEnabled(prevTask, succTask, lockObj, timeDifferenceLimit) && prevTask.Aircraft == succTask.Aircraft;

    /// <summary>
    /// 检查一对任务是否可以反转。
    /// Checks if a pair of tasks can be reversed.
    /// </summary>
    public bool Contains(FlightTask prevTask, FlightTask succTask)
        => _leftMapper.TryGetValue(prevTask.Key, out var pairs) && pairs.Any(p => p.SuccTask == succTask);

    /// <summary>
    /// 检查一对任务是否对称。
    /// Checks if a pair of tasks are symmetrical.
    /// </summary>
    public bool IsSymmetrical(FlightTask prevTask, FlightTask succTask)
        => _leftMapper.TryGetValue(prevTask.Key, out var pairs)
           && pairs.Any(p => p.SuccTask.OriginTask == succTask && p.Symmetrical);

    /// <summary>
    /// 查找给定任务作为前序任务的所有可反转对。
    /// Finds all reversible pairs where the given task is the predecessor.
    /// </summary>
    public IReadOnlyList<ReversiblePair> LeftFind(FlightTask task)
        => _leftMapper.TryGetValue(task.Key, out var pairs) ? pairs : Array.Empty<ReversiblePair>();

    /// <summary>
    /// 查找给定任务作为后续任务的所有可反转对。
    /// Finds all reversible pairs where the given task is the successor.
    /// </summary>
    public IReadOnlyList<ReversiblePair> RightFind(FlightTask task)
        => _rightMapper.TryGetValue(task.Key, out var pairs) ? pairs : Array.Empty<ReversiblePair>();

    private static bool Symmetrical(
        IReadOnlyList<FlightTaskBunch> originBunches,
        FlightTask prevTask, FlightTask succTask,
        Lock lockObj, TimeSpan timeDifferenceLimit) {
        return originBunches.Any(b => b.Contains(prevTask, succTask)) && prevTask.Arr == succTask.Dep;
    }
}
