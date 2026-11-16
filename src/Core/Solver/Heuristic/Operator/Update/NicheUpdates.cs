// 小生境类更新算子 / Niche-flavoured update operators.

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
/// 战争策略更新 / War-strategy update.
/// </summary>
/// <remarks>
/// **进攻与防守的步长系数必须不同**。进攻步长是向最优收缩并放大 `strategyScale`（默认 2.0），收敛是安全的；但防守的目标是
/// **随机战友**，对随机目标做同样的 2 倍外推会让整代种群互相反馈、模长逐代放大——**最优值不下降、解却在膨胀**，而这种发散在
/// 前几代看起来只是"多样性变好"。
/// **The attack and defence step scales must differ.** Its attack step contracts toward the best and is amplified by
/// `strategyScale` (default 2.0), which converges safely; but the defence target is a **random comrade**, and extrapolating 2x on a
/// random target makes the whole generation feed back into itself with a growing magnitude — **the objective stops improving while the
/// solutions inflate**, and that divergence looks like "better diversity" during the first few generations.
/// **父代组按优度升序传入**，所以 `parents[0]` 就是当前最优。
/// **The parent group arrives sorted ascending**, so `parents[0]` is the current best.
/// </remarks>
/// <param name="AttackRate">进攻概率 / The attack probability.</param>
/// <param name="StrategyScale">进攻步长放大倍数 / The attack step amplification.</param>
public sealed record WarStrategyUpdate(double AttackRate, double StrategyScale) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public WarStrategyUpdate() : this(0.6, 2.0) { }

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
        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            bool attacking = NumericSupport.Unit(entropy) < AttackRate;
            IReadOnlyList<V> target = attacking
                ? best
                : parents[global::System.Math.Clamp(
                    (int)(NumericSupport.Unit(entropy) * parents.Count), 0, parents.Count - 1)].Solution;

            int dimension = current.Count;
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double scale = attacking ? StrategyScale : 1.0;
                double step = scale * NumericSupport.Unit(entropy)
                    * (converter.FromValue(target[dim]).ToDouble() - converter.FromValue(current[dim]).ToDouble());
                moved.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(converter.FromValue(current[dim]).ToDouble() + step, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}

/// <summary>
/// 能量谷更新 / Energy-valley update.
/// </summary>
/// <remarks>
/// **前 `stableAmount` 个个体向最优收缩，其余向随机个体收缩**。全部向最优收缩会让种群在一代内塌缩到同一点；
/// 全部向随机个体收缩则失去选择压力。"稳定个体比例"就是这个权衡的旋钮。
/// **The leading `stableAmount` individuals contract toward the best, the rest toward a random individual.** Contracting everything
/// toward the best collapses the population onto one point within a generation; contracting everything toward a random individual
/// loses selection pressure. The "stable individual ratio" is the knob for that trade-off.
/// </remarks>
/// <param name="StabilityRate">稳定个体比例 / The stable-individual ratio.</param>
/// <param name="DecayScale">非稳定个体的步长倍数 / The step scale for unstable individuals.</param>
public sealed record EnergyValleyUpdate(double StabilityRate, double DecayScale) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public EnergyValleyUpdate() : this(0.5, 1.0) { }

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
        int stableAmount = global::System.Math.Clamp(
            (int)(parents.Count * StabilityRate), 1, parents.Count);
        var children = new List<Solution<V>>(parents.Count);
        for (int index = 0; index < parents.Count; index++) {
            IReadOnlyList<V> current = parents[index].Solution;
            bool stable = index < stableAmount;
            IReadOnlyList<V> target = stable
                ? best
                : parents[global::System.Math.Clamp(
                    (int)(NumericSupport.Unit(entropy) * parents.Count), 0, parents.Count - 1)].Solution;
            double scale = stable ? 1.0 : DecayScale;

            int dimension = current.Count;
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double step = scale * NumericSupport.Unit(entropy)
                    * (converter.FromValue(target[dim]).ToDouble() - converter.FromValue(current[dim]).ToDouble());
                moved.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(converter.FromValue(current[dim]).ToDouble() + step, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}

/// <summary>
/// 生发中心更新 / Germinal-centre update.
/// </summary>
/// <remarks>
/// **收缩项与变异项必须同时存在**。只保留向精英质心收缩，种群会在一代内塌缩；只保留变异，就退化成随机搜索。
/// **The contraction term and the mutation term must both be present.** Keeping only the contraction toward the elite centroid
/// collapses the population within a generation; keeping only the mutation degrades into random search.
/// **质心只由前 `eliteAmount` 个个体计算**。用全体平均，质心会被劣质个体拖走，"生发中心"不再是精英所在的位置。
/// **The centroid is computed from the leading `eliteAmount` individuals only.** Averaging everyone drags the centroid toward the
/// inferior individuals, and the "germinal centre" stops being where the elites are.
/// **变异幅度按区间宽度缩放**。用绝对量会让宽区间几乎不动、窄区间被完全覆盖，同一套参数在不同维度上行为完全不同。
/// **The mutation magnitude scales with the interval width.** An absolute magnitude barely moves wide intervals and completely
/// covers narrow ones, so one parameter set behaves differently across dimensions.
/// </remarks>
/// <param name="EliteAmount">精英数量 / The elite amount.</param>
/// <param name="MutationScale">变异幅度比例 / The mutation-magnitude ratio.</param>
public sealed record GerminalCenterUpdate(int EliteAmount, double MutationScale) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public GerminalCenterUpdate() : this(3, 0.1) { }

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

        int eliteCount = global::System.Math.Clamp(EliteAmount, 1, parents.Count);
        int dimension = parents[0].Solution.Count;
        double[] centroid = new double[dimension];
        for (int index = 0; index < eliteCount; index++) {
            IReadOnlyList<V> solution = parents[index].Solution;
            for (int dim = 0; dim < dimension; dim++) {
                centroid[dim] += converter.FromValue(solution[dim]).ToDouble();
            }
        }
        for (int dim = 0; dim < dimension; dim++) {
            centroid[dim] /= eliteCount;
        }

        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double width = lower is double low && upper is double high
                    && double.IsFinite(low) && double.IsFinite(high)
                    ? high - low
                    : 1.0;
                double position = converter.FromValue(current[dim]).ToDouble();
                double contraction = NumericSupport.Unit(entropy) * (centroid[dim] - position);
                double mutation = MutationScale * ((NumericSupport.Unit(entropy) * 2.0) - 1.0) * width;
                moved.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(position + contraction + mutation, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}
