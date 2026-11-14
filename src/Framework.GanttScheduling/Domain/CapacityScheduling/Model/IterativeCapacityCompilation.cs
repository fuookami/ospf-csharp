#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
/// <summary>
/// 迭代产能编译决策对象（列生成主问题）
/// Iterative capacity compilation decision object (column generation master problem)
/// </summary>
/// <remarks>
/// 用于列生成迭代求解场景。变量结构：每台设备的二维整型变量 x[executor][iteration, columnIndex] -> amount。
/// Used for column generation/iterative solving scenarios. Variable structure: 2D integer variable per executor.
/// </remarks>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class IterativeCapacityCompilation<E, A> : ICapacity<A>
    where E : Executor
    where A : IProductionAction {
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlyList<A> _actions;
    private readonly IReadOnlyList<TimeRange> _slots;
    private readonly TimeWindow<Flt64> _timeWindow;
    private readonly Dictionary<E, CapacityColumnAggregation<E, A>> _columnsByExecutor;
    private readonly Dictionary<Executor, E> _executorByRef;

    /// <summary>
    /// 迭代产能编译构造 / Iterative capacity compilation constructor
    /// </summary>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="actions">生产动作列表 / List of production actions</param>
    /// <param name="slots">时隙列表 / List of time slots</param>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    public IterativeCapacityCompilation(
        IReadOnlyList<E> executors,
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow) {
        _executors = executors;
        _actions = actions;
        _slots = slots;
        _timeWindow = timeWindow;
        _columnsByExecutor = executors.ToDictionary(e => e, _ => new CapacityColumnAggregation<E, A>());
        _executorByRef = executors.ToDictionary(e => (Executor)e);

        foreach (ManualIndexed action in actions.OfType<ManualIndexed>()) {
            if (!action.Indexed) {
                action.SetIndexed();
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Executor> Executors => _executors;

    /// <summary>执行器列表（强类型）/ Executors (strongly typed)</summary>
    public IReadOnlyList<E> ExecutorsTyped => _executors;

    /// <summary>生产动作列表 / List of production actions</summary>
    public IReadOnlyList<A> Actions => _actions;

    /// <summary>时隙列表 / List of time slots</summary>
    public IReadOnlyList<TimeRange> Slots => _slots;

    /// <summary>时间窗口 / Time window</summary>
    public TimeWindow<Flt64> TimeWindow => _timeWindow;

    /// <summary>每台设备的列聚合（按迭代分组）/ Column aggregation per executor</summary>
    public IReadOnlyDictionary<E, CapacityColumnAggregation<E, A>> ColumnsByExecutor => _columnsByExecutor;

    /// <summary>
    /// 添加新列 / Add new columns
    /// </summary>
    /// <param name="iteration">当前迭代号 / Current iteration number</param>
    /// <param name="newColumns">要添加的新列 / New columns to add</param>
    /// <returns>去重后的列 / Deduplicated columns</returns>
    public Result<IReadOnlyList<CapacityColumn<E, A>>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<CapacityColumn<E, A>> newColumns) {
        var allAddedColumns = new List<CapacityColumn<E, A>>();

        foreach (IGrouping<E, CapacityColumn<E, A>> group in newColumns.GroupBy(c => c.Executor)) {
            if (!_columnsByExecutor.TryGetValue(group.Key, out CapacityColumnAggregation<E, A>? columnAggregation)) {
                continue;
            }

            var sanitizedColumns = group
                .Where(column =>
                    column.SlotIndex >= 0 && column.SlotIndex < _slots.Count &&
                    column.Allocations.Keys.All(action => _actions.Contains(action) && Equals(action.Executor, group.Key)))
                .ToList();

            if (sanitizedColumns.Count == 0) {
                continue;
            }

            IReadOnlyList<CapacityColumn<E, A>> addedColumns = columnAggregation.AddColumns(iteration, sanitizedColumns);
            if (addedColumns.Count > 0) {
                allAddedColumns.AddRange(addedColumns);
            }
        }

        return Results.Ok<IReadOnlyList<CapacityColumn<E, A>>>(allAddedColumns);
    }


    /// <inheritdoc/>
    /// <summary>
    /// 提取解 / Extract the solution.
    /// </summary>
    /// <param name="model">模型 / The model.</param>
    /// <returns>解或失败 / The solution or a failure.</returns>
    /// <remarks>
    /// **这个编译对象没有决策变量**，所以解无从谈起。第一版返回的是 `Ok` 加一个分配表为空的解——
    /// 调用方读到的是「排好了，什么都没排」，**空解和真解无法区分**。
    ///
    /// **This compilation has no decision variables**, so there is no solution to speak of. The first version returned `Ok` with an empty allocation table — the caller read
    /// "solved, nothing scheduled", and **an empty solution and a real one cannot be told apart**.
    /// </remarks>
    public Result<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>> ExtractSolution(object model) {
        ArgumentNullException.ThrowIfNull(model);
        return new Failed<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>>(
            new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "这个容量编译对象没有决策变量，解无从提取；用 `CapacityOrderCompilation` 的那条路径 / "
                + "this capacity compilation has no decision variables, so there is no solution to extract; use the `CapacityOrderCompilation` path"));
    }
}
