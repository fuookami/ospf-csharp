// 种群拓扑、幸存者选择与多样性度量 / Population topology, survivor selection and diversity metrics.
// 与固定 Kotlin 、
// 对应。
// , and.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Fractal;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 种群拓扑 / A population topology.
/// </summary>
/// <remarks>
/// **拓扑决定信息传播速度**。全局拓扑下一次迭代就能把最优信息传到全种群（收敛快、易早熟）；环形拓扑需要多代才能传播到对端
/// （多样性好、收敛慢）。把拓扑从配置里拿掉，这两类行为就无法在同一套算法里对照。
/// **Topology determines how fast information spreads.** Under a global topology the best information reaches the whole population in
/// one iteration (fast convergence, easy premature collapse); a ring needs several generations to reach the far side (better diversity,
/// slower convergence). Dropping topology from the configuration makes these two behaviours impossible to compare inside one algorithm.
/// **邻居集合必须包含自己**。不含自己的话，个体在更新时读不到自身状态，"原地保持"这一最保守的选择就消失了。
/// **The neighbour set must include the centre itself.** Without it, an individual cannot read its own state during update, and the
/// most conservative option — keeping still — disappears.
/// </remarks>
public interface ITopology {
    /// <summary>
    /// 获取邻居下标集合 / Get the neighbour index set.
    /// </summary>
    /// <param name="index">中心个体下标 / The centre individual index.</param>
    /// <param name="size">种群规模 / The population size.</param>
    /// <returns>邻居下标集合 / The neighbour index set.</returns>
    IReadOnlySet<int> Neighbours(int index, int size);
}

/// <summary>
/// 全局拓扑，所有个体互为邻居 / Global topology where every individual neighbours every other.
/// </summary>
public sealed class GlobalTopology : ITopology {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static GlobalTopology Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlySet<int> Neighbours(int index, int size) =>
        Enumerable.Range(0, global::System.Math.Max(size, 0)).ToHashSet();
}

/// <summary>
/// 环形拓扑，左右各一个邻居 / Ring topology with one neighbour on each side.
/// </summary>
public sealed class RingTopology : ITopology {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static RingTopology Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlySet<int> Neighbours(int index, int size) {
        if (size <= 1) {
            return new HashSet<int> { index };
        }
        return new HashSet<int> {
            (index - 1 + size) % size,
            index,
            (index + 1) % size,
        };
    }
}

/// <summary>
/// 环形 K 邻域拓扑，左右各取 radius 个邻居 / Ring-K topology with `radius` neighbours on each side.
/// </summary>
/// <param name="Radius">邻域半径 / The neighbourhood radius.</param>
public sealed record RingKTopology(int Radius) : ITopology {
    /// <inheritdoc/>
    public IReadOnlySet<int> Neighbours(int index, int size) {
        if (size <= 1 || Radius <= 0) {
            return new HashSet<int> { index };
        }
        var result = new HashSet<int>();
        for (int offset = -Radius; offset <= Radius; offset++) {
            result.Add(((index + offset) % size + size) % size);
        }
        return result;
    }
}

/// <summary>
/// 二维冯·诺依曼拓扑，按宽度切分为网格 / A two-dimensional von Neumann topology on a grid of the given width.
/// </summary>
/// <param name="Width">网格宽度 / The grid width.</param>
public sealed record VonNeumannTopology(int Width) : ITopology {
    /// <inheritdoc/>
    public IReadOnlySet<int> Neighbours(int index, int size) {
        if (size <= 1 || Width <= 0) {
            return new HashSet<int> { index };
        }

        int row = index / Width;
        int column = index % Width;
        var result = new HashSet<int>();
        foreach ((int rowOffset, int columnOffset) in new[] { (0, 0), (-1, 0), (1, 0), (0, -1), (0, 1) }) {
            int neighbourRow = row + rowOffset;
            int neighbourColumn = column + columnOffset;
            // 列越界要按网格边界丢弃，而不是 wrap 到下一行——wrap 会把网格拓扑悄悄变成一条莫比乌斯带，邻居关系与二维直觉完全不同。
            // A column overflow must be dropped at the grid edge, not wrapped into the next row: wrapping silently turns the grid
            // topology into a Möbius band whose neighbour relations match no two-dimensional intuition.
            if (neighbourColumn < 0 || neighbourColumn >= Width) {
                continue;
            }
            int neighbour = neighbourRow * Width + neighbourColumn;
            if (neighbour >= 0 && neighbour < size) {
                result.Add(neighbour);
            }
        }
        return result;
    }
}

