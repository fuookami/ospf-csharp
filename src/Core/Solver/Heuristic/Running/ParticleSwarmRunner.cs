// 粒子群运行器 / The particle-swarm runner.
// 与固定 Kotlin 基线 `runner/ParticleSwarmRunner.kt` 对应。
// Mirrors the pinned baseline's `runner/ParticleSwarmRunner.kt`.

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
/// 粒子状态 / A particle state.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Individual">当前个体 / The current individual.</param>
/// <param name="Velocity">速度向量 / The velocity vector.</param>
/// <param name="Best">个体历史最优 / The personal best.</param>
public sealed record ParticleState<ObjValue, V>(
    IIndividual<ObjValue, V> Individual,
    IReadOnlyList<Flt64> Velocity,
    IIndividual<ObjValue, V> Best)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 粒子群运行器 / The particle-swarm runner.
/// </summary>
/// <remarks>
/// **`globalBest` 每步只从"当前个体"里选一次，且只选一次**。为每个粒子各选一次会让同一步内不同粒子朝不同"全局最优"飞行，
/// **而算法名字仍然叫 PSO**；从"个体历史最优"里选则会让社会项退化成"个人项的重复"，
/// **于是群体行为消失、只剩若干个独立爬山**。
/// **`globalBest` is chosen once per step, from the current individuals only.** Choosing it per particle makes particles within one step
/// fly toward different "global optima", **while the algorithm is still called PSO**; choosing it from the personal bests degrades the
/// social term into a duplicate of the personal one, **so the swarm behaviour disappears and only independent hill climbers remain**.
/// **速度必须先按 `maxVelocity` 截断，再用于位置更新**。只截断位置会让速度无限累积，
/// **于是粒子在若干步之后"瞬移"，而轨迹上看不出是哪一步出的问题**。
/// **The velocity must be clamped to `maxVelocity` before it moves the position.** Clamping only the position lets the velocity accumulate
/// without bound, **so the particle teleports after a few steps and the trajectory gives no hint which step went wrong**.
/// **位置必须按变量的上下界截断**。不截断会让粒子飞出可搜索域，**而目标值仍然算得出来**——于是搜索"成功"在一个非法解上。
/// **The position must be clamped to the variable bounds.** Without it a particle leaves the search domain, **while the objective still
/// evaluates** — so the search "succeeds" on an illegal solution.
/// **评估失败时整条粒子状态原样保留，包括速度**。把速度清零会让"这一步没评估成功"变成"这个粒子从此静止"，
/// **而停滞看起来像收敛**。
/// **A failed evaluation keeps the whole particle state, velocity included.** Zeroing the velocity turns "this step failed to evaluate" into
/// "this particle is now frozen", **and the stagnation looks like convergence**.
/// **个人最优只在严格改进时更新**。用"不差于"会让个人最优被一个等价解反复替换，
/// **而检查点里记录的"历史最优"其实只是"最近一个等价解"**。
/// **The personal best updates only on strict improvement.** Using "not worse" lets an equivalent solution repeatedly overwrite it, **so the
/// "historical best" recorded in a checkpoint is really just "the most recent equivalent solution"**.
/// **两个随机数按"个人项在前、社会项在后"的顺序抽取**。颠倒顺序不会改变分布，却会让同一随机流下的轨迹与基线不同，
/// **而差异无法从结果里归因**。
/// **The two random draws happen in the order personal-then-social.** Swapping them changes no distribution, yet makes the trajectory under
/// the same random stream differ from the baseline, **and the difference cannot be attributed from the result**.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class ParticleSwarmRunner<ObjValue, V>
    : AbstractRunner<List<ParticleState<ObjValue, V>>, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IParameterSource _inertia;
    private readonly IParameterSource _personalWeight;
    private readonly IParameterSource _socialWeight;
    private readonly IInitializer _initializer;
    private readonly IFitnessEvaluator _fitnessEvaluator;
    private readonly Func<int, Flt64> _initialVelocity;
    private readonly Func<Flt64> _entropy;

    /// <summary>
    /// 构造粒子群运行器 / Construct the particle-swarm runner.
    /// </summary>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="archive">档案 / The archive.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="size">粒子数 / The particle count.</param>
    /// <param name="inertia">惯性权重 / The inertia weight.</param>
    /// <param name="personalWeight">个人权重 / The personal weight.</param>
    /// <param name="socialWeight">社会权重 / The social weight.</param>
    /// <param name="maxVelocity">速度上限 / The velocity limit.</param>
    /// <param name="initialVelocity">初始速度 / The initial velocity.</param>
    /// <param name="initializer">初始化器 / The initializer.</param>
    /// <param name="fitnessEvaluator">适应度评估器 / The fitness evaluator.</param>
    /// <param name="observers">迭代观察者 / The iteration observers.</param>
    public ParticleSwarmRunner(
        Func<Flt64> entropy,
        IArchive<ObjValue, V> archive,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        int size = 20,
        IParameterSource? inertia = null,
        IParameterSource? personalWeight = null,
        IParameterSource? socialWeight = null,
        Flt64? maxVelocity = null,
        Func<int, Flt64>? initialVelocity = null,
        IInitializer? initializer = null,
        IFitnessEvaluator? fitnessEvaluator = null,
        IReadOnlyList<IIterationObserverContract>? observers = null)
        : base(archive, stopping, converter, observers) {
        ArgumentNullException.ThrowIfNull(entropy);
        if (size <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), "swarm size must be positive");
        }
        _entropy = entropy;
        _inertia = inertia ?? new ConstantSource(new Flt64(0.4));
        _personalWeight = personalWeight ?? new ConstantSource(new Flt64(2.0));
        _socialWeight = socialWeight ?? new ConstantSource(new Flt64(2.0));
        _initializer = initializer ?? ModelInitializer.Instance;
        _fitnessEvaluator = fitnessEvaluator ?? DefaultOnMissFitnessEvaluator.Instance;
        _initialVelocity = initialVelocity ?? (_ => Flt64.Zero);
        Size = size;
        MaxVelocity = maxVelocity ?? new Flt64(10000.0);
    }

    /// <summary>粒子数 / The particle count.</summary>
    public int Size { get; }

    /// <summary>速度上限 / The velocity limit.</summary>
    public Flt64 MaxVelocity { get; }

    /// <inheritdoc/>
    protected override Task<List<ParticleState<ObjValue, V>>> Initialize(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        IReadOnlyList<Solution<V>> solutions =
            _initializer.Generate<V, object?, ObjValue>(iteration, model, (ulong)Size, Converter);
        var particles = new List<ParticleState<ObjValue, V>>(Size);
        foreach (Solution<V> solution in solutions) {
            IIndividual<ObjValue, V>? individual = ToIndividual(model, solution);
            if (individual is null) {
                continue;
            }
            var velocity = new List<Flt64>(individual.Solution.Count);
            for (int index = 0; index < individual.Solution.Count; index++) {
                velocity.Add(_initialVelocity(index));
            }
            particles.Add(new ParticleState<ObjValue, V>(individual, velocity, individual));
            if (particles.Count == Size) {
                break;
            }
        }
        return Task.FromResult(particles);
    }

    /// <inheritdoc/>
    protected override Task<List<ParticleState<ObjValue, V>>> Advance(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration iteration,
        List<ParticleState<ObjValue, V>> state) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Count == 0) {
            return Task.FromResult(state);
        }

        // 全局最优：每步只从"当前个体"里选一次。
        // The global best: chosen once per step from the current individuals only.
        ParticleState<ObjValue, V> globalBest = state[0];
        for (int index = 1; index < state.Count; index++) {
            if (HeuristicOrdering.CompareStrictly(
                    model, state[index].Individual.Objective, globalBest.Individual.Objective) < 0) {
                globalBest = state[index];
            }
        }

        double weight = _inertia.Value(iteration).ToDouble();
        double personal = _personalWeight.Value(iteration).ToDouble();
        double social = _socialWeight.Value(iteration).ToDouble();
        double limit = MaxVelocity.ToDouble();

        var moved = new List<ParticleState<ObjValue, V>>(state.Count);
        foreach (ParticleState<ObjValue, V> particle in state) {
            int dimension = particle.Individual.Solution.Count;
            var velocity = new List<Flt64>(dimension);
            var values = new List<V>(dimension);
            for (int index = 0; index < dimension; index++) {
                double current = particle.Individual.Solution[index].ToFlt64().ToDouble();
                double personalTarget = particle.Best.Solution[index].ToFlt64().ToDouble();
                double globalTarget = globalBest.Individual.Solution[index].ToFlt64().ToDouble();
                // 先抽个人项、再抽社会项。
                // Draw the personal term first, then the social one.
                double personalDraw = _entropy().ToDouble();
                double socialDraw = _entropy().ToDouble();
                double speed = (weight * particle.Velocity[index].ToDouble())
                    + (personal * personalDraw * (personalTarget - current))
                    + (social * socialDraw * (globalTarget - current));
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
                // 评估失败：整条粒子状态原样保留，包括速度。
                // A failed evaluation keeps the whole particle state, velocity included.
                moved.Add(particle);
                continue;
            }
            bool improved = HeuristicOrdering.CompareStrictly(
                model, candidate.Objective, particle.Best.Objective) < 0;
            moved.Add(new ParticleState<ObjValue, V>(
                candidate, velocity, improved ? candidate : particle.Best));
        }
        return Task.FromResult(moved);
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<IIndividual<ObjValue, V>> MembersOf(
        List<ParticleState<ObjValue, V>> state) {
        var members = new List<IIndividual<ObjValue, V>>(state.Count);
        foreach (ParticleState<ObjValue, V> particle in state) {
            members.Add(particle.Individual);
        }
        return members;
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
