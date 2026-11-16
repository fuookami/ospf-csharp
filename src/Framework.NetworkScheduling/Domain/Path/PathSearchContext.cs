// 无求解器依赖的资源标签搜索 / Solver-independent resource-label search.
//
// 这一层的关键设计是**把"搜索"与"业务资源"彻底分离**：搜索只知道图、标签前驱链和几个回调，完全不知道 VRPTW、对偶或定价。
// 因此 ESPPRC 定价是它的一个使用者，将来别的定价模型（带时间窗的、带容量维度的）也用同一套搜索。
// The design point here is **total separation between search and business resources**: the search knows only the graph, the
// label predecessor chain and a handful of callbacks — nothing about VRPTW, duals or pricing. ESPPRC pricing is one user of it,
// and a future pricing model (time-windowed, multi-dimensional capacity) reuses the same search.
//
// 另一条贯穿全文件的约束是**"完成"必须是可证明的**：截断、未认证的支配剪枝、完整路径复核被拒，都不能宣称搜索穷尽。
// 这些情况各有独立终态，绝不能被折叠成一个"成功"。
// The other constraint running through the file is that **"complete" must be provable**: truncation, uncertified dominance
// pruning and rejected full-trace validation may not claim exhaustion. Each has its own terminal state and must never be
// collapsed into one "success".

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Path;

/// <summary>
/// 搜索完成证据 / Search-completion evidence.
/// </summary>
/// <remarks>
/// 截断、未认证支配剪枝或完整路径复核拒绝，**都不能证明不存在改进路径**，因此各有独立终态。
/// Truncation, uncertified dominance pruning and rejected full-trace validation **none prove the absence of an improving path**,
/// hence a distinct terminal state for each.
/// </remarks>
public enum PathSearchCompletion {
    /// <summary>搜索空间穷尽 / The search space was exhausted.</summary>
    Exhausted,

    /// <summary>触及标签预算 / A label budget was reached.</summary>
    BudgetLimit,

    /// <summary>被外部中断 / Interrupted externally.</summary>
    Interrupted,

    /// <summary>使用了未认证的支配剪枝 / Uncertified dominance pruning was applied.</summary>
    UncertifiedDominance,

    /// <summary>完整路径复核拒绝了某些终止标签 / Full-trace validation rejected some terminals.</summary>
    UncertifiedValidation,
}

/// <summary>
/// 搜索预算，与业务路径长度约束分离 / Search budgets, separate from domain path limits.
/// </summary>
/// <remarks>
/// **搜索预算与业务步数上限是两件事**：前者是"算力不够了"，后者是"业务上不允许更长的路线"。混成一个参数会让"因为预算耗尽
/// 而没找到更优路线"被误报成"业务上不存在更优路线"。
/// **A search budget is not a domain step bound**: the former means "out of compute", the latter "the business forbids longer
/// routes". Merging them makes "no better route within budget" masquerade as "no better route exists".
/// </remarks>
/// <param name="MaximumLabels">标签总预算 / Total-label budget.</param>
/// <param name="MaximumLabelsPerNode">每节点标签预算 / Per-node label budget.</param>
/// <param name="ShouldStop">外部中断检查 / External interruption check.</param>
public sealed class PathSearchBudget(
    int MaximumLabels = int.MaxValue,
    int MaximumLabelsPerNode = int.MaxValue,
    Func<bool>? ShouldStop = null) {
    /// <summary>标签总预算 / Total-label budget.</summary>
    public int MaximumLabels { get; } = MaximumLabels;

    /// <summary>每节点标签预算 / Per-node label budget.</summary>
    public int MaximumLabelsPerNode { get; } = MaximumLabelsPerNode;

    /// <summary>外部中断检查 / External interruption check.</summary>
    public Func<bool> ShouldStop { get; } = ShouldStop ?? (() => false);
}