/// <summary>
/// 随机拓扑，每次查询返回随机的若干邻居 / Random topology returning a fresh random neighbourhood per query.
/// </summary>
/// <remarks>
/// **每次查询都重新抽样意味着邻居关系逐代变化**。这是有意为之（增加信息流动），但它也意味着同一份种子的重放要求熵源状态一并恢复；
/// 若把邻居集合缓存下来，"随机拓扑"就退化成一次性的固定图，而配置看起来毫无变化。
/// **Resampling per query means neighbour relations change every generation.** That is intentional (it increases information flow),
/// but it also means replay under the same seed requires the entropy state to be restored along with everything else; caching the
/// neighbour set would silently degrade "random topology" into a fixed one-time graph while the configuration looks unchanged.
/// </remarks>
/// <param name="Amount">邻居数量 / The neighbour amount.</param>
/// <param name="Entropy">均匀分布熵源 / The uniform entropy source.</param>
public sealed record RandomTopology(int Amount, Func<Flt64> Entropy) : ITopology {
    /// <inheritdoc/>
    public IReadOnlySet<int> Neighbours(int index, int size) {
        if (size <= 1) {
            return new HashSet<int> { index };
        }

        var result = new HashSet<int>();
        int target = global::System.Math.Min(Amount, size);
        // 抽样同样需要尝试上限：`amount` 接近 `size` 时"抽满不重复的邻居"要大量重抽，无上限会退化成近似无限循环。
        // Sampling needs the same attempt cap: with `amount` close to `size`, drawing enough distinct neighbours takes many redraws
        // and an unbounded loop degenerates into a near-infinite one.
        int attempts = 0;
        while (result.Count < target && attempts < size * 4) {
            int candidate = (int)(NumericSupport.Unit(Entropy) * size);
            result.Add(global::System.Math.Clamp(candidate, 0, size - 1));
            attempts++;
        }
        result.Add(index);
        return result;
    }
}

/// <summary>
/// 选出进入下一代的个体 / Select the individuals entering the next generation.
/// </summary>
public interface ISurvivorSelection {
    /// <summary>
    /// 选出进入下一代的个体 / Select the individuals entering the next generation.
    /// </summary>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="parents">父代个体 / The parents.</param>
    /// <param name="offspring">子代个体 / The offspring.</param>
    /// <param name="amount">目标个体数量 / The target individual amount.</param>
    /// <returns>下一代的个体 / The next generation's individuals.</returns>
    IReadOnlyList<T> Select<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<T> parents,
        IReadOnlyList<T> offspring,
        int amount)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V>;
}

/// <summary>
/// 幸存者选择共用辅助 / Shared helpers for survivor selection.
/// </summary>
public static class SurvivorSupport {
    /// <summary>
    /// 取最优的若干个个体 / Take the best individuals.
    /// </summary>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="items">候选个体 / The candidates.</param>
    /// <param name="amount">需要的数量 / The requested amount.</param>
    /// <returns>最优的个体 / The best individuals.</returns>
    public static IReadOnlyList<T> BestOf<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<T> items,
        int amount)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(items);
        if (amount <= 0) {
            return Array.Empty<T>();
        }
        var ranked = new List<T>(items);
        ranked.Sort((lhs, rhs) => HeuristicOrdering.CompareStrictly(model, lhs.Objective, rhs.Objective));
        return ranked.Take(global::System.Math.Min(amount, ranked.Count)).ToList();
    }
}

