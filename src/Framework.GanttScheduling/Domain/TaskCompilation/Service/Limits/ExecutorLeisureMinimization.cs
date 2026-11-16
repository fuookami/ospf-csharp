#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
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
    /// <remarks>
    /// 基线：`withExecutorLeisure` 且**给了系数**时最小化 `Σ coefficient(e) * z[e]`。
    ///
    /// 注意基线的两层保护，这里都照抄：
    /// <list type="number">
    /// <item>空闲未启用 → 直接 `Ok`（`z` 根本不存在，无事可做不是假施加）；</item>
    /// <item>**没给系数 → 也直接 `Ok`**（`coefficient?.let { … }`）。这一条容易看漏：它意味着"空闲最小化默认不生效"，
    /// 而不是"默认按 1 计成本"。</item>
    /// </list>
    /// 与基线不同的只有一处：系数提取失败时基线用无穷大，`Flt64` 没有无穷，所以这里失败。
    ///
    /// The baseline minimises `Σ coefficient(e) * z[e]` when `withExecutorLeisure` **and a coefficient is given**. Both of its guards are copied here: leisure disabled → `Ok`
    /// (`z` does not exist; doing nothing is not a fake application) and **no coefficient → also `Ok`** (`coefficient?.let { … }`). The second is easy to miss: it means "leisure
    /// minimisation is off by default", not "default to a cost of 1". The only departure is an extraction failure: the baseline uses infinity, `Flt64` has none, so it fails.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (!_compilation.WithExecutorLeisure) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        // 没给系数 → 基线什么都不做。这不是"没接线"，是"调用方没要求"。
        // No coefficient → the baseline does nothing. That is not "unwired", it is "the caller did not ask".
        if (_coefficient is null) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "执行器空闲最小化只能施加到线性元模型 / executor leisure minimisation can only be applied to a linear meta model"));
        }
        if (_compilation is not ICompilationColumns<E, A> columns) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "该编译不提供空闲列，无法最小化空闲成本 / this compilation exposes no leisure columns"));
        }

        var monomials = new List<LinearMonomial<Flt64>>();
        foreach (E executor in _executors) {
            if (!columns.Leisure.TryGetValue(executor, out object? leisure)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "执行器没有空闲列，不能计算空闲成本：" + executor.Id
                    + " / the executor has no leisure column: " + executor.Id));
            }
            if (_coefficient(executor) is not { } penalty) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "空闲成本系数提取失败，该执行器无法定价：" + executor.Id
                    + " / leisure cost extraction failed: " + executor.Id));
            }
            if (penalty.ToDouble() == 0.0) {
                continue;
            }
            monomials.Add(new LinearMonomial<Flt64>(penalty, (IVariableItem)leisure));
        }

        if (monomials.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(monomials, Flt64.Zero),
            "executor leisure",
            displayName: null);
    }
}
