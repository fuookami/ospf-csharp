// NSGA-II 运行器 / The NSGA-II runner.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// 多目标搜索结果 / A multi-objective search result.
/// </summary>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Front">最终前沿 / The final front.</param>
/// <param name="Objectives">对应的目标向量 / The matching objective vectors.</param>
/// <param name="Iteration">产生该前沿的迭代状态 / The iteration state that produced the front.</param>
public sealed record MultiObjectiveResult<V>(
    IReadOnlyList<Solution<V>> Front,
    IReadOnlyList<IReadOnlyList<Flt64>> Objectives,
    Iteration Iteration)
    where V : struct, IRealNumber<V>, INumberField<V>;


/// <summary>
/// NSGA-II 运行器 / The NSGA-II runner.
/// </summary>
/// <remarks>
/// **目标向量在<strong>进入种群之前</strong>过滤**：空向量与非有限分量都判为失败。放进来会让支配比较在 `NaN` 上返回假，
/// **于是 `NaN` 个体永远不会被支配、永远不会被淘汰，最终占据整个前沿而看起来"多样"**。
/// **Objective vectors are filtered <strong>before they enter the population</strong>**: empty vectors and non-finite components both count
/// as failures. Letting them in makes dominance comparisons return false on `NaN`, **so the `NaN` individuals are never dominated, never
/// culled, and end up occupying the whole front while looking "diverse"**.
/// **每一代的父母是"当前个体 + 它的下一个邻居"（环形）**，不是随机配对。随机配对会让交叉算子在两代之间没有任何结构，
/// **而收敛曲线只是"更抖"**。
/// **Each generation's parents are "the current individual and its next neighbour" (cyclic)**, not a random pairing. Random pairing leaves
/// the crossover operator with no structure across generations, **and the convergence curve merely looks "more jittery"**.
/// **变异是对"每个当前个体"逐一施加的，作用在父代本身而不是交叉后代上**。作用在交叉后代上会让探索完全依赖交叉的产出，
/// **而一旦交叉产出退化（例如父母相同），整个种群就再也没有新的方向**。
/// **Mutation is applied to "each current individual" one by one, on the parents themselves rather than on the crossover offspring.**
/// Applying it to the offspring makes exploration depend entirely on the crossover's output, **and once that output degenerates (e.g.
/// identical parents) the population has no new direction at all**.
/// **最后一层用拥挤距离逐个挑最大的**，而不是按距离排序后取前若干个。逐次挑选在距离有并列时行为不同，
/// 本实现取的是"每次剩余集合里的最大者"。
/// **The last layer picks the largest crowding distance one at a time**, not "sort by distance and take the first few". Repeated selection
/// behaves differently on ties, and this implementation picks "the maximum of the remaining set" each time.
/// **"按约束分组选幸存者"由构造参数 `constraints` 接进来**。`constraintSurvivors` 先向模型要约束处理器，
/// 有则按可行组分组选择、或按随机排序取前 `amount` 个；本实现把处理器放在问题旁边而非模型上（既定形状差异，见 `SearchConstraints`），
/// 所以由调用方构造 `ConstrainedSearch` 传进来。**不传时退回纯拥挤距离**，与接线前的行为一致。
/// **接线前那一版有个真实后果**：不可行个体照样能靠拥挤距离活下来，而这种情况**没有任何判据会红**——
/// 因为"没有可行性概念"的种群看起来同样"多样"。所以判据必须能证明处理器**真的被调用**，而不是只看结果个数。
/// **"Select survivors by constraint group" comes in through the `constraints` constructor argument** . Its `constraintSurvivors` asks the model for
/// a constraint handler and, if present, selects per feasibility group or takes the top `amount` by stochastic ranking; this implementation keeps the handler next to the
/// problem rather than on the model (an established shape difference, see `SearchConstraints`), so the caller builds a `ConstrainedSearch` and passes it in.
/// **Without it the runner falls back to crowding distance**, matching the pre-wiring behaviour.
/// **The pre-wiring version had a real consequence**: infeasible individuals survived on crowding distance just the same, and **no criterion would have gone red** —
/// a population with no notion of feasibility looks equally "diverse". That is why the criterion has to prove the handler is **actually called**, not just count results.
/// </remarks>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public class Nsga2Runner<V>
    : AbstractRunner<IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>>, IReadOnlyList<Flt64>, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly Func<Solution<V>, IReadOnlyList<Flt64>> _objectivesOf;
    private readonly IMutationOperator _mutation;
    private readonly ISolutionUpdate _update;
    private readonly Func<Flt64> _entropy;

    /// <summary>
    /// 构造 NSGA-II 运行器 / Construct the NSGA-II runner.
    /// </summary>
    /// <param name="objectivesOf">目标向量提取器 / The objective-vector extractor.</param>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <param name="update">解更新算子 / The solution-update operator.</param>
    /// <param name="mutation">变异算子 / The mutation operator.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="mutationRate">变异率 / The mutation rate.</param>
    /// <param name="archiveAmount">档案容量 / The archive capacity.</param>
    /// <param name="observers">迭代观察者 / The iteration observers.</param>
    /// <param name="constraints">约束处理（可选，见类备注）/ The constraint handling, optional (see the class remarks).</param>
    public Nsga2Runner(
        Func<Solution<V>, IReadOnlyList<Flt64>> objectivesOf,
        IDominance dominance,
        ISolutionUpdate update,
        IMutationOperator mutation,
        Func<Flt64> entropy,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        int size = 20,
        Flt64? mutationRate = null,
        int archiveAmount = 100,
        IReadOnlyList<IIterationObserverContract>? observers = null,
        ConstrainedSearch<IReadOnlyList<Flt64>, V>? constraints = null)
        : base(new ParetoSearchArchive<V>(archiveAmount, dominance), stopping, converter, observers) {
        ArgumentNullException.ThrowIfNull(objectivesOf);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(entropy);
        if (size <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), "population size must be positive");
        }
        if (archiveAmount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(archiveAmount), "archive amount must be positive");
        }
        _objectivesOf = objectivesOf;
        _mutation = mutation;
        _update = update;
        _entropy = entropy;
        Constraints = constraints;
        Dominance = dominance;
        Size = size;
        MutationRate = mutationRate ?? new Flt64(0.1);
    }

    /// <summary>名称 / The name.</summary>
    public virtual string Name => "nsga-ii";

    /// <summary>支配判据 / The dominance predicate.</summary>
    public IDominance Dominance { get; }

    /// <summary>种群规模 / The population size.</summary>
    public int Size { get; }

    /// <summary>约束处理，缺省时按纯拥挤距离选幸存者 / The constraint handling; without it survivors are picked by crowding distance alone.</summary>
    public ConstrainedSearch<IReadOnlyList<Flt64>, V>? Constraints { get; }

    /// <summary>变异率 / The mutation rate.</summary>
    public Flt64 MutationRate { get; }

    /// <inheritdoc/>
    protected override Task<IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>>> Initialize(
        IAbstractCallBackModelInterface<object?, IReadOnlyList<Flt64>, V> model,
        Iteration iteration) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        var population = new List<IIndividual<IReadOnlyList<Flt64>, V>>(Size);
        foreach (IReadOnlyList<V> provided in model.InitialSolutions((ulong)Size)) {
            IIndividual<IReadOnlyList<Flt64>, V>? individual =
                Evaluate(model, new Solution<V>(provided));
            if (individual is null) {
                continue;
            }
            population.Add(individual);
            if (population.Count == Size) {
                break;
            }
        }
        return Task.FromResult<IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>>>(population);
    }

    /// <inheritdoc/>
    protected override async Task<IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>>> Advance(
        IAbstractCallBackModelInterface<object?, IReadOnlyList<Flt64>, V> model,
        Iteration iteration,
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> state) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Count == 0) {
            return state;
        }
        var entries = new List<IIndividual<IReadOnlyList<Flt64>, V>>(state);
        entries.AddRange(await Breed(model, iteration, state));
        // **有约束处理就走分组选择，没有就走纯拥挤距离**（接线）：`advance` 调的正是 `constraintSurvivors`，
        // 而本实现此前把处理器挡在运行器外面，**等于每一代都丢弃了可行性信息**——不可行的个体照样能靠拥挤距离活下来。
        //
        // **With constraint handling, survivors are picked per feasibility group; without it, by crowding distance alone** (wired): `advance`
        // calls `constraintSurvivors`, and this implementation kept the handler outside the runner — **so every generation discarded feasibility information**, and infeasible
        // individuals survived on crowding distance just the same.
        return Constraints is null
            ? Select(entries, Size)
            : SearchConstraints.SelectSurvivors(Constraints, entries, Size, Select);
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> MembersOf(
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> state) => state;

    /// <summary>
    /// 从导入的候选里挑选存活个体 / Select survivors from imported candidates.
    /// </summary>
    /// <param name="candidates">候选 / The candidates.</param>
    /// <returns>存活个体 / The survivors.</returns>
    public IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> SelectImported(
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> candidates) {
        ArgumentNullException.ThrowIfNull(candidates);
        // **导入入口和推进入口走同一条选择路径**：`selectImported` 调的也是 `constraintSurvivors`。
        // 只接 `Advance` 会有个隐蔽的后果——**外部导入的种群按拥挤距离筛，而内部每一代按可行性筛**，
        // 两处规则不同却都不报错，症状只会表现为"导入之后收敛变差"。
        //
 // **The import entry and the advance entry take the same selection path**: the implementation `selectImported` calls `constraintSurvivors` too. Wiring
        // only `Advance` has a quiet consequence — **imported populations are filtered by crowding distance while each internal generation is filtered by
        // feasibility**, and two different rules that never complain look exactly like "convergence got worse after importing".
        return Constraints is null
            ? Select(candidates, Size)
            : SearchConstraints.SelectSurvivors(Constraints, candidates, Size, Select);
    }

    /// <summary>
    /// 按非支配分层与拥挤距离选择 / Select by non-dominated layering and crowding distance.
    /// </summary>
    /// <param name="candidates">候选 / The candidates.</param>
    /// <param name="amount">目标数量 / The target amount.</param>
    /// <returns>选中的个体 / The selected individuals.</returns>
    protected virtual IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> Select(
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> candidates,
        int amount) {
        if (candidates.Count <= amount) {
            return candidates;
        }
        IReadOnlyList<IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>>> fronts =
            FastNonDominatedSort.Sort(
                candidates, individual => individual.Objective, Dominance);
        var selected = new List<IIndividual<IReadOnlyList<Flt64>, V>>(amount);
        foreach (IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> front in fronts) {
            if (selected.Count + front.Count <= amount) {
                selected.AddRange(front);
                continue;
            }
            // 最后一层：逐个挑拥挤距离最大的，维持前沿的覆盖度。
            // Last layer: pick the largest crowding distance one at a time to preserve the front's spread.
            var objectives = new List<IReadOnlyList<Flt64>>(front.Count);
            foreach (IIndividual<IReadOnlyList<Flt64>, V> individual in front) {
                objectives.Add(individual.Objective);
            }
            IReadOnlyList<double> distances = CrowdingDistance.Assign(objectives);
            var order = new List<int>(front.Count);
            for (int index = 0; index < front.Count; index++) {
                order.Add(index);
            }
            int remaining = amount - selected.Count;
            for (int round = 0; round < remaining; round++) {
                int bestPosition = 0;
                double bestDistance = double.NegativeInfinity;
                for (int position = 0; position < order.Count; position++) {
                    double distance = distances[order[position]];
                    if (distance > bestDistance) {
                        bestDistance = distance;
                        bestPosition = position;
                    }
                }
                selected.Add(front[order[bestPosition]]);
                order.RemoveAt(bestPosition);
            }
            break;
        }
        return selected;
    }

    /// <summary>
    /// 交叉与变异 / Crossover and mutation.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="iteration">迭代状态 / The iteration state.</param>
    /// <param name="population">当前种群 / The current population.</param>
    /// <returns>新产出的个体 / The newly produced individuals.</returns>
    private async Task<List<IIndividual<IReadOnlyList<Flt64>, V>>> Breed(
        IAbstractCallBackModelInterface<object?, IReadOnlyList<Flt64>, V> model,
        Iteration iteration,
        IReadOnlyList<IIndividual<IReadOnlyList<Flt64>, V>> population) {
        var produced = new List<IIndividual<IReadOnlyList<Flt64>, V>>();
        for (int index = 0; index < population.Count; index++) {
            var parents = new List<IIndividual<IReadOnlyList<Flt64>, V>> {
                population[index],
                population[(index + 1) % population.Count],
            };
            IReadOnlyList<Solution<V>> offspring = _update.Update<
                object?, IReadOnlyList<Flt64>, V, IIndividual<IReadOnlyList<Flt64>, V>>(
                iteration, parents, model, Converter, _entropy);
            foreach (Solution<V> solution in offspring) {
                IIndividual<IReadOnlyList<Flt64>, V>? individual = Evaluate(model, solution);
                if (individual is not null) {
                    produced.Add(individual);
                }
            }
        }
        for (int index = 0; index < population.Count; index++) {
            Solution<V> mutated = _mutation.Invoke<
                V, object?, IReadOnlyList<Flt64>, IIndividual<IReadOnlyList<Flt64>, V>>(
                iteration, population[index], model, Converter, _entropy, MutationRate);
            IIndividual<IReadOnlyList<Flt64>, V>? individual = Evaluate(model, mutated);
            if (individual is not null) {
                produced.Add(individual);
            }
        }
        await Task.CompletedTask;
        return produced;
    }

    /// <summary>
    /// 评估一个解的目标向量 / Evaluate one solution's objective vector.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="solution">解 / The solution.</param>
    /// <returns>个体，目标向量无效时返回 null / The individual, or null when the objective vector is invalid.</returns>
    private IIndividual<IReadOnlyList<Flt64>, V>? Evaluate(
        IAbstractCallBackModelInterface<object?, IReadOnlyList<Flt64>, V> model,
        Solution<V> solution) {
        IReadOnlyList<Flt64> objectives = _objectivesOf(solution);
        if (objectives.Count == 0) {
            return null;
        }
        foreach (Flt64 objective in objectives) {
            if (!double.IsFinite(objective.ToDouble())) {
                return null;
            }
        }
        return new SolutionWithFitness<IReadOnlyList<Flt64>, V>(solution.Values, objectives, 0.0);
    }
}
