// 局部搜索更新算子（此前：模式搜索与组合器）/ Local-search update operators (pattern search and composites).
// The last four implementations of the: .

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 模式搜索更新，从当前最优解出发做坐标模式搜索 / Pattern-search update descending from the current best by coordinate patterns.
/// </summary>
/// <remarks>
/// **只从 `parents[0]` 出发**，因为它是父代组里最优的一个（父代按优度升序传入）。从每个父代各搜一次会让评估预算按种群规模放大。
/// **Descends from `parents[0]` only**, since it is the best of the group (parents arrive sorted ascending). Searching from every parent
/// would multiply the evaluation budget by the population size.
/// **参数非法时返回空列表**：`initialStep` 必须有限且为正，`contraction` 必须落在 `(0, 1)`，`sweeps` 必须为正。`contraction >= 1`
/// 会让网格永远不收缩，搜索在一整个平面上反复试探而**迭代计数看起来完全正常**。
/// **Invalid parameters yield an empty list**: `initialStep` must be finite and positive, `contraction` must lie in `(0, 1)`, and
/// `sweeps` must be positive. A `contraction >= 1` means the mesh never shrinks and the search keeps probing a whole plane **while the
/// iteration counter looks entirely normal**.
/// **差异（预算感知）**：`model is SearchEvaluation &amp;&amp; model.remaining &lt;= 2` 这样的提前退出这里没有；C# 侧在早先已把约束与预算
/// 从模型上剥离（`SearchConstraints` 取代模型装饰器），因此这里**没有**模型侧预算读取。调用方必须用 `SearchBudget` 包住整次调用；
/// 否则单次调用的评估量只受 `sweeps × 维数 × 2` 约束，一个宽网格就可能把整轮预算吃光。
/// **Divergence (budget awareness)**: an early exit via `model is SearchEvaluation &amp;&amp; model.remaining &lt;= 2` is not available;
/// the C# side detached constraints and budgets from the model in an earlier change (`SearchConstraints` replaced the model decorator), so there
/// is **no** model-side budget read here. The caller must wrap the whole call in a `SearchBudget`; otherwise the evaluation count per call
/// is bounded only by `sweeps * dimension * 2`, and a wide mesh can consume an entire run's budget.
/// </remarks>
/// <param name="InitialStep">初始网格步长 / The initial mesh step.</param>
/// <param name="Contraction">网格收缩系数 / The mesh contraction factor.</param>
/// <param name="Sweeps">扫描轮数 / The number of sweeps.</param>
public sealed record PatternSearchUpdate(double InitialStep, double Contraction, int Sweeps) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public PatternSearchUpdate() : this(0.1, 0.5, 32) { }

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(parents);
        if (parents.Count == 0) {
            return Array.Empty<Solution<V>>();
        }
        if (!double.IsFinite(InitialStep) || InitialStep <= 0
            || !(Contraction > 0) || Contraction >= 1
            || Sweeps <= 0) {
            return Array.Empty<Solution<V>>();
        }

        Solution<V> seed = new(parents[0].Solution);
        if (FitnessEvaluatorSupport.ToIndividual(model, seed) is not IIndividual<TObjValue, V> start) {
            return Array.Empty<Solution<V>>();
        }

        double[] mesh = new double[seed.Values.Count];
        for (int index = 0; index < seed.Values.Count; index++) {
            double? lower = NumericSupport.LowerBoundOf(model, index, converter);
            double? upper = NumericSupport.UpperBoundOf(model, index, converter);
            double width = lower is double low && upper is double high ? high - low : double.NaN;
            double localScale = global::System.Math.Max(
                global::System.Math.Abs(converter.FromValue(seed.Values[index]).ToDouble()), 1.0);
            mesh[index] = InitialStep
                * (double.IsFinite(width) && width > 0
                    ? global::System.Math.Min(width, localScale)
                    : localScale);
        }

        IIndividual<TObjValue, V> current = start;
        for (int sweep = 0; sweep < Sweeps; sweep++) {
            IReadOnlyList<V> baseSolution = current.Solution;
            for (int index = 0; index < seed.Values.Count; index++) {
                foreach (double sign in new[] { 1.0, -1.0 }) {
                    List<V> trial = new(current.Solution);
                    double? lower = NumericSupport.LowerBoundOf(model, index, converter);
                    double? upper = NumericSupport.UpperBoundOf(model, index, converter);
                    trial[index] = NumericSupport.AsValue(
                        NumericSupport.ClampTo(
                            converter.FromValue(current.Solution[index]).ToDouble() + (sign * mesh[index]),
                            lower, upper),
                        converter);
                    if (trial.SequenceEqual(current.Solution)) {
                        continue;
                    }
                    if (FitnessEvaluatorSupport.ToIndividual(model, new Solution<V>(trial))
                        is not IIndividual<TObjValue, V> evaluated) {
                        continue;
                    }
                    if (HeuristicOrdering.CompareStrictly(model, evaluated.Objective, current.Objective) < 0) {
                        current = evaluated;
                    }
                }
            }

            if (current.Solution.SequenceEqual(baseSolution)) {
                for (int index = 0; index < mesh.Length; index++) {
                    mesh[index] *= Contraction;
                }
                continue;
            }

            // 模式移动：沿"当前减基准"的方向再走同样的一步。只靠坐标试探收敛是线性的，模式移动把它抬到超线性。
            // Pattern move: step again along "current minus base". Coordinate probing alone converges linearly; the pattern move
            // lifts it toward superlinear.
            var pattern = new List<V>(current.Solution.Count);
            for (int index = 0; index < current.Solution.Count; index++) {
                double? lower = NumericSupport.LowerBoundOf(model, index, converter);
                double? upper = NumericSupport.UpperBoundOf(model, index, converter);
                pattern.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(
                        (2.0 * converter.FromValue(current.Solution[index]).ToDouble())
                            - converter.FromValue(baseSolution[index]).ToDouble(),
                        lower, upper),
                    converter));
            }
            if (pattern.SequenceEqual(current.Solution)) {
                continue;
            }
            if (FitnessEvaluatorSupport.ToIndividual(model, new Solution<V>(pattern))
                    is IIndividual<TObjValue, V> patternIndividual
                && HeuristicOrdering.CompareStrictly(model, patternIndividual.Objective, current.Objective) < 0) {
                current = patternIndividual;
            }
        }

        return new[] { new Solution<V>(current.Solution) };
    }
}

