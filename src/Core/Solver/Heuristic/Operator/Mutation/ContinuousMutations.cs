// 连续变异算子 / Continuous mutation operators.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Distribution;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;

/// <summary>
/// 连续变异共用辅助 / Shared helpers for continuous mutations.
/// </summary>
public static class ContinuousMutationSupport {
    /// <summary>
    /// 变量宽度，无有限上下界时退化为 1.0 / The variable width, degrading to 1.0 when the bounds are not finite.
    /// </summary>
    /// <remarks>
    /// **退化值是 1.0 而不是 0**。退化到 0 会让所有基于宽度的步长变成零位移——变异静默失效，而种群看上去仍在正常迭代。
    /// **The degradation value is 1.0, not 0.** Degrading to 0 would make every width-based step a zero displacement: the mutation
    /// silently stops working while the population still appears to iterate normally.
    /// </remarks>
    /// <param name="lower">下界 / The lower bound.</param>
    /// <param name="upper">上界 / The upper bound.</param>
    /// <returns>宽度 / The width.</returns>
    public static double WidthOf(double? lower, double? upper) =>
        lower is double low && upper is double high && double.IsFinite(low) && double.IsFinite(high)
            ? high - low
            : 1.0;

    /// <summary>
    /// 逐基因施加变异 / Apply a mutation gene by gene.
    /// </summary>
    /// <typeparam name="TObj">目标类型 / The objective type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="solution">原始解 / The original solution.</param>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    /// <param name="mutationRate">逐基因变异率 / The per-gene mutation rate.</param>
    /// <param name="step">单基因变换函数 / The per-gene transformation.</param>
    /// <returns>变异后的解 / The mutated solution.</returns>
    public static Solution<V> MutateGenes<TObj, TObjValue, V>(
        IReadOnlyList<V> solution,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        double mutationRate,
        Func<int, double, double?, double?, double> step) where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(step);

        var result = new List<V>(solution);
        for (int index = 0; index < result.Count; index++) {
            if (!NumericSupport.ShouldFire(entropy, mutationRate)) {
                continue;
            }
            double current = result[index].ToFlt64().ToDouble();
            double? lower = NumericSupport.LowerBoundOf(model, index, converter);
            double? upper = NumericSupport.UpperBoundOf(model, index, converter);
            double mutated = NumericSupport.ClampTo(step(index, current, lower, upper), lower, upper);
            result[index] = NumericSupport.AsValue(mutated, converter);
        }
        return new Solution<V>(result);
    }
}

/// <summary>
/// 高斯变异 / Gaussian mutation.
/// </summary>
/// <param name="Sigma">标准差（占变量宽度的比例）/ The standard deviation as a ratio of the variable width.</param>
public sealed record GaussianMutation(Flt64 Sigma) : IMutationOperator {
    /// <summary>以默认标准差构造 / Construct with the default sigma.</summary>
    public GaussianMutation() : this(new Flt64(0.1)) { }

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        var gaussian = new Gaussian(Flt64.Zero, Flt64.One);
        double scale = Sigma.ToDouble();
        return ContinuousMutationSupport.MutateGenes(
            individual.Solution, model, converter, entropy, mutationRate.ToDouble(),
            (_, current, lower, upper) =>
                current + gaussian.Sample(() => entropy()).ToDouble() * scale
                    * ContinuousMutationSupport.WidthOf(lower, upper));
    }
}

/// <summary>
/// 柯西变异，重尾步长有利于跳出局部最优 / Cauchy mutation whose heavy-tailed step helps escape local optima.
/// </summary>
/// <param name="Gamma">尺度参数（占变量宽度的比例）/ The scale parameter as a ratio of the variable width.</param>
public sealed record CauchyMutation(Flt64 Gamma) : IMutationOperator {
    /// <summary>以默认尺度构造 / Construct with the default scale.</summary>
    public CauchyMutation() : this(new Flt64(0.1)) { }

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        var cauchy = new Cauchy(Flt64.Zero, Flt64.One);
        double scale = Gamma.ToDouble();
        return ContinuousMutationSupport.MutateGenes(
            individual.Solution, model, converter, entropy, mutationRate.ToDouble(),
            (_, current, lower, upper) =>
                current + cauchy.Sample(() => entropy()).ToDouble() * scale
                    * ContinuousMutationSupport.WidthOf(lower, upper));
    }
}

