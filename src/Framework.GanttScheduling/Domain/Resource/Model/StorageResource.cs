#nullable enable

using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using ValueRange = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Fuookami.Ospf.Math.Algebra.Number.Flt64>;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// 存储资源 / Storage resource.
/// </summary>
public abstract class StorageResource<C, V> : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>计算指定时长内的固定成本 / Calculate fixed cost in the given duration.</summary>
    public virtual V FixedCostIn(TimeSpan time) => default(V);

    /// <summary>计算指定时间范围内的固定成本 / Calculate fixed cost in the given time range.</summary>
    public virtual V FixedCostIn(TimeRange time) => FixedCostIn(time.Duration);

    /// <summary>计算任务在指定时长内的资源消耗量 / Calculate resource consumption of a task in the given duration.</summary>
    public abstract V CostBy<T, E, A>(T task, TimeSpan time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E>;

    /// <summary>计算任务在指定时间范围内的资源消耗量 / Calculate resource consumption of a task in the given time range.</summary>
    public virtual V CostBy<T, E, A>(T task, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        TimeRange intersectionTime = task.Time?.IntersectionWith(time) ?? time;
        return CostBy<T, E, A>(task, intersectionTime?.Duration ?? time.Duration);
    }

    /// <summary>计算指定时长内的固定供给量 / Calculate fixed supply in the given duration.</summary>
    public virtual V FixedSupplyIn(TimeSpan time) => default(V);

    /// <summary>计算指定时间范围内的固定供给量 / Calculate fixed supply in the given time range.</summary>
    public virtual V FixedSupplyIn(TimeRange time) => FixedSupplyIn(time.Duration);

    /// <summary>计算任务在指定时长内的资源供给量 / Calculate resource supply of a task in the given duration.</summary>
    public abstract V SupplyBy<T, E, A>(T task, TimeSpan time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E>;

    /// <summary>计算任务在指定时间范围内的资源供给量 / Calculate resource supply of a task in the given time range.</summary>
    public virtual V SupplyBy<T, E, A>(T task, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        TimeRange intersectionTime = task.Time?.IntersectionWith(time) ?? time;
        return SupplyBy<T, E, A>(task, intersectionTime?.Duration ?? time.Duration);
    }

    /// <summary>计算任务在指定时间范围内的净使用量 / Calculate net usage of a task in the given time range.</summary>
    public V UsedQuantity<T, E, A>(T task, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E>
        => (dynamic)SupplyBy<T, E, A>(task, time) - CostBy<T, E, A>(task, time);

    /// <inheritdoc/>
    public override V UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time)
        => (dynamic)SupplyBy(bunch, time) - CostBy(bunch, time);

    /// <summary>计算任务束在指定时间范围内的资源消耗量 / Calculate resource consumption of a task bunch.</summary>
    public virtual V CostBy<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var sum = default(V);
        foreach (T task in bunch.Tasks) {
            sum = (dynamic)sum + CostBy<T, E, A>(task, time);
            if (task.Time is not null && task.Time.End >= time.End) {
                break;
            }
        }
        return sum;
    }

    /// <summary>计算任务束在指定时间范围内的资源供给量 / Calculate resource supply of a task bunch.</summary>
    public virtual V SupplyBy<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E> {
        var sum = default(V);
        foreach (T task in bunch.Tasks) {
            sum = (dynamic)sum + SupplyBy<T, E, A>(task, time);
            if (task.Time is not null && task.Time.End >= time.End) {
                break;
            }
        }
        return sum;
    }
}

/// <summary>
/// 存储资源时间槽 / Storage resource time slot.
/// </summary>
public class StorageResourceTimeSlot<R, C, V> : IResourceTimeSlot<R, C, V>
    where R : StorageResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    public StorageResourceTimeSlot(TimeWindow<V> timeWindow, ITimeSlot origin, R resource, C resourceCapacity, ulong indexInRule) {
        TimeWindow = timeWindow;
        Origin = origin;
        Resource = resource;
        ResourceCapacity = resourceCapacity;
        IndexInRule = indexInRule;
        Index = AutoIndexed.AutoIndexedImpl.NextIndex(GetType());
    }

    /// <summary>时间窗口 / Time window.</summary>
    public TimeWindow<V> TimeWindow { get; }

    /// <inheritdoc/>
    public ITimeSlot Origin { get; }

    /// <inheritdoc/>
    public R Resource { get; }

    /// <inheritdoc/>
    public C ResourceCapacity { get; }

    /// <inheritdoc/>
    public ulong IndexInRule { get; }

    /// <inheritdoc/>
    public TimeRange Time => Origin.Time;

    /// <inheritdoc/>
    public int Index { get; }

    /// <inheritdoc/>
    public ITimeSlot? SubOf(TimeRange subTime) => Origin.SubOf(subTime);

    /// <inheritdoc/>
    public V RelationTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => task is not null ? default(V) : default(V);

    /// <inheritdoc/>
    public bool RelatedTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => !RelationTo(prevTask, task).Equals(default(V));

    /// <inheritdoc/>
    public override string ToString() => $"{Resource}_{ResourceCapacity}_{IndexInRule}";
}

