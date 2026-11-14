// 教案-学习、樽海鞘、引力搜索与萤火虫更新 / Teaching-learning, salp-swarm, gravitational-search and firefly updates.
// 与固定 Kotlin 基线 `operator/update/Tier2Updates.kt`（15155 B）对应。
// Mirrors the pinned baseline's `Tier2Updates.kt`.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 教学阶段更新 / Teaching-phase update.
/// </summary>
/// <remarks>
/// **教学因子必须用"四舍五入远离零"**。Kotlin 的 `Math.round` 是 half-up，而 C# 的 `Math.Round` 默认是**银行家舍入**：
/// `Math.Round(0.5) == 0`、`Math.Round(1.5) == 2`。教学因子是 `1 + round(unit * range)`，两种舍入会产出不同的整数序列，
/// 同一份种子的轨迹随之分叉——**而算法看起来仍然正常工作**。这里显式指定 `MidpointRounding.AwayFromZero`。
/// **The teaching factor must round half away from zero.** Kotlin's `Math.round` is half-up while C#'s `Math.Round` defaults to
/// **banker's rounding**: `Math.Round(0.5) == 0`, `Math.Round(1.5) == 2`. The factor is `1 + round(unit * range)`, so the two roundings
/// produce different integer sequences and the same seed yields a diverging trajectory — **while the algorithm still looks fine**. The
/// midpoint mode is pinned to `AwayFromZero` here.
/// **均值必须是全种群均值，不是精英均值**。用精英均值会让"向教师靠拢"的幅度变小，教学阶段退化成微调。
/// **The mean must be the whole population's, not the elites'.** An elite mean shrinks the pull toward the teacher and the teaching
/// phase degrades into fine-tuning.
/// </remarks>
/// <param name="TeachingFactorRange">教学因子取值范围 / The teaching-factor range.</param>
public sealed record TeachingLearningUpdate(double TeachingFactorRange) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public TeachingLearningUpdate() : this(2.0) { }

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

        IReadOnlyList<V> teacher = parents[0].Solution;
        int dimension = teacher.Count;
        double[] mean = new double[dimension];
        foreach (T parent in parents) {
            for (int dim = 0; dim < dimension; dim++) {
                mean[dim] += converter.FromValue(parent.Solution[dim]).ToDouble();
            }
        }
        for (int dim = 0; dim < dimension; dim++) {
            mean[dim] /= parents.Count;
        }

        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            double factor = 1.0 + global::System.Math.Round(
                NumericSupport.Unit(entropy) * TeachingFactorRange, MidpointRounding.AwayFromZero);
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double step = NumericSupport.Unit(entropy)
                    * (converter.FromValue(teacher[dim]).ToDouble() - (factor * mean[dim]));
                moved.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(converter.FromValue(current[dim]).ToDouble() + step, lower, upper),
                    converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}

/// <summary>
/// 学习阶段更新 / Learner-phase update.
/// </summary>
/// <remarks>
/// **抽到"自己"时必须换人**，否则 `partner - current` 恒为 0，该个体原地不动——而"学习阶段"的名字让人以为它在学习。
/// **A self-draw must switch partners**, otherwise `partner - current` is always 0 and the individual stands still — while the name
/// "learner phase" suggests it is learning.
/// **符号取决于两者的排名先后**（`index &lt; partnerIndex` 为正）。取反会让学习者远离同伴而不是靠近，而种群规模与迭代数毫无异常。
/// **The sign depends on their rank order** (`index &lt; partnerIndex` is positive). Flipping it makes the learner move away from its
/// partner instead of toward it, with no anomaly in population size or iteration count.
/// </remarks>
public sealed class LearnerPhaseUpdate : ISolutionUpdate {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static LearnerPhaseUpdate Instance { get; } = new();

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
        if (parents.Count < 2) {
            return System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.Select(parents, parent => new Solution<V>(parent.Solution)));
        }

        var children = new List<Solution<V>>(parents.Count);
        for (int index = 0; index < parents.Count; index++) {
            IReadOnlyList<V> current = parents[index].Solution;
            int partnerIndex = global::System.Math.Clamp(
                (int)(NumericSupport.Unit(entropy) * parents.Count), 0, parents.Count - 1);
            if (partnerIndex == index) {
                partnerIndex = (index + 1) % parents.Count;
            }
            IReadOnlyList<V> partner = parents[partnerIndex].Solution;
            double sign = index < partnerIndex ? 1.0 : -1.0;

            int dimension = current.Count;
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double step = NumericSupport.Unit(entropy) * sign
                    * (converter.FromValue(partner[dim]).ToDouble() - converter.FromValue(current[dim]).ToDouble());
                moved.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(converter.FromValue(current[dim]).ToDouble() + step, lower, upper),
                    converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}

