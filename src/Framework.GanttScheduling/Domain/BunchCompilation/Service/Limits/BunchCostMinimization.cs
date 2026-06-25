#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Service.Limits;
/// <summary>
/// 任务束成本最小化 / Bunch cost minimization
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class BunchCostMinimization<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly BunchCompilation<AbstractTaskBunch<T, E, A>, T, E, A> _compilation;
    private readonly string _name;

    /// <summary>
    /// 任务束成本最小化构造 / Bunch cost minimization constructor
    /// </summary>
    /// <param name="compilation">任务束编译结果 / Bunch compilation result</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public BunchCostMinimization(
        BunchCompilation<AbstractTaskBunch<T, E, A>, T, E, A> compilation,
        string name = "bunch_cost_minimization") {
        _compilation = compilation;
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) =>
        // In full implementation, would add minimize objective to model
        Results.Ok<Success>(Results.SuccessInstance);
}
