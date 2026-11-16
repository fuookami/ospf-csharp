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
/// 任务执行器成本最小化 / Task executor cost minimization.
/// 最小化任务分配到执行器的成本。
/// Minimizes the cost of assigning tasks to executors.
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskExecutorCostMinimization<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<E> _executors;
    private readonly ICompilation _compilation;
    private readonly Func<(T Task, E Executor), Flt64?> _costCalculator;
    private readonly string _name;

    /// <summary>
    /// 任务执行器成本最小化构造 / Task executor cost minimization constructor
    /// </summary>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="costCalculator">任务执行器成本提取器；`null` 表示 1 / Extractor for the task-executor cost; null means 1</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public TaskExecutorCostMinimization(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        ICompilation compilation,
        Func<(T Task, E Executor), Flt64?>? costCalculator = null,
        string name = "task_executor_cost_minimization") {
        _tasks = tasks;
        _executors = executors;
        _compilation = compilation;
        _costCalculator = costCalculator ?? (_ => Flt64.One);
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 基线：最小化 `Σ_{任务,执行器} cost(任务,执行器) × task_assignment[任务,执行器]`，目标名 `task executor`。
    ///
    /// **两个集合都要遍历**：只遍历"任务自己启用的执行器"会把不可用组合当成零成本——而它们本来就在编译表里被钉成 0，
    /// 遍历全表等于多加一些零项（无害），漏项则会让某些组合**完全不计成本**，解依旧可行。
    ///
    /// 系数提取失败时无穷大；`Flt64` 没有无穷，所以这里失败——静默取 0 会让那个任务对目标不产生任何压力。
    ///
    /// The code minimises `Σ_{task, executor} cost(task, executor) × task_assignment[task, executor]`, objective name `task executor`. **Both collections are traversed**:
    /// iterating only the executors a task enables would treat unavailable pairs as free — they are already pinned to 0 in the compilation table, so covering the whole table adds
    /// harmless zeros, while missing entries would leave some pairs **entirely uncharged** with the solution still feasible. An extraction failure uses infinity in the code and
    /// `Flt64` has none, so it fails here — silently zeroing would put no pressure on that task at all.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务执行器成本只能施加到线性元模型 / the task-executor cost can only be applied to a linear meta model"));
        }
        if (_compilation is not ICompilationColumns<E, A> compilation) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "编译结果没有任务分配表 / the compilation does not expose a task-assignment table"));
        }

        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        foreach (T task in _tasks) {
            foreach (E executor in _executors) {
                (IAbstractTask<E, A> Task, E Executor) key = ((IAbstractTask<E, A>)task, executor);
                if (!compilation.TaskAssignment.TryGetValue(key, out object? symbol)
                    || symbol is not Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64> assignment) {
                    // 编译表里没有这一对 → 这个组合根本不该存在，跳过不算漏项。
                    // No such pair in the compilation table → the combination does not exist; skipping is not a missing term.
                    continue;
                }
                Flt64? cost = _costCalculator((task, executor));
                if (cost is not { } coefficient) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "任务执行器成本提取失败：" + task.Id + "/" + executor.Id
                        + " / the task-executor cost could not be extracted: " + task.Id + "/" + executor.Id));
                }
                foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in assignment.Polynomial.Monomials) {
                    monomials.Add(new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                        coefficient * monomial.Coefficient, monomial.Symbol));
                }
            }
        }

        return linear.AddObject(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(monomials.ToArray(), Flt64.Zero),
            "task executor",
            displayName: null);
    }
}