/// <summary>
/// 不可变标签，前驱索引仅在本轮有效 / Immutable label with request-local predecessor identity.
/// </summary>
/// <remarks>
/// `Predecessor` 是本轮标签数组的下标，**跨请求无效**：回调必须从 `State` 推导后续行为，不能依赖它。
/// `Predecessor` is an index into this request's label array and **is meaningless across requests**: callbacks must derive
/// future behaviour from `State`, never from it.
/// </remarks>
/// <typeparam name="S">业务资源状态类型 / Business resource state type.</typeparam>
/// <typeparam name="A">弧载荷类型 / Arc payload type.</typeparam>
/// <param name="Index">本轮标签索引 / Request-local index.</param>
/// <param name="Node">节点 / Node.</param>
/// <param name="State">业务资源状态 / Business resource state.</param>
/// <param name="Predecessor">前驱索引 / Predecessor index.</param>
/// <param name="Incoming">到达弧 / Incoming arc.</param>
/// <param name="Visited">已访问节点 / Visited nodes.</param>
/// <param name="Steps">业务路径长度 / Domain path length.</param>
public sealed record PathLabel<S, A>(
    int Index,
    NetworkNodeId Node,
    S State,
    int? Predecessor,
    A? Incoming,
    IReadOnlySet<NetworkNodeId> Visited,
    int Steps) where S : class where A : class;

/// <summary>
/// 路径问题 / Path problem.
/// </summary>
/// <remarks>
/// 状态与回调**不应跨请求共享可变值**。终止标签与可扩展标签不能相互支配；elementarity 搜索还要求支配标签已访问的节点不多于
/// 被支配者；存在步数上限时还要求其步数不多于被支配者——这三条由 <see cref="PathSearchContext"/> 在调用 <see cref="Dominates"/>
/// 之前先行检查，因此业务回调只需要比较资源本身。
/// States and callbacks **must not share mutable values across requests**. Terminal and expandable labels cannot dominate one
/// another; an elementary search additionally requires the dominating label to have visited no more nodes, and a finite step
/// bound requires it to use no more steps — <see cref="PathSearchContext"/> checks all three before invoking
/// <see cref="Dominates"/>, so business callbacks only compare resources.
/// </remarks>
/// <typeparam name="N">节点载荷类型 / Node payload type.</typeparam>
/// <typeparam name="A">弧载荷类型 / Arc payload type.</typeparam>
/// <typeparam name="S">业务资源状态类型 / Business resource state type.</typeparam>
/// <param name="Graph">有效图 / Effective graph.</param>
/// <param name="Source">起点 / Source.</param>
/// <param name="Initial">初始状态 / Initial state.</param>
/// <param name="Transition">资源转移，null 表示不可行 / Transition; null means infeasible.</param>
/// <param name="Terminal">终点判定 / Terminal predicate.</param>
/// <param name="Accept">结果筛选 / Output predicate.</param>
/// <param name="Elementary">是否禁止重复节点 / Whether repeated nodes are forbidden.</param>
/// <param name="MaximumSteps">业务步数上限，不是搜索预算 / Domain step bound, not a search budget.</param>
/// <param name="Dominates">支配候选判定 / Dominance predicate.</param>
/// <param name="DominanceIsExact">是否由业务保证支配安全 / Whether the domain certifies dominance.</param>
/// <param name="Order">候选排序 / Candidate ordering.</param>
/// <param name="StartContribution">起点资源或成本贡献 / Start resource or cost contribution.</param>
/// <param name="TerminalContribution">终点资源或成本贡献 / Terminal resource or cost contribution.</param>
/// <param name="AllowedArc">弧过滤器 / Arc predicate.</param>
/// <param name="AllowedNode">节点过滤器 / Node predicate.</param>
/// <param name="Validate">基于完整路径标签链的独立校验 / Independent validation over the full label trace.</param>
public sealed class PathProblem<N, A, S>(
    NetworkGraph<N, A> Graph,
    NetworkNodeId Source,
    S Initial,
    Func<PathLabel<S, A>, A, Result<S?, ErrorCode, Error<ErrorCode>>> Transition,
    Func<NetworkNodeId, S, bool> Terminal,
    Func<S, bool>? Accept = null,
    bool Elementary = true,
    int? MaximumSteps = null,
    Func<PathLabel<S, A>, PathLabel<S, A>, bool>? Dominates = null,
    bool DominanceIsExact = false,
    IComparer<S>? Order = null,
    Func<NetworkNodeId, S, Result<S?, ErrorCode, Error<ErrorCode>>>? StartContribution = null,
    Func<PathLabel<S, A>, Result<S?, ErrorCode, Error<ErrorCode>>>? TerminalContribution = null,
    Func<A, bool>? AllowedArc = null,
    Func<NetworkNodeId, bool>? AllowedNode = null,
    Func<IReadOnlyList<PathLabel<S, A>>, Result<bool, ErrorCode, Error<ErrorCode>>>? Validate = null)
    where A : class where S : class {
    /// <summary>有效图 / Effective graph.</summary>
    public NetworkGraph<N, A> Graph { get; } = Graph;

    /// <summary>起点 / Source.</summary>
    public NetworkNodeId Source { get; } = Source;

    /// <summary>初始状态 / Initial state.</summary>
    public S Initial { get; } = Initial;

    /// <summary>资源转移 / Transition.</summary>
    public Func<PathLabel<S, A>, A, Result<S?, ErrorCode, Error<ErrorCode>>> Transition { get; } = Transition;

    /// <summary>终点判定 / Terminal predicate.</summary>
    public Func<NetworkNodeId, S, bool> Terminal { get; } = Terminal;

    /// <summary>结果筛选 / Output predicate.</summary>
    public Func<S, bool> Accept { get; } = Accept ?? (_ => true);

    /// <summary>是否禁止重复节点 / Whether repeated nodes are forbidden.</summary>
    public bool Elementary { get; } = Elementary;

    /// <summary>业务步数上限 / Domain step bound.</summary>
    public int? MaximumSteps { get; } = MaximumSteps;

    /// <summary>支配候选判定 / Dominance predicate.</summary>
    public Func<PathLabel<S, A>, PathLabel<S, A>, bool>? Dominates { get; } = Dominates;

    /// <summary>是否由业务保证支配安全 / Whether the domain certifies dominance.</summary>
    public bool DominanceIsExact { get; } = DominanceIsExact;

    /// <summary>候选排序 / Candidate ordering.</summary>
    public IComparer<S>? Order { get; } = Order;

    /// <summary>起点贡献 / Start contribution.</summary>
    public Func<NetworkNodeId, S, Result<S?, ErrorCode, Error<ErrorCode>>> StartContribution { get; } =
        StartContribution ?? ((_, state) => Results.Ok<S?>(state));

    /// <summary>终点贡献 / Terminal contribution.</summary>
    public Func<PathLabel<S, A>, Result<S?, ErrorCode, Error<ErrorCode>>> TerminalContribution { get; } =
        TerminalContribution ?? (label => Results.Ok<S?>(label.State));

    /// <summary>弧过滤器 / Arc predicate.</summary>
    public Func<A, bool> AllowedArc { get; } = AllowedArc ?? (_ => true);

    /// <summary>节点过滤器 / Node predicate.</summary>
    public Func<NetworkNodeId, bool> AllowedNode { get; } = AllowedNode ?? (_ => true);

    /// <summary>完整路径校验 / Full-trace validation.</summary>
    public Func<IReadOnlyList<PathLabel<S, A>>, Result<bool, ErrorCode, Error<ErrorCode>>> Validate { get; } =
        Validate ?? (_ => Results.Ok(true));
}