/// <summary>
/// 樽海鞘群更新 / Salp-swarm update.
/// </summary>
/// <remarks>
/// **首个个体是领导者，其余是跟随者**。领导者围绕食物做随机摆动，跟随者取"自己与前一名的中点"。把两者混同会让链式结构消失，
/// 种群退化成互相靠拢的一团。
/// **The first individual is the leader, the rest are followers.** The leader swings randomly around the food, and each follower takes the
/// midpoint between itself and its predecessor. Conflating the two loses the chain structure and the population collapses into a clump.
/// **领导者的抽样在每维内部进行**（每维各抽 `swing` 与 `sign` 两个数）。提前到循环外会让所有维度共用同一方向与幅度，
/// 领导者只在一条对角线上摆动。
/// **The leader's draws happen inside each dimension** (`swing` and `sign` per dimension). Hoisting them outside the loop makes every
/// dimension share one direction and magnitude, so the leader only swings along a diagonal.
/// </remarks>
/// <param name="Decay">摆动幅度演化源 / The swing-amplitude source.</param>
public sealed record SalpSwarmUpdate(IParameterSource Decay) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public SalpSwarmUpdate() : this(new LinearSource(new Flt64(2.0), Flt64.Zero, 100UL)) { }

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

        IReadOnlyList<V> leader = parents[0].Solution;
        double coefficient = Decay.Value(iteration).ToDouble();
        var children = new List<Solution<V>>(parents.Count);
        for (int index = 0; index < parents.Count; index++) {
            IReadOnlyList<V> current = parents[index].Solution;
            int dimension = current.Count;
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double width = lower is double low && upper is double high
                    && double.IsFinite(low) && double.IsFinite(high)
                    ? high - low
                    : 1.0;
                double origin = lower is double finiteLow && double.IsFinite(finiteLow) ? finiteLow : 0.0;

                double value;
                if (index == 0) {
                    double swing = NumericSupport.Unit(entropy);
                    double sign = NumericSupport.Unit(entropy) < 0.5 ? -1.0 : 1.0;
                    value = converter.FromValue(leader[dim]).ToDouble()
                        + (sign * coefficient * ((width * swing) + origin));
                } else {
                    value = (converter.FromValue(current[dim]).ToDouble()
                        + converter.FromValue(parents[index - 1].Solution[dim]).ToDouble()) / 2.0;
                }
                moved.Add(NumericSupport.AsValue(NumericSupport.ClampTo(value, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}

/// <summary>
/// 引力搜索更新 / Gravitational-search update.
/// </summary>
/// <remarks>
/// **质量按排名加权并归一化**：第 `index` 名的质量是 `(size - index) / (size(size+1)/2)`，总和恰为 1。改成均匀质量会让"优者引力更大"
/// 这一核心机制消失，而加速度的数值仍在合理范围内，**看不出任何异常**。
/// **Masses are rank-weighted and normalized**: rank `index` carries `(size - index) / (size(size+1)/2)`, summing to exactly 1.
/// Uniform masses remove the "better individuals pull harder" mechanism while the acceleration values stay plausible — **nothing looks
/// wrong**.
/// **距离必须加 `softness`**。两个个体坐标完全相同时 `distance == 0`，除以 0 会得到 `NaN` 或 `∞`；`softness`（默认 `1e-6`）
/// 是防止这一点塌陷的唯一手段，而 `ClampTo` 会把 `NaN` 折算成边界，**于是整代被钉在边界上，看起来只是"收敛得快"**。
/// **The distance must add `softness`.** Two individuals with identical coordinates give `distance == 0`, and dividing by 0 yields `NaN`
/// or `∞`; `softness` (default `1e-6`) is the only thing preventing that collapse, and `ClampTo` folds `NaN` onto a bound — **the whole
/// generation is pinned to the boundary and it merely looks like "fast convergence"**.
/// **距离是全体维度的欧氏距离，而加速度只取当前维的分量**。用单维差当距离会让每一维的引力被高估。
/// **The distance is Euclidean across all dimensions while the acceleration uses the current dimension's component.** Using the
/// single-dimension difference as the distance overestimates the pull on every dimension.
/// </remarks>
/// <param name="GravitationalConstant">引力常数演化源 / The gravitational-constant source.</param>
/// <param name="Softness">软化项 / The softening term.</param>
public sealed record GravitationalSearchUpdate(IParameterSource GravitationalConstant, double Softness)
    : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public GravitationalSearchUpdate()
        : this(new LinearSource(Flt64.One, new Flt64(0.01), 100UL), 1.0e-6) { }

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

        int size = parents.Count;
        double constant = GravitationalConstant.Value(iteration).ToDouble();
        double massTotal = size * (size + 1) / 2.0;
        double[] masses = new double[size];
        for (int index = 0; index < size; index++) {
            masses[index] = (size - index) / massTotal;
        }

        var children = new List<Solution<V>>(size);
        for (int index = 0; index < size; index++) {
            IReadOnlyList<V> current = parents[index].Solution;
            int dimension = current.Count;
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double acceleration = 0.0;
                for (int other = 0; other < size; other++) {
                    if (other == index) {
                        continue;
                    }
                    double distanceSquared = 0.0;
                    for (int axis = 0; axis < dimension; axis++) {
                        double delta = converter.FromValue(parents[other].Solution[axis]).ToDouble()
                            - converter.FromValue(current[axis]).ToDouble();
                        distanceSquared += delta * delta;
                    }
                    double distance = global::System.Math.Sqrt(distanceSquared) + Softness;
                    acceleration += constant * masses[other]
                        * (converter.FromValue(parents[other].Solution[dim]).ToDouble()
                            - converter.FromValue(current[dim]).ToDouble())
                        / distance;
                }
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                moved.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(converter.FromValue(current[dim]).ToDouble() + acceleration, lower, upper),
                    converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}

/// <summary>
/// 萤火虫更新 / Firefly update.
/// </summary>
/// <remarks>
/// **伙伴只能从排名更靠前的个体里选**。从全种群随机选会让萤火虫互相吸引、彼此拉扯，收敛停滞；`index == 0`（最优）没有更好的伙伴，
/// 只做随机抖动。
/// **The partner must come from better-ranked individuals only.** A uniform choice over the whole population makes fireflies attract each
/// other in both directions and convergence stalls; `index == 0` (the best) has no better partner and only jitters.
/// **吸引力随距离平方指数衰减**：`attraction * exp(-absorption * distanceSquared)`。用距离本身（而非平方）会让远处个体的吸引力
/// 衰减过慢，种群长时间保持全局随机。
/// **Attractiveness decays exponentially in the squared distance**: `attraction * exp(-absorption * distanceSquared)`. Using the distance
/// itself instead of its square makes distant individuals too attractive and the population stays globally random for too long.
/// **抖动幅度按区间宽度缩放**。用绝对量会让宽区间几乎不动、窄区间被完全覆盖。
/// **The jitter magnitude scales with the interval width.** An absolute magnitude barely moves wide intervals and completely covers
/// narrow ones.
/// </remarks>
/// <param name="Attractiveness">基础吸引力 / The base attractiveness.</param>
/// <param name="Absorption">吸收系数 / The absorption coefficient.</param>
/// <param name="Randomness">抖动幅度 / The jitter magnitude.</param>
public sealed record FireflyUpdate(double Attractiveness, double Absorption, double Randomness)
    : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public FireflyUpdate() : this(1.0, 1.0, 0.2) { }

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

        var children = new List<Solution<V>>(parents.Count);
        for (int index = 0; index < parents.Count; index++) {
            IReadOnlyList<V> current = parents[index].Solution;
            int dimension = current.Count;
            var moved = new List<V>(dimension);

            int betterAmount = index;
            int partnerIndex = betterAmount > 0
                ? global::System.Math.Clamp(
                    (int)(NumericSupport.Unit(entropy) * betterAmount), 0, betterAmount - 1)
                : index;
            IReadOnlyList<V> partner = parents[partnerIndex].Solution;

            double distanceSquared = 0.0;
            for (int dim = 0; dim < dimension; dim++) {
                double delta = converter.FromValue(partner[dim]).ToDouble()
                    - converter.FromValue(current[dim]).ToDouble();
                distanceSquared += delta * delta;
            }
            double attraction = Attractiveness * global::System.Math.Exp(-Absorption * distanceSquared);

            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double width = lower is double low && upper is double high
                    && double.IsFinite(low) && double.IsFinite(high)
                    ? high - low
                    : 1.0;
                double position = converter.FromValue(current[dim]).ToDouble();
                double pull = attraction * (converter.FromValue(partner[dim]).ToDouble() - position);
                double jitter = Randomness * (NumericSupport.Unit(entropy) - 0.5) * width;
                moved.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(position + pull + jitter, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}