/// <summary>
/// 任务束调度存储资源使用 / Bunch scheduling storage resource usage.
/// </summary>
public class BunchSchedulingStorageResourceUsage<E, R, C, V>
    : IResourceUsage<StorageResourceTimeSlot<R, C, V>, R, C, V>
    where E : Executor
    where R : StorageResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window.</param>
    /// <param name="executors">执行器列表 / List of executors.</param>
    /// <param name="resources">资源列表 / List of resources.</param>
    /// <param name="name">名称 / Name.</param>
    /// <param name="cost">按槽的消耗，缺省为零 / Per-slot consumption, zero by default.</param>
    /// <remarks>
    /// **消耗按槽**：基线的 `cost` 是子类提供的符号表，C# 侧还没有那个子类，先用"按槽给一个裸值"表达同一件事——
    /// 消耗是已知输入，不是决策变量。等任务编译层真的用上，再换成符号表。
    ///
    /// **Consumption is per slot**: the baseline's `cost` is a symbol table supplied by a subclass that does not exist on the C# side yet, so a raw per-slot value expresses the same thing for
    /// now — consumption is a known input, not a decision variable. Switch it to a symbol table once the task-compilation layer really uses storage.
    /// </remarks>
    public BunchSchedulingStorageResourceUsage(
        TimeWindow<V> timeWindow,
        IReadOnlyList<E> executors,
        IReadOnlyList<R> resources,
        string name,
        Func<StorageResourceTimeSlot<R, C, V>, Flt64>? cost = null) {
        Name = name;
        TimeSlots = BuildTimeSlots(timeWindow, resources);
        _windowStart = timeWindow.Start;
        _cost = cost;
    }

    private readonly DateTimeOffset _windowStart;

    private readonly Func<StorageResourceTimeSlot<R, C, V>, Flt64>? _cost;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<StorageResourceTimeSlot<R, C, V>> TimeSlots { get; }

    /// <inheritdoc/>
    public bool OverEnabled => true;

    /// <inheritdoc/>
    public bool LessEnabled => true;

    /// <summary>已经注册过的模型 / The model this usage was registered into.</summary>
    /// <remarks>
    /// **重复注册必须幂等，换模型必须重建。** 旧实现在同一次注册流程里把符号表再加一遍，于是第二次调用返回
    /// `Failed`——调用方读到的意思是"资源用量进不去模型"，而事实是它早就在里面了。
    ///
    /// **Re-registration must be idempotent and a new model must rebuild.** The old implementation re-added the symbol table on every call, so the second call returned `Failed` —
    /// telling the caller "the usage cannot enter the model" when it was already there.
    /// </remarks>
    private Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64>? _registeredModel;

    /// <summary>数量表 / The quantity table.</summary>
    private IReadOnlyDictionary<StorageResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? _quantityTable;

    /// <summary>超量表 / The over-quantity table.</summary>
    private IReadOnlyDictionary<StorageResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? _overTable;

    /// <summary>不足量表 / The less-quantity table.</summary>
    private IReadOnlyDictionary<StorageResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? _lessTable;

    /// <inheritdoc/>
    public object? Quantity => _quantityTable;

    /// <inheritdoc/>
    public object? OverQuantity => _overTable;

    /// <inheritdoc/>
    public object? LessQuantity => _lessTable;

    /// <inheritdoc/>
    /// <remarks>
    /// 对应基线 `BunchSchedulingStorageResourceUsage.register` 的组合式数量：**初始量 + 供应 − 消耗**。
    /// 与执行资源不同，存储的数量不是一个"已知的起点"而是三段的代数和——供应来自资源的固定供应
    /// （`FixedSupplyIn`），消耗由调用方按时间给出。
    ///
    /// Mirrors the baseline's combined quantity for storage: **initial + supply - consumption**. Unlike an execution resource, storage's quantity is not a known starting point but the
    /// algebra of three pieces — supply comes from the resource's fixed supply (`FixedSupplyIn`) and consumption is given per time by the caller.
    ///
    /// <para>
    /// **天花板：执行器维度的供应没有接。** 基线还有一张 `executorSupply`（三维：策略 × 资源 × 时间），
    /// 由任务编译层的子类填充；C# 侧既没有那个子类，也没有它的消费者，先只按资源的固定供应算。
    /// 升级路径：等任务编译层真的用上存储资源时，把那张表加进来并在此处求和。
    ///
    /// **Ceiling: the executor dimension of supply is not wired.** The baseline also has an `executorSupply` table (three dimensions: policy x resource x time) filled by subclasses in the
    /// task-compilation layer; the C# side has neither that subclass nor a consumer for it, so only the resource's fixed supply is counted. Upgrade path: once the task-compilation layer
    /// really uses storage resources, add that table and sum it here.
    /// </para>
    /// </remarks>
    public Try Register(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "资源使用只能注册到线性元模型 / resource usage can only be registered into a linear meta model"));
        }
        if (TimeSlots.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        if (ReferenceEquals(_registeredModel, linear)) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (_registeredModel is not null) {
            _quantityTable = null;
            _overTable = null;
            _lessTable = null;
        }
        _registeredModel = linear;

        if (_quantityTable is null) {
            var quantities = new Dictionary<StorageResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>();
            foreach (StorageResourceTimeSlot<R, C, V> slot in TimeSlots) {
                // 基线算的是"从头到本槽结束"这一段内的供应，不是本槽自己的长度。
                // The baseline counts supply over the span from the window's start to this slot's end, not over the slot's own length.
                V suppliedValue = slot.Resource.FixedSupplyIn(new TimeRange(_windowStart, slot.Time.End));
                Flt64 supplied = suppliedValue is Flt64 direct ? direct : new Flt64(Convert.ToDouble(suppliedValue));
                Flt64 consumed = _cost is null ? Flt64.Zero : _cost(slot);
                var symbol = new Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                        constant: slot.Resource.SolverInitialQuantity() + supplied - consumed),
                    name: Name + "_quantity_" + slot.Index);
                Result<ValueRange, ErrorCode, Error<ErrorCode>> range = ValueRange.Of(
                    slot.ResourceCapacity.SolverValueRangeLower(),
                    slot.ResourceCapacity.SolverValueRangeUpper());
                if (range is not Ok<ValueRange, ErrorCode, Error<ErrorCode>> ok) {
                    return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                        ((Failed<ValueRange, ErrorCode, Error<ErrorCode>>)range).Error);
                }
                symbol.Range.SetRange(ok.Value);
                quantities[slot] = symbol;
            }
            _quantityTable = quantities;
        }

        Try addedQuantity = linear.Add(_quantityTable.Values);
        if (!addedQuantity.IsOk) {
            return addedQuantity;
        }

        Try slacks = ResourceUsageSlackHelper.BuildSlacks(
            linear, Name, TimeSlots, _quantityTable,
            out IReadOnlyDictionary<StorageResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? over,
            out IReadOnlyDictionary<StorageResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? less,
            OverEnabled, LessEnabled,
            slot => slot.ResourceCapacity.OverEnabled,
            slot => slot.ResourceCapacity.LessEnabled,
            slot => slot.ResourceCapacity.SolverUpperBound(),
            slot => slot.ResourceCapacity.SolverLowerBound(),
            slot => slot.Index.ToString());
        if (!slacks.IsOk) {
            return slacks;
        }
        _overTable ??= over;
        _lessTable ??= less;

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static IReadOnlyList<StorageResourceTimeSlot<R, C, V>> BuildTimeSlots(
        TimeWindow<V> timeWindow, IReadOnlyList<R> resources) {
        var timeSlots = new List<StorageResourceTimeSlot<R, C, V>>();
        foreach (R resource in resources) {
            foreach (C capacity in resource.Capacities) {
                ulong index = 0;
                DateTimeOffset beginTime = capacity.Time.Start > timeWindow.Start ? capacity.Time.Start : timeWindow.Start;
                DateTimeOffset endTime = capacity.Time.End < timeWindow.End ? capacity.Time.End : timeWindow.End;
                while (beginTime < endTime) {
                    TimeSpan thisInterval = endTime - beginTime;
                    if (thisInterval > capacity.Interval) {
                        thisInterval = capacity.Interval;
                    }

                    if (thisInterval > timeWindow.Interval) {
                        thisInterval = timeWindow.Interval;
                    }

                    var time = new TimeRange(beginTime, beginTime + thisInterval);
                    timeSlots.Add(new StorageResourceTimeSlot<R, C, V>(timeWindow, time, resource, capacity, index));
                    beginTime += thisInterval;
                    index++;
                }
            }
        }
        return timeSlots;
    }
}
