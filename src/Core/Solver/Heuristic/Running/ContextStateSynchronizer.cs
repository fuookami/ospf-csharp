// 上下文状态同步器 / The cross-context state synchroniser.
// 与固定 Kotlin （7769 B）对应。

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// 上下文状态同步器 / The cross-context state synchroniser.
/// </summary>
/// <remarks>
/// 把"另一个上下文"产出的个体并回本地状态。每种状态各有自己的并回规则，因此没有统一抽象——这是刻意的：
/// 强行统一会让四种状态的差异藏进参数里，**而参数一错就表现为"同步之后收敛变慢"**。
/// Merges individuals produced by "another context" back into the local state. Each state has its own merge rule and therefore no shared
/// abstraction — deliberately: forcing one would bury the four states' differences in parameters, **and a wrong parameter shows up only as
/// "convergence got slower after syncing"**.
/// **`particles` 把外来个体写进从末尾数起的槽位**，而替换列表已按"个人最优"升序排好，所以**最差的槽位先被替换**。
/// 从头部写会让最好的粒子被外来个体顶掉，**而种群规模、速度、最优值看起来全部正常**。
/// **`particles` writes incoming individuals into slots counted from the end**, and the replacement list is already sorted by "personal
/// best" ascending, so **the worst slots are replaced first**. Writing from the head would evict the best particles, **while the population
/// size, velocities and best values all still look normal**.
/// **速度的来源粒子是"与外来个体距离最近的那一个"**，而不是对应的槽位。用对应槽位会让速度与实际的移动量无关，
/// **于是粒子上场时就带着一个方向错误的冲量**。
/// **The velocity comes from "the particle nearest to the incoming individual"**, not from the matching slot. Using the matching slot makes
/// the velocity unrelated to the actual displacement, **so the particle enters with an impulse pointing the wrong way**.
/// **速度算不出来时整体失败，而不是"跳过一个"**。跳过会让调用方拿到一个"部分同步"的状态却以为同步成功，
/// **于是两个上下文从此各自演化而不自知**。
/// **When the velocity cannot be computed the whole merge fails, rather than "skipping one"**. Skipping hands the caller a partially
/// Sync
/// **`clpso` 复用 `particles` 的规则，然后把学习对象重置成"该粒子自己的下标"**（`List(size) { index }` 闭包里取的是外层
/// `mapIndexed` 的 `index`，不是列表元素下标）。沿用这一点是有意的：它让同步后的粒子拥有一个"确定但无意义"的学习起点，
/// 由后续的停滞刷新去纠正。改成随机下标会让同步这一步额外消耗随机数，**而两个上下文的随机流就此错位**。
/// **`clpso` reuses `particles`' rule and then resets the exemplars to "the particle's own index"** (its `List(size) { index }`
/// captures the outer `mapIndexed` `index`, not the element index). Copying that is deliberate: it gives the synced particle a
/// "deterministic but meaningless" learning start that the later stagnation refresh corrects. Using random indices would make the sync
/// step consume entropy, **desynchronising the two contexts' random streams**.
/// **`shade` 把"被挤出去的旧个体"放进外部档案**，并按 `takeLast` 只保留最近若干个。这正是 SHADE 档案的含义（最近被淘汰的父代）；
/// 放"被接受的个体"会让档案变成第二个种群，**而收敛曲线只是"覆盖度更好"**。
/// **`shade` puts "the evicted old individuals" into the external archive** and keeps only the most recent ones via `takeLast`. That is
/// exactly the meaning of the SHADE archive (recently replaced parents); storing accepted individuals would make it a second population,
/// **while the convergence curve merely looks "better spread"**.
/// **`cmaEs` 的协方差除以 `initialSigma²` 并在对角加 `1e-10`**。不除会让协方差与步长的量纲错配，
/// **于是下一步的采样尺度与外来个体的实际分布无关**；不加对角则可能在退化方向上得到奇异矩阵。
/// **`cmaEs`' covariance divides by `initialSigma²` and adds `1e-10` on the diagonal.** Without the division the covariance and the step
/// size disagree in scale, **so the next sample's spread is unrelated to the incoming individuals' actual distribution**; without the
/// diagonal term a degenerate direction can yield a singular matrix.
/// **`cmaEs` 在只有 1 个外来个体时保留原协方差**。用 1 个样本算样本协方差会得到全零矩阵，
/// **于是下一次采样退化成"在原地采一个点"，而代数仍在推进**。
/// **`cmaEs` keeps the previous covariance when only one individual arrives.** Sample covariance over one sample is all zeros,
/// **so the next sample degenerates to "one point at the mean" while the generation counter keeps advancing**.
/// **有效性检查包含"已有个体的解必须有效"**。只检查外来个体会让一个有缺陷的本地状态在同步时被"洗白"，
/// **于是缺陷继续存在却再也不会被任何一道门禁发现**。
/// **The validity check also covers "the existing individuals' solutions"**. Checking only the incoming ones would launder a defective local
/// state during the sync, **so the defect survives while no gate can find it any more**.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class ContextStateSynchronizer<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly SearchProblem<ObjValue, V> _problem;

    /// <summary>
    /// 构造同步器 / Construct the synchroniser.
    /// </summary>
    /// <param name="problem">搜索问题 / The search problem.</param>
    /// <param name="maxVelocity">速度上限 / The velocity limit.</param>
    /// <param name="initialSigma">初始步长 / The initial step size.</param>
    public ContextStateSynchronizer(
        SearchProblem<ObjValue, V> problem,
        double maxVelocity = 10000.0,
        double initialSigma = 0.3) {
        ArgumentNullException.ThrowIfNull(problem);
        _problem = problem;
        MaxVelocity = maxVelocity;
        InitialSigma = initialSigma;
    }

    /// <summary>速度上限 / The velocity limit.</summary>
    public double MaxVelocity { get; }

    /// <summary>初始步长 / The initial step size.</summary>
    public double InitialSigma { get; }

    /// <summary>
    /// 由两代位置推算速度 / Derive a velocity from two positions.
    /// </summary>
    /// <param name="previous">上一代位置 / The previous positions.</param>
    /// <param name="current">当前位置 / The current positions.</param>
    /// <returns>速度，参数非法时失败 / The velocity, failing on invalid arguments.</returns>
    public Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> Velocity(
        IReadOnlyList<V> previous, IReadOnlyList<V> current) {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (previous.Count != current.Count
            || !double.IsFinite(MaxVelocity) || MaxVelocity <= 0.0) {
            return SearchCodecSupport.SearchInvalid<IReadOnlyList<Flt64>>(
                "invalid bridge positions/velocity scale");
        }
        var velocity = new List<Flt64>(current.Count);
        for (int index = 0; index < current.Count; index++) {
            double previousValue = previous[index].ToFlt64().ToDouble();
            double currentValue = current[index].ToFlt64().ToDouble();
            if (!double.IsFinite(previousValue) || !double.IsFinite(currentValue)) {
                return SearchCodecSupport.SearchInvalid<IReadOnlyList<Flt64>>(
                    "invalid bridge positions/velocity scale");
            }
            velocity.Add(new Flt64(global::System.Math.Clamp(
                currentValue - previousValue, -MaxVelocity, MaxVelocity)));
        }
        return Results.Ok<IReadOnlyList<Flt64>>(velocity);
    }

    /// <summary>
    /// 把外来个体并入粒子群状态 / Merge incoming individuals into a particle-swarm state.
    /// </summary>
    /// <param name="state">本地状态 / The local state.</param>
    /// <param name="incoming">外来个体 / The incoming individuals.</param>
    /// <returns>新状态，校验失败时失败 / The new state, failing on invalid input.</returns>
    public Result<IReadOnlyList<ParticleState<ObjValue, V>>, ErrorCode, Error<ErrorCode>> Particles(
        IReadOnlyList<ParticleState<ObjValue, V>> state,
        IReadOnlyList<IIndividual<ObjValue, V>> incoming) {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(incoming);
        if (!IsValid(incoming)) {
            return SearchCodecSupport.SearchInvalid<IReadOnlyList<ParticleState<ObjValue, V>>>(
                "invalid bridge imports");
        }
        foreach (ParticleState<ObjValue, V> particle in state) {
            if (!_problem.ValidSolution(particle.Individual.Solution)) {
                return SearchCodecSupport.SearchInvalid<IReadOnlyList<ParticleState<ObjValue, V>>>(
                    "invalid bridge imports");
            }
        }
        if (incoming.Count == 0 || state.Count == 0) {
            return Results.Ok<IReadOnlyList<ParticleState<ObjValue, V>>>(state);
        }

        var replacements = new List<ParticleState<ObjValue, V>>(state);
        replacements.Sort((lhs, rhs) =>
            HeuristicOrdering.CompareStrictly(
                _problem.Model, lhs.Best.Objective, rhs.Best.Objective));

        int count = global::System.Math.Min(incoming.Count, state.Count);
        for (int index = 0; index < count; index++) {
            IIndividual<ObjValue, V> candidate = incoming[index];
            ParticleState<ObjValue, V> source = NearestParticle(state, candidate);
            Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> motion =
                Velocity(source.Individual.Solution, candidate.Solution);
            if (motion is Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> failure) {
                return SearchCodecSupport.SearchInvalid<IReadOnlyList<ParticleState<ObjValue, V>>>(
                    failure.Error.Message);
            }
            IReadOnlyList<Flt64> velocity =
                ((Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>)motion).Value;
            IIndividual<ObjValue, V> best =
                HeuristicOrdering.CompareStrictly(
                    _problem.Model, source.Best.Objective, candidate.Objective) < 0
                    ? source.Best
                    : candidate;
            replacements[replacements.Count - 1 - index] = new ParticleState<ObjValue, V>(
                candidate, velocity, best);
        }
        return Results.Ok<IReadOnlyList<ParticleState<ObjValue, V>>>(replacements);
    }

    /// <summary>
    /// 把外来个体并入 CLPSO 状态 / Merge incoming individuals into a CLPSO state.
    /// </summary>
    /// <param name="state">本地状态 / The local state.</param>
    /// <param name="incoming">外来个体 / The incoming individuals.</param>
    /// <returns>新状态，校验失败时失败 / The new state, failing on invalid input.</returns>
    public Result<IReadOnlyList<ClpsoParticle<ObjValue, V>>, ErrorCode, Error<ErrorCode>> Clpso(
        IReadOnlyList<ClpsoParticle<ObjValue, V>> state,
        IReadOnlyList<IIndividual<ObjValue, V>> incoming) {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(incoming);
        if (incoming.Count == 0) {
            return Results.Ok<IReadOnlyList<ClpsoParticle<ObjValue, V>>>(state);
        }
        var particles = new List<ParticleState<ObjValue, V>>(state.Count);
        foreach (ClpsoParticle<ObjValue, V> particle in state) {
            particles.Add(new ParticleState<ObjValue, V>(
                particle.Individual, particle.Velocity, particle.Best));
        }
        Result<IReadOnlyList<ParticleState<ObjValue, V>>, ErrorCode, Error<ErrorCode>> merged =
            Particles(particles, incoming);
        if (merged is Failed<IReadOnlyList<ParticleState<ObjValue, V>>, ErrorCode, Error<ErrorCode>> failure) {
            return SearchCodecSupport.SearchInvalid<IReadOnlyList<ClpsoParticle<ObjValue, V>>>(
                failure.Error.Message);
        }
        IReadOnlyList<ParticleState<ObjValue, V>> synced =
            ((Ok<IReadOnlyList<ParticleState<ObjValue, V>>, ErrorCode, Error<ErrorCode>>)merged).Value;
        var result = new List<ClpsoParticle<ObjValue, V>>(synced.Count);
        for (int index = 0; index < synced.Count; index++) {
            ParticleState<ObjValue, V> particle = synced[index];
            // 学习对象全部取该粒子自己的下标——基线这里捕获的是外层下标，不是列表元素下标。
            // The exemplars all take the particle's own index — the code captures the outer index here, not the element index.
            var exemplars = new List<int>(particle.Individual.Solution.Count);
            for (int axis = 0; axis < particle.Individual.Solution.Count; axis++) {
                exemplars.Add(index);
            }
            result.Add(new ClpsoParticle<ObjValue, V>(
                particle.Individual, particle.Velocity, particle.Best, exemplars, 0));
        }
        return Results.Ok<IReadOnlyList<ClpsoParticle<ObjValue, V>>>(result);
    }

    /// <summary>
    /// 把外来个体并入 SHADE 状态 / Merge incoming individuals into a SHADE state.
    /// </summary>
    /// <param name="state">本地状态 / The local state.</param>
    /// <param name="incoming">外来个体 / The incoming individuals.</param>
    /// <returns>新状态，校验失败时失败 / The new state, failing on invalid input.</returns>
    public Result<ShadeState<ObjValue, V>, ErrorCode, Error<ErrorCode>> Shade(
        ShadeState<ObjValue, V> state,
        IReadOnlyList<IIndividual<ObjValue, V>> incoming) {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(incoming);
        if (!IsValid(incoming)) {
            return SearchCodecSupport.SearchInvalid<ShadeState<ObjValue, V>>("invalid SHADE imports");
        }

        var pool = new List<IIndividual<ObjValue, V>>(state.Population);
        foreach (IIndividual<ObjValue, V> individual in incoming) {
            bool duplicate = false;
            foreach (IIndividual<ObjValue, V> existing in pool) {
                if (SameSolution(existing.Solution, individual.Solution)) {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate) {
                pool.Add(individual);
            }
        }
        pool.Sort((lhs, rhs) =>
            HeuristicOrdering.CompareStrictly(_problem.Model, lhs.Objective, rhs.Objective));
        var population = new List<IIndividual<ObjValue, V>>(state.Population.Count);
        for (int index = 0; index < pool.Count && index < state.Population.Count; index++) {
            population.Add(pool[index]);
        }

        var displaced = new List<Solution<V>>();
        foreach (IIndividual<ObjValue, V> old in state.Population) {
            bool kept = false;
            foreach (IIndividual<ObjValue, V> current in population) {
                if (SameSolution(current.Solution, old.Solution)) {
                    kept = true;
                    break;
                }
            }
            if (!kept) {
                displaced.Add(new Solution<V>(old.Solution));
            }
        }

        var archive = new List<Solution<V>>(state.Archive);
        foreach (Solution<V> solution in displaced) {
            bool duplicate = false;
            foreach (Solution<V> existing in archive) {
                if (SameSolution(existing.Values, solution.Values)) {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate) {
                archive.Add(solution);
            }
        }
        int keep = global::System.Math.Min(state.Population.Count, archive.Count);
        archive = archive.GetRange(archive.Count - keep, keep);

        return Results.Ok<ShadeState<ObjValue, V>>(
            state with { Population = population, Archive = archive });
    }

    /// <summary>
    /// 把外来个体并入 CMA-ES 状态 / Merge incoming individuals into a CMA-ES state.
    /// </summary>
    /// <param name="state">本地状态 / The local state.</param>
    /// <param name="incoming">外来个体 / The incoming individuals.</param>
    /// <returns>新状态，校验失败时失败 / The new state, failing on invalid input.</returns>
    public Result<CmaEsState<ObjValue, V>, ErrorCode, Error<ErrorCode>> CmaEs(
        CmaEsState<ObjValue, V> state,
        IReadOnlyList<IIndividual<ObjValue, V>> incoming) {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(incoming);
        if (!IsValid(incoming) || !double.IsFinite(InitialSigma) || InitialSigma <= 0.0) {
            return SearchCodecSupport.SearchInvalid<CmaEsState<ObjValue, V>>(
                "invalid CMA bridge imports/scale");
        }
        if (incoming.Count == 0) {
            return Results.Ok<CmaEsState<ObjValue, V>>(state);
        }

        var ranked = new List<IIndividual<ObjValue, V>>(incoming);
        ranked.Sort((lhs, rhs) =>
            HeuristicOrdering.CompareStrictly(_problem.Model, lhs.Objective, rhs.Objective));
        int dimension = ranked[0].Solution.Count;
        foreach (IIndividual<ObjValue, V> individual in ranked) {
            if (individual.Solution.Count != dimension) {
                return SearchCodecSupport.SearchInvalid<CmaEsState<ObjValue, V>>(
                    "inconsistent CMA bridge dimensions");
            }
        }

        double[] mean = new double[dimension];
        for (int axis = 0; axis < dimension; axis++) {
            double total = 0.0;
            foreach (IIndividual<ObjValue, V> individual in ranked) {
                total += individual.Solution[axis].ToFlt64().ToDouble();
            }
            mean[axis] = total / ranked.Count;
            if (!double.IsFinite(mean[axis])) {
                return SearchCodecSupport.SearchInvalid<CmaEsState<ObjValue, V>>(
                    "invalid CMA bridge mean");
            }
        }

        IReadOnlyList<IReadOnlyList<double>> covariance;
        if (ranked.Count < 2) {
            covariance = state.Covariance;
        } else {
            var built = new List<IReadOnlyList<double>>(dimension);
            for (int row = 0; row < dimension; row++) {
                var rowValues = new List<double>(dimension);
                for (int column = 0; column < dimension; column++) {
                    double sum = 0.0;
                    foreach (IIndividual<ObjValue, V> individual in ranked) {
                        sum += (individual.Solution[row].ToFlt64().ToDouble() - mean[row])
                            * (individual.Solution[column].ToFlt64().ToDouble() - mean[column]);
                    }
                    double value = (sum / (ranked.Count - 1)) / (InitialSigma * InitialSigma);
                    if (row == column) {
                        value += 1.0e-10;
                    }
                    if (!double.IsFinite(value)) {
                        return SearchCodecSupport.SearchInvalid<CmaEsState<ObjValue, V>>(
                            "invalid CMA bridge covariance");
                    }
                    rowValues.Add(value);
                }
                built.Add(rowValues);
            }
            covariance = built;
        }

        IIndividual<ObjValue, V> best = state.Best is not null
            && HeuristicOrdering.CompareStrictly(
                _problem.Model, state.Best.Objective, ranked[0].Objective) < 0
            ? state.Best
            : ranked[0];

        var zeroes = new List<double>(dimension);
        for (int index = 0; index < dimension; index++) {
            zeroes.Add(0.0);
        }
        return Results.Ok<CmaEsState<ObjValue, V>>(
            new CmaEsState<ObjValue, V>(
                mean, InitialSigma, covariance, new List<double>(zeroes),
                new List<double>(zeroes), 0, best));
    }

    /// <summary>
    /// 校验外来个体 / Validate the incoming individuals.
    /// </summary>
    /// <param name="incoming">外来个体 / The incoming individuals.</param>
    /// <returns>是否全部有效 / Whether all of them are valid.</returns>
    private bool IsValid(IReadOnlyList<IIndividual<ObjValue, V>> incoming) {
        foreach (IIndividual<ObjValue, V> individual in incoming) {
            if (!_problem.ValidSolution(individual.Solution)
                || !_problem.Valid(individual.Objective)
                || !_problem.Admissible(individual.Solution)) {
                return false;
            }
            foreach (V value in individual.Solution) {
                if (!double.IsFinite(value.ToFlt64().ToDouble())) {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// 找与外来个体距离最近的粒子 / Find the particle nearest to the incoming individual.
    /// </summary>
    /// <param name="state">本地状态 / The local state.</param>
    /// <param name="candidate">外来个体 / The incoming individual.</param>
    /// <returns>最近的粒子 / The nearest particle.</returns>
    private static ParticleState<ObjValue, V> NearestParticle(
        IReadOnlyList<ParticleState<ObjValue, V>> state,
        IIndividual<ObjValue, V> candidate) {
        ParticleState<ObjValue, V> nearest = state[0];
        double bestDistance = double.PositiveInfinity;
        foreach (ParticleState<ObjValue, V> particle in state) {
            double distance = 0.0;
            for (int index = 0; index < candidate.Solution.Count; index++) {
                double delta = candidate.Solution[index].ToFlt64().ToDouble()
                    - particle.Individual.Solution[index].ToFlt64().ToDouble();
                distance += delta * delta;
            }
            if (distance < bestDistance) {
                bestDistance = distance;
                nearest = particle;
            }
        }
        return nearest;
    }

    /// <summary>
    /// 判断两个解是否相同 / Decide whether two solutions are identical.
    /// </summary>
    /// <param name="lhs">左解 / The left solution.</param>
    /// <param name="rhs">右解 / The right solution.</param>
    /// <returns>是否相同 / Whether they are identical.</returns>
    private static bool SameSolution(IReadOnlyList<V> lhs, IReadOnlyList<V> rhs) {
        if (lhs.Count != rhs.Count) {
            return false;
        }
        for (int index = 0; index < lhs.Count; index++) {
            if (lhs[index].ToFlt64().ToDouble() != rhs[index].ToFlt64().ToDouble()) {
                return false;
            }
        }
        return true;
    }
}
