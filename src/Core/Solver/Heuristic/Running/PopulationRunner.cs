// 种群运行器 / The population runner.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// 种群运行器 / The population runner.
/// </summary>
/// <remarks>
/// **邻域内的父代必须先按优度升序排序**，再交给更新算子。交叉本身不关心顺序，而位置更新类算子（GWO 的 α/β/δ 等）依赖
/// "前 k 名即头狼"的约定；不排序会让同一份种群喂给不同算子时得到语义不同的结果，**而更新算子看不出任何异常**。
/// **Neighbourhood parents must be ranked by ascending quality** before being handed to the update operator. Crossover does not care about
/// the order, whereas position-update operators (GWO's α/β/δ, …) rely on "the first k are the leaders"; without the ranking the same
/// population means different things to different operators, **and the update operator notices nothing**.
/// **变异作用在整份父代上，而不是只作用在子代上**。只变异子代会让"变异率"退化成"对交叉产物的二次扰动"，
/// **而参数名仍然叫变异率**。
/// **Mutation applies to the whole parent generation, not just the offspring.** Mutating only the offspring degrades "mutation rate" into
/// "a second perturbation of the crossover products", **while the parameter is still called the mutation rate**.
/// **幸存者选择拿到的是父代 + 子代 + 变异体三者的并集**。只传入子代会让父代无法存活超过一代，
/// **而"精英保留"这一类选择策略因此永远不会被触发**。
/// **The survivor selection receives the union of parents, offspring and mutants.** Passing only the offspring prevents any parent from
/// surviving more than one generation, **so elite-preserving strategies are never exercised**.
/// **空种群原样返回**。继续推进会让初始化器被反复调用，**而"种群为空"这件事被伪装成"这一步没有进展"**。
/// **An empty population is returned unchanged.** Advancing anyway would call the initializer repeatedly, **disguising "the population is
/// empty" as "no progress this step"**.
/// **初始化取前 `size` 个成功评估的个体**。评估失败的个体被丢弃而不是补位；补位会让初始化器被调用第二次，
/// **而两次调用的随机流位置不同**，于是同一个种子在"有没有失败"两种情形下产出不同种群。
/// **Initialization takes the first `size` successfully evaluated individuals.** Failed evaluations are dropped rather than backfilled;
/// backfilling calls the initializer a second time, **at a different position in the random stream**, so the same seed yields different
/// populations depending on whether any evaluation failed.
/// **并行度沿用运行器的串行语义**。`SearchExecution.dispatcher` 并发产生子代与变异体；本实现按批次顺序执行，
/// 因为共享的模型回调并非线程安全，而"并行但结果不确定"比"串行但可重放"更难归因。
/// ponytail: 需要真正的并行时再引入按索引分片的调度，那时的正确性判据是"同一随机流下结果与串行一致"。
/// **Parallelism follows the runner's serial semantics.** This implementation uses `SearchExecution.dispatcher` to produce offspring and mutants
/// concurrently; this implementation executes in order, because the shared model callback is not thread-safe and "parallel but non-deterministic" is
/// harder to attribute than "serial but replayable".
/// ponytail: introduce index-sharded scheduling when real parallelism is needed; the correctness criterion then is "the same random stream
/// yields the same result as the serial path".
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class PopulationRunner<ObjValue, V> : AbstractRunner<List<IIndividual<ObjValue, V>>, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IInitializer _initializer;
    private readonly IFitnessEvaluator _fitnessEvaluator;
    private readonly ISolutionUpdate _update;
    private readonly IMutationOperator _mutation;
    private readonly ISurvivorSelection _survivor;
    private readonly ITopology _topology;
    private readonly Func<Flt64> _entropy;

    /// <summary>
    /// 构造种群运行器 / Construct the population runner.
    /// </summary>
    /// <param name="update">更新算子 / The update operator.</param>
    /// <param name="mutation">变异算子 / The mutation operator.</param>
    /// <param name="survivor">幸存者选择 / The survivor selection.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="archive">档案 / The archive.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="mutationRate">逐基因变异率 / The per-gene mutation rate.</param>
    /// <param name="initializer">初始化器 / The initializer.</param>
    /// <param name="fitnessEvaluator">适应度评估器 / The fitness evaluator.</param>
    /// <param name="topology">拓扑 / The topology.</param>
    /// <param name="observers">迭代观察者 / The iteration observers.</param>
    public PopulationRunner(
        ISolutionUpdate update,
        IMutationOperator mutation,
        ISurvivorSelection survivor,
        Func<Flt64> entropy,
        IArchive<ObjValue, V> archive,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        int size = 20,
        Flt64? mutationRate = null,
        IInitializer? initializer = null,
        IFitnessEvaluator? fitnessEvaluator = null,
        ITopology? topology = null,
        IReadOnlyList<IIterationObserverContract>? observers = null)
        : base(archive, stopping, converter, observers) {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(survivor);
        ArgumentNullException.ThrowIfNull(entropy);
        if (size <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), "population size must be positive");
        }
        _update = update;
        _mutation = mutation;
        _survivor = survivor;
        _entropy = entropy;
        _topology = topology ?? GlobalTopology.Instance;
        _initializer = initializer ?? ModelInitializer.Instance;
        _fitnessEvaluator = fitnessEvaluator ?? DefaultOnMissFitnessEvaluator.Instance;
        Size = size;
        MutationRate = mutationRate ?? new Flt64(0.1);
    }

    /// <summary>种群规模 / The population size.</summary>
    public int Size { get; }

    /// <summary>逐基因变异率 / The per-gene mutation rate.</summary>
    public Flt64 MutationRate { get; }

    /// <summary>拓扑 / The topology.</summary>
    public ITopology Topology => _topology;

    /// <inheritdoc/>
    protected override Task<List<IIndividual<ObjValue, V>>> Initialize(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        IReadOnlyList<Solution<V>> solutions =
            _initializer.Generate<V, object?, ObjValue>(iteration, model, (ulong)Size, Converter);
        var population = new List<IIndividual<ObjValue, V>>(Size);
        foreach (Solution<V> solution in solutions) {
            IIndividual<ObjValue, V>? individual = ToIndividual(model, solution);
            if (individual is null) {
                continue;
            }
            population.Add(individual);
            if (population.Count == Size) {
                break;
            }
        }
        return Task.FromResult(population);
    }

    /// <inheritdoc/>
    protected override Task<List<IIndividual<ObjValue, V>>> Advance(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration iteration,
        List<IIndividual<ObjValue, V>> state) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Count == 0) {
            return Task.FromResult(state);
        }

        var offspring = new List<IIndividual<ObjValue, V>>();
        for (int index = 0; index < state.Count; index++) {
            // 邻域父代先按优度升序排列：位置更新类算子依赖"前 k 名即头狼"。
            // Neighbourhood parents are ranked ascending: position-update operators rely on "the first k are the leaders".
            var parents = new List<IIndividual<ObjValue, V>>();
            foreach (int neighbour in _topology.Neighbours(index, state.Count)) {
                parents.Add(state[neighbour]);
            }
            parents.Sort((lhs, rhs) =>
                HeuristicOrdering.CompareStrictly(model, lhs.Objective, rhs.Objective));

            IReadOnlyList<Solution<V>> generated = _update.Update<
                object?, ObjValue, V, IIndividual<ObjValue, V>>(
                iteration, parents, model, Converter, _entropy);
            foreach (Solution<V> solution in generated) {
                IIndividual<ObjValue, V>? individual = ToIndividual(model, solution);
                if (individual is not null) {
                    offspring.Add(individual);
                }
            }
        }

        // 变异作用在整份父代上，而不是只作用在子代上。
        // Mutation applies to the whole parent generation, not just the offspring.
        foreach (IIndividual<ObjValue, V> individual in state) {
            Solution<V> mutated = _mutation.Invoke<V, object?, ObjValue, IIndividual<ObjValue, V>>(
                iteration, individual, model, Converter, _entropy, MutationRate);
            IIndividual<ObjValue, V>? mutant = ToIndividual(model, mutated);
            if (mutant is not null) {
                offspring.Add(mutant);
            }
        }

        // 幸存者选择拿到父代 + 子代 + 变异体的并集。
        // The survivor selection receives the union of parents, offspring and mutants.
        return Task.FromResult(new List<IIndividual<ObjValue, V>>(
            _survivor.Select<object?, ObjValue, V, IIndividual<ObjValue, V>>(
                model, state, offspring, Size)));
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<IIndividual<ObjValue, V>> MembersOf(
        List<IIndividual<ObjValue, V>> state) => state;

    /// <summary>
    /// 打开一个岛 / Open an island.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <returns>岛的初始种群 / The island's initial population.</returns>
    public Task<List<IIndividual<ObjValue, V>>> OpenIsland(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration) =>
        Initialize(model, iteration);

    /// <summary>
    /// 推进一步岛 / Advance an island one step.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="state">岛的种群 / The island's population.</param>
    /// <returns>新区种群 / The new island population.</returns>
    public Task<List<IIndividual<ObjValue, V>>> StepIsland(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration iteration,
        List<IIndividual<ObjValue, V>> state) =>
        Advance(model, iteration, state);

    /// <summary>
    /// 把一个解包装成个体 / Wrap a solution into an individual.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="solution">解 / The solution.</param>
    /// <returns>个体，评估失败时返回 null / The individual, or null when evaluation fails.</returns>
    private IIndividual<ObjValue, V>? ToIndividual(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Solution<V> solution) {
        ObjValue? objective = _fitnessEvaluator.Evaluate<object?, ObjValue, V>(model, solution);
        if (objective is null) {
            return null;
        }
        return new SolutionWithFitness<ObjValue, V>(solution.Values, objective, 0.0);
    }
}
