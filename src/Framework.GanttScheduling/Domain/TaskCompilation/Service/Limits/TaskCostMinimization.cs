#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 任务成本最小化 / Task cost minimization.
/// 最小化任务编译的总成本。
/// Minimizes the total cost of task compilation.
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskCostMinimization<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly ICompilation _compilation;
    private readonly string _name;

    /// <summary>
    /// 任务成本最小化构造 / Task cost minimization constructor
    /// </summary>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskCostMinimization(
        ICompilation compilation,
        string name = "task_cost_minimization") {
        _compilation = compilation;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// `model.minimize(symbol = compilation.taskCost, name = "task cost")`。
    ///
    /// 普通编译**没有**代价项（它的代价写在列系数里），此时必须失败而不是空操作：
    /// 返回 `Ok` 等于宣称"成本已最小化"，而目标里根本没有这一项——解照样出来，只是完全不看成本。
    ///
 /// This implementation is `model.minimize(symbol = compilation.taskCost, name = "task cost")`.
    ///
    /// A plain compilation **has no** cost term (its cost sits in the column coefficients), and that must fail rather than do nothing: returning `Ok` claims "the cost was minimised"
    /// while the objective has no such term — a solution still comes out, simply ignoring cost entirely.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务成本最小化只能施加到线性元模型 / task cost minimisation can only be applied to a linear meta model"));
        }
        if (_compilation is not ITaskCost { TaskCost: LinearExpressionSymbol cost }) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "该编译没有代价项，不能最小化成本 / this compilation carries no cost term, so its cost cannot be minimised"));
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(cost.Polynomial.Monomials, cost.Polynomial.Constant),
            "task cost",
            displayName: null);
    }
}
