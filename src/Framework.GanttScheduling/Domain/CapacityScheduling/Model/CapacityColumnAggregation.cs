#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
/// <summary>
/// 列聚合（按迭代分组）/ Column aggregation (grouped by iteration)
/// </summary>
/// <remarks>
/// 管理产能列的聚合，支持按迭代分组和去重。
/// Manages aggregation of capacity columns with iteration grouping and deduplication.
/// </remarks>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class CapacityColumnAggregation<E, A>
    where A : IProductionAction {
    private readonly List<IReadOnlyList<CapacityColumn<E, A>>> _columnsIteration = new();
    private readonly List<CapacityColumn<E, A>> _columns = new();
    private readonly HashSet<CapacityColumn<E, A>> _removedColumns = new();

    /// <summary>按迭代分组的列 / Columns grouped by iteration</summary>
    public IReadOnlyList<IReadOnlyList<CapacityColumn<E, A>>> ColumnsIteration => _columnsIteration;

    /// <summary>所有列（扁平化）/ All columns (flattened)</summary>
    public IReadOnlyList<CapacityColumn<E, A>> Columns => _columns;

    /// <summary>已移除的列 / Removed columns</summary>
    public IReadOnlySet<CapacityColumn<E, A>> RemovedColumns => _removedColumns;

    /// <summary>最新迭代的列 / Columns from last iteration</summary>
    public IReadOnlyList<CapacityColumn<E, A>> LastIterationColumns
        => _columnsIteration.LastOrDefault(c => c.Count > 0) ?? Array.Empty<CapacityColumn<E, A>>();

    /// <summary>
    /// 添加新列 / Add new columns
    /// </summary>
    /// <param name="iteration">迭代号 / Iteration number</param>
    /// <param name="newColumns">要添加的新列 / New columns to add</param>
    /// <returns>去重后的列 / Unduplicated columns</returns>
    public IReadOnlyList<CapacityColumn<E, A>> AddColumns(ulong iteration, IReadOnlyList<CapacityColumn<E, A>> newColumns) {
        // Deduplicate within new columns (grouped by executor)
        var unduplicatedNewColumns = newColumns
            .GroupBy(c => c.Executor)
            .SelectMany(group => {
                var unduplicated = new List<CapacityColumn<E, A>>();
                foreach (CapacityColumn<E, A>? column in group) {
                    if (unduplicated.All(existing => !ColumnsEqual(column, existing))) {
                        unduplicated.Add(column);
                    }
                }
                return unduplicated;
            })
            .ToList();

        // Deduplicate with existing columns
        var unduplicatedColumns = unduplicatedNewColumns
            .Where(column => _columns.All(existing => !ColumnsEqual(column, existing)))
            .ToList();

        // Ensure iteration list has enough slots
        while (_columnsIteration.Count <= (int)iteration) {
            _columnsIteration.Add(Array.Empty<CapacityColumn<E, A>>());
        }

        var mergedIterationColumns = _columnsIteration[(int)iteration].ToList();
        mergedIterationColumns.AddRange(unduplicatedColumns);
        _columnsIteration[(int)iteration] = mergedIterationColumns;
        _columns.AddRange(unduplicatedColumns);

        return unduplicatedColumns;
    }

    /// <summary>移除列 / Remove a column</summary>
    public void RemoveColumn(CapacityColumn<E, A> column) {
        if (_removedColumns.Add(column)) {
            _columns.Remove(column);
        }
    }

    /// <summary>批量移除列 / Remove multiple columns</summary>
    public void RemoveColumns(IEnumerable<CapacityColumn<E, A>> columns) {
        foreach (CapacityColumn<E, A> column in columns) {
            RemoveColumn(column);
        }
    }

    /// <summary>清空所有列 / Clear all columns</summary>
    public void Clear() {
        _columnsIteration.Clear();
        _columns.Clear();
        _removedColumns.Clear();
    }

    /// <summary>列相等比较 / Column equality comparison</summary>
    private static bool ColumnsEqual(CapacityColumn<E, A> a, CapacityColumn<E, A> b) {
        if (!Equals(a.Executor, b.Executor)) {
            return false;
        }

        if (a.SlotIndex != b.SlotIndex) {
            return false;
        }

        if (a.Order != b.Order) {
            return false;
        }

        if (a.Allocations.Count != b.Allocations.Count) {
            return false;
        }

        foreach (KeyValuePair<A, ulong> kv in a.Allocations) {
            if (!b.Allocations.TryGetValue(kv.Key, out ulong otherAmount) || kv.Value != otherAmount) {
                return false;
            }
        }
        return true;
    }
}