/// <summary>
/// 流水线组合更新，逐级串联多个更新算子 / Pipeline composite update chaining several update operators.
/// </summary>
/// <remarks>
/// **每一级之后必须重新评估并按优度排序**。不排序就把"上一级的输出顺序"当成"下一级的优度顺序"，而下游算子普遍依赖
/// "父代按优度升序传入"这一契约——**取前几个最好个体时会取到最差的那些**，且没有任何报错。
/// **Every stage must re-evaluate and re-sort by quality.** Without sorting, "the previous stage's output order" is mistaken for
/// "the next stage's quality order", while downstream operators rely on the "parents sorted ascending" contract — **taking the top few
/// then takes the worst ones**, with no error anywhere.
/// **评估失败的个体被丢弃**，而不是带着默认目标继续往下传。
/// **Individuals that fail evaluation are dropped**, not passed on with a default objective.
/// </remarks>
/// <param name="Stages">串联的更新算子 / The chained update operators.</param>
public sealed record PipelineCompositeUpdate(IReadOnlyList<ISolutionUpdate> Stages) : ISolutionUpdate {
    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(parents);
        ArgumentNullException.ThrowIfNull(Stages);

        var current = new List<IIndividual<TObjValue, V>>();
        foreach (T parent in parents) {
            if (FitnessEvaluatorSupport.ToIndividual(model, new Solution<V>(parent.Solution))
                is IIndividual<TObjValue, V> individual) {
                current.Add(individual);
            }
        }

        foreach (ISolutionUpdate stage in Stages) {
            if (current.Count == 0) {
                break;
            }
            IReadOnlyList<Solution<V>> produced = stage.Update<TObj, TObjValue, V, IIndividual<TObjValue, V>>(
                iteration, current, model, converter, entropy);
            var next = new List<IIndividual<TObjValue, V>>();
            foreach (Solution<V> solution in produced) {
                if (FitnessEvaluatorSupport.ToIndividual(model, solution)
                    is IIndividual<TObjValue, V> individual) {
                    next.Add(individual);
                }
            }
            next.Sort((lhs, rhs) => HeuristicOrdering.CompareStrictly(model, lhs.Objective, rhs.Objective));
            current = next;
        }

        return current.Select(individual => new Solution<V>(individual.Solution)).ToList();
    }
}