/// <summary>
/// 精英幸存者：父代与子代合并后取最优 / Elitist survivor taking the best of parents and offspring combined.
/// </summary>
public sealed class ElitistSurvivor : ISurvivorSelection {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static ElitistSurvivor Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<T> Select<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<T> parents,
        IReadOnlyList<T> offspring,
        int amount)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> =>
        SurvivorSupport.BestOf<TObj, TObjValue, V, T>(model, parents.Concat(offspring).ToList(), amount);
}

/// <summary>
/// 世代幸存者：只看子代 / Generational survivor considering offspring only.
/// </summary>
public sealed class GenerationalSurvivor : ISurvivorSelection {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static GenerationalSurvivor Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<T> Select<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<T> parents,
        IReadOnlyList<T> offspring,
        int amount)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> =>
        SurvivorSupport.BestOf<TObj, TObjValue, V, T>(model, offspring, amount);
}

/// <summary>
/// 截断幸存者：合并后按固定上限取最优 / Truncation survivor taking the best up to a fixed cap.
/// </summary>
/// <param name="Cap">固定上限 / The fixed cap.</param>
public sealed record TruncationSurvivor(int Cap) : ISurvivorSelection {
    /// <inheritdoc/>
    public IReadOnlyList<T> Select<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<T> parents,
        IReadOnlyList<T> offspring,
        int amount)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> =>
        SurvivorSupport.BestOf<TObj, TObjValue, V, T>(
            model, parents.Concat(offspring).ToList(), global::System.Math.Min(Cap, amount));
}

/// <summary>
/// 保精英幸存者：先保父代精英，再从子代补足 / Elite-preserving survivor keeping parent elites first, filling from offspring.
/// </summary>
/// <remarks>
/// **精英只从父代取，补足只从子代取**。若两段都从合并池里取，最优个体可能被重复计入，实际保留的精英数量少于配置值，
/// 而种群规模看起来完全正确。
/// **Elites come from parents only; the remainder from offspring only.** Taking both segments from the combined pool could count the
/// same individual twice, leaving fewer elites than configured while the population size looks exactly right.
/// </remarks>
/// <param name="EliteAmount">精英数量 / The elite amount.</param>
public sealed record ElitistPreservingSurvivor(int EliteAmount) : ISurvivorSelection {
    /// <inheritdoc/>
    public IReadOnlyList<T> Select<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IReadOnlyList<T> parents,
        IReadOnlyList<T> offspring,
        int amount)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        IReadOnlyList<T> elites = SurvivorSupport.BestOf<TObj, TObjValue, V, T>(
            model, parents, global::System.Math.Min(EliteAmount, amount));
        IReadOnlyList<T> rest = SurvivorSupport.BestOf<TObj, TObjValue, V, T>(
            model, offspring, amount - elites.Count);
        return elites.Concat(rest).ToList();
    }
}

/// <summary>
/// 计算种群多样性 / Compute population diversity.
/// </summary>
public interface IDiversityMetric {
    /// <summary>
    /// 计算种群多样性 / Compute population diversity.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="solutions">解集合 / The solution set.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <returns>多样性度量值 / The diversity measure.</returns>
    Flt64 Measure<V>(IReadOnlyList<Solution<V>> solutions, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V>;
}

/// <summary>
/// 平均成对距离 / Mean pairwise distance.
/// </summary>
/// <remarks>
/// **少于两个解时返回 0**。返回"无穷大"或抛异常会让"种群塌缩成一个点"这一最需要被检测的状态反而报出最高多样性。
/// **Fewer than two solutions yields 0.** Returning infinity or throwing would make "the population collapsed to a single point" —
/// precisely the state most worth detecting — report the highest possible diversity.
/// </remarks>
public sealed class MeanPairwiseDistance : IDiversityMetric {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static MeanPairwiseDistance Instance { get; } = new();

