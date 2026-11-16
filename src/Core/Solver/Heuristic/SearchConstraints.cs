// 统一约束处理 / Unified constraint handling.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 原生搜索约束模式 / The native search constraint mode.
/// </summary>
public enum ConstraintMode {
    /// <summary>可行性优先 / Feasibility first.</summary>
    FeasibilityFirst,

    /// <summary>ε 约束 / Epsilon constraint.</summary>
    Epsilon,

    /// <summary>随机排序 / Stochastic ranking.</summary>
    StochasticRanking,
}

/// <summary>
/// 约束处理配置 / The constraint-handling configuration.
/// </summary>
/// <remarks>
/// **`Horizon` 与 `Exponent` 决定 ε 的收紧曲线**。`Horizon` 是 ε 归零所需的完整步数，`Exponent` 控制收紧快慢。这两个值一旦配成
/// 非法（零、负数、非有限），ε 会在整个搜索期间保持恒定或变成 `NaN`：前者让 ε 约束退化成"永远宽松"，后者让所有比较都失去意义。
/// **`Horizon` and `Exponent` define the epsilon tightening curve.** `Horizon` is how many complete steps until epsilon reaches
/// zero, and `Exponent` controls how fast it tightens. Configured illegally (zero, negative, non-finite), epsilon stays constant for
/// the whole search or becomes `NaN`: the first silently degrades the epsilon mode into "always relaxed", the second voids every
/// comparison.
/// **`InitialEpsilon` 为 null 表示从初始化候选校准**。写死一个与问题量级不匹配的初值，会让前期几乎所有候选都被判为不可行，搜索在
/// 最需要探索的阶段反而被锁死。
/// **A null `InitialEpsilon` means calibrating from the initialization candidates.** Hard-coding an initial value that does not match
/// the problem's scale makes nearly every early candidate infeasible, locking the search down exactly when exploration matters most.
/// </remarks>
/// <param name="Mode">处理模式 / The handling mode.</param>
/// <param name="InitialEpsilon">初始容差；null 从初始化候选校准 / The initial tolerance; null calibrates from initialization.</param>
/// <param name="Horizon">ε 归零的完整步数 / The complete steps until epsilon reaches zero.</param>
/// <param name="Exponent">收紧曲线指数 / The tightening exponent.</param>
/// <param name="ObjectiveProbability">随机排序按原目标比较的概率 / The probability of objective comparison in stochastic ranking.</param>
public sealed record ConstraintHandlingConfig(
    ConstraintMode Mode = ConstraintMode.FeasibilityFirst,
    double? InitialEpsilon = null,
    long Horizon = 1000,
    double Exponent = 2.0,
    double ObjectiveProbability = 0.45) {
    /// <summary>
    /// 校验配置 / Validate the configuration.
    /// </summary>
    /// <returns>校验结果 / The validation result.</returns>
    public Try Validate() {
        bool epsilonInvalid = InitialEpsilon is double epsilon && (!double.IsFinite(epsilon) || epsilon < 0);
        if (epsilonInvalid
            || Horizon <= 0
            || !double.IsFinite(Exponent)
            || Exponent <= 0
            || !double.IsFinite(ObjectiveProbability)
            || ObjectiveProbability is < 0.0 or > 1.0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "搜索配置或状态无效 / Invalid search configuration or state:"
                + " 约束处理配置无效 / Invalid constraint handling configuration"));
        }
        return Results.Ok(Results.SuccessInstance);
    }
}

/// <summary>
/// 可序列化的约束处理状态 / The serializable constraint-handling state.
/// </summary>
/// <param name="Version">协议版本 / The protocol version.</param>
/// <param name="Config">配置 / The configuration.</param>
/// <param name="Steps">已走过步数 / The completed steps.</param>
/// <param name="Scale">校准后的初始尺度 / The calibrated initial scale.</param>
/// <param name="Random">排序随机流状态 / The ranking stream state.</param>
public sealed record ConstraintSearchState(
    ConstraintHandlingConfig Config,
    long Steps,
    double? Scale,
    long Random,
    int Version = 1);