/// <summary>
/// 莱维变异，用重尾稳定分布提供大跨度跳变 / Levy mutation using a heavy-tailed stable distribution for long jumps.
/// </summary>
/// <param name="Alpha">稳定性参数 / The stability parameter.</param>
/// <param name="Scale">幅值比例 / The magnitude ratio.</param>
public sealed record LevyMutation(Flt64 Alpha, Flt64 Scale) : IMutationOperator {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public LevyMutation() : this(new Flt64(1.5), new Flt64(0.1)) { }

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        var levy = new Levy(Alpha);
        double magnitude = Scale.ToDouble();
        return ContinuousMutationSupport.MutateGenes(
            individual.Solution, model, converter, entropy, mutationRate.ToDouble(),
            (_, current, lower, upper) =>
                current + levy.Sample(() => entropy()).ToDouble() * magnitude
                    * ContinuousMutationSupport.WidthOf(lower, upper));
    }
}

/// <summary>
/// 均匀变异，在整个区间内重新采样 / Uniform mutation resampling across the whole interval.
/// </summary>
public sealed class UniformMutation : IMutationOperator {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static UniformMutation Instance { get; } = new();

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        return ContinuousMutationSupport.MutateGenes(
            individual.Solution, model, converter, entropy, mutationRate.ToDouble(),
            (_, current, lower, upper) =>
                lower is double low && upper is double high && double.IsFinite(low) && double.IsFinite(high)
                    ? low + NumericSupport.Unit(entropy) * (high - low)
                    : current + (NumericSupport.Unit(entropy) - 0.5) * 2.0);
    }
}

/// <summary>
/// 边界变异，把基因推到变量的下界或上界 / Boundary mutation pushing a gene onto the lower or upper bound.
/// </summary>
public sealed class BoundaryMutation : IMutationOperator {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static BoundaryMutation Instance { get; } = new();

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        return ContinuousMutationSupport.MutateGenes(
            individual.Solution, model, converter, entropy, mutationRate.ToDouble(),
            (_, current, lower, upper) =>
                lower is double low && upper is double high && double.IsFinite(low) && double.IsFinite(high)
                    ? (NumericSupport.Unit(entropy) < 0.5 ? low : high)
                    : current);
    }
}

/// <summary>
/// 多项式变异（Deb），由分布指数控制扰动集中在边界附近的强度 / Polynomial mutation (Deb) whose distribution index controls how
/// strongly perturbations concentrate near the bounds.
/// </summary>
/// <param name="DistributionIndex">分布指数 η / The distribution index eta.</param>
public sealed record PolynomialMutation(Flt64 DistributionIndex) : IMutationOperator {
    /// <summary>以默认分布指数构造 / Construct with the default distribution index.</summary>
    public PolynomialMutation() : this(new Flt64(20.0)) { }

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        double eta = global::System.Math.Max(DistributionIndex.ToDouble(), 0.0);
        return ContinuousMutationSupport.MutateGenes(
            individual.Solution, model, converter, entropy, mutationRate.ToDouble(),
            (_, current, lower, upper) => {
                if (lower is not double low || upper is not double high
                    || !double.IsFinite(low) || !double.IsFinite(high) || high <= low) {
                    return current + (NumericSupport.Unit(entropy) - 0.5) * 2.0;
                }

                double delta1 = (current - low) / (high - low);
                double delta2 = (high - current) / (high - low);
                double random = NumericSupport.Unit(entropy);
                double deltaQ = random <= 0.5
                    ? global::System.Math.Pow(
                        2.0 * random + (1.0 - 2.0 * random) * global::System.Math.Pow(1.0 - delta1, eta + 1.0),
                        1.0 / (eta + 1.0)) - 1.0
                    : 1.0 - global::System.Math.Pow(
                        2.0 * (1.0 - random) + 2.0 * (random - 0.5) * global::System.Math.Pow(1.0 - delta2, eta + 1.0),
                        1.0 / (eta + 1.0));
                return current + deltaQ * (high - low);
            });
    }
}

