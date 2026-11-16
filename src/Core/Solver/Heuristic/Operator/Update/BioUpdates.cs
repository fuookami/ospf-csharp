// 生物启发类更新算子 / Biologically-inspired update operators.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 鲸鱼优化更新 / Whale-optimization update.
/// </summary>
/// <remarks>
/// **A 与 C 按个体独立抽样，不是全种群共用一对**。这就是原文"每头鲸鱼各自更新"的含义；共用一对会让整代个体朝同一方向移动，
/// 种群在一代内塌缩，而每次迭代看起来都正常。
/// **A and C are sampled per individual, not shared across the population.** That is what "each whale updates independently" means in
/// the original; a shared pair moves the whole generation in one direction and collapses the population within a generation, while every
/// iteration looks normal.
/// **`|A| &gt;= 1` 时参照随机个体（探索），否则参照最优（开发）**。判据写反会让算法只做局部搜索、或永远不收敛。
/// **`|A| &gt;= 1` refers to a random individual (exploration), otherwise to the best (exploitation).** Reversing the predicate leaves
/// the algorithm doing pure local search, or never converging.
/// **螺旋分支围绕的是"最优"而不是抽样出的 `reference`**。用 `reference` 会让螺旋阶段也带探索性，与原文的两段结构不符。
/// **The spiral branch orbits the best, not the sampled `reference`.** Using `reference` would make the spiral phase exploratory too,
/// contradicting the original's two-phase structure.
/// </remarks>
/// <param name="Decay">收敛系数演化源 / The convergence-decay source.</param>
/// <param name="SpiralRate">螺旋增长率 / The spiral growth rate.</param>
public sealed record WhaleOptimizationUpdate(IParameterSource Decay, double SpiralRate) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public WhaleOptimizationUpdate() : this(new LinearSource(new Flt64(2.0), Flt64.Zero, 100UL), 1.0) { }

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

        IReadOnlyList<V> best = parents[0].Solution;
        double a = Decay.Value(iteration).ToDouble();
        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            int dimension = current.Count;
            var moved = new List<V>(dimension);

            // 抽样顺序必须：p → A → C →（探索时）参照下标 → spiralLength。
            // The draw order must match the code: p, then A, then C, then (on exploration) the reference index, then spiralLength.
            double p = NumericSupport.Unit(entropy);
            bool spiral = p >= 0.5;
            double coefficientA = (2.0 * a * NumericSupport.Unit(entropy)) - a;
            double coefficientC = 2.0 * NumericSupport.Unit(entropy);
            IReadOnlyList<V> reference = global::System.Math.Abs(coefficientA) >= 1.0
                ? parents[global::System.Math.Clamp(
                    (int)(NumericSupport.Unit(entropy) * parents.Count), 0, parents.Count - 1)].Solution
                : best;
            double spiralLength = ((a * 2.0) - 1.0) * NumericSupport.Unit(entropy);

            for (int dim = 0; dim < dimension; dim++) {
                double position = converter.FromValue(current[dim]).ToDouble();
                double target = converter.FromValue(reference[dim]).ToDouble();
                double value;
                if (spiral) {
                    double distance = global::System.Math.Abs(
                        converter.FromValue(best[dim]).ToDouble() - position);
                    value = (distance * global::System.Math.Exp(SpiralRate * spiralLength)
                        * global::System.Math.Cos(2.0 * global::System.Math.PI * spiralLength))
                        + converter.FromValue(best[dim]).ToDouble();
                } else {
                    value = target - (coefficientA * global::System.Math.Abs((coefficientC * target) - position));
                }
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                moved.Add(NumericSupport.AsValue(NumericSupport.ClampTo(value, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}

/// <summary>
/// 哈里斯鹰优化更新 / Harris-hawk-optimization update.
/// </summary>
/// <remarks>
/// **三段能量区间必须都保留**：`|E| &gt;= 1` 远距探索、`|E| &gt;= 0.5` 软围攻、否则硬围攻。把中间段合并会失去"软围攻"这一过渡，
/// 算法在大能量与小能量之间跳变，收敛曲线出现平台后突然贴身。
/// **All three energy regimes must remain**: `|E| &gt;= 1` distant exploration, `|E| &gt;= 0.5` soft siege, otherwise hard siege. Merging
/// the middle regime removes the transition, and the algorithm jumps between large and small energy — the convergence curve plateaus
/// then suddenly hugs the optimum.
/// **每维的额外抽样不能提前**。每维内部各抽一次（远距探索段抽一次、软围攻段再抽一次），提前到循环外会让同一维的多次更新
/// 共用一个随机数，探索性大幅下降而结果看起来仍然合理。
/// **The extra per-dimension draws cannot be hoisted.** The code draws once inside each dimension (one in the distant-exploration
/// branch, one in the soft-siege branch); hoisting them outside the loop makes every dimension reuse one random number, sharply reducing
/// exploration while the results still look reasonable.
/// </remarks>
/// <param name="Decay">能量衰减源 / The energy-decay source.</param>
public sealed record HarrisHawkUpdate(IParameterSource Decay) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public HarrisHawkUpdate() : this(new LinearSource(new Flt64(2.0), Flt64.Zero, 100UL)) { }

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

        IReadOnlyList<V> best = parents[0].Solution;
        double decayFactor = Decay.Value(iteration).ToDouble();
        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            int dimension = current.Count;
            double energy = decayFactor * ((2.0 * NumericSupport.Unit(entropy)) - 1.0);
            IReadOnlyList<V> random = parents[global::System.Math.Clamp(
                (int)(NumericSupport.Unit(entropy) * parents.Count), 0, parents.Count - 1)].Solution;
            double jump = 2.0 * (1.0 - NumericSupport.Unit(entropy));

            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double position = converter.FromValue(current[dim]).ToDouble();
                double bestPosition = converter.FromValue(best[dim]).ToDouble();
                double value;
                if (global::System.Math.Abs(energy) >= 1.0) {
                    double randomPosition = converter.FromValue(random[dim]).ToDouble();
                    value = randomPosition - (NumericSupport.Unit(entropy)
                        * global::System.Math.Abs(randomPosition - (2.0 * NumericSupport.Unit(entropy) * position)));
                } else if (global::System.Math.Abs(energy) >= 0.5) {
                    value = (position - (energy * global::System.Math.Abs((jump * bestPosition) - position)))
                        + (NumericSupport.Unit(entropy) * (bestPosition - position));
                } else {
                    value = position - (energy * global::System.Math.Abs(bestPosition - position));
                }
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                moved.Add(NumericSupport.AsValue(NumericSupport.ClampTo(value, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}

/// <summary>
/// 算术优化更新 / Arithmetic-optimization update.
/// </summary>
/// <remarks>
/// **概率必须夹紧到 `[1e-6, 1]`**。它出现在除法分母上，迭代到后期概率线性降到 0.001 甚至更低，一旦取到 0 就是除零，
/// 而"加速"这一看似合理的名字会让人以为它天然安全。
/// **The probability must be clamped into `[1e-6, 1]`.** It appears in a denominator, and the linearly decaying probability heads toward
/// 0.001 or lower late in a run; hitting 0 divides by zero, and the plausible-sounding name "accelerator" makes it look inherently safe.
/// **三个随机数按个体抽取一次，不在每维重抽**。这是写法：整个个体的所有维度共用同一组分支判据，否则同一维的更新会在
/// "加速"与"减速"之间逐维跳变。
/// **The three random numbers are drawn once per individual, not per dimension.** That is its shape: all dimensions of one
/// individual share the same branch decisions; otherwise the update flips between acceleration and deceleration dimension by dimension.
/// **`scale = width * sensitivity + origin`** 是原样写法（把区间宽度按灵敏度折算再平移到下界）。
/// **`scale = width * sensitivity + origin`** is its exact shape (the interval width scaled by the sensitivity, shifted to the
/// lower bound).
/// </remarks>
/// <param name="Accelerator">加速系数演化源 / The accelerator source.</param>
/// <param name="Probability">概率演化源 / The probability source.</param>
/// <param name="Sensitivity">灵敏度 / The sensitivity.</param>
public sealed record ArithmeticOptimizationUpdate(
    IParameterSource Accelerator,
    IParameterSource Probability,
    double Sensitivity) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public ArithmeticOptimizationUpdate() : this(
        new LinearSource(new Flt64(0.2), new Flt64(0.9), 100UL),
        new LinearSource(Flt64.One, new Flt64(0.001), 100UL),
        0.5) { }

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

        IReadOnlyList<V> best = parents[0].Solution;
        double accelerated = Accelerator.Value(iteration).ToDouble();
        double probability = global::System.Math.Clamp(
            Probability.Value(iteration).ToDouble(), 1.0e-6, 1.0);

        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            int dimension = current.Count;
            var moved = new List<V>(dimension);
            double first = NumericSupport.Unit(entropy);
            double second = NumericSupport.Unit(entropy);
            double third = NumericSupport.Unit(entropy);

            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double width = lower is double low && upper is double high
                    && double.IsFinite(low) && double.IsFinite(high)
                    ? high - low
                    : 1.0;
                double origin = lower is double finiteLow && double.IsFinite(finiteLow) ? finiteLow : 0.0;
                double scale = (width * Sensitivity) + origin;
                double bestPosition = converter.FromValue(best[dim]).ToDouble();

                double value;
                if (first > accelerated) {
                    value = second > 0.5
                        ? bestPosition / probability * scale
                        : bestPosition * probability * scale;
                } else {
                    value = third > 0.5
                        ? bestPosition - (probability * scale)
                        : bestPosition + (probability * scale);
                }
                moved.Add(NumericSupport.AsValue(NumericSupport.ClampTo(value, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}
