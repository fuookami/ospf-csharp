// 文化与模因求解器 / The memetic solver.
// 与固定 Kotlin 基线 `solver/MemeticSolvers.kt`（6970 B）对应。
// Mirrors the pinned baseline's `solver/MemeticSolvers.kt` (6970 B).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Solver;

/// <summary>
/// 文化与模因求解器：全局搜索之后再做一次局部打磨 / A memetic solver: a global search followed by a local polish.
/// </summary>
/// <remarks>
/// **全局失败与打磨失败的处理必须不对称**。全局搜索是主线，它的失败原样向外传；打磨只是"锦上添花"，
/// 它的失败**必须被吞掉并退回全局结果**——否则一次没必要的局部搜索失败会**把已经算出来的好解一起扔掉**，
/// 而上层只会看到"求解失败"。
/// **Failures of the global search and of the polish must be handled asymmetrically.** The global search is the main line and its failure
/// propagates unchanged; the polish is a bonus, so its failure **must be swallowed and the global result returned** — otherwise an
/// unnecessary local-search failure **throws away the good solution already found**, and the layer above only sees "solve failed".
///
/// **只有"严格更好"才采用打磨结果**。相等时保留全局结果，**否则同一个解会因为一次无意义的替换而改变来源**，
/// 让"打磨到底有没有起作用"再也无法从结果上判断。
/// **The polished result is kept only when it is strictly better.** On a tie the global result stands, **otherwise a meaningless swap
/// changes the provenance of an identical solution** and whether the polish did anything at all can no longer be told from the result.
///
/// **打磨拿到的是一份全新的迭代状态**（与基线一致）。沿用全局的迭代状态会让打磨的停止判据从全局已经用掉的预算开始数，
/// **于是打磨要么一步都不走，要么走得比配置的多**。
/// **The polish receives a fresh iteration state** (as in the baseline). Reusing the global one makes the polish's stopping criterion
/// count from the budget the global search already consumed, **so the polish either never takes a step or takes more than configured**.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class MemeticSolver<ObjValue, V> : IHeuristicSolver<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IHeuristicSolver<ObjValue, V> _global;
    private readonly Func<IReadOnlyList<double>, IHeuristicSolver<ObjValue, V>> _polish;

    /// <summary>
    /// 构造文化与模因求解器 / Construct the memetic solver.
    /// </summary>
    /// <param name="name">名称 / The name.</param>
    /// <param name="global">全局求解器 / The global solver.</param>
    /// <param name="polish">按给定起点构造打磨求解器 / Builds the polish solver from a start point.</param>
    public MemeticSolver(
        string name,
        IHeuristicSolver<ObjValue, V> global,
        Func<IReadOnlyList<double>, IHeuristicSolver<ObjValue, V>> polish) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(polish);
        Name = name;
        _global = global;
        _polish = polish;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>全局求解器 / The global solver.</summary>
    public IHeuristicSolver<ObjValue, V> Global => _global;

    /// <inheritdoc/>
    public async Task<Result<HeuristicResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> Invoke(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration? iteration = null) {
        ArgumentNullException.ThrowIfNull(model);
        Result<HeuristicResult<ObjValue, V>, ErrorCode, Error<ErrorCode>> globalResult =
            await _global.Invoke(model, iteration);
        if (globalResult is Failed<HeuristicResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>
            globalFailure) {
            return Results.Failed<HeuristicResult<ObjValue, V>>(globalFailure.Error);
        }
        HeuristicResult<ObjValue, V> global = 
            ((Ok<HeuristicResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>)globalResult).Value;
        if (global.BestSolution is null) {
            // 没有起点就没有可打磨的东西；原样返回，而不是伪造一次打磨。
            // With no start point there is nothing to polish; return unchanged rather than fake a polish.
            return globalResult;
        }
        var values = new List<double>(global.BestSolution.Count);
        foreach (V value in global.BestSolution) {
            // 与基线一致：先转成 Flt64 再取 double（V 上没有直接的 ToDouble）。
            // As in the baseline: go through Flt64 before taking the double (V has no direct ToDouble).
            values.Add(value.ToFlt64().ToDouble());
        }
        Result<HeuristicResult<ObjValue, V>, ErrorCode, Error<ErrorCode>> polishResult =
            await _polish(values).Invoke(model);
        if (polishResult is Failed<HeuristicResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>) {
            return globalResult;
        }
        HeuristicResult<ObjValue, V> polished =
            ((Ok<HeuristicResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>)polishResult).Value;
        return IsBetter(model, polished.BestObjective, global.BestObjective) ? polishResult : globalResult;
    }

    /// <summary>
    /// 判断候选是否严格更好 / Decide whether the candidate is strictly better.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="candidate">候选目标值 / The candidate objective.</param>
    /// <param name="incumbent">现有目标值 / The incumbent objective.</param>
    /// <returns>是否严格更好 / Whether it is strictly better.</returns>
    private static bool IsBetter(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        ObjValue? candidate,
        ObjValue? incumbent) {
        if (candidate is null) {
            return false;
        }
        if (incumbent is null) {
            // 现有结果没有目标值时，任何有目标值的候选都算更好——否则两边都为空会被判成"不变"。
            // When the incumbent has no objective, any candidate that has one counts as better — otherwise two nulls would read as "no change".
            return true;
        }
        return HeuristicOrdering.CompareStrictly(model, candidate, incumbent) < 0;
    }
}

/// <summary>
/// 文化与模因求解器工厂 / Memetic solver factory.
/// </summary>
public static class MemeticSolvers {
    /// <summary>
    /// 构造一个"全局搜索 + 局部打磨"求解器 / Build a "global search plus local polish" solver.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="global">全局求解器 / The global solver.</param>
    /// <param name="polishStopping">打磨的停止判据 / The polish stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="neighborhood">邻域 / The neighbourhood.</param>
    /// <param name="acceptance">接受判据 / The acceptance criterion.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IHeuristicSolver<ObjValue, V> Memetic<ObjValue, V>(
        IHeuristicSolver<ObjValue, V> global,
        IStoppingCriterion polishStopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        INeighborhood? neighborhood = null,
        IAcceptance? acceptance = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(polishStopping);
        return new MemeticSolver<ObjValue, V>(
            "memetic-" + global.Name,
            global,
            values => new RunnerSolver<ObjValue, V>(
                "memetic-polish",
                new TrajectoryRunner<ObjValue, V>(
                    neighborhood ?? new RandomPerturbation(1),
                    entropy,
                    new BestArchive<ObjValue, V>(1),
                    polishStopping,
                    converter,
                    initializer: new FixedInitializer(values),
                    acceptance: acceptance ?? NotWorseAcceptance.Instance)));
    }
}
