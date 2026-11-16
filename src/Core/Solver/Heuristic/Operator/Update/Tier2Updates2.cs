// 布谷鸟、蝙蝠、飞蛾扑火与蝗虫更新 / Cuckoo-search, bat, moth-flame and grasshopper updates.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 布谷鸟搜索更新 / Cuckoo-search update.
/// </summary>
/// <remarks>
/// **`levySigma` 依赖 Lanczos Γ 近似，系数必须逐位固定**。指数取 1.0 时公式退化为 1.0、取 2.0 时退化为 `1e-8` 量级，
/// 只有默认的 1.5 才给出有意义的 `0.6965745…`。把 `gamma` 换成 `Math.Gamma` 之类"等价"实现会因系数不同而产生
/// **看起来同样合理的**轨迹，因此这里保留8 个 Lanczos 系数，并用独立实现（Python）算出的黄金向量校验。
/// **`levySigma` depends on the Lanczos Γ approximation and its coefficients are pinned bit for bit.** With an exponent of 1.0 the formula
/// degenerates to 1.0 and with 2.0 it collapses to the `1e-8` scale; only the default 1.5 yields a meaningful `0.6965745…`. Swapping
/// `gamma` for an "equivalent" implementation such as `Math.Gamma` produces a **plausible-looking but different** trajectory, so the
/// eight Lanczos coefficients are kept and checked against golden vectors computed by an independent Python implementation.
/// **`v` 必须夹到 `1e-12` 以上**。`abs(normal)` 可能恰好为 0（四个均匀数之和恰为 2.0），于是 `u / 0^(1/1.5)` 是除零。
/// **`v` must be clamped above `1e-12`.** `abs(normal)` can be exactly 0 (four uniform draws summing to 2.0), making
/// `u / 0^(1/1.5)` a division by zero.
/// **`normal` 是"四个均匀数之和减 2"**，不是 `sqrt(12/n) * (sum - n/2)` 的标准近似。写成标准形式会改变分布方差，
/// 步长尺度随之变化，而算法仍然收敛——只是收敛速度不同。
/// **`normal` is "the sum of four uniform draws minus 2"**, not the standard `sqrt(12/n) * (sum - n/2)` approximation. Using the
/// standard form changes the distribution variance and hence the step scale, while the algorithm still converges — just at a different
/// rate.
/// **放弃巢穴的分支按个体判定，但逐维重建**。`abandoned` 在个体级别抽一次，然后每一维各自均匀重建；把它挪到维度级别会让
/// 同一只布谷鸟只有部分维度被重建，解处在"半新半旧"的不一致状态。
/// **The abandonment branch is decided per individual but rebuilt per dimension.** `abandoned` is drawn once per individual and then
/// every dimension is rebuilt uniformly; moving it to the dimension level leaves the same cuckoo with only some dimensions rebuilt, an
/// inconsistent half-new half-old solution.
/// </remarks>
/// <param name="LevyExponent">莱维指数 / The Levy exponent.</param>
/// <param name="StepScale">步长尺度 / The step scale.</param>
/// <param name="AbandonmentProbability">弃巢概率 / The abandonment probability.</param>
public sealed record CuckooSearchUpdate(double LevyExponent, double StepScale, double AbandonmentProbability)
    : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public CuckooSearchUpdate() : this(1.5, 0.01, 0.25) { }

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
        double sigma = LevySigma(LevyExponent);
        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            int dimension = current.Count;
            bool abandoned = NumericSupport.Unit(entropy) < AbandonmentProbability;
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double width = lower is double low && upper is double high
                    && double.IsFinite(low) && double.IsFinite(high)
                    ? high - low
                    : 1.0;
                double origin = lower is double finiteLow && double.IsFinite(finiteLow) ? finiteLow : 0.0;
                double position = converter.FromValue(current[dim]).ToDouble();

                double value;
                if (abandoned) {
                    value = origin + (NumericSupport.Unit(entropy) * width);
                } else {
                    double u = Normal(entropy) * sigma;
                    double v = global::System.Math.Max(
                        global::System.Math.Abs(Normal(entropy)), 1.0e-12);
                    double levy = u / global::System.Math.Pow(v, 1.0 / LevyExponent);
                    value = position + (StepScale * levy
                        * (position - converter.FromValue(best[dim]).ToDouble() + width));
                }
                moved.Add(NumericSupport.AsValue(NumericSupport.ClampTo(value, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }

    /// <summary>
    /// 莱维分布尺度 / The Levy-distribution scale.
    /// </summary>
    /// <param name="exponent">莱维指数 / The Levy exponent.</param>
    /// <returns>尺度 / The scale.</returns>
    internal static double LevySigma(double exponent) {
        double numerator = Gamma(1.0 + exponent) * global::System.Math.Sin(
            global::System.Math.PI * exponent / 2.0);
        double denominator = Gamma((1.0 + exponent) / 2.0) * exponent
            * global::System.Math.Pow(2.0, (exponent - 1.0) / 2.0);
        return global::System.Math.Pow(numerator / denominator, 1.0 / exponent);
    }

    /// <summary>
    /// Lanczos 近似的 Γ 函数 / The Γ function through the Lanczos approximation.
    /// </summary>
    /// <param name="value">自变量 / The argument.</param>
    /// <returns>Γ 值 / The Γ value.</returns>
    internal static double Gamma(double value) {
        double[] coefficients = {
            676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012,
            9.9843695780195716e-6, 1.5056327351493116e-7,
        };
        if (value < 0.5) {
            return global::System.Math.PI
                / (global::System.Math.Sin(global::System.Math.PI * value) * Gamma(1.0 - value));
        }
        double shifted = value - 1.0;
        double series = 0.99999999999980993;
        for (int index = 0; index < coefficients.Length; index++) {
            series += coefficients[index] / (shifted + index + 1.0);
        }
        double t = shifted + coefficients.Length - 0.5;
        return global::System.Math.Sqrt(2.0 * global::System.Math.PI)
            * global::System.Math.Pow(t, shifted + 0.5)
            * global::System.Math.Exp(-t)
            * series;
    }

    /// <summary>
    /// 四个均匀数之和减 2 / The sum of four uniform draws minus 2.
    /// </summary>
    /// <param name="entropy">均匀熵源 / The uniform entropy source.</param>
    /// <returns>近似正态数 / The near-normal number.</returns>
    private static double Normal(Func<Flt64> entropy) =>
        NumericSupport.Unit(entropy) + NumericSupport.Unit(entropy)
        + NumericSupport.Unit(entropy) + NumericSupport.Unit(entropy) - 2.0;
}

/// <summary>
/// 蝙蝠算法更新 / Bat-algorithm update.
/// </summary>
/// <remarks>
/// **频率按个体抽取一次，逐维共用**。这是写法：整只蝙蝠用同一个频率，但每一维各抽一个数。把频率挪进维度循环会让
/// 各维在完全不同的频段上更新，蝙蝠的运动失去一致性。
/// **The frequency is drawn once per individual and shared across dimensions.** That is its shape: one bat uses one
/// frequency while each dimension draws its own random number. Moving the frequency inside the dimension loop makes each dimension
/// update in a completely different band and the bat's motion loses coherence.
/// **步长方向是"远离最优"**：`(position - best) * frequency * unit`。写成 `best - position` 会让蝙蝠朝最优跳跃，
/// 而这与"回声定位探索"的意图相反——收敛更快、更容易早熟，但没有任何数值异常。
/// **The step points away from the best**: `(position - best) * frequency * unit`. Writing `best - position` makes the bat jump toward
/// the best, contradicting the echolocation-exploration intent — faster convergence and premature stagnation, with no numerical anomaly.
/// </remarks>
/// <param name="MinimumFrequency">最低频率 / The minimum frequency.</param>
/// <param name="MaximumFrequency">最高频率 / The maximum frequency.</param>
public sealed record BatAlgorithmUpdate(double MinimumFrequency, double MaximumFrequency)
    : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public BatAlgorithmUpdate() : this(0.0, 2.0) { }

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
            int dimension = current.Count;
            double frequency = MinimumFrequency
                + (NumericSupport.Unit(entropy) * (MaximumFrequency - MinimumFrequency));
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double position = converter.FromValue(current[dim]).ToDouble();
                double step = (position - converter.FromValue(best[dim]).ToDouble())
                    * frequency * NumericSupport.Unit(entropy);
                moved.Add(NumericSupport.AsValue(NumericSupport.ClampTo(position + step, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}

/// <summary>
/// 飞蛾扑火更新 / Moth-flame update.
/// </summary>
/// <remarks>
/// **火焰数量随迭代从 `size` 递减到 1**，搜索由多点并行探索过渡到围绕最优精修。把 `flameAmount` 固定为 `size` 会让算法永远
/// 在探索阶段，收敛停滞；固定为 1 则一开始就只围绕最优，早熟。
/// **The flame count decreases from `size` towards 1**, shifting the search from parallel exploration to refinement around the best.
/// Pinning `flameAmount` to `size` keeps the algorithm exploring forever and convergence stalls; pinning it to 1 makes it refine
/// around the best from the start and stagnate prematurely.
/// **螺旋参数按个体抽取一次，逐维共用**，而每一维的距离各自计算。这是形状。
/// **The spiral parameter is drawn once per individual and shared across dimensions**, while each dimension computes its own distance.
/// That is its shape.
/// </remarks>
/// <param name="SpiralRate">螺旋增长率 / The spiral growth rate.</param>
/// <param name="FlameRatio">火焰比例演化源 / The flame-ratio source.</param>
public sealed record MothFlameUpdate(double SpiralRate, IParameterSource FlameRatio) : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public MothFlameUpdate() : this(1.0, new LinearSource(Flt64.One, new Flt64(0.01), 100UL)) { }

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
        int flameAmount = global::System.Math.Clamp(
            (int)(size * FlameRatio.Value(iteration).ToDouble()), 1, size);
        var children = new List<Solution<V>>(size);
        for (int index = 0; index < size; index++) {
            IReadOnlyList<V> current = parents[index].Solution;
            int dimension = current.Count;
            IReadOnlyList<V> flame = parents[global::System.Math.Clamp(index % flameAmount, 0, size - 1)].Solution;
            double spiral = (NumericSupport.Unit(entropy) * 2.0) - 1.0;
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double flamePosition = converter.FromValue(flame[dim]).ToDouble();
                double distance = global::System.Math.Abs(
                    flamePosition - converter.FromValue(current[dim]).ToDouble());
                double value = (distance * global::System.Math.Exp(SpiralRate * spiral)
                    * global::System.Math.Cos(2.0 * global::System.Math.PI * spiral)) + flamePosition;
                moved.Add(NumericSupport.AsValue(NumericSupport.ClampTo(value, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}

/// <summary>
/// 蝗虫更新 / Grasshopper update.
/// </summary>
/// <remarks>
/// **距离为 0 的同伴必须跳过**。`interaction / distance` 在 `distance == 0` 时是除零，而"自己与自己"的距离恒为 0——
/// 每个个体都在自己的循环里，跳过判断就是唯一防线。`ClampTo` 会把 `NaN` 折算成边界，**于是整代被钉在边界上**。
/// **Companions at distance 0 must be skipped.** `interaction / distance` divides by zero at `distance == 0`, and "self with self" is
/// always 0 — every individual iterates over the whole population, so the skip is the only guard. `ClampTo` would fold the `NaN` onto
/// a bound and **pin the whole generation to the boundary**.
/// **收缩系数是平方衰减**：`coefficient * coefficient * (width / 2) * social`。只乘一次会让后期仍有大幅移动，种群在最优点附近震荡。
/// **The shrink coefficient is applied squared**: `coefficient * coefficient * (width / 2) * social`. Applying it once keeps late
/// iterations moving widely and the population oscillates around the optimum.
/// **结果以最优为基准平移**（`+ best[dim]`），而 `social` 只是相对位移。漏掉这一项会让整个种群滑向原点。
/// **The result is offset from the best** (`+ best[dim]`) while `social` is only a relative displacement. Omitting it slides the whole
/// population toward the origin.
/// </remarks>
/// <param name="Shrink">收缩系数演化源 / The shrink-coefficient source.</param>
/// <param name="AttractionScale">吸引力尺度 / The attraction scale.</param>
/// <param name="AttractionLength">吸引力长度 / The attraction length.</param>
public sealed record GrasshopperUpdate(IParameterSource Shrink, double AttractionScale, double AttractionLength)
    : ISolutionUpdate {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public GrasshopperUpdate()
        : this(new LinearSource(Flt64.One, new Flt64(0.00004), 100UL), 0.5, 1.5) { }

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
        double coefficient = Shrink.Value(iteration).ToDouble();
        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            int dimension = current.Count;
            var moved = new List<V>(dimension);
            for (int dim = 0; dim < dimension; dim++) {
                double? lower = NumericSupport.LowerBoundOf(model, dim, converter);
                double? upper = NumericSupport.UpperBoundOf(model, dim, converter);
                double width = lower is double low && upper is double high
                    && double.IsFinite(low) && double.IsFinite(high)
                    ? high - low
                    : 1.0;

                double social = 0.0;
                foreach (T other in parents) {
                    double position = converter.FromValue(current[dim]).ToDouble();
                    double otherPosition = converter.FromValue(other.Solution[dim]).ToDouble();
                    double distance = global::System.Math.Abs(otherPosition - position);
                    if (distance <= 0.0) {
                        continue;
                    }
                    double interaction = (AttractionScale * global::System.Math.Exp(-distance / AttractionLength))
                        - global::System.Math.Exp(-distance);
                    social += interaction * (otherPosition - position) / distance;
                }
                double value = (coefficient * coefficient * (width / 2.0) * social)
                    + converter.FromValue(best[dim]).ToDouble();
                moved.Add(NumericSupport.AsValue(NumericSupport.ClampTo(value, lower, upper), converter));
            }
            children.Add(new Solution<V>(moved));
        }
        return children;
    }
}