    /// <inheritdoc/>
    public Flt64 Measure<V>(IReadOnlyList<Solution<V>> solutions, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(solutions);
        ArgumentNullException.ThrowIfNull(converter);
        if (solutions.Count < 2) {
            return Flt64.Zero;
        }

        int dimension = solutions[0].Values.Count;
        double total = 0.0;
        long pairs = 0;
        for (int i = 0; i < solutions.Count; i++) {
            for (int j = i + 1; j < solutions.Count; j++) {
                double squared = 0.0;
                for (int k = 0; k < dimension; k++) {
                    double difference = converter.FromValue(solutions[i].Values[k]).ToDouble()
                        - converter.FromValue(solutions[j].Values[k]).ToDouble();
                    squared += difference * difference;
                }
                total += global::System.Math.Sqrt(squared);
                pairs++;
            }
        }
        // **这是"平均欧氏距离"，不是"距离的均方根"**。基线写 `accumulated / pairs`，即先逐对取欧氏距离再求平均；
        // 先累加平方再开方得到的是 RMS，两者在距离分布不均匀时差异显著——而两种值都是"合理的正数"，
        // **只看数值大小完全看不出用了哪一个**。此前这里误写成 RMS 且未记录偏离，已改正。
        // **This is the mean Euclidean distance, not the root of the mean squared distance.** The code writes `accumulated / pairs`,
        // i.e. take each pair's Euclidean distance then average; summing squares first yields an RMS, and the two differ noticeably when
        // distances are unevenly distributed — while both are plausible positive numbers, **so the value alone reveals nothing**.
        // This was wrongly written as an RMS without a recorded divergence; it now matches the code.
        return pairs == 0 ? Flt64.Zero : new Flt64(total / pairs);
    }
}

/// <summary>
/// 分形维数多样性 / Fractal-dimension diversity.
/// </summary>
/// <remarks>
/// **用关联维数（correlation dimension）而不是盒计数维数**。关联维数基于"两两距离小于 r 的比例"随 r 的变化，与种群的实际分布
/// 直接相关；盒计数维数依赖网格划分，同样的种群在不同网格原点下会得到不同的值。
/// **The correlation dimension rather than box counting.** The correlation dimension is based on how the fraction of pairs closer than r
/// varies with r, tying it directly to the population's distribution; box counting depends on grid placement, so the same population
/// yields different values under different grid origins.
/// **估计失败时返回 0**。空输入、样本不足或退化分布都会让估计失败，而把失败当成"高多样性"会让停止判据在该停的时候不停。
/// **A failed estimate returns 0.** Empty input, too few samples or a degenerate distribution all make the estimate fail, and treating
/// failure as "high diversity" would keep the stopping criterion from stopping when it should.
/// **这是`FractalDimensionDiversity` 的补齐**： 与
/// `TopologyAndSurvivor` 的度量族是同名不同址的两处定义，本实现把它们合并到同一组接口上，避免出现两个 `IDiversityMetric`。
/// **This completes its `FractalDimensionDiversity`**: the code defines its metric family both in
/// and alongside `TopologyAndSurvivor`, and this implementation merges them onto one interface so there is
/// only a single `IDiversityMetric`.
/// </remarks>
public sealed class FractalDimensionDiversity : IDiversityMetric {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static FractalDimensionDiversity Instance { get; } = new();

    /// <inheritdoc/>
    public Flt64 Measure<V>(IReadOnlyList<Solution<V>> solutions, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(solutions);
        ArgumentNullException.ThrowIfNull(converter);
        var converted = new List<IReadOnlyList<Flt64>>(solutions.Count);
        foreach (Solution<V> solution in solutions) {
            var row = new List<Flt64>(solution.Values.Count);
            foreach (V value in solution.Values) {
                row.Add(converter.FromValue(value));
            }
            converted.Add(row);
        }

        Result<Flt64, ErrorCode, Error<ErrorCode>> result = FractalAnalysis.CorrelationDimension(converted);
        return result is Ok<Flt64, ErrorCode, Error<ErrorCode>> ok ? ok.Value : Flt64.Zero;
    }
}
