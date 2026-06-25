#nullable enable

using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
/// <summary>
/// 甘特调度影子价格参数接口 / Gantt scheduling shadow price arguments interface.
/// </summary>
/// <typeparam name="E">执行者类型 / The executor type.</typeparam>
/// <typeparam name="A">分配策略类型 / The assignment policy type.</typeparam>
public interface IGanttSchedulingShadowPriceArguments<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>执行者 / The executor.</summary>
    E Executor { get; }

    /// <summary>任务 / The task.</summary>
    IAbstractTask<E, A>? Task => null;
}

/// <summary>
/// 任务级甘特调度影子价格参数 / Task-level Gantt scheduling shadow price arguments.
/// </summary>
public record TaskGanttSchedulingShadowPriceArguments<E, A>(
    E Executor,
    IAbstractTask<E, A>? Task = null
) : IGanttSchedulingShadowPriceArguments<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E>;

/// <summary>
/// 任务束级甘特调度影子价格参数 / Bunch-level Gantt scheduling shadow price arguments.
/// </summary>
public record BunchGanttSchedulingShadowPriceArguments<E, A>(
    E Executor,
    IAbstractTask<E, A>? Task = null,
    IAbstractTask<E, A>? PrevTask = null
) : IGanttSchedulingShadowPriceArguments<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E>;

/// <summary>
/// 甘特调度影子价格映射 / Gantt scheduling shadow price map.
/// </summary>
public class GanttSchedulingShadowPriceMap<E, A>
    : AbstractShadowPriceMap<IGanttSchedulingShadowPriceArguments<E, A>,
        GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E>;

/// <summary>
/// 甘特调度影子价格提取器类型 / Gantt scheduling shadow price extractor type.
/// </summary>
public delegate Flt64 GanttSchedulingShadowPriceExtractor<E, A>(
    GanttSchedulingShadowPriceMap<E, A> map,
    IGanttSchedulingShadowPriceArguments<E, A> args)
    where E : Executor
    where A : IAssignmentPolicy<E>;
