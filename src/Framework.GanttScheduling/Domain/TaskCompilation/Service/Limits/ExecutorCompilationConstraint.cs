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
/// 执行器编译影子价格键 / Executor compilation shadow price key
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
public sealed class ExecutorCompilationShadowPriceKey<E> : ShadowPriceKey
    where E : Executor {
    /// <summary>
    /// 构造 / Constructor
    /// </summary>
    /// <param name="executor">执行器 / Executor</param>
    public ExecutorCompilationShadowPriceKey(E executor) : base(typeof(ExecutorCompilationShadowPriceKey<E>)) {
        Executor = executor;
    }

    /// <summary>执行器 / Executor</summary>
    public E Executor { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ExecutorCompilationShadowPriceKey<E> other && EqualityComparer<E>.Default.Equals(Executor, other.Executor);

    /// <inheritdoc/>
    public override int GetHashCode() => Executor?.GetHashCode() ?? 0;
}

/// <summary>
/// 执行器编译约束 / Executor compilation constraint
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class ExecutorCompilationConstraint<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<E> _executors;
    private readonly ICompilation _compilation;
    private readonly string _name;

    /// <summary>
    /// 执行器编译约束构造 / Executor compilation constraint constructor
    /// </summary>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public ExecutorCompilationConstraint(
        IReadOnlyList<E> executors,
        ICompilation compilation,
        string name = "executor_compilation") {
        _executors = executors;
        _compilation = compilation;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <inheritdoc/>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => (map, args) => {
            if (args.Task == null) {
                var key = new ExecutorCompilationShadowPriceKey<E>(args.Executor);
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
