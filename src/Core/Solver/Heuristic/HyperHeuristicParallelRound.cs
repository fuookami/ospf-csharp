// 并行派发 / Parallel dispatch.
// 对应固定 Kotlin （47241 B）里 `advanceLocked` 的"一轮派发多个工作单元"：
// 选择互不相同的工作单元、逐个分配额度（预留额从剩余额度里扣）、并行执行、再顺序归并。
// "dispatch several workers in one round" part: 
// pick distinct workers, allocate each an allowance (reserving it out of the remaining budget), run them in parallel and merge sequentially.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 一轮里被派发的一个工作单元 / One worker dispatched in a round.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Index">工作单元下标 / The worker index.</param>
/// <param name="Dispatch">派发 / The dispatch.</param>
/// <param name="Context">派发时的上下文 / The context at dispatch time.</param>
/// <param name="Available">派发时的可选动作 / The actions available at dispatch time.</param>
/// <param name="Reference">派发前的记忆内容 / The memory contents before the dispatch.</param>
/// <param name="Initialized">派发时是否已初始化 / Whether it was initialised at dispatch time.</param>
/// <param name="Session">会话 / The session.</param>
public sealed record HyperHeuristicRoundSlot<O, V>(
    int Index,
    ISearchSession<O, V> Session,
    HyperHeuristicDispatch<O, V> Dispatch,
    SelectionContext Context,
    IReadOnlyList<string> Available,
    IReadOnlyList<IIndividual<O, V>> Reference,
    bool Initialized)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 一轮的执行结果 / The result of one round.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Slot">对应的派发 / The corresponding dispatch.</param>
/// <param name="Execution">执行结果 / The execution result.</param>
public sealed record HyperHeuristicRoundResult<O, V>(
    HyperHeuristicRoundSlot<O, V> Slot,
    HyperHeuristicExecution<O, V> Execution)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 并行执行一轮派发 / Execute one round of dispatches in parallel.
/// </summary>
/// <remarks>
/// **执行可以并行，归并必须顺序**。并行归并会让两个工作单元同时改写同一份记忆与同一张策略表——
/// 结果是"有时改进被吞掉、有时奖励算在别人头上"，而**两者的运行都正常结束**。
///
/// **Execution may be parallel; merging must be sequential.** Merging in parallel lets two workers rewrite the same memory and the same policy table at
/// once — the result is "sometimes an improvement is swallowed, sometimes a reward lands on someone else", while **both runs finish normally**.
///
/// **随机流不共享**。每个工作单元有自己的 `SearchRandom`，选择用的根流只在派发前顺序推进——
/// 若让执行线程也去动根流，同一配置的两次运行会给出不同的动作序列，而**日志里看不出任何异常**。
///
/// **The random streams are not shared.** Each worker owns its `SearchRandom`, and the root stream used for selection advances sequentially before
/// dispatch — letting execution threads touch the root stream would give two runs of the same configuration different action sequences, **with nothing
/// anomalous in the log**.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public static class HyperHeuristicParallelRound<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 执行一轮 / Execute one round.
    /// </summary>
    /// <param name="slots">派发列表 / The dispatch list.</param>
    /// <param name="budget">全局预算 / The global budget.</param>
    /// <param name="evaluationLimit">全局评估额度 / The global evaluation allowance.</param>
    /// <param name="parallel">是否并行 / Whether to run in parallel.</param>
    /// <returns>按派发顺序排列的结果 / The results, in dispatch order.</returns>
    public static IReadOnlyList<HyperHeuristicRoundResult<O, V>> Execute(
        IReadOnlyList<HyperHeuristicRoundSlot<O, V>> slots,
        SearchBudget budget,
        long evaluationLimit,
        bool parallel) {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(budget);
        var results = new HyperHeuristicRoundResult<O, V>[slots.Count];
        if (slots.Count == 0) {
            return results;
        }
        if (!parallel || slots.Count == 1) {
            for (int index = 0; index < slots.Count; index++) {
                HyperHeuristicRoundSlot<O, V> slot = slots[index];
                results[index] = new HyperHeuristicRoundResult<O, V>(
                    slot, HyperHeuristicExecutor<O, V>.Execute(
                        slot.Session, budget, slot.Dispatch, slot.Initialized, evaluationLimit));
            }
            return results;
        }

        var tasks = new Task<HyperHeuristicExecution<O, V>>[slots.Count];
        for (int index = 0; index < slots.Count; index++) {
            HyperHeuristicRoundSlot<O, V> slot = slots[index];
            tasks[index] = Task.Run(() => HyperHeuristicExecutor<O, V>.Execute(
                slot.Session, budget, slot.Dispatch, slot.Initialized, evaluationLimit));
        }
        Task.WaitAll(tasks);
        for (int index = 0; index < slots.Count; index++) {
            results[index] = new HyperHeuristicRoundResult<O, V>(slots[index], tasks[index].Result);
        }
        return results;
    }
}
