// 启发式求解器入口 / The heuristic-solver entry points.
// 与固定 Kotlin 基线 `HeuristicSolver.kt`（2904 B）对应；`HeuristicResult.kt` 的更早批次已移植。
// Mirrors the pinned baseline's `HeuristicSolver.kt` (2904 B); `HeuristicResult.kt` is ported alongside it.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 可被包装成求解器的运行器 / A runner that can be wrapped into a solver.
/// </summary>
/// <remarks>
/// `AbstractRunner` 的状态类型是它的泛型参数，求解器不该关心它，所以这里只暴露"跑完给我成员"这一个动作。
/// **这也是"运行器与求解器解耦"的全部内容**——多一个成员都会让两者重新耦合。
/// `AbstractRunner`'s state type is its own generic parameter and a solver should not care about it, so only "run and hand me the members"
/// is exposed here. **That is the whole of the runner/solver decoupling** — one more member would couple them again.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public interface IHeuristicRunner<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 运行到停止判据 / Run until the stopping criterion.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="iteration">迭代状态 / The iteration state.</param>
    /// <param name="runningCallBack">每步回调 / The per-step call-back.</param>
    /// <returns>成员集合 / The members.</returns>
    Task<Result<IReadOnlyList<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>>> Run(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration? iteration = null,
        Func<Iteration, IReadOnlyList<IIndividual<ObjValue, V>>,
            Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null);
}

/// <summary>
/// 启发式求解器 / A heuristic solver.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public interface IHeuristicSolver<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>求解器名称 / The solver name.</summary>
    string Name { get; }

    /// <summary>
    /// 执行一次求解 / Run one solve.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="iteration">迭代状态 / The iteration state.</param>
    /// <returns>搜索结果 / The search result.</returns>
    Task<Result<HeuristicResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> Invoke(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration? iteration = null);
}

/// <summary>
/// 把一个运行器包装成求解器 / Wrap a runner into a solver.
/// </summary>
/// <remarks>
/// **`HeuristicResult.BestSolution` 在本移植里是"解向量"而不是基线的 `Solution` 包装**。这是更早批次（PSO 求解器）定下的形状，
/// 已有消费者依赖它，因此这里**沿用而不改动**。差异写在类型注释里，不假装与基线逐字段一致。
/// **`HeuristicResult.BestSolution` is "the solution vector" here rather than the baseline's `Solution` wrapper.** That shape was fixed by
/// an earlier change (the PSO solver) and existing consumers depend on it, so it **stays unchanged**. The difference is recorded
/// here rather than being passed off as field-for-field compatible.
/// </remarks>
/// <remarks>
/// **搜不到任何个体时必须失败**，而不是返回一个"最优解为空"的结果。后者会让调用方拿到一个成功的返回值，
/// **而"算法什么都没找到"这件事被包装成一个正常结果**，上一层再也无从区分"收敛"与"根本没跑"。
/// **Producing no individual at all must fail**, rather than returning a result whose best solution is empty. The latter hands the caller a
/// successful value, **so "the algorithm found nothing" is dressed up as a normal result** and no layer above can tell "converged" from
/// "never ran".
/// **状态只由 `ConstraintSatisfied` 的"明确为假"判定为不可行；"未知"算作可行**。把"未知"当成不可行会让所有没声明约束的模型
/// 一上来就被标成不可行，**而目标值与迭代数看起来完全正常**。
/// **The status is infeasible only when `ConstraintSatisfied` explicitly returns `false`; "unknown" counts as feasible.** Treating "unknown"
/// as infeasible marks every model that declares no constraints as infeasible from the start, **while its objective and iteration count
/// look perfectly normal**.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class RunnerSolver<ObjValue, V> : IHeuristicSolver<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IHeuristicRunner<ObjValue, V> _runner;

    /// <summary>
    /// 构造运行器求解器 / Construct the runner solver.
    /// </summary>
    /// <param name="name">名称 / The name.</param>
    /// <param name="runner">底层运行器 / The underlying runner.</param>
    public RunnerSolver(string name, IHeuristicRunner<ObjValue, V> runner) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(runner);
        Name = name;
        _runner = runner;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>底层运行器 / The underlying runner.</summary>
    public IHeuristicRunner<ObjValue, V> Runner => _runner;

    /// <inheritdoc/>
    public async Task<Result<HeuristicResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> Invoke(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration? iteration = null) {
        ArgumentNullException.ThrowIfNull(model);
        Iteration current = iteration ?? new Iteration();
        Result<IReadOnlyList<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>> searched =
            await _runner.Run(model, current);
        if (searched is Failed<IReadOnlyList<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>>
            failure) {
            return Results.Failed<HeuristicResult<ObjValue, V>>(failure.Error);
        }
        IReadOnlyList<IIndividual<ObjValue, V>> individuals =
            ((Ok<IReadOnlyList<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>>)searched).Value;
        if (individuals.Count == 0) {
            return Results.Failed<HeuristicResult<ObjValue, V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "the search produced no individual / 搜索未产生任何个体"));
        }
        IIndividual<ObjValue, V> best = individuals[0];
        HeuristicSolutionStatus status = model.ConstraintSatisfied(best.Solution) == false
            ? HeuristicSolutionStatus.Infeasible
            : HeuristicSolutionStatus.Feasible;
        return Results.Ok(new HeuristicResult<ObjValue, V>(
            best.Solution, best.Objective, status, current));
    }
}
