#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
/// <summary>
/// 任务束聚合 / Bunch aggregation
/// </summary>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class BunchAggregation<B, T, E, A>
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly List<IReadOnlyList<B>> _bunchesIteration = new();
    private readonly List<B> _bunches = new();
    private readonly HashSet<B> _removedBunches = new();

    /// <summary>任务束迭代列表 / Bunch iteration list</summary>
    public IReadOnlyList<IReadOnlyList<B>> BunchesIteration => _bunchesIteration;

    /// <summary>所有任务束列表 / All bunches list</summary>
    public IReadOnlyList<B> Bunches => _bunches;

    /// <summary>已移除任务束集合 / Removed bunches set</summary>
    public IReadOnlySet<B> RemovedBunches => _removedBunches;

    /// <summary>最后迭代的任务束列表 / Last iteration bunches</summary>
    public IReadOnlyList<B> LastIterationBunches =>
        _bunchesIteration.LastOrDefault(b => b.Count > 0) ?? Array.Empty<B>();

    /// <summary>
    /// 添加列 / Add columns
    /// </summary>
    /// <param name="newBunches">新任务束列表 / List of new bunches</param>
    /// <returns>去重后的新任务束列表 / Deduplicated list of new bunches</returns>
    public async System.Threading.Tasks.Task<IReadOnlyList<B>> AddColumnsAsync(IReadOnlyList<B> newBunches) {
        // Group by (executor, task count) for parallel deduplication within groups
        var grouped = newBunches
            .GroupBy(b => (b.Executor, b.Tasks.Count))
            .ToList();

        var undupWithinGroups = new List<B>();
        await System.Threading.Tasks.Task.WhenAll(grouped.Select(async group => {
            var undup = new List<B>();
            foreach (B? bunch in group) {
                if (undup.All(existing => !SameColumnAs(bunch, existing))) {
                    undup.Add(bunch);
                }
            }
            lock (undupWithinGroups) {
                undupWithinGroups.AddRange(undup);
            }
        }));

        // Deduplicate against existing bunches
        var unduplicated = new List<B>();
        foreach (B bunch in undupWithinGroups) {
            if (_bunches.All(existing => !SameColumnAs(bunch, existing))) {
                unduplicated.Add(bunch);
            }
        }

        // Flush and index
        ManualIndexed.Impl.Flush(typeof(AbstractTaskBunch<T, E, A>));
        foreach (B bunch in unduplicated) {
            bunch.SetIndexed();
        }

        _bunchesIteration.Add(unduplicated);
        _bunches.AddRange(unduplicated);

        return unduplicated;
    }

    /// <summary>
    /// 添加列（同步版本） / Add columns (synchronous version)
    /// </summary>
    /// <param name="newBunches">新任务束列表 / List of new bunches</param>
    /// <returns>去重后的新任务束列表 / Deduplicated list of new bunches</returns>
    public IReadOnlyList<B> AddColumns(IReadOnlyList<B> newBunches) => AddColumnsAsync(newBunches).GetAwaiter().GetResult();

    /// <summary>
    /// 检查是否为同一列 / Check if same column
    /// </summary>
    /// <param name="lhs">任务束 / Bunch</param>
    /// <param name="rhs">另一个任务束 / Another bunch</param>
    /// <returns>是否为同一列 / Whether same column</returns>
    protected virtual bool SameColumnAs(B lhs, B rhs) => lhs.Eq(rhs);

    /// <summary>
    /// 移除列 / Remove column
    /// </summary>
    /// <param name="bunch">要移除的任务束 / Bunch to remove</param>
    public void RemoveColumn(B bunch) {
        if (!_removedBunches.Contains(bunch)) {
            _removedBunches.Add(bunch);
            _bunches.Remove(bunch);
        }
    }

    /// <summary>
    /// 移除多列 / Remove columns
    /// </summary>
    /// <param name="bunches">要移除的任务束列表 / List of bunches to remove</param>
    public void RemoveColumns(IReadOnlyList<B> bunches) {
        foreach (B bunch in bunches) {
            RemoveColumn(bunch);
        }
    }

    /// <summary>
    /// 清空所有数据 / Clear all data
    /// </summary>
    public void Clear() {
        _bunchesIteration.Clear();
        _bunches.Clear();
        _removedBunches.Clear();
    }
}
