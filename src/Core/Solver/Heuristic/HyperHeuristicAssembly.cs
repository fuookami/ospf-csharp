// 超启发式的算子绑定与工作单元装配 / Operator bindings and worker assembly for the hyper-heuristic.
// 对应固定 Kotlin （47241 B）里 `open` 的装配段：
// 每个算法一条分支问题、一条独立随机流、一组算子绑定、一个会话。
// Assembly part: one branch problem, one independent random
// stream, one set of operator bindings and one session per algorithm.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 一个算法的工作单元 / The working unit of one algorithm.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Id">算法标识 / The algorithm identifier.</param>
/// <param name="Problem">分支问题 / The branch problem.</param>
/// <param name="Random">独立随机流 / The independent random stream.</param>
/// <param name="Session">会话 / The session.</param>
/// <param name="Initialization">初始化模式 / The initialisation mode.</param>
public sealed record HyperHeuristicWorker<O, V>(
    string Id,
    SearchProblem<O, V> Problem,
    SearchRandom Random,
    ISearchSession<O, V> Session,
    InitializationMode Initialization)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 按算法配置组装算子绑定 / Assemble operator bindings from an algorithm configuration.
/// </summary>
/// <remarks>
/// **配置里点名的算子必须逐个解析成功，且必须适用于问题的编码**。把解析失败的算子悄悄丢掉，
/// 会让算法用"剩余的算子"继续跑——**而配置上写着那个算子**，事后从运行轨迹里根本看不出它从未被执行。
///
/// **Every operator named in the configuration must resolve, and must suit the problem's encoding.** Quietly dropping an operator that failed to
/// resolve lets the algorithm carry on with the remaining ones — **while the configuration names the dropped one**, and nothing in the run trace ever
/// shows it was never executed.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public static class HyperHeuristicBindings<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 组装一组算子绑定 / Assemble one set of operator bindings.
    /// </summary>
    /// <param name="registry">搜索注册表 / The search registry.</param>
    /// <param name="algorithm">算法配置 / The algorithm configuration.</param>
    /// <param name="problem">分支问题 / The branch problem.</param>
    /// <param name="random">随机流 / The random stream.</param>
    /// <param name="learning">是否学习 / Whether it learns.</param>
    /// <returns>算子绑定 / The operator bindings.</returns>
    public static Result<OperatorBindings, ErrorCode, Error<ErrorCode>> Build(
        SearchRegistry<O, V> registry,
        AlgorithmConfig algorithm,
        SearchProblem<O, V> problem,
        SearchRandom random,
        bool learning) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(random);
        SearchEncoding encoding = problem.Encoding;

        Result<OperatorPool<ISolutionUpdate>, ErrorCode, Error<ErrorCode>> updates =
            Pool(registry, algorithm.Selection, algorithm.Updates ?? Array.Empty<ComponentSpec>(),
                spec => registry.Update(spec, encoding), random, learning, "update");
        if (updates is Failed<OperatorPool<ISolutionUpdate>, ErrorCode, Error<ErrorCode>> updateFailure) {
            return Results.Failed<OperatorBindings>(updateFailure.Error);
        }
        Result<OperatorPool<IMutationOperator>, ErrorCode, Error<ErrorCode>> mutations =
            Pool(registry, algorithm.Selection, algorithm.Mutations ?? Array.Empty<ComponentSpec>(),
                spec => registry.Mutation(spec, encoding), random, learning, "mutation");
        if (mutations is Failed<OperatorPool<IMutationOperator>, ErrorCode, Error<ErrorCode>> mutationFailure) {
            return Results.Failed<OperatorBindings>(mutationFailure.Error);
        }
        Result<OperatorPool<INeighborhood>, ErrorCode, Error<ErrorCode>> neighborhoods =
            Pool(registry, algorithm.Selection, algorithm.Neighborhoods ?? Array.Empty<ComponentSpec>(),
                spec => registry.Neighborhood(spec, encoding), random, learning, "neighborhood");
        if (neighborhoods is Failed<OperatorPool<INeighborhood>, ErrorCode, Error<ErrorCode>> neighborhoodFailure) {
            return Results.Failed<OperatorBindings>(neighborhoodFailure.Error);
        }

        var parameters = new Dictionary<string, OperatorPool<IParameterSource>>(StringComparer.Ordinal);
        if (algorithm.Parameters is not null) {
            foreach ((string name, IReadOnlyList<ComponentSpec> specs) in algorithm.Parameters) {
                Result<OperatorPool<IParameterSource>, ErrorCode, Error<ErrorCode>> pool = Pool(
                    registry, algorithm.Selection, specs, registry.Parameter, random, learning,
                    "parameter " + name);
                if (pool is Failed<OperatorPool<IParameterSource>, ErrorCode, Error<ErrorCode>> parameterFailure) {
                    return Results.Failed<OperatorBindings>(parameterFailure.Error);
                }
                parameters[name] = ((Ok<OperatorPool<IParameterSource>, ErrorCode, Error<ErrorCode>>)pool).Value;
            }
        }

        return Results.Ok(new OperatorBindings(
            updates is null ? null : ((Ok<OperatorPool<ISolutionUpdate>, ErrorCode, Error<ErrorCode>>)updates).Value,
            mutations is null ? null : ((Ok<OperatorPool<IMutationOperator>, ErrorCode, Error<ErrorCode>>)mutations).Value,
            neighborhoods is null ? null : ((Ok<OperatorPool<INeighborhood>, ErrorCode, Error<ErrorCode>>)neighborhoods).Value,
            parameters));
    }

    /// <summary>
    /// 构造一个算子池 / Build one operator pool.
    /// </summary>
    /// <remarks>
    /// **空列表返回空池（`null`），而不是"默认算子"**。把空列表当成"没配置所以给个默认"会让
    /// `updates: []` 与"没有 `updates` 字段"产生完全相同的运行结果——**而前者是用户明确说了"不要更新算子"**。
    ///
    /// **An empty list yields no pool (`null`), not a "default operator"**. Treating an empty list as "unconfigured, so take a default" makes
    /// `updates: []` and a missing `updates` field behave identically — **while the former is the user explicitly saying "no update operator"**.
    /// </remarks>
    /// <typeparam name="T">算子类型 / The operator type.</typeparam>
    /// <param name="registry">注册表 / The registry.</param>
    /// <param name="selection">选择规格 / The selection spec.</param>
    /// <param name="specs">算子规格 / The operator specs.</param>
    /// <param name="resolve">解析函数 / The resolver.</param>
    /// <param name="random">随机流 / The random stream.</param>
    /// <param name="learning">是否学习 / Whether it learns.</param>
    /// <param name="kind">种类名 / The kind name.</param>
    /// <returns>算子池 / The operator pool.</returns>
    private static Result<OperatorPool<T>, ErrorCode, Error<ErrorCode>> Pool<T>(
        SearchRegistry<O, V> registry,
        ComponentSpec? selection,
        IReadOnlyList<ComponentSpec> specs,
        Func<ComponentSpec, Result<T, ErrorCode, Error<ErrorCode>>> resolve,
        SearchRandom random,
        bool learning,
        string kind) where T : class {
        if (specs.Count == 0) {
            return Results.Ok<OperatorPool<T>>(null!);
        }
        var candidates = new Dictionary<string, T>(StringComparer.Ordinal);
        var ids = new List<string>(specs.Count);
        foreach (ComponentSpec spec in specs) {
            Result<T, ErrorCode, Error<ErrorCode>> resolved = resolve(spec);
            if (resolved is Failed<T, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<OperatorPool<T>>(failure.Error);
            }
            string id = spec.Type + (candidates.ContainsKey(spec.Type) ? "#" + candidates.Count : "");
            candidates[id] = ((Ok<T, ErrorCode, Error<ErrorCode>>)resolved).Value;
            ids.Add(id);
        }
        Result<ISelectionPolicy, ErrorCode, Error<ErrorCode>> policy =
            registry.Selection(selection ?? HyperHeuristicDefaults.Selection, ids, learning);
        return policy is Failed<ISelectionPolicy, ErrorCode, Error<ErrorCode>> policyFailure
            ? Results.Failed<OperatorPool<T>>(policyFailure.Error)
            : Results.Ok(new OperatorPool<T>(kind, candidates,
                ((Ok<ISelectionPolicy, ErrorCode, Error<ErrorCode>>)policy).Value, random,
                new HashSet<string>(candidates.Keys, StringComparer.Ordinal)));
    }
}

