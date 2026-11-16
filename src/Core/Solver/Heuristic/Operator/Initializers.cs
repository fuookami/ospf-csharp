// 参数演化源与初始化算子 / Parameter evolution sources and initializers.
// 与固定 Kotlin 、
// 对应。
// , and.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 随迭代演化的参数源 / A parameter source that evolves with the iterations.
/// </summary>
/// <remarks>
/// **把参数写成 `Iteration` 的函数，而不是在算子内部维护可变状态**。可变参数会让"同一份种子重放"取决于调用顺序与中断点；
/// 写成纯函数后，检查点只需记录迭代计数，恢复即可得到同一个参数值。
/// **Parameters are functions of `Iteration` rather than mutable state inside operators.** Mutable parameters make "replay under the
/// same seed" depend on call order and interruption points; as pure functions, a checkpoint only needs the iteration count and the
/// restore yields the same parameter value.
/// </remarks>
public interface IParameterSource {
    /// <summary>
    /// 计算当前迭代的参数值 / Compute the parameter value at the current iteration.
    /// </summary>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <returns>参数值 / The parameter value.</returns>
    Flt64 Value(Iteration iteration);
}

/// <summary>
/// 参数演化辅助 / Parameter-evolution helpers.
/// </summary>
public static class ParameterSources {
    /// <summary>
    /// 迭代进度，归一到 [0,1] / The iteration progress normalized into [0,1].
    /// </summary>
    /// <remarks>
    /// **`limit == 0` 时进度取 1 而不是 0**。取 0 会让"上限未设置"被解释成"还没开始"，线性退火因此永远停在起点，
    /// 而搜索看起来已经在跑。
    /// **A zero `limit` yields progress 1, not 0.** Yielding 0 would interpret "no limit configured" as "not started yet", pinning
    /// linear annealing at its starting point while the search appears to be running.
    /// </remarks>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="limit">迭代上限 / The iteration limit.</param>
    /// <returns>进度 / The progress.</returns>
    public static Flt64 Progress(Iteration iteration, ulong limit) {
        ArgumentNullException.ThrowIfNull(iteration);
        if (limit == 0) {
            return Flt64.One;
        }
        return new Flt64(global::System.Math.Clamp((double)iteration.Current / limit, 0.0, 1.0));
    }
}

/// <summary>
/// 恒定参数 / A constant parameter.
/// </summary>
/// <param name="Parameter">参数值 / The parameter value.</param>
public sealed record ConstantSource(Flt64 Parameter) : IParameterSource {
    /// <inheritdoc/>
    public Flt64 Value(Iteration iteration) => Parameter;
}

/// <summary>
/// 线性演化参数 / A linearly evolving parameter.
/// </summary>
/// <param name="From">起始值 / The starting value.</param>
/// <param name="To">终止值 / The ending value.</param>
/// <param name="Limit">迭代上限 / The iteration limit.</param>
public sealed record LinearSource(Flt64 From, Flt64 To, ulong Limit) : IParameterSource {
    /// <inheritdoc/>
    public Flt64 Value(Iteration iteration) {
        double progress = ParameterSources.Progress(iteration, Limit).ToDouble();
        return new Flt64(From.ToDouble() + (To.ToDouble() - From.ToDouble()) * progress);
    }
}

/// <summary>
/// 几何演化参数 / A geometrically evolving parameter.
/// </summary>
/// <param name="Initial">初始值 / The initial value.</param>
/// <param name="Ratio">每步比值 / The per-step ratio.</param>
public sealed record GeometricSource(Flt64 Initial, Flt64 Ratio) : IParameterSource {
    /// <inheritdoc/>
    public Flt64 Value(Iteration iteration) {
        ArgumentNullException.ThrowIfNull(iteration);
        return new Flt64(Initial.ToDouble() * global::System.Math.Pow(Ratio.ToDouble(), iteration.Current));
    }
}

