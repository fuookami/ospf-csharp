#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// 抽象资源容量接口 / Abstract resource capacity interface.
/// </summary>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public interface IAbstractResourceCapacity<V> where V : struct, IRealNumber<V> {
    /// <summary>将 V 转换为 Flt64 的辅助方法 / Helper method to convert V to Flt64.</summary>
    private static Flt64 ToFlt64(V value) {
        if (value is Flt64 f) {
            return f;
        }

        return new Flt64(Convert.ToDouble(value));
    }
    /// <summary>时间范围 / Time range.</summary>
    TimeRange Time { get; }

    /// <summary>数量范围下界 / Quantity range lower bound.</summary>
    V LowerBound { get; }

    /// <summary>数量范围上界 / Quantity range upper bound.</summary>
    V UpperBound { get; }

    /// <summary>不足数量 / Less quantity value.</summary>
    V? LessQuantity => null;

    /// <summary>超量数量 / Over quantity value.</summary>
    V? OverQuantity => null;

    /// <summary>时间间隔 / Time interval.</summary>
    TimeSpan Interval => TimeSpan.MaxValue;

    /// <summary>名称 / Name.</summary>
    string? Name => null;

    /// <summary>是否启用不足 / Whether less quantity is enabled.</summary>
    bool LessEnabled => LessQuantity.HasValue;

    /// <summary>是否启用超量 / Whether over quantity is enabled.</summary>
    bool OverEnabled => OverQuantity.HasValue;

    /// <summary>求解器下界 / Solver lower bound.</summary>
    Flt64 SolverLowerBound() => ToFlt64(LowerBound);

    /// <summary>求解器上界 / Solver upper bound.</summary>
    Flt64 SolverUpperBound() => ToFlt64(UpperBound);

    /// <summary>求解器不足量 / Solver less quantity.</summary>
    Flt64 SolverLessQuantity() => LessQuantity.HasValue ? ToFlt64(LessQuantity.Value) : Flt64.Zero;

    /// <summary>求解器超限量 / Solver over quantity.</summary>
    Flt64 SolverOverQuantity() => OverQuantity.HasValue ? ToFlt64(OverQuantity.Value) : Flt64.Zero;

    /// <summary>求解器数值范围下界 / Solver value range lower bound.</summary>
    Flt64 SolverValueRangeLower() => SolverLowerBound() - SolverLessQuantity();

    /// <summary>求解器数值范围上界 / Solver value range upper bound.</summary>
    Flt64 SolverValueRangeUpper() => SolverUpperBound() + SolverOverQuantity();
}

/// <summary>
/// 资源容量 / Resource capacity.
/// </summary>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public record ResourceCapacity<V>(
    TimeRange Time,
    V LowerBound,
    V UpperBound,
    V? LessQuantity = null,
    V? OverQuantity = null,
    TimeSpan? Interval = null,
    string? Name = null
) : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <inheritdoc/>
    TimeSpan IAbstractResourceCapacity<V>.Interval => Interval ?? TimeSpan.MaxValue;

    /// <inheritdoc/>
    public override string ToString() => Name ?? $"{LowerBound}_{UpperBound}_{(Interval ?? TimeSpan.MaxValue)}";
}

/// <summary>
/// 资源抽象类 / Resource abstract class.
/// </summary>
/// <typeparam name="C">资源容量类型 / Resource capacity type.</typeparam>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public abstract class Resource<C, V> : ManualIndexed
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>资源ID / Resource ID.</summary>
    public abstract string Id { get; }

    /// <summary>资源名称 / Resource name.</summary>
    public abstract string Name { get; }

    /// <summary>容量列表 / List of capacities.</summary>
    public abstract IReadOnlyList<C> Capacities { get; }

    /// <summary>初始数量裸值 / Initial quantity raw value.</summary>
    public abstract V InitialQuantityValue { get; }

    /// <summary>
    /// 计算任务束在指定时间范围内的资源使用量 / Calculate resource usage of a task bunch in the given time range.
    /// </summary>
    public abstract V UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time)
        where T : IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E>;

    /// <summary>初始数量的 solver 值 / Solver initial quantity.</summary>
    public Flt64 SolverInitialQuantity() => new Flt64(Convert.ToDouble(InitialQuantityValue));
}

/// <summary>
/// 资源时间槽接口 / Resource time slot interface.
/// </summary>
/// <typeparam name="R">资源类型 / Resource type.</typeparam>
/// <typeparam name="C">资源容量类型 / Resource capacity type.</typeparam>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public interface IResourceTimeSlot<R, C, V> : ITimeSlot, IIndexed
    where R : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>原始时间槽 / Origin time slot.</summary>
    ITimeSlot Origin { get; }

    /// <summary>资源 / Resource.</summary>
    R Resource { get; }

    /// <summary>资源容量 / Resource capacity.</summary>
    C ResourceCapacity { get; }

    /// <summary>规则内索引 / Index in rule.</summary>
    ulong IndexInRule { get; }

    /// <summary>时间范围 / Time range.</summary>
    new TimeRange Time => Origin.Time;

    /// <summary>
    /// 计算任务与此时槽的关联量 / Calculate the relation quantity between tasks and this time slot.
    /// </summary>
    V RelationTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => Resource.InitialQuantityValue;

    /// <summary>
    /// 判断任务是否与此时槽相关 / Check whether tasks are related to this time slot.
    /// </summary>
    bool RelatedTo<E, A>(IAbstractTask<E, A>? prevTask, IAbstractTask<E, A>? task)
        where E : Executor
        where A : IAssignmentPolicy<E>
        => !RelationTo(prevTask, task).Equals(default(V));
}

/// <summary>
/// 资源使用接口 / Resource usage interface.
/// </summary>
/// <typeparam name="S">资源时间槽类型 / Resource time slot type.</typeparam>
/// <typeparam name="R">资源类型 / Resource type.</typeparam>
/// <typeparam name="C">资源容量类型 / Resource capacity type.</typeparam>
/// <typeparam name="V">值类型 / Value type.</typeparam>
public interface IResourceUsage<S, R, C, V>
    where S : IResourceTimeSlot<R, C, V>
    where R : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>名称 / Name.</summary>
    string Name { get; }

    /// <summary>时间槽列表 / List of time slots.</summary>
    IReadOnlyList<S> TimeSlots { get; }

    /// <summary>是否启用超量 / Whether over quantity is enabled.</summary>
    bool OverEnabled { get; }

    /// <summary>是否启用不足 / Whether less quantity is enabled.</summary>
    bool LessEnabled { get; }

    /// <summary>
    /// 注册松弛变量到元模型 / Register slack variables to the meta model.
    /// </summary>
    Try Register(object model);
}
