// 边界处理、终止判据与接受准则 / Boundary handling, stopping criteria and acceptance rules.
// 与固定 Kotlin 、
// 对应。
// , and.

using System;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 处理越界值 / Handle an out-of-range value.
/// </summary>
/// <remarks>
/// **越界处理不是可选装饰**。连续算子的步长按变量宽度缩放，一次大跨度扰动就能把基因推到区间外；`NaN` / `±∞` 也会在这里被拦下。
/// 不处理的话，越界值会直接进入目标函数，随后以"目标溢出为 ∞、排序比较不满足全序"的形式在**很远的地方**炸开。
/// **Boundary handling is not optional decoration.** Continuous operators scale their step by the variable width, so one long jump
/// pushes a gene outside the interval; `NaN` / `±∞` are caught here too. Without it, out-of-range values reach the objective and blow
/// up **far away**, as "objective overflows to ∞ and the sort no longer forms a total order".
/// **三种策略的取舍**：截断会堆积在边界上（损失多样性），反射保留方向信息，环绕维持分布均匀。没有一种全对，所以三者都提供。
/// **The three strategies trade off differently**: clamping piles up on the bounds (losing diversity), reflection keeps directional
/// information, wrapping keeps the distribution uniform. None is universally right, so all three are provided.
/// </remarks>
public interface IBoundaryHandler {
    /// <summary>
    /// 处理越界值 / Handle an out-of-range value.
    /// </summary>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="index">变量下标 / The variable index.</param>
    /// <param name="value">待处理值 / The value to handle.</param>
    /// <param name="lowerBound">下界 / The lower bound.</param>
    /// <param name="upperBound">上界 / The upper bound.</param>
    /// <returns>处理后的值 / The handled value.</returns>
    Flt64 Invoke(Iteration iteration, int index, Flt64 value, Flt64 lowerBound, Flt64 upperBound);
}

/// <summary>
/// 截断边界处理，直接把越界值压回边界 / Clamping boundary handling that presses out-of-range values back onto the bounds.
/// </summary>
public sealed class ClampBoundary : IBoundaryHandler {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static ClampBoundary Instance { get; } = new();

    /// <inheritdoc/>
    public Flt64 Invoke(Iteration iteration, int index, Flt64 value, Flt64 lowerBound, Flt64 upperBound) =>
        new(global::System.Math.Clamp(value.ToDouble(), lowerBound.ToDouble(), upperBound.ToDouble()));
}

/// <summary>
/// 反射边界处理，越界后按边界镜像折返 / Reflecting boundary handling that mirrors out-of-range values back across the bounds.
/// </summary>
/// <remarks>
/// **折返次数上限是 64 而不是"直到收敛"**。宽度极窄而值极大时，朴素的折返循环需要天文数字次迭代才能落回区间；设上限后
/// 最坏情况退化为截断，而不是让一次变异卡死整个搜索——**这类卡死只在极端取值上出现，平时测不到**。
/// **The reflection count is capped at 64 rather than "until converged".** With a very narrow width and a very large value, a naive
/// reflection loop needs an astronomical number of iterations. The cap degrades the worst case into clamping instead of letting one
/// mutation hang the whole search — **a hang that only appears at extreme values and is never seen in normal runs**.
/// </remarks>
public sealed class ReflectBoundary : IBoundaryHandler {
    private const int MaxPeriods = 64;

    /// <summary>共享单例 / The shared singleton.</summary>
    public static ReflectBoundary Instance { get; } = new();

    /// <inheritdoc/>
    public Flt64 Invoke(Iteration iteration, int index, Flt64 value, Flt64 lowerBound, Flt64 upperBound) {
        double lower = lowerBound.ToDouble();
        double upper = upperBound.ToDouble();
        double width = upper - lower;
        if (width <= 0.0) {
            return lowerBound;
        }

        double position = value.ToDouble() - lower;
        int periods = 0;
        while ((position < 0.0 || position > width) && periods < MaxPeriods) {
            position = position < 0.0 ? -position : 2.0 * width - position;
            periods++;
        }
        return new Flt64(global::System.Math.Clamp(lower + position, lower, upper));
    }
}

/// <summary>
/// 环绕边界处理，越界后按区间长度取模环绕 / Wrapping boundary handling that wraps out-of-range values modulo the interval width.
/// </summary>
public sealed class WrapBoundary : IBoundaryHandler {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static WrapBoundary Instance { get; } = new();