/// <summary>
/// 路径搜索结果 / Path-search result.
/// </summary>
/// <typeparam name="S">业务资源状态类型 / Business resource state type.</typeparam>
/// <typeparam name="A">弧载荷类型 / Arc payload type.</typeparam>
/// <param name="Paths">筛选后的终止标签 / Selected terminal labels.</param>
/// <param name="Labels">全部前驱标签 / All predecessor labels.</param>
/// <param name="Completion">搜索终态 / Completion state.</param>
/// <param name="ExpandedLabels">已扩展标签数 / Expanded-label count.</param>
/// <param name="DominatedLabels">支配剪枝数 / Dominance-pruning count.</param>
/// <param name="TerminalCount">终止贡献成功的标签数 / Labels whose terminal contribution succeeded.</param>
/// <param name="HeuristicDominanceUsed">是否使用过未认证支配剪枝 / Whether uncertified dominance pruning occurred.</param>
public sealed record PathSearchResult<S, A>(
    IReadOnlyList<PathLabel<S, A>> Paths,
    IReadOnlyList<PathLabel<S, A>> Labels,
    PathSearchCompletion Completion,
    int ExpandedLabels,
    int DominatedLabels,
    int TerminalCount,
    bool HeuristicDominanceUsed = false) where A : class where S : class {
    /// <summary>
    /// 搜索是否穷尽且未使用不确定剪枝或路径校验过滤 / Whether the search is exhaustive without uncertified pruning or trace
    /// rejection.
    /// </summary>
    public bool Complete => Completion == PathSearchCompletion.Exhausted;

    /// <summary>
    /// 回溯完整标签链 / Backtrack a complete label chain.
    /// </summary>
    /// <remarks>
    /// 前驱链一旦与标签数组对不上（索引越界或指向的对象不是同一条），返回空链而不是尽力拼一条：拼出来的半截路径会让上游
    /// 以为找到了一条更短路线，而它实际上并不存在。
    /// If the predecessor chain disagrees with the label array (index out of range or a different object), an empty chain is
    /// returned rather than a best-effort one: a truncated path would make the caller believe it found a shorter route that does
    /// not exist.
    /// </remarks>
    /// <param name="path">终止标签 / Terminal label.</param>
    /// <returns>有序标签 / Ordered labels.</returns>
    public IReadOnlyList<PathLabel<S, A>> Trace(PathLabel<S, A> path) {
        ArgumentNullException.ThrowIfNull(path);
        var result = new List<PathLabel<S, A>>();
        PathLabel<S, A>? current = path;
        int traversed = 0;
        while (current is not null) {
            if (traversed >= Labels.Count || Labels[current.Index] != current) {
                return Array.Empty<PathLabel<S, A>>();
            }
            result.Add(current);
            int? predecessor = current.Predecessor;
            if (predecessor is { } index && index >= Labels.Count) {
                return Array.Empty<PathLabel<S, A>>();
            }
            current = predecessor is { } next ? Labels[next] : null;
            traversed += 1;
        }
        result.Reverse();
        return result;
    }
}

