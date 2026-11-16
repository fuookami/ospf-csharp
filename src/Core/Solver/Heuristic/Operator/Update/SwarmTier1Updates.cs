// 水循环与海鸥更新算子 / Water-cycle and seagull update operators.

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
/// 水循环更新 / Water-cycle update.
/// </summary>
/// <remarks>
/// **父代组按优度升序传入**：`parents[0]` 是"海"，紧接的 `riverAmount` 个是"河"，其余是"溪"。这个分层完全依赖排序契约；
/// 若父代乱序，"溪流流向河流"就变成"流向随机个体"，而算法仍会正常运行，只是失去了分层结构。
/// **The parent group arrives sorted ascending**: `parents[0]` is the "sea", the next `riverAmount` are "rivers", and the rest are
/// "streams". This layering depends entirely on the sort contract; with an unsorted group "streams flow into rivers" becomes "flow
/// into a random individual" and the algorithm still runs, having merely lost its layering.
/// **海自己做一次小幅随机游走**，不向任何目标流动。让海也参与"向最优流动"会让它原地不动（它自己就是最优），种群随即失去唯一的
/// 全局参考点。
/// **The sea performs a small random walk** rather than flowing toward any target. Making the sea flow toward the best would leave it
/// pinned in place (it is the best), and the population would lose its only global reference point.
/// **降雨分支在取值域内均匀重生**，起点是下界而不是 0。固定从 0 起采样会让下界远大于 0 的变量空间根本采不到有效值，
/// 全部夹紧到同一边界点，**而"随机重生"看起来毫无问题**。
/// **The rain branch rebirths uniformly across the domain**, starting from the lower bound rather than 0. Sampling from 0 makes variables
/// whose lower bound is far above 0 unreachable and clamps everything to one boundary point, **while the "random rebirth" looks fine**.
/// **河流下标按 `(index - 1) % riverCount + 1` 轮转分配**。`riverCount == 0` 时必须回落到海，否则会除零。
/// **River indices rotate via `(index - 1) % riverCount + 1`.** When `riverCount == 0` the target must fall back to the sea, otherwise
/// the modulo divides by zero.
/// </remarks>
/// <param name="RiverAmount">河流数量 / The river amount.</param>
/// <param name="FlowScale">流动步长倍数 / The flow-step scale.</param>
/// <param name="RainingProbability">降雨概率 / The raining probability.</param>
public sealed record WaterCycleUpdate(int RiverAmount, double FlowScale, double RainingProbability)
    : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public WaterCycleUpdate() : this(4, 1.0, 0.1) { }

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

        IReadOnlyList<V> sea = parents[0].Solution;
        int riverCount = global::System.Math.Clamp(
            RiverAmount, 0, global::System.Math.Max(parents.Count - 1, 0));

        var children = new List<Solution<V>>(parents.Count);
        for (int index = 0; index < parents.Count; index++) {
            IReadOnlyList<V> current = parents[index].Solution;
            int dimension = current.Count;

            if (NumericSupport.Unit(entropy) < RainingProbability) {
                var rain = new List<V>(dimension);
                for (int dim = 0; dim < dimension; dim++) {
                    double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                    double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                    double width = lower is double low && upper is double high
                        && double.IsFinite(low) && double.IsFinite(high)
                        ? high - low
                        : 1.0;
                    double origin = lower is double finiteLow && double.IsFinite(finiteLow) ? finiteLow : 0.0;
                    rain.Add(NumericSupport.AsValue(
                        NumericSupport.ClampTo(origin + (NumericSupport.Unit(entropy) * width), lower, upper),
                        converter));
                }
                children.Add(new Solution<V>(rain));
                continue;
            }

            IReadOnlyList<V> target = index == 0 || index <= riverCount || riverCount == 0
                ? sea
                : parents[(((index - 1) % riverCount) + 1)].Solution;

            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double step;
                if (index == 0) {
                    double width = lower is double low && upper is double high
                        && double.IsFinite(low) && double.IsFinite(high)
                        ? high - low
                        : 1.0;
                    step = ((NumericSupport.Unit(entropy) * 2.0) - 1.0) * 0.1 * width * FlowScale;
                } else {
                    step = NumericSupport.Unit(entropy)
                        * (converter.FromValue(target[dim]).ToDouble() - converter.FromValue(current[dim]).ToDouble())
                        * FlowScale;
                }
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
/// 海鸥更新 / Seagull update.
/// </summary>
/// <remarks>
/// **两个阶段按控制系数是否达到 1.0 切换**。控制系数从 2.0 线性降到 0，所以前期走"迁移"（向最优大幅靠拢），后期走"螺旋攻击"
/// （围绕最优盘旋收敛）。把判据写反或把两段合并，算法仍会收敛，但会失去"先探索后开发"的两段结构。
/// **The two phases switch on whether the control coefficient reaches 1.0.** The coefficient decays linearly from 2.0 to 0, so the early
/// phase is "migration" (a large move toward the best) and the late phase is the "spiral attack" (circling in on the best). Reversing
/// the predicate or merging the phases still converges, but the explore-then-exploit structure is gone.
/// **螺旋半径是 `exp(theta * spiralRate)`，必须随角度增长**。固定半径会退化成在圆上打转、永不靠近最优。
/// **The spiral radius is `exp(theta * spiralRate)` and must grow with the angle.** A fixed radius degenerates into orbiting a circle
/// without ever approaching the best.
/// **`theta` 必须取自 `[0, 2π)` 的均匀分布**。把 `2π` 漏掉会让所有个体挤在同一个角度上，螺旋退化成一条射线。
/// **`theta` must be drawn uniformly from `[0, 2π)`.** Dropping the `2π` puts every individual on the same angle and the spiral
/// degenerates into a single ray.
/// </remarks>
/// <param name="Control">控制系数演化源 / The control-coefficient source.</param>
/// <param name="SpiralRate">螺旋增长率 / The spiral growth rate.</param>
public sealed record SeagullUpdate(IParameterSource Control, double SpiralRate) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public SeagullUpdate() : this(new LinearSource(new Flt64(2.0), Flt64.Zero, 100UL), 0.1) { }

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
        double coefficient = Control.Value(iteration).ToDouble();
        bool migrated = coefficient >= 1.0;

        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            var child = new List<V>(current.Count);
            for (int index = 0; index < current.Count; index++) {
                double? lower = NumericSupport.LowerBoundOf(model, index, converter);
                double? upper = NumericSupport.UpperBoundOf(model, index, converter);
                double position = converter.FromValue(current[index]).ToDouble();
                double target = converter.FromValue(best[index]).ToDouble();

                double moved;
                if (migrated) {
                    double amplification = 2.0 * coefficient * coefficient * NumericSupport.Unit(entropy);
                    moved = position + (amplification * (target - position));
                } else {
                    double theta = 2.0 * global::System.Math.PI * NumericSupport.Unit(entropy);
                    double radius = global::System.Math.Exp(theta * SpiralRate);
                    moved = target + ((position - target) * radius * global::System.Math.Cos(theta));
                }
                child.Add(NumericSupport.AsValue(NumericSupport.ClampTo(moved, lower, upper), converter));
            }
            children.Add(new Solution<V>(child));
        }
        return children;
    }
}
