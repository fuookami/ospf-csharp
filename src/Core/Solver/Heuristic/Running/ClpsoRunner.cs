// 综合学习粒子群运行器 / The comprehensive-learning particle-swarm runner.
// 与固定 Kotlin 对应。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// 综合学习粒子 / A comprehensive-learning particle.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Individual">当前个体 / The current individual.</param>
/// <param name="Velocity">速度向量 / The velocity vector.</param>
/// <param name="Best">个体历史最优 / The personal best.</param>
/// <param name="Exemplars">逐维学习对象下标 / The per-dimension exemplar indices.</param>
/// <param name="Stagnation">停滞计数 / The stagnation counter.</param>
public sealed record ClpsoParticle<ObjValue, V>(
    IIndividual<ObjValue, V> Individual,
    IReadOnlyList<Flt64> Velocity,
    IIndividual<ObjValue, V> Best,
    IReadOnlyList<int> Exemplars,
    int Stagnation)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 综合学习粒子群运行器 / The comprehensive-learning particle-swarm runner.
/// </summary>
/// <remarks>
/// **学习对象来自"上一代"的个人最优，因此必须先快照成 `bests`**。不快照会让先处理的粒子用本代已更新的最优作为学习目标，
/// **于是同一代内互相污染，而结果仍然"合理"**——只是它对应的是一个别的算法。
/// **Exemplars come from the previous generation's personal bests, so they must be snapshotted into `bests` first.** Without the
/// snapshot, earlier particles' updates leak into later particles' learning targets within the same generation, **and the result still
/// looks plausible** — it merely corresponds to a different algorithm.
/// **`MembersOf` 返回个人最优，而不是当前个体**（这一点与 `ParticleSwarmRunner` 相反）。返回当前个体等于把 CLPSO 的"学习记忆"
/// 从档案里抹掉，**而档案看起来只是"收敛得更慢"**。
/// **`MembersOf` returns the personal bests, not the current individuals** (the opposite of `ParticleSwarmRunner`). Returning the current
/// individuals erases CLPSO's "learning memory" from the archive, **and the archive merely looks like it "converges more slowly"**.
/// **停滞超过 `stagnationLimit` 时把个人最优重置到当前位置**，并重新抽取学习对象、把停滞计数清零。不重置会让一个"卡住的"粒子
/// 永远朝同一个方向学习，**而停滞计数继续增长却没有任何行为变化**。
/// **Exceeding `stagnationLimit` resets the personal best to the current position**, re-draws the exemplars and zeroes the stagnation
/// counter. Skipping the reset leaves a stuck particle learning toward the same target forever, **while the stagnation counter keeps
/// growing with no behavioural change**.
/// **`stagnation >= refreshInterval` 时在扫描过程中重新抽取学习对象**。这条判据用的是**刷新前的**停滞计数，
/// 与"改进后清零"的顺序耦合：把它挪到更新之后再判断，会让一次刚刚成功的改进立刻触发刷新。
/// **`stagnation >= refreshInterval` re-draws the exemplars during the sweep.** The predicate reads the **pre-update** stagnation
/// counter and is order-coupled with "reset on improvement"; moving it after the update makes a just-succeeded improvement trigger a
/// refresh immediately.
/// **评估失败时保留个人最优、只把停滞计数加一，但速度仍然写成新算出的值**。这一点与 `ParticleSwarmRunner` 的"整条状态原样保留"
/// **不同**，是有意为之：CLPSO 的速度是"学习方向"，丢了它等于把这次失败的学习结果也丢掉。
/// **A failed evaluation keeps the personal best and only increments stagnation, while the velocity is still written with the freshly
/// computed value.** This differs from `ParticleSwarmRunner`'s "keep the whole state" — deliberately: CLPSO's
/// velocity is the "learning direction", and discarding it discards the failed step's learning too.
/// **`randomIndex` 在规模为 1 时返回 0 且不消耗随机数**。消耗随机数会让单粒子情形下的随机流位置与多粒子情形不同，
/// **而两种情形本该只在"抽样"上不同**。
/// **`randomIndex` returns 0 for a size of one without consuming entropy.** Consuming it would put the single-particle case at a
/// different position in the random stream than the multi-particle case, **and the two should differ only in the sampling**.
/// **速度必须先按 `maxVelocity` 截断**，位置再按变量上下界截断（同 `ParticleSwarmRunner` 的理由）。
/// **The velocity must be clamped to `maxVelocity` first**, then the position to the variable bounds (same reasoning as
/// `ParticleSwarmRunner`).
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class ClpsoRunner<ObjValue, V>
    : AbstractRunner<List<ClpsoParticle<ObjValue, V>>, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IParameterSource _inertia;
    private readonly IInitializer _initializer;
    private readonly IFitnessEvaluator _fitnessEvaluator;
    private readonly Flt64 _learningWeight;
    private readonly Func<Flt64> _entropy;

    /// <summary>
    /// 构造综合学习粒子群运行器 / Construct the CLPSO runner.
    /// </summary>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="archive">档案 / The archive.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="size">粒子数 / The particle count.</param>
    /// <param name="inertia">惯性权重 / The inertia weight.</param>
    /// <param name="learningWeight">学习系数 / The learning coefficient.</param>
    /// <param name="maxVelocity">速度上限 / The velocity limit.</param>
    /// <param name="refreshInterval">学习对象刷新间隔 / The exemplar refresh interval.</param>
    /// <param name="stagnationLimit">停滞重置阈值 / The stagnation reset threshold.</param>
    /// <param name="initializer">初始化器 / The initializer.</param>
    /// <param name="fitnessEvaluator">适应度评估器 / The fitness evaluator.</param>
    /// <param name="observers">迭代观察者 / The iteration observers.</param>
    public ClpsoRunner(
        Func<Flt64> entropy,
        IArchive<ObjValue, V> archive,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        int size = 20,
        IParameterSource? inertia = null,
        Flt64? learningWeight = null,
        Flt64? maxVelocity = null,
        int refreshInterval = 7,
        int stagnationLimit = 5,
        IInitializer? initializer = null,
        IFitnessEvaluator? fitnessEvaluator = null,
        IReadOnlyList<IIterationObserverContract>? observers = null)
        : base(archive, stopping, converter, observers) {
        ArgumentNullException.ThrowIfNull(entropy);
        if (size <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), "swarm size must be positive");
        }
        if (refreshInterval < 0 || stagnationLimit < 0) {
            throw new ArgumentOutOfRangeException(
                nameof(refreshInterval), "intervals must be nonnegative");
        }
        _entropy = entropy;
        _inertia = inertia ?? new LinearSource(new Flt64(0.9), new Flt64(0.4), 100UL);
        _learningWeight = learningWeight ?? new Flt64(1.49445);
        _initializer = initializer ?? ModelInitializer.Instance;
        _fitnessEvaluator = fitnessEvaluator ?? DefaultOnMissFitnessEvaluator.Instance;
        Size = size;
        RefreshInterval = refreshInterval;
        StagnationLimit = stagnationLimit;
        MaxVelocity = maxVelocity ?? new Flt64(10000.0);
    }

    /// <summary>粒子数 / The particle count.</summary>
    public int Size { get; }

    /// <summary>学习对象刷新间隔 / The exemplar refresh interval.</summary>
    public int RefreshInterval { get; }

    /// <summary>停滞重置阈值 / The stagnation reset threshold.</summary>
    public int StagnationLimit { get; }

    /// <summary>速度上限 / The velocity limit.</summary>
    public Flt64 MaxVelocity { get; }

    /// <inheritdoc/>
    protected override Task<List<ClpsoParticle<ObjValue, V>>> Initialize(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        IReadOnlyList<Solution<V>> solutions =
            _initializer.Generate<V, object?, ObjValue>(iteration, model, (ulong)Size, Converter);
        var particles = new List<ClpsoParticle<ObjValue, V>>(Size);
        foreach (Solution<V> solution in solutions) {
            IIndividual<ObjValue, V>? individual = ToIndividual(model, solution);
            if (individual is null) {
                continue;
            }
            var velocity = new List<Flt64>(individual.Solution.Count);
            var exemplars = new List<int>(individual.Solution.Count);
            for (int index = 0; index < individual.Solution.Count; index++) {
                velocity.Add(Flt64.Zero);
                exemplars.Add(RandomIndex(Size));
            }
            particles.Add(new ClpsoParticle<ObjValue, V>(
                individual, velocity, individual, exemplars, 0));
            if (particles.Count == Size) {
                break;
            }
        }
        return Task.FromResult(particles);
    }

    /// <inheritdoc/>
    protected override Task<List<ClpsoParticle<ObjValue, V>>> Advance(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration iteration,
        List<ClpsoParticle<ObjValue, V>> state) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Count == 0) {
            return Task.FromResult(state);
        }

        // 学习对象来自上一代的个人最优：先快照，避免同代内互相污染。
        // Exemplars come from the previous generation's personal bests: snapshot first to avoid intra-generation contamination.
        var bests = new List<IIndividual<ObjValue, V>>(state.Count);
        foreach (ClpsoParticle<ObjValue, V> particle in state) {
            bests.Add(particle.Best);
        }

        double weight = _inertia.Value(iteration).ToDouble();
        double coefficient = _learningWeight.ToDouble();
        double limit = MaxVelocity.ToDouble();

        var moved = new List<ClpsoParticle<ObjValue, V>>(state.Count);
        foreach (ClpsoParticle<ObjValue, V> particle in state) {
            int dimension = particle.Individual.Solution.Count;
            bool refresh = particle.Stagnation >= RefreshInterval;
            var velocity = new List<Flt64>(dimension);
            var values = new List<V>(dimension);
            var exemplars = new List<int>(dimension);
            for (int index = 0; index < dimension; index++) {
                int exemplarIndex = refresh
                    ? RandomIndex(state.Count)
                    : global::System.Math.Clamp(particle.Exemplars[index], 0, state.Count - 1);
                exemplars.Add(exemplarIndex);
                double current = particle.Individual.Solution[index].ToFlt64().ToDouble();
                double target = bests[exemplarIndex].Solution[index].ToFlt64().ToDouble();
                double speed = (weight * particle.Velocity[index].ToDouble())
                    + (coefficient * _entropy().ToDouble() * (target - current));
                speed = global::System.Math.Clamp(speed, -limit, limit);
                velocity.Add(new Flt64(speed));
                double? lower = NumericSupport.LowerBoundOf(model, index, Converter);
                double? upper = NumericSupport.UpperBoundOf(model, index, Converter);
                values.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(current + speed, lower, upper), Converter));
            }

            IIndividual<ObjValue, V>? candidate =
                ToIndividual(model, new Solution<V>(values));
            if (candidate is null) {
                // 保留个人最优，只把停滞计数加一；速度仍写入新算出的学习方向。
                // Keep the personal best, only increment stagnation; the velocity still records the fresh learning direction.
                moved.Add(new ClpsoParticle<ObjValue, V>(
                    particle.Individual, velocity, particle.Best,
                    particle.Exemplars, particle.Stagnation + 1));
                continue;
            }

            bool improved = HeuristicOrdering.CompareStrictly(
                model, candidate.Objective, particle.Best.Objective) < 0;
            if (improved) {
                moved.Add(new ClpsoParticle<ObjValue, V>(
                    candidate, velocity, candidate, particle.Exemplars, 0));
            } else if (particle.Stagnation + 1 > StagnationLimit) {
                // 长期停滞：把个人最优重置到当前位置，迫使粒子换一个学习方向。
                // Prolonged stagnation: reset the personal best to the current position to force a new learning direction.
                var freshExemplars = new List<int>(dimension);
                for (int index = 0; index < dimension; index++) {
                    freshExemplars.Add(RandomIndex(state.Count));
                }
                moved.Add(new ClpsoParticle<ObjValue, V>(
                    candidate, velocity, candidate, freshExemplars, 0));
            } else {
                moved.Add(new ClpsoParticle<ObjValue, V>(
                    candidate, velocity, particle.Best,
                    refresh ? exemplars : particle.Exemplars,
                    particle.Stagnation + 1));
            }
        }
        return Task.FromResult(moved);
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<IIndividual<ObjValue, V>> MembersOf(
        List<ClpsoParticle<ObjValue, V>> state) {
        // **返回个人最优**，而不是当前个体。
        // **Returns the personal bests**, not the current individuals.
        var members = new List<IIndividual<ObjValue, V>>(state.Count);
        foreach (ClpsoParticle<ObjValue, V> particle in state) {
            members.Add(particle.Best);
        }
        return members;
    }

    /// <summary>
    /// 抽一个学习对象下标 / Draw an exemplar index.
    /// </summary>
    /// <param name="amount">候选数量 / The candidate count.</param>
    /// <returns>下标 / The index.</returns>
    private int RandomIndex(int amount) {
        if (amount <= 1) {
            return 0;
        }
        int drawn = (int)(_entropy().ToDouble() * amount);
        return global::System.Math.Clamp(drawn, 0, amount - 1);
    }

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
