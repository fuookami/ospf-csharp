#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
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
    private readonly ITaskCost _compilation;
    private readonly string _name;

    /// <summary>
    /// 任务束成本最小化构造 / Bunch cost minimization constructor
    /// </summary>
    /// <param name="compilation">任务束编译结果 / Bunch compilation result</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    /// <remarks>
    /// 参数类型是 **`ITaskCost`** 而不是具体的 `BunchCompilation&lt;…&gt;`：基线的 `BunchCompilation&lt;*, V, T, E, A&gt;` 那个星号在 C# 里做不到
    /// （泛型不变），于是 `BunchCompilation&lt;FlightTaskBunch, …&gt;` 根本传不进来——Demo4 因此把这条管线**跳过**了。
    /// 目标真正需要的只是"成本符号"，那是 `ITaskCost` 的事。
    ///
    /// The parameter is **`ITaskCost`**, not the concrete `BunchCompilation&lt;…&gt;`: the baseline's `BunchCompilation&lt;*, V, T, E, A&gt;` star cannot be expressed in C#
    /// (generics are invariant), so `BunchCompilation&lt;FlightTaskBunch, …&gt;` could not be passed at all — which is why Demo4 **skipped** this pipeline. The objective only needs
    /// "the cost symbol", and that is `ITaskCost`'s job.
    /// </remarks>
    public BunchCostMinimization(
        ITaskCost compilation,
        string name = "bunch_cost_minimization") {
        _compilation = compilation;
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 把 `bunch_cost`（`Σ 束成本 × x`）作为一个 `Minimum` 子目标加进模型。
    ///
    /// **空成本不加目标**：一列都没有时 `bunch_cost` 恒为零，加一个空子目标只会让模型多一个恒零项，读起来却像"有目标"。
    /// **未注册就失败**：`bunch_cost` 由编译结果的 `Register` 建出来，取不到说明流程顺序错了，而不是"成本是零"。
    ///
    /// Adds `bunch_cost` (`Σ bunch cost × x`) as a `Minimum` sub-objective.
    /// **No terms, no objective**: with no columns `bunch_cost` is identically zero, and an empty sub-objective only gives the model a constant-zero term that reads like "there
    /// is an objective". **Unregistered fails**: `bunch_cost` comes from the compilation's `Register`, and not finding it means the flow is out of order, not that the cost is zero.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务束成本最小化只能施加到线性元模型 / bunch cost minimisation can only be applied to a linear meta model"));
        }
        if (_compilation.TaskCost is not ILinearIntermediateSymbol<Flt64> cost) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "任务束编译尚未注册，成本符号不存在 / the bunch compilation is not registered yet, so the cost symbol does not exist"));
        }

        LinearPolynomial<Flt64> polynomial = cost.Polynomial;
        if (polynomial.Monomials.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(polynomial.Monomials, polynomial.Constant),
            "bunch cost",
            displayName: null);
    }
}