/// <summary>
/// 工作单元的装配 / Assembling the working units.
/// </summary>
/// <remarks>
/// **每个算法必须拿到自己的模型与令牌表**。共用模型会让两个算法互相改写对方的求解状态，
/// **而两者的结果看上去都"正常"**——只是没有一次运行是可复现的。
///
/// **Every algorithm must get its own model and token table.** Sharing a model lets two algorithms overwrite each other's solving state,
/// **while both results look "normal"** — except that no run is reproducible.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public static class HyperHeuristicAssembly<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 装配全部工作单元 / Assemble every working unit.
    /// </summary>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="registry">注册表 / The registry.</param>
    /// <param name="random">随机流 / The random stream.</param>
    /// <returns>工作单元 / The working units.</returns>
    public static Result<IReadOnlyList<HyperHeuristicWorker<O, V>>, ErrorCode, Error<ErrorCode>> Build(
        HyperHeuristicConfig config,
        SearchRegistry<O, V> registry,
        SearchRandom random) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(random);
        PersistenceConfig persistence = config.Persistence ?? new PersistenceConfig();
        LearningConfig learning = config.Learning ?? new LearningConfig();
        Result<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>> rootResult =
            registry.Problem(config.Problem, random);
        if (rootResult is Failed<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>> rootFailure) {
            return Results.Failed<IReadOnlyList<HyperHeuristicWorker<O, V>>>(rootFailure.Error);
        }
        SearchProblem<O, V> root =
            ((Ok<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>>)rootResult).Value;

        var models = new List<object>();
        var tokenTables = new List<object>();
        models.Add(root.Model);
        tokenTables.Add(root.Model.Tokens);
        var workers = new List<HyperHeuristicWorker<O, V>>(config.Algorithms.Count);
        var ordered = new List<AlgorithmConfig>(config.Algorithms);
        ordered.Sort((lhs, rhs) => string.CompareOrdinal(lhs.Id, rhs.Id));
        foreach (AlgorithmConfig algorithm in ordered) {
            SearchRandom stream = random.Split("algorithm:" + algorithm.Id);
            Result<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>> branchResult =
                registry.Problem(config.Problem, stream);
            if (branchResult is Failed<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>> branchFailure) {
                Close(workers, root);
                return Results.Failed<IReadOnlyList<HyperHeuristicWorker<O, V>>>(
                    branchFailure.Error);
            }
            SearchProblem<O, V> branch =
                ((Ok<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>>)branchResult).Value;

            bool shared = Contains(models, branch.Model) || Contains(tokenTables, branch.Model.Tokens);
            if (shared
                || branch.Id != root.Id
                || branch.Fingerprint != root.Fingerprint
                || branch.MultiObjective != root.MultiObjective) {
                try {
                    branch.Model.Dispose();
                } catch (Exception) {
                    // 关闭失败不改变"装配失败"这一结论，也不必掩盖原始错误。
                    // A failing close does not change the "assembly failed" outcome and must not mask the original error.
                }
                Close(workers, root);
                return Results.Failed<IReadOnlyList<HyperHeuristicWorker<O, V>>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    shared
                        ? "problem factory shared a model or token table"
                        : "problem factory returned incompatible problem definitions"));
            }
            models.Add(branch.Model);
            tokenTables.Add(branch.Model.Tokens);

            if (persistence.Enabled && !registry.RecoverableProblem(config.Problem, branch)) {
                Close(workers, root);
                return Results.Failed<IReadOnlyList<HyperHeuristicWorker<O, V>>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "branch callbacks are not recoverable"));
            }

            Result<OperatorBindings, ErrorCode, Error<ErrorCode>> bindings =
                HyperHeuristicBindings<O, V>.Build(
                    registry, algorithm, branch, stream, learning.Mode != LearningMode.Frozen);
            if (bindings is Failed<OperatorBindings, ErrorCode, Error<ErrorCode>> bindingFailure) {
                Close(workers, root);
                return Results.Failed<IReadOnlyList<HyperHeuristicWorker<O, V>>>(bindingFailure.Error);
            }
            Result<ISearchSession<O, V>, ErrorCode, Error<ErrorCode>> session = registry.Create(
                algorithm, branch, stream,
                ((Ok<OperatorBindings, ErrorCode, Error<ErrorCode>>)bindings).Value);
            if (session is Failed<ISearchSession<O, V>, ErrorCode, Error<ErrorCode>> sessionFailure) {
                Close(workers, root);
                return Results.Failed<IReadOnlyList<HyperHeuristicWorker<O, V>>>(sessionFailure.Error);
            }
            ISearchSession<O, V> created =
                ((Ok<ISearchSession<O, V>, ErrorCode, Error<ErrorCode>>)session).Value;
            workers.Add(new HyperHeuristicWorker<O, V>(
                algorithm.Id, branch, stream, created, algorithm.Initialization));
            if (persistence.Enabled
                && created.Snapshot() is Failed<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>) {
                Close(workers, root);
                return Results.Failed<IReadOnlyList<HyperHeuristicWorker<O, V>>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "algorithm " + algorithm.Id + " contains non-recoverable state"));
            }
        }
        return Results.Ok<IReadOnlyList<HyperHeuristicWorker<O, V>>>(workers);
    }

    /// <summary>按引用判断是否已收录 / Whether an object is already recorded, by reference.</summary>
    /// <param name="items">已收录对象 / The recorded objects.</param>
    /// <param name="candidate">待查对象 / The candidate.</param>
    /// <returns>是否已收录 / Whether it is recorded.</returns>
    private static bool Contains(List<object> items, object candidate) {
        foreach (object item in items) {
            if (ReferenceEquals(item, candidate)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 装配失败时关闭已建立的资源 / Close what was already built when assembly fails.
    /// </summary>
    /// <param name="workers">已建立的工作单元 / The established working units.</param>
    /// <param name="root">根问题 / The root problem.</param>
    private static void Close(List<HyperHeuristicWorker<O, V>> workers, SearchProblem<O, V> root) {
        foreach (HyperHeuristicWorker<O, V> worker in workers) {
            try {
                worker.Session.Close();
                worker.Problem.Model.Dispose();
            } catch (Exception) {
                // 清理阶段的异常不能覆盖真正的失败原因。
                // An exception during cleanup must not mask the real failure reason.
            }
        }
        try {
            root.Model.Dispose();
        } catch (Exception) {
            // 同上 / As above.
        }
    }
}