/// <summary>
/// 约束读数 / A constraint reading.
/// </summary>
/// <param name="Violation">归一化违约度 / The normalized violation.</param>
/// <param name="Feasible">是否可行 / Whether feasible.</param>
internal sealed record ConstraintReading(double Violation, bool Feasible);

/// <summary>
/// 已装配约束处理的问题 / A problem with constraint handling attached.
/// </summary>
/// <remarks>
/// **C# 用"约束搜索对象"代替模型装饰器**。处理器挂到 `model` 上，是因为 `compareIndividuals(model, …)` 只拿到模型引用；
/// C# 直接把处理器与问题放在一起，省掉一整层接口委托装饰器。语义相同（同一个处理器、同一份缓存），但**不再能通过模型引用找回
/// 处理器**——这是有意的形状差异。
/// **C# replaces its model decorator with a constrained-search object.** The code hangs the handler on `model` because
/// `compareIndividuals(model, …)` only receives the model reference; C# keeps the handler next to the problem and drops an entire
/// layer of delegating decorator. The semantics match (same handler, same cache), but the handler is **no longer recoverable from the
/// model reference** — a deliberate shape difference.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
/// <param name="Problem">原始问题 / The original problem.</param>
/// <param name="Handling">约束处理器 / The constraint handler.</param>
public sealed record ConstrainedSearch<O, V>(
    SearchProblem<O, V> Problem,
    SearchConstraintHandling<O, V> Handling) where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 统一约束处理，不覆盖原始业务目标 / Unified constraint handling that never overwrites the business objective.
