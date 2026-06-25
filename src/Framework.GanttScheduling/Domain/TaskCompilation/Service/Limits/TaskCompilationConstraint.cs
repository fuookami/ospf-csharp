#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 任务编译影子价格键 / Task compilation shadow price key
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public sealed class TaskCompilationShadowPriceKey<E, A> : ShadowPriceKey
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>构造 / Constructor</summary>
    /// <param name="task">任务 / Task</param>
    public TaskCompilationShadowPriceKey(IAbstractTask<E, A> task) : base(typeof(TaskCompilationShadowPriceKey<E, A>)) {
        Task = task;
    }

    /// <summary>任务 / Task</summary>
    public IAbstractTask<E, A> Task { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is TaskCompilationShadowPriceKey<E, A> other
        && EqualityComparer<IAbstractTask<E, A>>.Default.Equals(Task, other.Task);

    /// <inheritdoc/>
    public override int GetHashCode() => Task?.GetHashCode() ?? 0;
}

/// <summary>
/// 任务编译约束 / Task compilation constraint
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskCompilationConstraint<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<IAbstractTask<E, A>> _tasks;
    private readonly ICompilation _compilation;
    private readonly string _name;

    /// <summary>
    /// 任务编译约束构造 / Task compilation constraint constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskCompilationConstraint(
        IReadOnlyList<IAbstractTask<E, A>> tasks,
        ICompilation compilation,
        string name = "task_compilation") {
        _tasks = tasks;
        _compilation = compilation;
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <inheritdoc/>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => (map, args) => {
            if (args.Task != null) {
                var key = new TaskCompilationShadowPriceKey<E, A>(args.Task);
                return map.Map.TryGetValue(key, out ShadowPrice? sp) ? sp.Price : Flt64.Zero;
            }
            return Flt64.Zero;
        };

    /// <inheritdoc/>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) => Results.Ok<Success>(Results.SuccessInstance);
}
