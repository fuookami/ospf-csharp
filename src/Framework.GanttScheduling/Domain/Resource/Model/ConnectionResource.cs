#nullable enable

using Fuookami.Ospf.Core.Symbol;
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
/// 连接资源 / Connection resource.
/// </summary>
public abstract class ConnectionResource<C, V> : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 计算任务间连接在指定时间范围内的资源消耗量 / Calculate resource consumption of a task connection in the given time range.
    /// </summary>
    public abstract V UsedBy<T, E, A>(T? prevTask, T? task, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E>;

    /// <inheritdoc/>
    public override V UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time) {
        var counter = default(V);
        for (int i = 0; i < bunch.Tasks.Count; i++) {
            V usage = i == 0
                ? UsedBy<T, E, A>(bunch.LastTask, bunch.Tasks[i], time)
                : UsedBy<T, E, A>(bunch.Tasks[i - 1], bunch.Tasks[i], time);
            counter = (dynamic)counter + usage;
        }
        return counter;
    }
}

/// <summary>
/// 连接资源时间槽 / Connection resource time slot.
/// </summary>
public class ConnectionResourceTimeSlot<R, C, V> : IResourceTimeSlot<R, C, V>
    where R : ConnectionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    public ConnectionResourceTimeSlot(ITimeSlot origin, R resource, C resourceCapacity, ulong indexInRule) {
        Origin = origin;
        Resource = resource;
        ResourceCapacity = resourceCapacity;
        IndexInRule = indexInRule;
        Index = AutoIndexed.AutoIndexedImpl.NextIndex(GetType());
    }

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
        => default(V);

    /// <inheritdoc/>
    public bool RelatedTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => !RelationTo(prevTask, task).Equals(default(V));

    /// <inheritdoc/>
    public override string ToString() => $"{Resource}_{ResourceCapacity}_{IndexInRule}";
}

/// <summary>
/// 任务束调度连接资源使用 / Bunch scheduling connection resource usage.
/// </summary>
public class BunchSchedulingConnectionResourceUsage<R, C, V>
    : IResourceUsage<ConnectionResourceTimeSlot<R, C, V>, R, C, V>
    where R : ConnectionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    public BunchSchedulingConnectionResourceUsage(
        TimeWindow<V> timeWindow,
        IReadOnlyList<R> resources,
        string name) {
        Name = name;
        TimeSlots = BuildTimeSlots(timeWindow, resources);
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ConnectionResourceTimeSlot<R, C, V>> TimeSlots { get; }

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
    private IReadOnlyDictionary<ConnectionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? _quantityTable;

    /// <summary>超量表 / The over-quantity table.</summary>
    private IReadOnlyDictionary<ConnectionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? _overTable;

    /// <summary>不足量表 / The less-quantity table.</summary>
    private IReadOnlyDictionary<ConnectionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? _lessTable;

    /// <inheritdoc/>
    public object? Quantity => _quantityTable;

    /// <inheritdoc/>
    public object? OverQuantity => _overTable;

    /// <inheritdoc/>
    public object? LessQuantity => _lessTable;

    /// <inheritdoc/>
    /// <remarks>
    /// 对应基线 `BunchSchedulingConnectionResourceUsage.register`：每个时间槽的 `quantity` 是一个以资源初始数量为常数的符号，范围取
    /// `[下界 − 不足, 上界 + 超量]`；超量 \/ 不足两张表交给共用的 `ResourceUsageSlackHelper`。
    ///
    /// 与执行资源的形状相同、与存储的不同（存储是三段代数和）。**连接资源在 `invoke` 时另按"任务与槽的关联量"挂项**，
    /// 那是装配流水线的事，不属于 `register`。
    ///
    /// Each time slot's `quantity` is a symbol constant at the resource's initial quantity, ranged over
    /// `[lower - less, upper + over]`; the over and less tables go through the shared `ResourceUsageSlackHelper`. The shape matches the execution resource and differs from storage
    /// (which is a three-part algebra). **The code's connection resource attaches terms by "the task-slot relation quantity" at `invoke` time**, which belongs to the assembly pipeline
    /// rather than to `register`.
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
            var quantities = new Dictionary<ConnectionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>();
            foreach (ConnectionResourceTimeSlot<R, C, V> slot in TimeSlots) {
                var symbol = new Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                        constant: slot.Resource.SolverInitialQuantity()),
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
            out IReadOnlyDictionary<ConnectionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? over,
            out IReadOnlyDictionary<ConnectionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? less,
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

    private static IReadOnlyList<ConnectionResourceTimeSlot<R, C, V>> BuildTimeSlots(
        TimeWindow<V> timeWindow, IReadOnlyList<R> resources) {
        var timeSlots = new List<ConnectionResourceTimeSlot<R, C, V>>();
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
                    timeSlots.Add(new ConnectionResourceTimeSlot<R, C, V>(time, resource, capacity, index));
                    beginTime += thisInterval;
                    index++;
                }
            }
        }
        return timeSlots;
    }
}
