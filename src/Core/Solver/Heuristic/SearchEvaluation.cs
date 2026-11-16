// 计费搜索评估 / Metered search evaluation.
// 与固定 Kotlin （1875 字节）对应。

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 单次评估结果，拒绝额度与无效候选分别表达 / The result of one evaluation, separating a denied admission from an invalid candidate.
/// </summary>
/// <remarks>
/// **`Admitted` 与 `Candidate` 必须分开表达**。把"没拿到额度"和"拿到了额度但候选无效"压成同一个 `null`，会让上层无法区分
/// "还需要更多预算"与"这个邻域里没有可行解"——前者该等，后者该换邻域，而这两种应对方式完全相反。
/// **`Admitted` and `Candidate` must be expressed separately.** Collapsing "no token" and "token spent but the candidate is invalid"
/// into one `null` leaves the caller unable to tell "needs more budget" from "nothing feasible in this neighbourhood". The first
/// should wait; the second should move on — opposite responses.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
/// <param name="Admitted">是否消耗了请求额度 / Whether a request token was consumed.</param>
/// <param name="Candidate">已验证候选，目标缺失或不可行时为 null / The validated candidate; null when the objective is missing or
/// the solution is infeasible.</param>
public sealed record SearchEvaluationResult<O, V>(bool Admitted, IIndividual<O, V>? Candidate)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 自定义会话共用的计费评估入口 / The metered evaluation entry shared by custom sessions.
/// </summary>
/// <remarks>
/// **先领额度，再复制解**。反过来的话，被拒绝的评估已经付掉一次向量复制的代价；在高频调用下，这笔浪费恰好落在"预算已耗尽"这条最热的
/// 路径上。
/// **Claim the token before copying the solution.** The other order makes every denied evaluation pay for a vector copy, and under
/// high call frequency that waste lands precisely on the hottest path — the one where the budget is already gone.
/// **被拒绝时不记目标调用**。目标调用次数是成本度量，只在真正调用模型目标时增加。
/// **A denied admission records no objective call.** The objective-call count is a cost metric and only moves when the model
/// objective is actually invoked.
/// **不可行的候选仍然算消耗了额度**。它可以被拒绝进入档案，但评估已经发生；不计数会让"按评估次数限流"在大量不可行候选上失效。
/// **An infeasible candidate still consumes the token.** It may be rejected from the archive, but the evaluation did happen; not
/// counting it would defeat any admission control expressed in evaluations when candidates are mostly infeasible.
/// </remarks>
public static class SearchEvaluation {
    /// <summary>
    /// 计费评估一个解 / Evaluate one solution against the meter.
    /// </summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <param name="solution">待复制与验证的解 / The solution to copy and validate.</param>
    /// <param name="budget">当前片段 / The current slice.</param>
    /// <returns>评估结果 / The evaluation result.</returns>
    public static Result<SearchEvaluationResult<O, V>, ErrorCode, Error<ErrorCode>> Evaluate<O, V>(
        this SearchProblem<O, V> problem,
        Solution<V> solution,
        SearchSlice budget) where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(budget);

        if (!budget.Acquire()) {
            return Results.Ok(new SearchEvaluationResult<O, V>(false, null));
        }

        Result<SearchEvaluationResult<O, V>, ErrorCode, Error<ErrorCode>> evaluated =
            SearchBoundary.Execute(() => Validate(problem, solution, budget));
        return evaluated;
    }

    /// <summary>
    /// 在边界内校验并评估 / Validate and evaluate inside the boundary.
    /// </summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <param name="solution">待复制与验证的解 / The solution to copy and validate.</param>
    /// <param name="budget">当前片段 / The current slice.</param>
    /// <returns>评估结果 / The evaluation result.</returns>
    private static SearchEvaluationResult<O, V> Validate<O, V>(
        SearchProblem<O, V> problem,
        Solution<V> solution,
        SearchSlice budget) where V : struct, IRealNumber<V>, INumberField<V> {
        IReadOnlyList<V> copied = solution.Values.ToList();
        if (!problem.ValidSolution(copied) || !problem.Admissible(copied)) {
            return new SearchEvaluationResult<O, V>(true, null);
        }

        budget.Global.ObjectiveCalled();
        O? objective = problem.Model.Objective(copied);
        if (objective is null || !problem.Valid(objective)) {
            return new SearchEvaluationResult<O, V>(true, null);
        }

        return new SearchEvaluationResult<O, V>(true, new EvaluatedIndividual<O, V>(copied, objective));
    }

}

/// <summary>
/// 纯评估个体：只有解与目标，没有适应度 / A plain evaluated individual: solution and objective only, no fitness.
/// </summary>
/// <remarks>
/// **不借用 `SolutionWithFitness`**。那个类型要求一个 `Fitness` 值，而这一层根本没有适应度概念；填 0 会让下游误以为这是
/// "适应度为 0"的真实读数。
/// **`SolutionWithFitness` is not reused.** That type demands a `Fitness` value while this layer has no fitness concept at all;
/// filling in 0 would read downstream as a genuine "fitness is zero" measurement.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
/// <param name="Solution">解向量 / The solution vector.</param>
/// <param name="Objective">目标值 / The objective value.</param>
public sealed record EvaluatedIndividual<O, V>(IReadOnlyList<V> Solution, O Objective) : IIndividual<O, V>
    where V : struct, IRealNumber<V>, INumberField<V>;
