// 解更新框架与两个最小更新算子 / The solution-update framework and two smallest update operators.
// 与固定 Kotlin 基线 `operator/update/SolutionUpdate.kt`、`operator/update/Cross.kt`、
// `operator/update/GreyWolfUpdate.kt`、`operator/update/HarmonySearchUpdate.kt` 对应。
// Mirrors the pinned baseline's `SolutionUpdate.kt`, `Cross.kt`, `GreyWolfUpdate.kt` and `HarmonySearchUpdate.kt`.

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
/// 产生新解 / Produce new solutions.
/// </summary>
/// <remarks>
/// **父代组按优度升序传入**，这是所有更新算子共享的契约。算子内部若要取"最好的前几个"（灰狼的 α/β/δ、和声的记忆库等），
/// 直接取前若干个即可，不必自己再排一次序——**重复排序既浪费又容易和调用方的排序方向不一致**（一个升序一个降序，取到的是最差的那个）。
/// **The parent group arrives sorted by ascending quality**, a contract shared by every update operator. Operators that need "the top
/// few" (the grey wolf's α/β/δ, the harmony memory, and so on) can just take the leading elements instead of sorting again —
/// **re-sorting is both wasteful and easy to disagree with the caller's direction** (one ascending, one descending, and the operator
/// takes the worst individual instead of the best).
/// **返回的每个解都必须与父代同维**。维数变化会在下一次评估时静默产生一个"半合法"的解向量，而错误只在很晚的约束检查里才暴露。
/// **Every returned solution must share the parents' dimension.** A dimension change silently produces a "half-legal" solution vector
/// on the next evaluation, and the fault only surfaces much later in a constraint check.
/// </remarks>
public interface ISolutionUpdate {
    /// <summary>
    /// 产生新解 / Produce new solutions.
    /// </summary>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <typeparam name="T">父代个体类型 / The parent-individual type.</typeparam>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="parents">父代组 / The parent group.</param>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    /// <returns>新解列表 / The new solutions.</returns>
    IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V>;
}

/// <summary>
/// 交叉算子 / A crossover operator.
/// </summary>
/// <remarks>
/// **交叉与更新的区别是语义而非机制**：交叉组合两个及以上父代的遗传物质，更新可以只依赖单个父代（如变异、和声记忆取用）。
/// 分开标记让算法骨架能在"需要两个不同父代"的场合拒绝单父代算子，而不必依赖运行期才发现索引越界。
/// **Crossover differs from update in meaning, not mechanism**: a crossover combines genetic material from two or more parents, while
/// an update may depend on a single parent (mutation, harmony memory consideration). The separate marker lets the algorithm skeleton
/// reject single-parent operators where two distinct parents are required, instead of discovering an index overflow at run time.
/// </remarks>
public interface ICross : ISolutionUpdate;

/// <summary>
/// 灰狼优化更新 / Grey-wolf-optimizer update.
/// </summary>
/// <remarks>
/// **α/β/δ 直接取父代组前三个**（父代已按优度升序）。自己再排一次序容易与调用方方向不一致，从而把最差的当成 α。
/// **α/β/δ are simply the first three parents** (already sorted ascending). Re-sorting risks disagreeing with the caller's direction
/// and promoting the worst individual to α.
/// **收敛系数在整个父代组上取一次**，不是每个父代各取一次。逐父代重取会让同一代内的个体按不同收敛强度移动，种群步长失去同步。
/// **The convergence coefficient is read once for the whole parent group**, not once per parent. Re-reading per parent makes
/// individuals within one generation move under different convergence strengths and desynchronizes the population's step size.
/// **三个领导者的贡献先累加再平均**，不是逐个直接赋值；后者会让 δ 覆盖 α 的结果，实际只剩最后一个领导者在起作用。
/// **The three leaders' contributions are accumulated and then averaged**, not assigned one after another; the latter lets δ overwrite
/// α's result so only the last leader has any effect.
/// </remarks>
/// <param name="Coefficient">收敛系数源 / The convergence-coefficient source.</param>
/// <param name="LeaderAmount">领导者数量 / The leader amount.</param>
public sealed record GreyWolfUpdate(IParameterSource Coefficient, int LeaderAmount) : ISolutionUpdate {
    /// <summary>
    /// 以默认参数构造 / Construct with the default parameters.
    /// </summary>
    public GreyWolfUpdate() : this(new LinearSource(new Flt64(2.0), Flt64.Zero, 100UL), 3) { }

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
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(entropy);
        if (parents.Count == 0) {
            return Array.Empty<Solution<V>>();
        }

