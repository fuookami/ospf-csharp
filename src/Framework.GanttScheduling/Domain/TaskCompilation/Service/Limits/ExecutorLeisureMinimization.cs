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
/// 执行器空闲最小化 / Executor leisure minimization
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class ExecutorLeisureMinimization<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<E> _executors;
    private readonly ICompilation _compilation;
    private readonly Func<E, Flt64?>? _coefficient;
    private readonly string _name;

    /// <summary>
    /// 执行器空闲最小化构造 / Executor leisure minimization constructor
    /// </summary>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="coefficient">执行器空闲系数提取器 / Extractor for executor leisure coefficient</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public ExecutorLeisureMinimization(
        IReadOnlyList<E> executors,
        ICompilation compilation,
        Func<E, Flt64?>? coefficient = null,
        string name = "executor_leisure_minimization") {
        _executors = executors;
        _compilation = compilation;
        _coefficient = coefficient;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
