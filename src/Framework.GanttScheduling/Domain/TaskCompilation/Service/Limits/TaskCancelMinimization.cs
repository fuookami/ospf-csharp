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
using System;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 任务取消最小化 / Task cancel minimization
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskCancelMinimization<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly ICompilation _compilation;
    private readonly Func<T, Flt64?> _coefficient;
    private readonly string _name;

    /// <summary>
    /// 任务取消最小化构造 / Task cancel minimization constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="coefficient">取消成本系数提取器；`null` 表示每任务 1 / Extractor for the cancellation cost; null means 1 per task</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskCancelMinimization(
        IReadOnlyList<T> tasks,
        ICompilation compilation,
        Func<T, Flt64?>? coefficient = null,
        string name = "task_cancel_minimization") {
        _tasks = tasks;
        _compilation = compilation;
        // 基线的默认系数是 `Flt64.one`：没有系数就是"每个取消一视同仁"。
        // The baseline's default coefficient is `Flt64.one`: without one, every cancellation counts the same.
        _coefficient = coefficient ?? (_ => Flt64.One);
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 基线：`taskCancelEnabled` 时最小化 `Σ coefficient(task) * y[task]`，只对 `cancelEnabled` 的任务生效。
    ///
    /// 三处与基线不同，都是"无法表达"而不是"换个说法"：
    /// <list type="number">
    /// <item>取消未启用时基线返回 `Ok`——**这里保留**：那条目标本就不存在，无事可做不是"假装做了"；</item>
    /// <item>系数提取失败时基线用无穷大；`Flt64` 没有无穷（的同一处限制），所以这里失败；</item>
    /// <item>编译没实现 `ICompilationColumns` 时失败，而不是加一个不存在的目标。</item>
    /// </list>
    ///
    /// The baseline minimises `Σ coefficient(task) * y[task]` when `taskCancelEnabled`, only over tasks with `cancelEnabled`. Three departures, all "cannot express" rather than
    /// "rephrase": cancellation disabled still returns `Ok` (**kept** — the objective does not exist, doing nothing is not pretending); an extraction failure uses infinity in the
    /// baseline and `Flt64` has none (the same limit), so it fails here; and a compilation that does not implement `ICompilationColumns` fails instead of adding a nonexistent
    /// objective.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        // 取消未启用 → 没有这项成本，不是"没接线"。
        // Cancellation disabled → this cost does not exist, it is not "unwired".
        if (!_compilation.TaskCancelEnabled) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务取消最小化只能施加到线性元模型 / task cancel minimisation can only be applied to a linear meta model"));
        }
        if (_compilation is not ICompilationColumns<E, A> columns) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "该编译不提供取消列，无法最小化取消成本 / this compilation exposes no cancellation columns"));
        }

        var monomials = new List<LinearMonomial<Flt64>>();
        foreach (T task in _tasks) {
            // 基线只对允许取消的任务计成本：不允许取消的任务 `y == 0`，加进去也只是个恒零项。
            // The baseline charges only tasks that may be cancelled: a task that cannot cancel has `y == 0`, so adding it would be a permanently-zero term.
            if (!task.CancelEnabled) {
                continue;
            }
            if (!columns.Cancellation.TryGetValue(task, out object? cancellation)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务没有取消列，不能计算取消成本：" + task.Id
                    + " / the task has no cancellation column: " + task.Id));
            }
            if (_coefficient(task) is not { } weight) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "取消成本系数提取失败，该任务无法定价：" + task.Id
                    + " / cancellation cost extraction failed: " + task.Id));
            }
            if (weight.ToDouble() == 0.0) {
                continue;
            }
            monomials.Add(new LinearMonomial<Flt64>(weight, (IVariableItem)cancellation));
        }

        // 一个非零项都没有 → 目标恒为 0，加进去只会让模型多一个空子目标。
        // No non-zero term at all → the objective is identically zero; adding it would only give the model an empty sub-objective.
        if (monomials.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(monomials, Flt64.Zero),
            "task cancel",
            displayName: null);
    }

    /// <inheritdoc/>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => null;

    /// <inheritdoc/>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) => Results.Ok<Success>(Results.SuccessInstance);
}