        var leaders = parents
            .Take(global::System.Math.Min(global::System.Math.Max(LeaderAmount, 1), parents.Count))
            .Select(parent => (IReadOnlyList<V>)parent.Solution)
            .ToList();
        double convergence = Coefficient.Value(iteration).ToDouble();

        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            int dimension = current.Count;
            double[] accumulated = new double[dimension];
            foreach (IReadOnlyList<V> leader in leaders) {
                double firstRandom = NumericSupport.Unit(entropy);
                double secondRandom = NumericSupport.Unit(entropy);
                double a = (2.0 * convergence * firstRandom) - convergence;
                double c = 2.0 * secondRandom;
                for (int index = 0; index < dimension; index++) {
                    double target = converter.FromValue(leader[index]).ToDouble();
                    accumulated[index] += target - (a * global::System.Math.Abs((c * target) - converter.FromValue(current[index]).ToDouble()));
                }
            }

            double scale = leaders.Count;
            var child = new List<V>(dimension);
            for (int index = 0; index < dimension; index++) {
                double? lower = NumericSupport.LowerBoundOf(model, index, converter);
                double? upper = NumericSupport.UpperBoundOf(model, index, converter);
                child.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(accumulated[index] / scale, lower, upper), converter));
            }
            children.Add(new Solution<V>(child));
        }
        return children;
    }
}

/// <summary>
/// 和声搜索更新 / Harmony-search update.
/// </summary>
/// <remarks>
/// **"随机生成"分支的起点必须是下界而不是 0**。固定从 0 起采样，会让下界远大于 0 的变量空间根本采不到有效值，随后被夹紧到同一个
/// 边界点上——整代个体在这一维完全相同，**而随机性看起来毫无问题**。
/// **The "random generation" branch must start from the lower bound, never 0.** Sampling from 0 makes variables whose lower bound is far
/// above 0 unreachable; every sample then clamps to the same boundary point, so the whole generation shares one value in that
/// dimension **while the randomness looks perfectly intact**.
/// **音高调整的方向对称**（±），只朝一个方向调整会把和声库逐步推向单侧边界。
/// **Pitch adjustment must be symmetric** (±); adjusting in one direction only drifts the harmony memory toward one boundary.
/// </remarks>
/// <param name="MemoryConsiderationRate">记忆库取用概率 / The memory-consideration rate.</param>
/// <param name="PitchAdjustmentRate">音高调整概率 / The pitch-adjustment rate.</param>
/// <param name="Bandwidth">音高调整带宽 / The pitch-adjustment bandwidth.</param>
public sealed record HarmonySearchUpdate(
    double MemoryConsiderationRate,
    double PitchAdjustmentRate,
    double Bandwidth) : ISolutionUpdate {
    /// <summary>
    /// 以默认参数构造 / Construct with the default parameters.
    /// </summary>
    public HarmonySearchUpdate() : this(0.9, 0.3, 0.01) { }

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
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(entropy);
        if (parents.Count == 0) {
            return Array.Empty<Solution<V>>();
        }

        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            int dimension = parent.Solution.Count;
            var child = new List<V>(dimension);
            for (int index = 0; index < dimension; index++) {
                double? lower = NumericSupport.LowerBoundOf(model, index, converter);
                double? upper = NumericSupport.UpperBoundOf(model, index, converter);
                double width = lower is double low && upper is double high
                    && double.IsFinite(low) && double.IsFinite(high)
                    ? high - low
                    : 1.0;

                double value;
                if (NumericSupport.Unit(entropy) < MemoryConsiderationRate) {
                    int donorIndex = (int)(NumericSupport.Unit(entropy) * parents.Count);
                    T donor = parents[global::System.Math.Clamp(donorIndex, 0, parents.Count - 1)];
                    value = converter.FromValue(donor.Solution[index]).ToDouble();
                    if (NumericSupport.Unit(entropy) < PitchAdjustmentRate) {
                        double direction = NumericSupport.Unit(entropy) < 0.5 ? -1.0 : 1.0;
                        value += direction * Bandwidth * width;
                    }
                } else {
                    double origin = lower is double finiteLow && double.IsFinite(finiteLow) ? finiteLow : 0.0;
                    value = origin + (NumericSupport.Unit(entropy) * width);
                }
                child.Add(NumericSupport.AsValue(NumericSupport.ClampTo(value, lower, upper), converter));
            }
            children.Add(new Solution<V>(child));
        }
        return children;
    }
}