/// </summary>
/// <remarks>
/// **分层比较先按可行性归档，再在同类内比较目标**。把违约度直接加进目标会改变业务目标的含义，调用方拿到的"最优解"是一个被惩罚项扭
/// 曲过的解，而这个扭曲程度无法从结果里还原。
/// **Tiered comparison ranks by feasibility first, then compares objectives within a tier.** Folding the violation straight into the
/// objective changes what the business objective means; the "optimum" a caller receives is a solution distorted by a penalty term,
/// and that distortion cannot be recovered from the result.
/// **违约度必须有限且非负**。非有限值会让所有比较都返回"相等"，档案于是退化成"按插入顺序"；负违约度则会让最不可行的解看起来最优秀。
/// **The violation must be finite and nonnegative.** A non-finite value makes every comparison report "equal", degrading the archive
/// to insertion order; a negative violation makes the least feasible solution look best.
/// **违约度回调失败后，该步内不再重试**。重试会掩盖"回调本身坏了"这个事实，把一张持续失败的回调伪装成"这步没有信息"。
/// **After the violation callback fails, no retry happens within the step.** Retrying would mask the fact that the callback itself is
/// broken, disguising a persistently failing callback as "no information this step".
/// **随机排序绝不能作为 `Comparator` 暴露**。比较函数必须满足传递性；随机排序按定义不满足，一旦被排序库当作比较器使用，会在某些
/// 输入上抛"比较方法违反通用契约"。因此 `Rank` 是独立入口，不是比较器。
/// **Stochastic ranking is never exposed as a `Comparator`.** A comparison function must be transitive; stochastic ranking is not,
/// by construction. Once a sorting library uses it as a comparator it throws "comparison method violates its general contract" on
/// some inputs. `Rank` is therefore a separate entry point, not a comparator.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
public sealed class SearchConstraintHandling<O, V> where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly SearchProblem<O, V> _problem;
    private readonly SearchRandom _random;
    private readonly Dictionary<SolutionKey<V>, ConstraintReading> _readings = new();
    private long _steps;
    private double? _scale;
    private double _pendingMaximum;

    /// <summary>
    /// 构造约束处理器 / Construct the constraint handler.
    /// </summary>
    /// <param name="config">处理配置 / The handling configuration.</param>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <param name="random">排序随机流 / The ranking stream.</param>
    public SearchConstraintHandling(
        ConstraintHandlingConfig config,
        SearchProblem<O, V> problem,
        SearchRandom random) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(random);
        Config = config;
        _problem = problem;
        _random = random;
        _scale = config.InitialEpsilon;
    }

    /// <summary>处理配置 / The handling configuration.</summary>
    public ConstraintHandlingConfig Config { get; }

    /// <summary>失败信息 / The failure message.</summary>
    public string? Failure { get; private set; }

    /// <summary>当前 ε / The current epsilon.</summary>
    public double Epsilon {
        get {
            double progress = 1.0 - ((double)_steps / Config.Horizon);
            return (_scale ?? 0.0) * global::System.Math.Pow(
                global::System.Math.Clamp(progress, 0.0, 1.0), Config.Exponent);
        }
    }

    /// <summary>
    /// 保存状态 / Capture the state.
    /// </summary>
    /// <returns>约束处理状态 / The constraint-handling state.</returns>
    public ConstraintSearchState Save() => new(Config, _steps, _scale, _random.State);

    /// <summary>
    /// 恢复状态 / Restore the state.
    /// </summary>
    /// <param name="state">约束处理状态 / The constraint-handling state.</param>
    /// <returns>恢复结果 / The restore result.</returns>
    public Try Load(ConstraintSearchState state) {
        ArgumentNullException.ThrowIfNull(state);
        bool scaleInvalid = state.Scale is double scale && (!double.IsFinite(scale) || scale < 0);
        if (state.Version != 1
            || state.Config != Config
            || state.Steps < 0
            || scaleInvalid
            || (Config.InitialEpsilon is double initial && state.Scale != initial)
            || (state.Steps > 0 && state.Scale is null)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "搜索配置或状态无效 / Invalid search configuration or state:"
                + " 约束处理状态不兼容 / Incompatible constraint handling state"));
        }
        _steps = state.Steps;
        _scale = state.Scale;
        // 随机流状态必须一起恢复：只恢复 ε 而不恢复排序随机流，会让恢复后的随机排序与原始运行分叉，且分叉点无法定位。
        // The stream state must be restored too: restoring only epsilon would fork the stochastic ranking from the original run at
        // a point that cannot be located afterwards.
        RestoreRandom(state.Random);
        Begin();
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 开始新的一步 / Begin a new step.
    /// </summary>
    public void Begin() {
        _readings.Clear();
        _pendingMaximum = 0.0;
        Failure = null;
    }

    /// <summary>
    /// 提交一步 / Commit one step.
    /// </summary>
    /// <remarks>
    /// **尺度只在第一次提交时校准**。之后每步都重新校准，会让 ε 随当前种群的最大违约度上下跳动，搜索永远收敛不到可行域边界。
    /// **The scale is calibrated only on the first commit.** Recalibrating every step would make epsilon jump with the current
    /// population's maximum violation, and the search never converges to the feasibility boundary.
    /// </remarks>
    public void Commit() {
        _scale ??= _pendingMaximum;
        _steps++;
        _readings.Clear();
    }

    /// <summary>
    /// 观察一个解 / Observe one solution.
    /// </summary>
    /// <param name="solution">解向量 / The solution vector.</param>
    public void Observe(IReadOnlyList<V> solution) => Reading(solution);

    /// <summary>
    /// 读取一个解的约束读数 / Read the constraint reading of one solution.
    /// </summary>
    /// <param name="solution">解向量 / The solution vector.</param>
    /// <returns>约束读数，不可用时为 null / The constraint reading, or null when unavailable.</returns>
    private ConstraintReading? Reading(IReadOnlyList<V> solution) {
        ArgumentNullException.ThrowIfNull(solution);
        var key = new SolutionKey<V>(solution);
        if (_readings.TryGetValue(key, out ConstraintReading? cached)) {
            return cached;
        }
        if (Failure is not null) {
            return null;
        }
        if (_problem.ConstraintViolation is null) {
            Failure = "违约度回调缺失 / Missing violation callback";
            return null;
        }

        // 回调在外部边界之外：异常必须变成"这一步回调失败"，而不是穿透整个搜索栈。
        // The callback lives outside the boundary: an exception must become "the callback failed this step", not tear through the
        // whole search stack.
        Result<Flt64, ErrorCode, Error<ErrorCode>> violationResult;
        try {
            violationResult = _problem.ConstraintViolation(solution);
        } catch (Exception exception) {
            violationResult = Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.ApplicationError, exception.Message));
        }
        if (!violationResult.IsOk) {
            Failure = "违约度回调失败 / Violation callback failed: "
                + ((Failed<Flt64, ErrorCode, Error<ErrorCode>>)violationResult).Error.Message;
            return null;
        }
        double violation = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)violationResult).Value.ToDouble();

        bool feasible;
        try {
            feasible = _problem.Feasible(solution);
        } catch (Exception exception) {
            Failure = $"可行性回调失败 / Feasibility callback failed: {exception.Message}";
            return null;
        }

        if (!double.IsFinite(violation) || violation < 0) {
            Failure = $"违约度必须有限且非负 / Violation must be finite and nonnegative: {violation}";
            return null;
        }

        _pendingMaximum = global::System.Math.Max(_pendingMaximum, violation);
        var reading = new ConstraintReading(violation, feasible);
        _readings[key] = reading;
        return reading;
    }

    /// <summary>
    /// 比较两个目标的优先次序 / Compare the priority of two objectives.
    /// </summary>
    /// <param name="lhs">左个体 / The left individual.</param>
    /// <param name="rhs">右个体 / The right individual.</param>
    /// <returns>负值表示左方优先 / Negative means the left individual is preferred.</returns>
    private int Objective(IIndividual<O, V> lhs, IIndividual<O, V> rhs) {
        Order? order = _problem.Model.CompareObjective(lhs.Objective, rhs.Objective);
        if (order is not null) {
            // 最小化语义下 `Order.Less` 表示左方更优，因此取其 `Value` 即可与 `CompareTo` 的方向一致。
            // Under minimization, `Order.Less` means the left side is better, so its `Value` matches the `CompareTo` direction.
            return order.Value;
        }
        if (_problem.Dominates(lhs.Objective, rhs.Objective)) {
            return -1;
        }
        return _problem.Dominates(rhs.Objective, lhs.Objective) ? 1 : 0;
    }

    /// <summary>
    /// 计算一个读数所属的层级 / Compute the tier of one reading.
    /// </summary>
    /// <param name="reading">约束读数 / The constraint reading.</param>
    /// <param name="relaxed">是否启用 ε 放宽 / Whether epsilon relaxation applies.</param>
    /// <returns>层号与层内度量 / The tier index and the within-tier measure.</returns>
    private (int Tier, double Measure) TierOf(ConstraintReading reading, bool relaxed) {
        double epsilon = Epsilon;
        return reading.Feasible || (relaxed && epsilon > 0 && reading.Violation <= epsilon)
            ? (0, 0.0)
            : (1, reading.Violation);
    }

    /// <summary>
    /// 只比较可行性层级 / Compare the feasibility tier only.
    /// </summary>
    /// <param name="lhs">左个体 / The left individual.</param>
    /// <param name="rhs">右个体 / The right individual.</param>
    /// <returns>负值表示左方优先 / Negative means the left individual is preferred.</returns>
    public int CompareTier(IIndividual<O, V> lhs, IIndividual<O, V> rhs) {
        ArgumentNullException.ThrowIfNull(lhs);
        ArgumentNullException.ThrowIfNull(rhs);
        ConstraintReading? left = Reading(lhs.Solution);
        if (left is null) {
            return 0;
        }
        ConstraintReading? right = Reading(rhs.Solution);
        if (right is null) {
            return 0;
        }

        bool relaxed = Config.Mode == ConstraintMode.Epsilon;
        (int Tier, double Measure) a = TierOf(left, relaxed);
        (int Tier, double Measure) b = TierOf(right, relaxed);
        return a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : a.Measure.CompareTo(b.Measure);
    }

    /// <summary>
    /// 比较两个个体的优先次序 / Compare the priority of two individuals.
    /// </summary>
    /// <param name="lhs">左个体 / The left individual.</param>
    /// <param name="rhs">右个体 / The right individual.</param>
    /// <returns>负值表示左方优先 / Negative means the left individual is preferred.</returns>
    public int Compare(IIndividual<O, V> lhs, IIndividual<O, V> rhs) {
        int compared = CompareTier(lhs, rhs);
        return compared == 0 ? Objective(lhs, rhs) : compared;
    }

    /// <summary>
    /// 按层级分组 / Group by tier.
    /// </summary>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="entries">完整个体 / The complete individuals.</param>
    /// <returns>按层级从优到劣排列的分组 / The groups ordered from best tier to worst.</returns>
    public IReadOnlyList<IReadOnlyList<T>> Groups<T>(IReadOnlyList<T> entries) where T : IIndividual<O, V> {
        ArgumentNullException.ThrowIfNull(entries);
        bool relaxed = Config.Mode == ConstraintMode.Epsilon;
        var buckets = new Dictionary<(int Tier, double Measure), List<T>>();
        foreach (T entry in entries) {
            ConstraintReading? reading = Reading(entry.Solution);
            (int Tier, double Measure) key = reading is null
                ? (1, double.PositiveInfinity)
                : TierOf(reading, relaxed);
            if (!buckets.TryGetValue(key, out List<T>? bucket)) {
                bucket = new List<T>();
                buckets[key] = bucket;
            }
            bucket.Add(entry);
        }

        return buckets.Keys
            .OrderBy(key => key.Tier)
            .ThenBy(key => key.Measure)
            .Select(key => (IReadOnlyList<T>)buckets[key])
            .ToList();
    }

    /// <summary>
    /// 批量排序，随机排序不走比较器 / Order a batch; stochastic ranking never goes through a comparator.
    /// </summary>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="entries">完整个体 / The complete individuals.</param>
    /// <returns>排序结果 / The ranked individuals.</returns>
    public IReadOnlyList<T> Rank<T>(IReadOnlyList<T> entries) where T : IIndividual<O, V> {
        ArgumentNullException.ThrowIfNull(entries);
        if (Config.Mode != ConstraintMode.StochasticRanking) {
            return entries.OrderBy(entry => entry, Comparer<T>.Create((lhs, rhs) => Compare(lhs, rhs))).ToList();
        }

        var ranked = entries.ToList();
        for (int pass = 0; pass < ranked.Count; pass++) {
            bool swapped = false;
            for (int index = 0; index + 1 < ranked.Count; index++) {
                T a = ranked[index];
                T b = ranked[index + 1];
                ConstraintReading? left = Reading(a.Solution);
                if (left is null) {
                    return entries;
                }
                ConstraintReading? right = Reading(b.Solution);
                if (right is null) {
                    return entries;
                }

                bool byObjective = (left.Feasible && right.Feasible)
                    || _random.NextDouble() < Config.ObjectiveProbability;
                int compared = byObjective ? Objective(a, b) : Compare(a, b);
                if (compared > 0) {
                    ranked[index] = b;
                    ranked[index + 1] = a;
                    swapped = true;
                }
            }
            if (!swapped) {
                return ranked;
            }
        }
        return ranked;
    }

    /// <summary>
    /// 恢复排序随机流状态 / Restore the ranking stream state.
    /// </summary>
    /// <param name="state">随机流状态 / The stream state.</param>
    private void RestoreRandom(long state) => _random.Restore(state);
}

