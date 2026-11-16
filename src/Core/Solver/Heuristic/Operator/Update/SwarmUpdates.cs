// 群智能类更新算子 / Swarm-intelligence update operators.
// 与固定 Kotlin （7617 B）对应。

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
/// 正弦余弦更新 / Sine-cosine update.
/// </summary>
/// <remarks>
/// **父代组按优度升序传入**，`parents[0]` 就是当前最优。取错方向会让"向最优收缩"变成"向最差收缩"，而这种反转在单代里看不出来。
/// **The parent group arrives sorted ascending**, so `parents[0]` is the current best. Getting the direction wrong turns "contract
/// toward the best" into "contract toward the worst", and that inversion is invisible within a single generation.
/// **正弦与余弦按 `r4 &lt; 0.5` 各半选择**。只用正弦会让搜索轨迹偏向外侧（`sin` 在 `[0, π]` 为正），只用余弦则偏向内侧，
/// 两者混用才是"围绕最优来回扫描"。
/// **Sine and cosine are chosen half-and-half on `r4 &lt; 0.5`.** Sine alone biases the trajectory outward (`sin` is positive on
/// `[0, π]`); cosine alone biases it inward. Mixing them is what produces the "scan around the best" behaviour.
/// **幅值按迭代线性衰减**（默认从 2.0 降到 0）。幅值恒定会让搜索永远保持全局随机性，永不在最优点附近收敛。
/// **The amplitude decays linearly with the iteration** (2.0 down to 0 by default). A constant amplitude keeps the search globally
/// random forever and it never settles near the optimum.
/// </remarks>
/// <param name="Amplitude">幅值演化源 / The amplitude source.</param>
public sealed record SineCosineUpdate(IParameterSource Amplitude) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public SineCosineUpdate() : this(new LinearSource(new Flt64(2.0), Flt64.Zero, 100UL)) { }

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
        double amplitude = Amplitude.Value(iteration).ToDouble();
        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            var child = new List<V>(current.Count);
            for (int index = 0; index < current.Count; index++) {
                double r2 = NumericSupport.Unit(entropy) * 2.0 * global::System.Math.PI;
                double r3 = NumericSupport.Unit(entropy) * 2.0;
                double r4 = NumericSupport.Unit(entropy);
                double position = converter.FromValue(current[index]).ToDouble();
                double target = converter.FromValue(best[index]).ToDouble();
                double wave = r4 < 0.5 ? global::System.Math.Sin(r2) : global::System.Math.Cos(r2);
                double moved = position + (amplitude * wave * global::System.Math.Abs((r3 * target) - position));
                double? lower = NumericSupport.LowerBoundOf(model, index, converter);
                double? upper = NumericSupport.UpperBoundOf(model, index, converter);
                child.Add(NumericSupport.AsValue(NumericSupport.ClampTo(moved, lower, upper), converter));
            }
            children.Add(new Solution<V>(child));
        }
        return children;
    }
}

/// <summary>
/// 多元宇宙更新 / Multi-verse update.
/// </summary>
/// <remarks>
/// **捐赠者按排名加权抽样，不是均匀抽样**。`pickDonor` 给第 `rank` 名分配权重 `size - rank`，于是最优个体被选中的概率是最后一名的高
/// `size` 倍。改成均匀抽样后，算法仍会运行、白洞/黑洞的日志也仍然正常，但"向最优个体学习"这一核心机制消失了。
/// **Donors are sampled with rank weighting, not uniformly.** `pickDonor` gives rank `r` the weight `size - r`, so the best individual is
/// `size` times more likely to be picked than the last. Switching to uniform sampling still runs, still logs ordinary white/black-hole
/// behaviour, but the "learn from the best" mechanism is gone.
/// **三个分支必须覆盖整个 `[0, 1)` 区间**：虫洞（`second &lt; wormhole`）、膨胀（`second &lt; inflation`）、原地。`inflation` 按排名从
/// `1.0` 递减到 `1/size`，所以靠后的个体更容易被"膨胀"到别人的位置上。
/// **The three branches must cover the whole `[0, 1)` range**: wormhole (`second &lt; wormhole`), inflation
/// (`second &lt; inflation`), and standing still. `inflation` decreases with rank from `1.0` to `1/size`, so later individuals are more
/// likely to inflate into someone else's position.
/// **虫洞偏移必须带方向**：`third &lt; 0.5` 取正、否则取负。只朝一个方向偏移会把种群持续推向同一侧边界。
/// **The wormhole offset needs a direction**: positive when `third &lt; 0.5`, negative otherwise. One-directional offsets drift the
/// population steadily toward the same boundary.
/// </remarks>
/// <param name="WormholeRate">虫洞概率演化源 / The wormhole-rate source.</param>
/// <param name="TravellingRate">旅行距离演化源 / The travelling-distance source.</param>
public sealed record MultiVerseUpdate(IParameterSource WormholeRate, IParameterSource TravellingRate)
    : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public MultiVerseUpdate() : this(
        new LinearSource(new Flt64(0.2), Flt64.One, 100UL),
        new LinearSource(Flt64.One, Flt64.Zero, 100UL)) { }

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
        int size = parents.Count;
        double wormhole = WormholeRate.Value(iteration).ToDouble();
        double travelling = TravellingRate.Value(iteration).ToDouble();

        var children = new List<Solution<V>>(size);
        for (int index = 0; index < size; index++) {
            IReadOnlyList<V> current = parents[index].Solution;
            double inflation = (double)(size - index) / size;
            var child = new List<V>(current.Count);
            for (int dimension = 0; dimension < current.Count; dimension++) {
                double? lower = NumericSupport.LowerBoundOf(model, dimension, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dimension, converter);
                double second = NumericSupport.Unit(entropy);
                double third = NumericSupport.Unit(entropy);
                double fourth = NumericSupport.Unit(entropy);
                double width = lower is double low && upper is double high
                    && double.IsFinite(low) && double.IsFinite(high)
                    ? high - low
                    : 1.0;
                double origin = lower is double finiteLow && double.IsFinite(finiteLow) ? finiteLow : 0.0;

                double value;
                if (second < wormhole) {
                    double offset = travelling * ((width * fourth) + origin);
                    value = third < 0.5
                        ? converter.FromValue(best[dimension]).ToDouble() + offset
                        : converter.FromValue(best[dimension]).ToDouble() - offset;
                } else if (second < inflation) {
                    int donor = PickDonor(size, entropy);
                    value = converter.FromValue(parents[donor].Solution[dimension]).ToDouble();
                } else {
                    value = converter.FromValue(current[dimension]).ToDouble();
                }
                child.Add(NumericSupport.AsValue(NumericSupport.ClampTo(value, lower, upper), converter));
            }
            children.Add(new Solution<V>(child));
        }
        return children;
    }

    /// <summary>
    /// 按排名加权抽取捐赠者 / Pick a donor with rank weighting.
    /// </summary>
    /// <param name="size">种群规模 / The population size.</param>
    /// <param name="entropy">均匀熵源 / The uniform entropy source.</param>
    /// <returns>捐赠者下标 / The donor index.</returns>
    private static int PickDonor(int size, Func<Flt64> entropy) {
        if (size <= 1) {
            return 0;
        }
        double total = size * (size + 1) / 2.0;
        double remaining = NumericSupport.Unit(entropy) * total;
        for (int rank = 0; rank < size; rank++) {
            double weight = size - rank;
            if (remaining < weight) {
                return rank;
            }
            remaining -= weight;
        }
        return 0;
    }
}
