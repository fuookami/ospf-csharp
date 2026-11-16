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
/// 执行器成本最小化 / Executor cost minimization
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class ExecutorCostMinimization<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<E> _executors;
    private readonly ICompilation _compilation;
    private readonly Func<E, Flt64?>? _coefficient;
    private readonly string _name;

    /// <summary>
    /// 执行器成本最小化构造 / Executor cost minimization constructor
    /// </summary>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="coefficient">执行器成本系数提取器 / Extractor for executor cost coefficient</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public ExecutorCostMinimization(
        IReadOnlyList<E> executors,
        ICompilation compilation,
        Func<E, Flt64?>? coefficient = null,
        string name = "executor_cost_minimization") {
        _executors = executors;
        _compilation = compilation;
        _coefficient = coefficient;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// **只有给了系数提取器**才最小化 `Σ_执行器 penalty(执行器) × executor_compilation[执行器]`，目标名 `executor`；
    /// 没给提取器就什么都不做（`Ok`）。这不是"漏接线"——"用不用执行器要花钱"这件事本身就是可选的，没有成本就没有目标。
    ///
    /// 提取失败时无穷大；`Flt64` 没有无穷，所以这里失败。
    ///
    /// This minimises `Σ_executor penalty(executor) × executor_compilation[executor]` (objective name `executor`) **only when an extractor is given**; without one it does
    /// nothing and returns `Ok`. That is not missing wiring — "using an executor costs money" is itself optional, and no cost means no objective. An extraction failure uses
    /// infinity, and `Flt64` has none, so it fails here.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (_coefficient is null) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "执行器成本只能施加到线性元模型 / the executor cost can only be applied to a linear meta model"));
        }
        if (_compilation is not IExecutorCompilation<E> compilation) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "编译结果没有执行器编译表 / the compilation does not expose an executor-compilation table"));
        }

        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        foreach (E executor in _executors) {
            if (!compilation.ExecutorCompilation.TryGetValue(executor, out object? symbol)
                || symbol is not Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64> executorSymbol) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "执行器编译表缺项：" + executor.Id
                    + " / the executor-compilation table is missing an entry: " + executor.Id));
            }
            Flt64? penalty = _coefficient(executor);
            if (penalty is not { } coefficient) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "执行器成本提取失败：" + executor.Id
                    + " / the executor cost could not be extracted: " + executor.Id));
            }
            foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in executorSymbol.Polynomial.Monomials) {
                monomials.Add(new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                    coefficient * monomial.Coefficient, monomial.Symbol));
            }
        }

        return linear.AddObject(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(monomials.ToArray(), Flt64.Zero),
            "executor",
            displayName: null);
    }
}