/// <summary>
/// 变邻域下降组合更新 / Variable-neighbourhood-descent composite update.
/// </summary>
/// <remarks>
/// **一旦某个邻域产生改进，邻域序号必须重置为 0**，否则搜索会一直停在靠后的邻域上，前面的邻域再也不会被访问——
/// 变邻域下降的核心就是"改进后回到最简单的邻域重新开始"。
/// **Once a neighbourhood yields an improvement the index must reset to 0**; otherwise the search stays on the later neighbourhoods and
/// the earlier ones are never revisited — resetting to the simplest neighbourhood after an improvement is the whole point of VND.
/// **`maxTrials` 是硬上限**。没有它，一个持续产生微小改进的邻域会让循环永不退出，**而每次改进看起来都合法**。
/// **`maxTrials` is a hard cap.** Without it, a neighbourhood that keeps producing tiny improvements makes the loop never exit,
/// **while every single improvement looks legitimate**.
/// </remarks>
/// <param name="Neighborhoods">邻域算子集合 / The neighbourhood operators.</param>
/// <param name="MaxTrials">最大尝试次数 / The maximum trial count.</param>
public sealed record VNDCompositeUpdate(IReadOnlyList<ISolutionUpdate> Neighborhoods, int MaxTrials)
    : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    /// <param name="neighborhoods">邻域算子集合 / The neighbourhood operators.</param>
    public VNDCompositeUpdate(IReadOnlyList<ISolutionUpdate> neighborhoods) : this(neighborhoods, 50) { }

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(parents);
        ArgumentNullException.ThrowIfNull(Neighborhoods);
        if (parents.Count == 0) {
            return Array.Empty<Solution<V>>();
        }
        if (FitnessEvaluatorSupport.ToIndividual(model, new Solution<V>(parents[0].Solution))
            is not IIndividual<TObjValue, V> seed) {
            return Array.Empty<Solution<V>>();
        }

        IIndividual<TObjValue, V> current = seed;
        int index = 0;
        int trials = 0;
        while (index < Neighborhoods.Count && trials++ < MaxTrials) {
            IReadOnlyList<Solution<V>> produced = Neighborhoods[index]
                .Update<TObj, TObjValue, V, IIndividual<TObjValue, V>>(
                    iteration, new[] { current }, model, converter, entropy);

            IIndividual<TObjValue, V>? candidate = null;
            foreach (Solution<V> solution in produced) {
                if (FitnessEvaluatorSupport.ToIndividual(model, solution)
                    is not IIndividual<TObjValue, V> individual) {
                    continue;
                }
                if (candidate is null
                    || HeuristicOrdering.CompareStrictly(model, individual.Objective, candidate.Objective) < 0) {
                    candidate = individual;
                }
            }

            if (candidate is not null
                && HeuristicOrdering.CompareStrictly(model, candidate.Objective, current.Objective) < 0) {
                current = candidate;
                index = 0;
            } else {
                index++;
            }
        }

        return new[] { new Solution<V>(current.Solution) };
    }
}

/// <summary>
/// 精英局部搜索包装，只对前若干个个体做局部搜索 / Elite local-search wrapper, searching the leading individuals only.
/// </summary>
/// <remarks>
/// **只搜索前 `Amount` 个个体，其余原样返回**。局部搜索的评估代价远高于普通更新；对全种群做一次局部搜索等于把整轮预算压缩到一代里，
/// **而收敛曲线看起来只是"这一代特别好"**。
/// **Only the leading `Amount` individuals are searched; the rest pass through unchanged.** Local search costs far more evaluations than a
/// plain update, and running it over the whole population squeezes an entire run's budget into one generation **while the convergence
/// curve merely looks like "a very good generation"**.
/// **输出顺序保持"被搜索的在前、原样的在后"**，与"父代按优度升序"的契约一致。
/// **The output keeps searched-first, unchanged-after order**, consistent with the "parents sorted ascending" contract.
/// </remarks>
/// <param name="LocalSearch">被包装的局部搜索算子 / The wrapped local-search operator.</param>
/// <param name="Amount">搜索的精英数量 / The number of elites to search.</param>
public sealed record EliteLocalSearchWrapper(ISolutionUpdate LocalSearch, int Amount) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    /// <param name="localSearch">被包装的局部搜索算子 / The wrapped local-search operator.</param>
    public EliteLocalSearchWrapper(ISolutionUpdate localSearch) : this(localSearch, 1) { }

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(parents);
        ArgumentNullException.ThrowIfNull(LocalSearch);
        int amount = global::System.Math.Clamp(Amount, 0, parents.Count);
        if (amount == 0) {
            return parents.Select(parent => new Solution<V>(parent.Solution)).ToList();
        }

        var elites = new List<IIndividual<TObjValue, V>>(amount);
        for (int index = 0; index < amount; index++) {
            elites.Add(parents[index]);
        }

        var children = new List<Solution<V>>(parents.Count);
        children.AddRange(LocalSearch.Update<TObj, TObjValue, V, IIndividual<TObjValue, V>>(
            iteration, elites, model, converter, entropy));
        for (int index = amount; index < parents.Count; index++) {
            children.Add(new Solution<V>(parents[index].Solution));
        }
        return children;
    }
}