    /// <inheritdoc/>
    public Flt64 Invoke(Iteration iteration, int index, Flt64 value, Flt64 lowerBound, Flt64 upperBound) {
        double lower = lowerBound.ToDouble();
        double upper = upperBound.ToDouble();
        double width = upper - lower;
        if (width <= 0.0) {
            return lowerBound;
        }

        // C# 的 `%` 对负数返回负余数，必须显式补一个宽度；直接用 `%` 会让左越界值落在区间外。
        // C#'s `%` yields a negative remainder for negative operands, so one width must be added explicitly; using `%` alone leaves
        // left-out-of-range values outside the interval.
        double position = (value.ToDouble() - lower) % width;
        if (position < 0.0) {
            position += width;
        }
        return new Flt64(lower + position);
    }
}

/// <summary>
/// 判断是否应当终止 / Decide whether the search should stop.
/// </summary>
public interface IStoppingCriterion {
    /// <summary>
    /// 判断是否应当终止 / Decide whether the search should stop.
    /// </summary>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <returns>是否终止 / Whether to stop.</returns>
    bool ShouldStop(Iteration iteration);
}

/// <summary>
/// 从不终止 / Never stop.
/// </summary>
public sealed class NeverStop : IStoppingCriterion {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static NeverStop Instance { get; } = new();

    /// <inheritdoc/>
    public bool ShouldStop(Iteration iteration) => false;
}

/// <summary>
/// 迭代次数上限 / An iteration-count limit.
/// </summary>
/// <param name="Limit">迭代次数上限 / The iteration-count limit.</param>
public sealed record IterationLimit(ulong Limit) : IStoppingCriterion {
    /// <inheritdoc/>
    public bool ShouldStop(Iteration iteration) {
        ArgumentNullException.ThrowIfNull(iteration);
        return (ulong)iteration.Current >= Limit;
    }
}

/// <summary>
/// 连续无改进迭代上限 / A consecutive no-improvement limit.
/// </summary>
/// <param name="Limit">无改进迭代上限 / The no-improvement limit.</param>
public sealed record NotBetterLimit(ulong Limit) : IStoppingCriterion {
    /// <inheritdoc/>
    public bool ShouldStop(Iteration iteration) {
        ArgumentNullException.ThrowIfNull(iteration);
        return (ulong)iteration.NotBetterIteration >= Limit;
    }
}

/// <summary>
/// 时间上限 / A time limit.
/// </summary>
/// <remarks>
/// **依赖 `Iteration.Time` 是实时读数**。若 `Time` 只在推进迭代时更新，那么长时间不推进迭代的空档里，超时判据读到的是过期值——
/// 而这恰恰是它最该准确的时刻。这正是 earlier 给 `Iteration` 补上实时读数与暂停/恢复的原因。
/// **Relies on `Iteration.Time` being a live reading.** If `Time` only moved when the iteration advanced, a timeout check during a
/// long gap between iterations would read a stale value — exactly when it most needs to be accurate. That is why earlier gave
/// `Iteration` a live reading plus pause/resume.
/// </remarks>
/// <param name="Limit">时间上限 / The time limit.</param>
public sealed record TimeLimit(TimeSpan Limit) : IStoppingCriterion {
    /// <inheritdoc/>
    public bool ShouldStop(Iteration iteration) {
        ArgumentNullException.ThrowIfNull(iteration);
        return iteration.Time >= Limit;
    }
}

/// <summary>
/// 终止判据组合 / Combinators for stopping criteria.
/// </summary>
public static class StoppingCriteria {
    /// <summary>
    /// 任一判据成立即终止 / Stop when either criterion holds.
    /// </summary>
    /// <param name="first">第一个判据 / The first criterion.</param>
    /// <param name="second">第二个判据 / The second criterion.</param>
    /// <returns>组合后的判据 / The combined criterion.</returns>
    public static IStoppingCriterion Or(this IStoppingCriterion first, IStoppingCriterion second) {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return new AnyOfCriterion(new[] { first, second });
    }

    /// <summary>
    /// 全部判据成立才终止 / Stop only when every criterion holds.
    /// </summary>
    /// <param name="first">第一个判据 / The first criterion.</param>
    /// <param name="second">第二个判据 / The second criterion.</param>
    /// <returns>组合后的判据 / The combined criterion.</returns>
    public static IStoppingCriterion And(this IStoppingCriterion first, IStoppingCriterion second) {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return new AllOfCriterion(new[] { first, second });
    }

