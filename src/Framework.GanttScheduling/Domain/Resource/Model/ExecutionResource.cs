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
/// 执行资源 / Execution resource.
/// </summary>
/// <typeparam name="C">资源容量类型 / Resource capacity type.</typeparam>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public abstract class ExecutionResource<C, V> : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 计算任务在指定时间范围内的资源消耗量 / Calculate resource consumption of a task in the given time range.
    /// </summary>
    public abstract V UsedBy<E, A>(IAbstractTask<E, A> task, TimeRange time)
        where E : Executor
        where A : IAssignmentPolicy<E>;

    /// <inheritdoc/>
    public override V UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time) {
        var counter = default(V);
        foreach (T task in bunch.Tasks) {
            counter = (dynamic)counter + UsedBy(task, time);
        }
        return counter;
    }
}

/// <summary>
/// 执行资源时间槽 / Execution resource time slot.
/// </summary>
/// <typeparam name="R">执行资源类型 / Execution resource type.</typeparam>
/// <typeparam name="C">资源容量类型 / Resource capacity type.</typeparam>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public class ExecutionResourceTimeSlot<R, C, V> : IResourceTimeSlot<R, C, V>
    where R : ExecutionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    public ExecutionResourceTimeSlot(ITimeSlot origin, R resource, C resourceCapacity, ulong indexInRule) {
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

    /// <summary>
    /// 计算任务在此时槽的资源消耗量 / Calculate resource consumption of a task at this time slot.
    /// </summary>
    public V UsedBy<E, A>(IAbstractTask<E, A> task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => Resource.UsedBy(task, Time);

    /// <inheritdoc/>
    public V RelationTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => task is not null ? UsedBy(task) : default(V);

    /// <inheritdoc/>
    public bool RelatedTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => !RelationTo(prevTask, task).Equals(default(V));

    /// <inheritdoc/>
    public override string ToString() => $"{Resource}_{ResourceCapacity}_{IndexInRule}";
}

/// <summary>
/// 任务束调度执行资源使用 / Bunch scheduling execution resource usage.
/// </summary>
/// <typeparam name="R">执行资源类型 / Execution resource type.</typeparam>
/// <typeparam name="C">资源容量类型 / Resource capacity type.</typeparam>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public class BunchSchedulingExecutionResourceUsage<R, C, V>
    : IResourceUsage<ExecutionResourceTimeSlot<R, C, V>, R, C, V>
    where R : ExecutionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window.</param>
    /// <param name="resources">资源列表 / List of resources.</param>
    /// <param name="name">名称 / Name.</param>
    public BunchSchedulingExecutionResourceUsage(
        TimeWindow<V> timeWindow,
        IReadOnlyList<R> resources,
        string name) {
        Name = name;
        TimeSlots = BuildTimeSlots(timeWindow, resources);
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ExecutionResourceTimeSlot<R, C, V>> TimeSlots { get; }

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
    private IReadOnlyDictionary<ExecutionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? _quantityTable;

    /// <summary>超量表 / The over-quantity table.</summary>
    private IReadOnlyDictionary<ExecutionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? _overTable;

    /// <summary>不足量表 / The less-quantity table.</summary>
    private IReadOnlyDictionary<ExecutionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? _lessTable;

    /// <inheritdoc/>
    public object? Quantity => _quantityTable;

    /// <inheritdoc/>
    public object? OverQuantity => _overTable;

    /// <inheritdoc/>
    public object? LessQuantity => _lessTable;

    /// <inheritdoc/>
    /// <remarks>
    /// 对应基线 `BunchSchedulingExecutionResourceUsage.register`：先给每个时间槽建 `quantity` 符号（常数是资源的初始数量，
    /// 范围是 `[下界 − 不足, 上界 + 超量]`），再按槽建超量 \/ 不足两组松弛。
    ///
    /// **范围必须设**：不设范围时 `ExpressionRange` 是全域，松弛可以取到任意大，超量约束就失去了意义——
    /// 模型仍然可解，只是那个"超出多少"永远算成零。
    ///
    /// 超量松弛：`x − 上界` 的正部（`withNegative: false`, `withPositive: true`）；
    /// 不足松弛：`x − 下界` 的负部（`withNegative: true`, `withPositive: false`）。两者都**不建约束**——
    /// 约束由 `ResourceCapacityConstraint` 加，松弛只负责度量。
    ///
    /// Mirrors the baseline's `BunchSchedulingExecutionResourceUsage.register`: first build a `quantity` symbol per time slot (constant = the resource's initial quantity, range
    /// `[lower - less, upper + over]`), then build the over and less slacks per slot. **The range must be set**: an unset range is the full domain, so a slack could take any value and the
    /// over-quantity constraint would lose its meaning — the model stays solvable, only "how much over" always computes to zero. The over slack is the positive part of `x - upper`
    /// (`withNegative: false`, `withPositive: true`); the less slack is the negative part of `x - lower` (`withNegative: true`, `withPositive: false`). Neither **builds a constraint** — the
    /// constraint is added by `ResourceCapacityConstraint`, the slack only measures.
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
            var quantities = new Dictionary<ExecutionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>();
            foreach (ExecutionResourceTimeSlot<R, C, V> slot in TimeSlots) {
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

        // **数量表要先加进模型**：松弛的 `x` 就是表里的符号，符号不进模型，松弛就无处可加。
        // **The quantity table must enter the model first**: the slacks' `x` are those very symbols, and a symbol outside the model has nowhere to be added.
        Try addedQuantity = linear.Add(_quantityTable.Values);
        if (!addedQuantity.IsOk) {
            return addedQuantity;
        }

        Try slacks = ResourceUsageSlackHelper.BuildSlacks(
            linear, Name, TimeSlots, _quantityTable,
            out IReadOnlyDictionary<ExecutionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? over,
            out IReadOnlyDictionary<ExecutionResourceTimeSlot<R, C, V>, ILinearIntermediateSymbol<Flt64>>? less,
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

    private static IReadOnlyList<ExecutionResourceTimeSlot<R, C, V>> BuildTimeSlots(
        TimeWindow<V> timeWindow, IReadOnlyList<R> resources) {
        var timeSlots = new List<ExecutionResourceTimeSlot<R, C, V>>();
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
                    timeSlots.Add(new ExecutionResourceTimeSlot<R, C, V>(
                        time, resource, capacity, index));
                    beginTime += thisInterval;
                    index++;
                }
            }
        }
        return timeSlots;
    }
}
