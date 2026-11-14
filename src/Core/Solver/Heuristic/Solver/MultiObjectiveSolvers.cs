// 多目标求解器 / The multi-objective solvers.
// 与固定 Kotlin 基线 `solver/MultiObjectiveSolvers.kt`（6491 B）对应。
// Mirrors the pinned baseline's `solver/MultiObjectiveSolvers.kt` (6491 B).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Solver;

/// <summary>
/// 多目标求解器 / A multi-objective solver.
/// </summary>
/// <remarks>
/// **空前沿必须以"失败"的形式返回，而不是"成功的空结果"**。把"一个个体都没产出"包装成成功，会让调用方以为
/// **算法收敛到了一个空集**，而真正的原因（模型没有给出任何合法解）再也不会出现在任何地方。
/// **An empty front must be reported as a failure, not as a "successful empty result"**. Wrapping "not a single individual was produced"
/// as a success makes the caller believe **the algorithm converged to the empty set**, and the real cause (the model produced no legal
/// solution at all) never surfaces again.
/// </remarks>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public interface IMultiObjectiveSolver<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>求解器名称 / The solver name.</summary>
    string Name { get; }

    /// <summary>
    /// 求解一个多目标问题 / Solve a multi-objective problem.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <returns>前沿结果 / The front result.</returns>
    Task<Result<MultiObjectiveResult<V>, ErrorCode, Error<ErrorCode>>> Invoke(
        IAbstractCallBackModelInterface<object?, IReadOnlyList<Flt64>, V> model);
}

/// <summary>
/// NSGA 系列求解器 / An NSGA-family solver.
/// </summary>
/// <remarks>
/// **名称一律从 runner 取，不从包装类型取**。基线的 II 与 III 共用同一个包装类型，区别只在 runner；
/// 若把名称写死成 `"nsga-ii"`，**那么用 NSGA-III 跑出来的东西会自称 NSGA-II，而结果本身完全正常**——
/// 这是最难被发现的一类错误：**结果对、标签错**。
/// **The name always comes from the runner, never from the wrapper type.** The baseline shares one wrapper between II and III, with only
/// the runner differing; hard-coding `"nsga-ii"` makes **an NSGA-III run call itself NSGA-II while its results are perfectly normal** —
/// the hardest kind of error to spot: **right results, wrong label**.
/// </remarks>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class Nsga2Solver<V> : IMultiObjectiveSolver<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly Nsga2Runner<V> _runner;

    /// <summary>
    /// 构造 NSGA 系列求解器 / Construct the NSGA-family solver.
    /// </summary>
    /// <param name="runner">底层运行器 / The underlying runner.</param>
    public Nsga2Solver(Nsga2Runner<V> runner) {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc/>
    public string Name => _runner.Name;

    /// <summary>底层运行器 / The underlying runner.</summary>
    public Nsga2Runner<V> Runner => _runner;

    /// <inheritdoc/>
    public async Task<Result<MultiObjectiveResult<V>, ErrorCode, Error<ErrorCode>>> Invoke(
        IAbstractCallBackModelInterface<object?, IReadOnlyList<Flt64>, V> model) {
        ArgumentNullException.ThrowIfNull(model);
        var iteration = new Iteration();
        Result<IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>>, ErrorCode, Error<ErrorCode>>
            searched = await _runner.Run(model, iteration);
        if (searched is Failed<IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>>, ErrorCode,
                Error<ErrorCode>> failure) {
            return Results.Failed<MultiObjectiveResult<V>>(failure.Error);
        }
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> front =
            ((Ok<IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>>, ErrorCode, Error<ErrorCode>>)searched)
            .Value;
        if (front.Count == 0) {
            return Results.Failed<MultiObjectiveResult<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "模型未产生有效解 / The model produced no valid solution"));
        }
        var solutions = new List<Solution<V>>(front.Count);
        var objectives = new List<IReadOnlyList<Flt64>>(front.Count);
        foreach (IIndividual<IReadOnlyList<Flt64>, V> individual in front) {
            solutions.Add(new Solution<V>(individual.Solution));
            objectives.Add(individual.Objective);
        }
        return Results.Ok(new MultiObjectiveResult<V>(solutions, objectives, iteration));
    }
}

/// <summary>
/// 多目标求解器工厂 / Multi-objective solver factories.
/// </summary>
public static class MultiObjectiveSolvers {
    /// <summary>
    /// 构造 NSGA-II 求解器 / Build an NSGA-II solver.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="objectivesOf">目标向量提取器 / The objective-vector extractor.</param>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">解更新算子 / The solution-update operator.</param>
    /// <param name="mutation">变异算子 / The mutation operator.</param>
    /// <param name="mutationRate">变异率 / The mutation rate.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IMultiObjectiveSolver<V> Nsga2<V>(
        Func<Solution<V>, IReadOnlyList<Flt64>> objectivesOf,
        IDominance dominance,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        IMutationOperator? mutation = null,
        Flt64? mutationRate = null,
        int archiveAmount = 100)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new Nsga2Solver<V>(new Nsga2Runner<V>(
            objectivesOf, dominance,
            update ?? new SimulatedBinaryCross(new Flt64(20.0)),
            mutation ?? new PolynomialMutation(new Flt64(20.0)),
            entropy, stopping, converter,
            size, mutationRate, archiveAmount));

    /// <summary>
    /// 构造 NSGA-III 求解器 / Build an NSGA-III solver.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="objectivesOf">目标向量提取器 / The objective-vector extractor.</param>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="update">解更新算子 / The solution-update operator.</param>
    /// <param name="mutation">变异算子 / The mutation operator.</param>
    /// <param name="mutationRate">变异率 / The mutation rate.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="divisions">每个目标的分割数 / The divisions per objective.</param>
    /// <returns>求解器 / The solver.</returns>
    public static IMultiObjectiveSolver<V> Nsga3<V>(
        Func<Solution<V>, IReadOnlyList<Flt64>> objectivesOf,
        IDominance dominance,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        int size = 20,
        ISolutionUpdate? update = null,
        IMutationOperator? mutation = null,
        Flt64? mutationRate = null,
        int archiveAmount = 100,
        int divisions = 8)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        // 与基线一致：II 与 III 共用同一个包装类型，区别只在 runner。
        // As in the baseline: II and III share one wrapper type and differ only in the runner.
        new Nsga2Solver<V>(new Nsga3Runner<V>(
            objectivesOf, dominance,
            update ?? new SimulatedBinaryCross(new Flt64(20.0)),
            mutation ?? new PolynomialMutation(new Flt64(20.0)),
            entropy, stopping, converter,
            size, mutationRate, archiveAmount, divisions));
}