    /// <summary>
    /// 任一成立的组合判据 / The "any of" criterion.
    /// </summary>
    /// <param name="Criteria">成员判据 / The member criteria.</param>
    private sealed record AnyOfCriterion(System.Collections.Generic.IReadOnlyList<IStoppingCriterion> Criteria)
        : IStoppingCriterion {
        /// <inheritdoc/>
        public bool ShouldStop(Iteration iteration) {
            foreach (IStoppingCriterion criterion in Criteria) {
                if (criterion.ShouldStop(iteration)) {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 全部成立的组合判据 / The "all of" criterion.
    /// </summary>
    /// <param name="Criteria">成员判据 / The member criteria.</param>
    private sealed record AllOfCriterion(System.Collections.Generic.IReadOnlyList<IStoppingCriterion> Criteria)
        : IStoppingCriterion {
        /// <inheritdoc/>
        public bool ShouldStop(Iteration iteration) {
            foreach (IStoppingCriterion criterion in Criteria) {
                if (!criterion.ShouldStop(iteration)) {
                    return false;
                }
            }
            return true;
        }
    }
}

/// <summary>
/// 判断是否接受候选解 / Decide whether to accept the candidate.
/// </summary>
/// <remarks>
/// **接受准则与"哪个解更好"是两件事**。更好是偏序关系，接受还要考虑"是否愿意为了跳出局部最优而暂时变差"。把两者合并，
/// 任何想加退火或随机接受的尝试都得改写比较逻辑本身。
/// **Acceptance and "which solution is better" are two different questions.** Better is a partial order; acceptance additionally asks
/// "are we willing to get temporarily worse to escape a local optimum". Merging them forces every attempt to add annealing or
/// stochastic acceptance to rewrite the comparison logic itself.
/// **约束感知的接受走 `SearchConstraints`**。本组算子只表达无约束语义；带约束的层内比较与幸存者选择已由
/// `SearchConstraints.SelectSurvivors` / `RankIndividuals` 提供，不在这里再开一个平行入口。
/// **Constraint-aware acceptance lives in `SearchConstraints`.** These operators express unconstrained semantics only; tiered
/// comparison and survivor selection already live in `SearchConstraints.SelectSurvivors` / `RankIndividuals`, and no parallel entry
/// point is opened here.
/// </remarks>
public interface IAcceptance {
    /// <summary>
    /// 判断是否接受候选解 / Decide whether to accept the candidate.
    /// </summary>
    /// <typeparam name="TObj">目标符号类型 / The objective-symbol type.</typeparam>
    /// <typeparam name="TObjValue">目标值类型 / The objective-value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="model">回调模型接口 / The call-back model interface.</param>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="current">当前个体 / The current individual.</param>
    /// <param name="candidate">候选个体 / The candidate individual.</param>
    /// <returns>是否接受 / Whether to accept.</returns>
    bool Invoke<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Iteration iteration,
        T current,
        T candidate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V>;
}

/// <summary>
/// Metropolis 接受判据（模拟退火）/ The Metropolis acceptance criterion (simulated annealing).
/// </summary>
/// <remarks>
/// **变差也按概率接受，是这个算法与爬山法的唯一区别**。把这个概率写成 0，**算法照样跑完、照样给出"最优解"**——
/// 只是它变成了爬山法，而名字还叫模拟退火。
/// **Accepting a worse candidate with some probability is the only thing separating this from hill climbing.** Setting that probability to zero
/// **still runs to completion and still reports a "best solution"** — it has merely become hill climbing under the annealing name.
///
/// **温度必须以迭代为自变量**：`GeometricSource` 按迭代指数衰减。把温度写成常数，**早期就不会有探索**（或后期不会有收敛），
/// 而每一步的结果看起来都仍然合理。
/// **The temperature must be a function of the iteration**: `GeometricSource` decays it geometrically. Pinning it to a constant **removes the
/// early exploration (or the late convergence)**, while every individual step still looks reasonable.
///
/// **门槛顺序是"可行性 → 严格更好 → 概率"**。把可行性的判断挪到最后，会让一个不可行的候选因为"目标值看起来更好"而被接受——
/// **于是搜索跑进不可行域，而目标值确实在下降**。
/// **The order is "feasibility, then strictly better, then probability".** Moving the feasibility check to the end lets an infeasible candidate be
/// accepted because "its objective looks better" — **so the search walks into the infeasible region while the objective really does improve**.
///
/// **温度为 0（或小到 1e-12 以下）时直接拒绝变差的候选**，否则会算出 `exp(-delta/0)` = 0 与 `NaN` 两类结果，
/// 而 `NaN` 与任何数比较都恒为假，**看起来"恰好也拒绝了"，实际上是把数值问题伪装成了语义**。
/// **At a temperature of zero (or below 1e-12) a worse candidate is rejected outright**; otherwise `exp(-delta/0)` yields both 0 and `NaN`,
/// and a NaN comparison is always false — **which looks like "it rejected anyway" while actually disguising a numeric problem as semantics**.
/// </remarks>
public sealed class MetropolisAcceptance : IAcceptance {
    /// <summary>温度下限 / The temperature floor.</summary>
    private const double MinimumTemperature = 1.0e-12;

    private readonly IParameterSource _temperature;
    private readonly Func<Flt64> _entropy;

    /// <summary>
    /// 构造 Metropolis 接受判据 / Construct the Metropolis acceptance criterion.
    /// </summary>
    /// <param name="temperature">温度计划 / The temperature plan.</param>
    /// <param name="entropy">均匀分布熵源 / The uniform entropy source.</param>
    public MetropolisAcceptance(IParameterSource temperature, Func<Flt64> entropy) {
        ArgumentNullException.ThrowIfNull(temperature);
        ArgumentNullException.ThrowIfNull(entropy);
        _temperature = temperature;
        _entropy = entropy;
    }

    /// <summary>温度计划 / The temperature plan.</summary>
    public IParameterSource Temperature => _temperature;

    /// <inheritdoc/>
    public bool Invoke<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Iteration iteration,
        T current,
        T candidate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidate);

        bool? candidateFeasible = model.ConstraintSatisfied(candidate.Solution);
        bool? currentFeasible = model.ConstraintSatisfied(current.Solution);
        if (candidateFeasible != currentFeasible) {
            return candidateFeasible == true;
        }
        if (HeuristicOrdering.CompareStrictly(model, candidate.Objective, current.Objective) < 0) {
            return true;
        }
        if (candidate is not SolutionWithFitness<TObjValue, V> candidateFitness
            || current is not SolutionWithFitness<TObjValue, V> currentFitness) {
            // 没有可用的标量适应度时，**不做概率接受**：拿一个编造出来的差值去算概率，比直接拒绝更危险。
            // Without a scalar fitness there is **no probabilistic acceptance**: computing a probability from an invented delta is worse than
            // rejecting outright.
            return false;
        }
        double delta = candidateFitness.Fitness - currentFitness.Fitness;
        if (delta <= 0.0) {
            return true;
        }
        double temperature = _temperature.Value(iteration).ToDouble();
        if (!(temperature > MinimumTemperature)) {
            return false;
        }
        double threshold = global::System.Math.Exp(-delta / temperature);
        return _entropy().ToDouble() < threshold;
    }
}

/// <summary>
/// 严格改进才接受，等价于爬山法 / Accept only on strict improvement, i.e. hill climbing.
/// </summary>
public sealed class BetterOnlyAcceptance : IAcceptance {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static BetterOnlyAcceptance Instance { get; } = new();

    /// <inheritdoc/>
    public bool Invoke<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Iteration iteration,
        T current,
        T candidate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidate);
        return HeuristicOrdering.CompareStrictly(model, candidate.Objective, current.Objective) < 0;
    }
}

/// <summary>
/// 不劣于当前解即接受，允许在平台上横向移动 / Accept anything not worse, allowing lateral moves across plateaus.
/// </summary>
public sealed class NotWorseAcceptance : IAcceptance {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static NotWorseAcceptance Instance { get; } = new();

    /// <inheritdoc/>
    public bool Invoke<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Iteration iteration,
        T current,
        T candidate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidate);
        return HeuristicOrdering.CompareStrictly(model, candidate.Objective, current.Objective) <= 0;
    }
}

/// <summary>
/// 无条件接受，用于纯随机游走 / Accept unconditionally, used for pure random walks.
/// </summary>
public sealed class AlwaysAcceptance : IAcceptance {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static AlwaysAcceptance Instance { get; } = new();

    /// <inheritdoc/>
    public bool Invoke<TObj, TObjValue, V, T>(
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        Iteration iteration,
        T current,
        T candidate)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> => true;
}
