#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Path;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttNetworkScheduling.Domain.Network;

/// <summary>
/// 把网络路径适配成业务任务束 / Adapt network paths to business bunches.
/// </summary>
/// <remarks>
/// 主模型与分支状态由 Gantt 调用方持有，这里**只做路径 → 任务束的转换与完成状态的翻译**。
///
/// The master model and branch state stay with the Gantt caller; this **only converts paths
/// into bunches and translates the completion status**.
///
/// **完成状态的翻译才是这段代码的真正内容**，不是形式上的适配：
/// `UncertifiedDominance` 与 `UncertifiedValidation` 都映射到 `Heuristic`——"搜索跑完了"不等于"证明了没有更好的列"。
/// 而 `noImprovingColumns` **只有在调用方给出独立证据、且完成状态是 `Exhausted` 时才为真**；
/// 其余情况一律 `null`（未知），**不是 `false`**——"没证明"与"证明没有"是两件事。
///
/// **Translating the completion status is the real content here**, not a formality: both `UncertifiedDominance` and `UncertifiedValidation` map to
/// `Heuristic` — "the search finished" is not "no better column was proved to exist". And `noImprovingColumns` is true **only when the caller supplies
/// independent evidence and the completion is `Exhausted`**; otherwise it is `null` (unknown), **not `false`** — "not proved" and "proved none" differ.
/// </remarks>
public sealed class NetworkBunchGenerationContext {
    private readonly PathSearchContext _search;

    /// <summary>
    /// 创建适配上下文 / Create the adaptation context.
    /// </summary>
    /// <param name="search">网络搜索内核 / Network search kernel.</param>
    public NetworkBunchGenerationContext(PathSearchContext? search = null) {
        _search = search ?? new PathSearchContext();
    }

    /// <summary>
    /// 搜索并保留完整业务路径状态 / Search while retaining the complete business path state.
    /// </summary>
    /// <typeparam name="N">节点类型 / Node type.</typeparam>
    /// <typeparam name="A">弧类型 / Arc type.</typeparam>
    /// <typeparam name="S">状态类型 / State type.</typeparam>
    /// <typeparam name="B">任务束类型 / Bunch type.</typeparam>
    /// <param name="problem">由当前任务、分支与对偶构造的问题 / Problem built for the current tasks, branch and duals.</param>
    /// <param name="budget">搜索预算 / Search budget.</param>
    /// <param name="scope">当前请求作用域 / Current request scope.</param>
    /// <param name="maximumResults">返回列上限 / Output limit.</param>
    /// <param name="noImprovementEvidence">对搜索结果的独立无改进证明判定器 / Independent no-improvement proof predicate.</param>
    /// <param name="onSearchCompleted">搜索完成后的统计回调 / Diagnostics callback after the search completes.</param>
    /// <param name="extract">业务任务束构造器 / Business bunch extractor.</param>
    /// <returns>任务束及完成信息 / Bunches and completion metadata.</returns>
    public Result<ColumnGenerationBatch<B>, ErrorCode, Error<ErrorCode>> Generate<N, A, S, B>(
        PathProblem<N, A, S> problem,
        PathSearchBudget budget,
        ColumnGenerationScope scope,
        int maximumResults = int.MaxValue,
        Func<PathSearchResult<S, A>, bool>? noImprovementEvidence = null,
        Action<PathSearchResult<S, A>>? onSearchCompleted = null,
        Func<PathLabel<S, A>, PathSearchResult<S, A>, Result<B?, ErrorCode, Error<ErrorCode>>>? extract = null)
        where A : class where S : class where B : class {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(extract);

        Result<PathSearchResult<S, A>, ErrorCode, Error<ErrorCode>> searchRet =
            _search.Search(problem, budget, maximumResults);
        if (searchRet is not Ok<PathSearchResult<S, A>, ErrorCode, Error<ErrorCode>> searchOk) {
            Error<ErrorCode> searchErr =
                (searchRet as Failed<PathSearchResult<S, A>, ErrorCode, Error<ErrorCode>>)?.Error
                ?? new Err<ErrorCode>(ErrorCode.Unknown);
            return new Failed<ColumnGenerationBatch<B>, ErrorCode, Error<ErrorCode>>(searchErr);
        }
        PathSearchResult<S, A> result = searchOk.Value;
        onSearchCompleted?.Invoke(result);

        var bunches = new List<B>();
        bool rejected = false;
        foreach (PathLabel<S, A> path in result.Paths) {
            Result<B?, ErrorCode, Error<ErrorCode>> extractRet = extract(path, result);
            if (extractRet is Failed<B?, ErrorCode, Error<ErrorCode>> extractFailed) {
                return new Failed<ColumnGenerationBatch<B>, ErrorCode, Error<ErrorCode>>(extractFailed.Error);
            }
            B? bunch = ((Ok<B?, ErrorCode, Error<ErrorCode>>)extractRet).Value;
            if (bunch is null) {
                // 抽取器返回 null 表示这条路径**被拒绝**，不是"没有路径"——它必须影响完成状态。
                // An extractor returning null means the path was **rejected**, not that there were no paths — it has to affect the completion status.
                rejected = true;
                continue;
            }
            bunches.Add(bunch);
        }

        ColumnGenerationCompletion completion = Translate(result.Completion, rejected);
        // **只有"调用方给了独立证据"且"搜索已穷尽"时才有证明**；否则是 null（未知），不是 false。
        // **A proof exists only when the caller supplies independent evidence and the search was exhausted**; otherwise it is null (unknown), not false.
        bool provedNoImprovement = noImprovementEvidence?.Invoke(result) == true
            && completion == ColumnGenerationCompletion.Exhausted;

        return new Ok<ColumnGenerationBatch<B>, ErrorCode, Error<ErrorCode>>(
            new ColumnGenerationBatch<B>(
                columns: bunches,
                completion: completion,
                scope: scope,
                noImprovingColumns: provedNoImprovement ? true : null));
    }

    /// <summary>
    /// 把路径搜索完成状态翻译成列生成完成状态 / Translate a path-search completion into a column-generation completion.
    /// </summary>
    /// <remarks>
    /// 有一条路径被拒绝时一律降级为 `Heuristic`——**"大部分路径可用"不等于"这次搜索可以当作穷尽"**。
    ///
    /// When any path was rejected the result degrades to `Heuristic` — **"most paths were usable" is not "this search may be treated as exhausted"**.
    /// </remarks>
    /// <param name="completion">路径搜索完成状态 / Path-search completion.</param>
    /// <param name="rejected">是否有路径被拒绝 / Whether any path was rejected.</param>
    /// <returns>列生成完成状态 / Column-generation completion.</returns>
    public static ColumnGenerationCompletion Translate(PathSearchCompletion completion, bool rejected) {
        if (rejected) {
            return ColumnGenerationCompletion.Heuristic;
        }
        return completion switch {
            PathSearchCompletion.Exhausted => ColumnGenerationCompletion.Exhausted,
            PathSearchCompletion.BudgetLimit => ColumnGenerationCompletion.BudgetLimit,
            PathSearchCompletion.Interrupted => ColumnGenerationCompletion.Interrupted,
            // 未认证的支配/校验一律按启发式：**"跑完了"不等于"证明了"**。
            // Uncertified dominance/validation count as heuristic: **"it finished" is not "it was proved"**.
            _ => ColumnGenerationCompletion.Heuristic,
        };
    }
}