/// <summary>
/// 指数衰减参数 / An exponentially decaying parameter.
/// </summary>
/// <param name="Initial">初始值 / The initial value.</param>
/// <param name="Rate">衰减率 / The decay rate.</param>
public sealed record ExponentialSource(Flt64 Initial, Flt64 Rate) : IParameterSource {
    /// <inheritdoc/>
    public Flt64 Value(Iteration iteration) {
        ArgumentNullException.ThrowIfNull(iteration);
        return new Flt64(Initial.ToDouble() * global::System.Math.Exp(-Rate.ToDouble() * iteration.Current));
    }
}

/// <summary>
/// 生成初始解 / Generate initial solutions.
/// </summary>
public interface IInitializer {
    /// <summary>
    /// 生成初始解 / Generate initial solutions.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="amount">需要的解数量 / The required solution amount.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <returns>初始解列表 / The initial solutions.</returns>
    IReadOnlyList<Solution<V>> Generate<V, TObj, TObjValue>(
        Iteration iteration,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        ulong amount,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V>;
}

/// <summary>
/// 直接使用模型提供的初始解 / Use the initial solutions provided by the model.
/// </summary>
public sealed class ModelInitializer : IInitializer {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static ModelInitializer Instance { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Generate<V, TObj, TObjValue>(
        Iteration iteration,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        ulong amount,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<IReadOnlyList<V>> provided = model.InitialSolutions(amount);
        var result = new List<Solution<V>>(provided.Count);
        foreach (IReadOnlyList<V> solution in provided) {
            result.Add(new Solution<V>(solution));
        }
        return result;
    }
}

/// <summary>
/// 均匀随机初始化，在变量上下界内均匀采样 / Uniform random initialization sampling within the variable bounds.
/// </summary>
/// <remarks>
/// **只有全部变量都有有限上下界时才按区间采样，否则回退到模型自带的初始解**。对无界变量瞎猜一个区间，会让种群从一开始就落在
/// 一个与问题量级无关的范围内，之后的适应度与收敛曲线全部失真，而没有任何信号提示这一点。
/// **Sampling only happens when every variable has finite bounds; otherwise this falls back to the model's own initial solutions.**
/// Guessing an interval for an unbounded variable would place the population in a range unrelated to the problem's scale from the
/// very first generation, distorting every fitness and convergence curve with no signal that it happened.
/// </remarks>
/// <param name="Entropy">均匀分布熵源 / The uniform entropy source.</param>
public sealed record RandomUniformInitializer(Func<Flt64> Entropy) : IInitializer {
    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Generate<V, TObj, TObjValue>(
        Iteration iteration,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        ulong amount,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(converter);

        IReadOnlyList<Fuookami.Ospf.Core.Token.Token<V>> tokens = model.Tokens.TokensInSolver;
        // **空令牌表必须回退，不能走采样**。`all { ... }` 在零变量上空真成立，于是"全都有界"，接着生成一批长度为零的解——
        // 种群看起来有 `amount` 个成员，实际每个都不含任何基因，目标评估只会得到默认值。这是最容易"看起来一切正常"的一类缺陷。
        // **An empty token table must fall back, never sample.** The code's `all { ... }` is vacuously true over zero variables, so
        // "everything is bounded" holds and a batch of zero-length solutions is produced: the population looks like it has `amount`
        // members while none of them carries a single gene, and objective evaluation only ever returns the default. This is the
        // easiest kind of defect to "look completely normal".
        if (tokens.Count == 0) {
            return ModelInitializer.Instance.Generate(iteration, model, amount, converter);
        }
        var bounds = new List<(double Lower, double Upper)>(tokens.Count);
        foreach (Fuookami.Ospf.Core.Token.Token<V> token in tokens) {
            double? lower = NumericSupport.LowerBoundOf(model, bounds.Count, converter);
            double? upper = NumericSupport.UpperBoundOf(model, bounds.Count, converter);
            if (lower is not double low || upper is not double high
                || !double.IsFinite(low) || !double.IsFinite(high)) {
                return ModelInitializer.Instance.Generate(iteration, model, amount, converter);
            }
            bounds.Add((low, high));
        }

        var result = new List<Solution<V>>((int)global::System.Math.Min(amount, int.MaxValue));
        for (ulong index = 0; index < amount; index++) {
            var values = new List<V>(bounds.Count);
            foreach ((double lower, double upper) in bounds) {
                double unit = NumericSupport.Unit(Entropy);
                values.Add(NumericSupport.AsValue(lower + unit * (upper - lower), converter));
            }
            result.Add(new Solution<V>(values));
        }
        return result;
    }
}

/// <summary>
/// 反对立学习初始化，在基础初始化之上补充镜像解 / Opposition-based learning initialization augmenting a base initializer with
/// mirrored solutions.
/// </summary>
/// <remarks>
/// **这一实现会对无界变量直接崩溃**：它用 `token.lowerBound!!` 非空断言取值。本实现改为——无界变量保持原值、不参与镜像，
/// 而不是抛异常；否则"模型里有一个无界变量"就会让整个初始化失败，而调用方拿到的只是一个空引用异常。
/// **The code's implementation crashes outright on unbounded variables**: it dereferences `token.lowerBound!!`. This implementation keeps
/// the original value for unbounded variables and skips mirroring them instead of throwing; otherwise "one unbounded variable in the
/// model" fails the whole initialization, and the caller only sees a null-reference exception.
/// **镜像解会成倍放大种群**。`originals + mirrored` 的长度是请求数量的两倍——调用方若已按 `amount` 分配了缓冲区，必须自行截断。
/// **Mirroring doubles the population.** `originals + mirrored` is twice the requested amount; a caller that already sized a buffer by
/// `amount` must truncate.
/// </remarks>
/// <param name="Base">基础初始化算子 / The base initializer.</param>
public sealed record OppositionBasedInitializer(IInitializer? Base = null) : IInitializer {
    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Generate<V, TObj, TObjValue>(
        Iteration iteration,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        ulong amount,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(converter);

        IReadOnlyList<Solution<V>> originals =
            (Base ?? ModelInitializer.Instance).Generate(iteration, model, amount, converter);
        var result = new List<Solution<V>>(originals.Count * 2);
        result.AddRange(originals);

        foreach (Solution<V> solution in originals) {
            var mirrored = new List<V>(solution.Values.Count);
            for (int index = 0; index < solution.Values.Count; index++) {
                double? lower = NumericSupport.LowerBoundOf(model, index, converter);
                double? upper = NumericSupport.UpperBoundOf(model, index, converter);
                if (lower is not double low || upper is not double high
                    || !double.IsFinite(low) || !double.IsFinite(high)) {
                    mirrored.Add(solution.Values[index]);
                    continue;
                }
                double mirroredValue = low + high - converter.FromValue(solution.Values[index]).ToDouble();
                mirrored.Add(NumericSupport.AsValue(
                    NumericSupport.ClampTo(mirroredValue, low, high), converter));
            }
            result.Add(new Solution<V>(mirrored));
        }
        return result;
    }
}

/// <summary>
/// 固定值初始化，把给定向量复制成多份 / Fixed-value initialization copying a given vector several times.
/// </summary>
/// <param name="Values">固定解向量 / The fixed solution vector.</param>
public sealed record FixedInitializer(IReadOnlyList<double> Values) : IInitializer {
    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Generate<V, TObj, TObjValue>(
        Iteration iteration,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        ulong amount,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(converter);
        if (Values.Count == 0) {
            return Array.Empty<Solution<V>>();
        }

        var values = new List<V>(Values.Count);
        foreach (double value in Values) {
            values.Add(NumericSupport.AsValue(value, converter));
        }

        // ：请求 0 份时仍返回 1 份。空列表在种子生成路径上会直接导致"没有种群"，比多给一份更难排查。
        // Asking for 0 still yields 1. An empty list on the seeding path means "no population at all", which
        // is harder to diagnose than one extra entry.
        ulong count = global::System.Math.Max(amount, 1);
        var result = new List<Solution<V>>((int)global::System.Math.Min(count, int.MaxValue));
        for (ulong index = 0; index < count; index++) {
            result.Add(new Solution<V>(new List<V>(values)));
        }
        return result;
    }
}