/// <summary>
/// 解向量的结构键 / A structural key for a solution vector.
/// </summary>
/// <remarks>
/// **不能用引用相等当缓存键**。同一个解在评估与比较两条路径上通常是不同的列表实例；按引用做键会让缓存永远不命中，违约度回调被反复
/// 调用，而调用方以为它在缓存。
/// **Reference equality is unusable as a cache key.** The same solution usually arrives as different list instances on the evaluation
/// and comparison paths; keying by reference means the cache never hits, the violation callback is re-invoked constantly, and callers
/// believe it is cached.
/// </remarks>
/// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
internal readonly struct SolutionKey<V> : IEquatable<SolutionKey<V>> where V : struct {
    private readonly IReadOnlyList<V> _values;
    private readonly int _hash;

    public SolutionKey(IReadOnlyList<V> values) {
        _values = values;
        var hash = new HashCode();
        foreach (V value in values) {
            hash.Add(value);
        }
        _hash = hash.ToHashCode();
    }

    /// <inheritdoc/>
    public bool Equals(SolutionKey<V> other) {
        if (_values is null || other._values is null) {
            return ReferenceEquals(_values, other._values);
        }
        if (_values.Count != other._values.Count) {
            return false;
        }
        for (int index = 0; index < _values.Count; index++) {
            if (!EqualityComparer<V>.Default.Equals(_values[index], other._values[index])) {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SolutionKey<V> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _hash;
}

/// <summary>
/// 约束相关的外部入口 / External entry points for constraint handling.
/// </summary>
public static class SearchConstraints {
    /// <summary>
    /// 为问题装配原生约束处理 / Attach native constraint handling to a problem.
    /// </summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <param name="config">约束处理配置 / The constraint-handling configuration.</param>
    /// <param name="random">独立且可恢复的排序随机流 / The independent restorable ranking stream.</param>
    /// <returns>已装配约束处理的问题 / The problem with constraint handling attached.</returns>
    public static Result<ConstrainedSearch<O, V>, ErrorCode, Error<ErrorCode>> WithConstraints<O, V>(
        this SearchProblem<O, V> problem,
        ConstraintHandlingConfig config,
        SearchRandom random) where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(random);

        Try valid = config.Validate();
        if (!valid.IsOk) {
            return Results.Failed<ConstrainedSearch<O, V>>(
                ((Failed<Success, ErrorCode, Error<ErrorCode>>)valid).Error);
        }
        if (problem.ConstraintViolation is null) {
            return Results.Failed<ConstrainedSearch<O, V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "搜索配置或状态无效 / Invalid search configuration or state:"
                + " 约束回调缺失 / Missing constraint callback"));
        }

        return Results.Ok(new ConstrainedSearch<O, V>(
            problem, new SearchConstraintHandling<O, V>(config, problem, random)));
    }

    /// <summary>
    /// 比较完整个体 / Compare complete individuals.
    /// </summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
    /// <param name="search">已装配约束处理的搜索 / The constraint-aware search.</param>
    /// <param name="lhs">左个体 / The left individual.</param>
    /// <param name="rhs">右个体 / The right individual.</param>
    /// <returns>负值表示左方优先 / Negative means the left individual is preferred.</returns>
    public static int CompareIndividuals<O, V>(
        ConstrainedSearch<O, V> search,
        IIndividual<O, V> lhs,
        IIndividual<O, V> rhs) where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(search);
        return search.Handling.Compare(lhs, rhs);
    }

    /// <summary>
    /// 批量排序 / Order a batch.
    /// </summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="search">已装配约束处理的搜索 / The constraint-aware search.</param>
    /// <param name="entries">完整个体 / The complete individuals.</param>
    /// <returns>排序结果 / The ranked individuals.</returns>
    public static IReadOnlyList<T> RankIndividuals<O, V, T>(
        ConstrainedSearch<O, V> search,
        IReadOnlyList<T> entries) where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<O, V> {
        ArgumentNullException.ThrowIfNull(search);
        return search.Handling.Rank(entries);
    }

    /// <summary>
    /// 按层级选取幸存者 / Select survivors tier by tier.
    /// </summary>
    /// <typeparam name="O">目标类型 / The objective type.</typeparam>
    /// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
    /// <typeparam name="T">个体类型 / The individual type.</typeparam>
    /// <param name="search">已装配约束处理的搜索 / The constraint-aware search.</param>
    /// <param name="entries">完整个体 / The complete individuals.</param>
    /// <param name="amount">需要的数量 / The requested amount.</param>
    /// <param name="select">层内选择器 / The within-tier selector.</param>
    /// <returns>幸存者 / The survivors.</returns>
    public static IReadOnlyList<T> SelectSurvivors<O, V, T>(
        ConstrainedSearch<O, V> search,
        IReadOnlyList<T> entries,
        int amount,
        Func<IReadOnlyList<T>, int, IReadOnlyList<T>> select) where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<O, V> {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(select);

        if (search.Handling.Config.Mode == ConstraintMode.StochasticRanking) {
            return search.Handling.Rank(entries).Take(amount).ToList();
        }

        var kept = new List<T>();
        foreach (IReadOnlyList<T> group in search.Handling.Groups(entries)) {
            int remaining = global::System.Math.Max(amount - kept.Count, 0);
            kept.AddRange(select(group, remaining));
            if (kept.Count >= amount) {
                break;
            }
        }
        return kept;
    }
}