/// <summary>
/// 无求解器依赖的资源标签搜索 / Solver-independent resource-label search.
/// </summary>
public sealed class PathSearchContext {
    /// <summary>
    /// 执行有限域搜索；输出列限制不截断搜索 / Search a finite domain; output limits do not truncate the search.
    /// </summary>
    /// <remarks>
    /// `maximumResults` 只限制**返回**多少条路径，绝不改变搜索本身：若用它截断搜索，"返回了 N 条"就会被误读成"只有 N 条
    /// 负列"，而实际上还有更多没被找出来。收敛判定因此永远基于 `Completion`，而不是返回条数。
    /// `maximumResults` only bounds **how many** paths are returned and never changes the search itself: truncating the search by
    /// it would turn "returned N paths" into "only N negative columns exist" when more were never explored. Convergence is
    /// therefore always judged from `Completion`, never from the returned count.
    /// </remarks>
    /// <typeparam name="N">节点载荷类型 / Node payload type.</typeparam>
    /// <typeparam name="A">弧载荷类型 / Arc payload type.</typeparam>
    /// <typeparam name="S">业务资源状态类型 / Business resource state type.</typeparam>
    /// <param name="problem">路径问题 / Path problem.</param>
    /// <param name="budget">搜索预算 / Search budget.</param>
    /// <param name="maximumResults">返回路径数上限 / Output path limit.</param>
    /// <returns>路径和完成状态 / Paths and completion state.</returns>
    public Result<PathSearchResult<S, A>, ErrorCode, Error<ErrorCode>> Search<N, A, S>(
        PathProblem<N, A, S> problem,
        PathSearchBudget? budget = null,
        int maximumResults = int.MaxValue) where A : class where S : class {
        ArgumentNullException.ThrowIfNull(problem);
        PathSearchBudget effectiveBudget = budget ?? new PathSearchBudget();
        NetworkGraph<N, A> graph = problem.Graph;

        if (graph.IndexOf(problem.Source) is null || maximumResults < 0
            || effectiveBudget.MaximumLabels < 1 || effectiveBudget.MaximumLabelsPerNode < 1
            || (problem.MaximumSteps is { } steps && steps < 0)) {
            return NetworkSchedulingErrors.Failure<PathSearchResult<S, A>>(
                "路径请求或搜索预算无效 / Invalid path request or search budget");
        }
        if (!problem.AllowedNode(problem.Source)) {
            return Results.Ok(Empty<S, A>());
        }

        NetworkGraph<N, A> searchGraph = graph
            .FilteredNodes(node => problem.AllowedNode(graph.NodeId(node)))
            .Filtered(problem.AllowedArc);
        IReadOnlyList<NetworkNodeId>? topology = new NetworkTopology<N, A>(searchGraph).TopologicalOrder();
        Dictionary<NetworkNodeId, int>? topologicalPosition = topology is null
            ? null
            : topology.Select((id, position) => (id, position)).ToDictionary(pair => pair.id, pair => pair.position);
        if (!problem.Elementary && problem.MaximumSteps is null && topology is null) {
            return NetworkSchedulingErrors.Failure<PathSearchResult<S, A>>(
                "循环路径必须声明有限业务访问域 / Cyclic walks require a finite domain bound");
        }

        S? startState;
        Result<S?, ErrorCode, Error<ErrorCode>> startContribution =
            problem.StartContribution(problem.Source, problem.Initial);
        if (startContribution is Failed<S?, ErrorCode, Error<ErrorCode>> startFailure) {
            return Results.Failed<PathSearchResult<S, A>>(startFailure.Error);
        }
        startState = startContribution.Value;
        if (startState is null) {
            return Results.Ok(Empty<S, A>());
        }

        var labels = new List<PathLabel<S, A>> {
            new(0, problem.Source, startState, null, null,
                new HashSet<NetworkNodeId> { problem.Source }, 0),
        };
        var active = new Dictionary<NetworkNodeId, List<int>> {
            [problem.Source] = new List<int> { 0 },
        };
        var removed = new HashSet<int>();
        var queue = new PriorityQueue<int, (int Position, int Index)>(
            Comparer<(int Position, int Index)>.Create((left, right) => {
                int positionComparison = left.Position.CompareTo(right.Position);
                return positionComparison != 0 ? positionComparison : left.Index.CompareTo(right.Index);
            }));
        queue.Enqueue(0, (PositionOf(0), 0));
        var terminals = new List<int>();
        int expanded = 0;
        int dominated = 0;
        int terminalCount = 0;
        bool heuristicDominanceUsed = false;
        PathSearchCompletion completion = PathSearchCompletion.Exhausted;
        bool totalBudgetReached = false;

        int PositionOf(int index) =>
            topologicalPosition is not null && topologicalPosition.TryGetValue(labels[index].Node, out int position)
                ? position
                : index;

        bool Dominates(PathLabel<S, A> first, PathLabel<S, A> second) {
            if (problem.Terminal(first.Node, first.State) != problem.Terminal(second.Node, second.State)) {
                return false;
            }
            if (problem.Elementary && !second.Visited.IsSupersetOf(first.Visited)) {
                return false;
            }
            if (problem.MaximumSteps is not null && first.Steps > second.Steps) {
                return false;
            }
            return problem.Dominates is not null && problem.Dominates(first, second);
        }

        List<PathLabel<S, A>> TraceOf(int index) {
            var result = new List<PathLabel<S, A>>();
            int? current = index;
            while (current is { } position) {
                PathLabel<S, A> label = labels[position];
                result.Add(label);
                current = label.Predecessor;
            }
            result.Reverse();
            return result;
        }

        while (queue.Count > 0) {
            if (effectiveBudget.ShouldStop()) {
                completion = PathSearchCompletion.Interrupted;
                break;
            }
            int index = queue.Dequeue();
            if (removed.Contains(index)) {
                continue;
            }
            PathLabel<S, A> label = labels[index];

            if (problem.Terminal(label.Node, label.State)) {
                if (active.TryGetValue(label.Node, out List<int>? bucket)) {
                    bucket.Remove(index);
                }
                Result<S?, ErrorCode, Error<ErrorCode>> contribution = problem.TerminalContribution(label);
                if (contribution is Failed<S?, ErrorCode, Error<ErrorCode>> contributionFailure) {
                    return Results.Failed<PathSearchResult<S, A>>(contributionFailure.Error);
                }
                if (contribution.Value is { } contributed) {
                    labels[index] = label with { State = contributed };
                    terminalCount += 1;
                    Result<bool, ErrorCode, Error<ErrorCode>> validation = problem.Validate(TraceOf(index));
                    if (validation is Failed<bool, ErrorCode, Error<ErrorCode>> validationFailure) {
                        return Results.Failed<PathSearchResult<S, A>>(validationFailure.Error);
                    }
                    if (validation.Value) {
                        terminals.Add(index);
                    } else if (completion == PathSearchCompletion.Exhausted) {
                        completion = PathSearchCompletion.UncertifiedValidation;
                    }
                }
                continue;
            }

            if (problem.MaximumSteps is { } maximumSteps && label.Steps >= maximumSteps) {
                continue;
            }
            expanded += 1;
            foreach (A arc in searchGraph.Outgoing(label.Node)) {
                if (effectiveBudget.ShouldStop()) {
                    completion = PathSearchCompletion.Interrupted;
                    break;
                }
                NetworkNodeId next = searchGraph.To(arc);
                if (problem.Elementary && label.Visited.Contains(next)) {
                    continue;
                }
                Result<S?, ErrorCode, Error<ErrorCode>> transition = problem.Transition(label, arc);
                if (transition is Failed<S?, ErrorCode, Error<ErrorCode>> transitionFailure) {
                    return Results.Failed<PathSearchResult<S, A>>(transitionFailure.Error);
                }
                if (transition.Value is not { } state) {
                    continue;
                }
                var visited = new HashSet<NetworkNodeId>(label.Visited) { next };
                var candidate = new PathLabel<S, A>(
                    labels.Count, next, state, index, arc, visited, label.Steps + 1);

                if (!active.TryGetValue(next, out List<int>? atNode)) {
                    atNode = new List<int>();
                    active[next] = atNode;
                }
                Func<PathLabel<S, A>, PathLabel<S, A>, bool>? dominance = problem.Dominates;
                if (dominance is not null && atNode.Any(existing => Dominates(labels[existing], candidate))) {
                    dominated += 1;
                    if (!problem.DominanceIsExact) {
                        heuristicDominanceUsed = true;
                        if (completion == PathSearchCompletion.Exhausted) {
                            completion = PathSearchCompletion.UncertifiedDominance;
                        }
                    }
                    continue;
                }
                if (labels.Count >= effectiveBudget.MaximumLabels) {
                    completion = PathSearchCompletion.BudgetLimit;
                    totalBudgetReached = true;
                    break;
                }
                if (dominance is not null) {
                    var obsolete = atNode.Where(existing => Dominates(candidate, labels[existing])).ToList();
                    removed.UnionWith(obsolete);
                    atNode.RemoveAll(obsolete.Contains);
                    dominated += obsolete.Count;
                    if (obsolete.Count > 0 && !problem.DominanceIsExact) {
                        heuristicDominanceUsed = true;
                        if (completion == PathSearchCompletion.Exhausted) {
                            completion = PathSearchCompletion.UncertifiedDominance;
                        }
                    }
                }
                labels.Add(candidate);
                atNode.Add(candidate.Index);
                if (atNode.Count > effectiveBudget.MaximumLabelsPerNode) {
                    completion = PathSearchCompletion.BudgetLimit;
                    int worst = problem.Order is null
                        ? candidate.Index
                        : atNode.MaxBy(existing => labels[existing].State, problem.Order);
                    atNode.Remove(worst);
                    removed.Add(worst);
                }
                if (!removed.Contains(candidate.Index)) {
                    queue.Enqueue(candidate.Index, (PositionOf(candidate.Index), candidate.Index));
                }
            }
            if (completion == PathSearchCompletion.Interrupted || totalBudgetReached) {
                break;
            }
        }

        IEnumerable<PathLabel<S, A>> selected = terminals
            .Where(index => !removed.Contains(index))
            .Select(index => labels[index])
            .Where(label => problem.Accept(label.State));
        if (problem.Order is not null) {
            selected = selected.OrderBy(label => label.State, problem.Order);
        }
        return Results.Ok(new PathSearchResult<S, A>(
            selected.Take(maximumResults).ToList(),
            labels.ToList(),
            completion,
            expanded,
            dominated,
            terminalCount,
            heuristicDominanceUsed));
    }

    /// <summary>构造一个"什么也没找到但已穷尽"的结果 / Build an exhausted-but-empty result.</summary>
    /// <typeparam name="S">业务资源状态类型 / Business resource state type.</typeparam>
    /// <typeparam name="A">弧载荷类型 / Arc payload type.</typeparam>
    /// <returns>空结果 / The empty result.</returns>
    private static PathSearchResult<S, A> Empty<S, A>() where A : class where S : class =>
        new(
            Array.Empty<PathLabel<S, A>>(),
            Array.Empty<PathLabel<S, A>>(),
            PathSearchCompletion.Exhausted,
            0, 0, 0);
}
