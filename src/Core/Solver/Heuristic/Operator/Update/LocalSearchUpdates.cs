// 局部搜索更新算子（此前）/ Local-search update operators (first set).
// The first three implementations of the: .

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 把解包装成已评估个体的辅助 / Helper turning a solution into an evaluated individual.
/// </summary>
/// <remarks>
/// **评估失败必须返回 `null`，不能返回目标为默认值的个体**。返回一个"目标被填成默认值"的个体，会让它在最小化问题里看起来最优，
/// 随后被局部搜索当作改进保留下来——整条搜索链从此建立在一个从未真正评估过的点上。
/// **A failed evaluation must return `null`, never an individual whose objective is the default.** Such an individual looks optimal in a
/// minimization problem and gets retained by the local search as an improvement, so the whole chain is built on a point that was never
/// actually evaluated.
/// </remarks>
public static class FitnessEvaluatorSupport {
    /// <summary>
    /// 评估解并包装成个体 / Evaluate a solution and wrap it as an individual.
    /// </summary>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="solution">待评估解 / The solution to evaluate.</param>
    /// <returns>已评估个体，无法评估时返回 null / The evaluated individual, or null when it cannot be evaluated.</returns>
    public static IIndividual<TObjValue, V>? ToIndividual<TObj, TObjValue, V>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Solution<V> solution)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(solution);
        TObjValue? objective = model.Objective(solution.Values);
        return objective is null ? null : new EvaluatedIndividual<TObjValue, V>(solution.Values, objective);
    }
}

/// <summary>
/// 变异包装更新 / Mutation-wrapping update.
/// </summary>
/// <remarks>
/// **复用已注入熵源的变异算子，而不是在这里另起一个随机源**。另起一个会破坏"同一份种子完全可重放"的契约，
/// 而且两处熵源的抽取顺序一旦变化，重放结果就再也对不上。
/// **Reuse the injected-entropy mutation operator instead of starting another RNG here.** A second source breaks the
/// "same seed replays exactly" contract, and once the draw order of the two sources shifts, replay never matches again.
/// </remarks>
/// <param name="Mutation">变异算子 / The mutation operator.</param>
/// <param name="Rate">变异率 / The mutation rate.</param>
public sealed record MutationUpdate(IMutationOperator Mutation, Flt64 Rate) : ISolutionUpdate {
    /// <summary>以默认变异率构造 / Construct with the default mutation rate.</summary>
    /// <param name="mutation">变异算子 / The mutation operator.</param>
    public MutationUpdate(IMutationOperator mutation) : this(mutation, Flt64.One) { }

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
        ArgumentNullException.ThrowIfNull(Mutation);
        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            children.Add(Mutation.Invoke<V, TObj, TObjValue, T>(
                iteration, parent, model, converter, entropy, Rate));
        }
        return children;
    }
}

/// <summary>
/// 邻域包装更新 / Neighbourhood-wrapping update.
/// </summary>
/// <param name="Neighborhood">邻域算子 / The neighbourhood operator.</param>
public sealed record NeighborhoodUpdate(INeighborhood Neighborhood) : ISolutionUpdate {
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
        ArgumentNullException.ThrowIfNull(Neighborhood);
        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            children.Add(Neighborhood.Generate<TObj, TObjValue, V>(
                iteration, parent, model, entropy, converter));
        }
        return children;
    }
}

/// <summary>
/// 反向学习更新 / Opposition-based-learning update.
/// </summary>
/// <remarks>
/// **无有限边界的变量必须原样保留，不能按某个默认区间取反**。对无界变量做"反向"，等于凭空发明一个区间，
/// 得到的值既不是原解的反向，也不在可行域里——而它看起来只是一个普通的解。
/// **Variables without finite bounds must be kept unchanged, not reflected about some default interval.** Reflecting an unbounded
/// variable invents an interval out of thin air; the result is neither the opposite of the original nor feasible — yet it looks like
/// an ordinary solution.
/// **反向值仍需夹紧**。`lower + (upper - value)` 在 `value` 恰好等于边界时是合法的，但浮点误差可能让它略微越界。
/// **The reflected value is still clamped.** `lower + (upper - value)` is legal when `value` sits exactly on a bound, but floating-point
/// error can push it slightly outside.
/// </remarks>
public sealed class OppositionUpdate : ISolutionUpdate {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static OppositionUpdate Instance { get; } = new();

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
        var children = new List<Solution<V>>(parents.Count);
        foreach (T parent in parents) {
            IReadOnlyList<V> current = parent.Solution;
            var reflected = new List<V>(current.Count);
            for (int index = 0; index < current.Count; index++) {
                V value = current[index];
                double? lower = NumericSupport.LowerBoundOf(model, index, converter);
                double? upper = NumericSupport.UpperBoundOf(model, index, converter);
                if (lower is not double low || upper is not double high
                    || !double.IsFinite(low) || !double.IsFinite(high)) {
                    reflected.Add(value);
                    continue;
                }
                double mirrored = low + (high - converter.FromValue(value).ToDouble());
                reflected.Add(NumericSupport.AsValue(NumericSupport.ClampTo(mirrored, low, high), converter));
            }
            children.Add(new Solution<V>(reflected));
        }
        return children;
    }
}