/// <summary>
/// 非一致变异（Michalewicz），扰动幅度随迭代推进而衰减 / Non-uniform mutation (Michalewicz) whose perturbation magnitude decays
/// with the iterations.
/// </summary>
/// <param name="Decay">衰减指数 b / The decay exponent b.</param>
/// <param name="MaxIteration">迭代上限，用于计算进度 / The iteration limit used to compute progress.</param>
public sealed record NonUniformMutation(Flt64 Decay, ulong MaxIteration) : IMutationOperator {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public NonUniformMutation() : this(new Flt64(5.0), 1000) { }

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        ArgumentNullException.ThrowIfNull(iteration);
        double decay = global::System.Math.Max(Decay.ToDouble(), 1.0e-12);
        double progress = MaxIteration == 0
            ? 0.0
            : global::System.Math.Clamp((double)iteration.Current / MaxIteration, 0.0, 1.0);
        double shrink = global::System.Math.Pow(1.0 - progress, decay);

        return ContinuousMutationSupport.MutateGenes(
            individual.Solution, model, converter, entropy, mutationRate.ToDouble(),
            (_, current, lower, upper) => {
                if (lower is not double low || upper is not double high
                    || !double.IsFinite(low) || !double.IsFinite(high)) {
                    return current + (NumericSupport.Unit(entropy) - 0.5) * 2.0;
                }

                double random = NumericSupport.Unit(entropy);
                double delta = 1.0 - global::System.Math.Pow(random, shrink);
                return NumericSupport.Unit(entropy) < 0.5
                    ? current + delta * (high - current)
                    : current - delta * (current - low);
            });
    }
}

/// <summary>
/// 混沌变异，先用 Logistic 映射打散均匀随机数再映射到变量区间 / Chaotic mutation scrambling the uniform sample through the
/// logistic map before mapping it onto the variable interval.
/// </summary>
/// <param name="Iterations">Logistic 映射迭代次数 / The number of logistic-map iterations.</param>
public sealed record ChaoticMutation(int Iterations) : IMutationOperator {
    /// <summary>以默认迭代次数构造 / Construct with the default iteration count.</summary>
    public ChaoticMutation() : this(3) { }

    /// <inheritdoc/>
    public Solution<V> Invoke<V, TObj, TObjValue, T>(
        Iteration iteration,
        T individual,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy,
        Flt64 mutationRate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(individual);
        return ContinuousMutationSupport.MutateGenes(
            individual.Solution, model, converter, entropy, mutationRate.ToDouble(),
            (_, current, lower, upper) => {
                // Logistic 映射在 0 与 1 上是不动点，必须把初值夹到开区间内，否则整段扰动恒为零。
                // The logistic map has fixed points at 0 and 1, so the seed must be clamped into the open interval; otherwise the
                // perturbation is identically zero.
                double chaotic = global::System.Math.Clamp(NumericSupport.Unit(entropy), 1.0e-6, 1.0 - 1.0e-6);
                for (int step = 0; step < global::System.Math.Max(Iterations, 1); step++) {
                    chaotic = 4.0 * chaotic * (1.0 - chaotic);
                }
                return lower is double low && upper is double high && double.IsFinite(low) && double.IsFinite(high)
                    ? low + chaotic * (high - low)
                    : current + (chaotic - 0.5) * 2.0;
            });
    }
}
