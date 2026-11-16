#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using SessionRet = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Analysis.CriticalConstraintAnalysisSession, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>为后续分析器预留的分析阶段缓存桶 / Analysis-stage cache buckets reserved for later analyzers.</summary>
public enum AnalysisCacheKind
{
    /// <summary>活动性 / Activity.</summary>
    Activity,
    /// <summary>固定整数 LP / Fixed-integer LP.</summary>
    FixedIntegerLp,
    /// <summary>扰动 / Perturbation.</summary>
    Perturbation,
    /// <summary>目标条件 / Target.</summary>
    Target,
    /// <summary>冲突 / Conflict.</summary>
    Conflict,
}

/// <summary>
/// 围绕一个不可变 CP snapshot 协调分析阶段 / Coordinates analysis stages around one immutable CP snapshot.
/// </summary>
/// <remarks>
/// **此类型刻意不求解、不降阶、不扰动、不修改模型**；它提供 S1+ 分析器可共享的生命周期与稳定输入。
/// 缓存访问保持 internal，使本模块算法包可复用 session 而不通过公共 API 暴露 solver 产物。
///
/// **更正此前的账**： 记的是"持有活的求解器实例，要真实后端"。**那是 grep 代理读错了**——
/// 唯一带 `ConstraintProgrammingSolver` 的参数**只用来读 `Descriptor`**，`BaselineSnapshot` 之外没有任何求解调用。
/// 所以它是**完全不需要后端**的。
///
/// **This type intentionally does not solve, lower, perturb, or mutate a model**; it provides the lifecycle and stable inputs that S1+ analyzers share. **corrects the
/// an earlier ledger entry**: recorded "holds a live solver instance, needs a real backend". **That was the grep proxy misleading the reading** — the one parameter typed
/// `ConstraintProgrammingSolver` **is used only to read its `Descriptor`**, and nothing outside `BaselineSnapshot` ever solves. It therefore **needs no backend at all**.
/// </remarks>
public sealed class CriticalConstraintAnalysisSession : IDisposable
{
    private readonly Dictionary<AnalysisCacheKind, Dictionary<AnalysisCacheKey, object>> _caches;
    private readonly object _gate = new();
    private bool _closed;

    /// <summary>构造分析会话 / Construct an analysis session.</summary>
    /// <param name="baselineSnapshot">不可变模型基线 / Immutable model baseline.</param>
    /// <param name="baselineSolution">可选基线 incumbent / Optional baseline incumbent.</param>
    /// <param name="baselineObjectiveValue">可选基线目标值 / Optional baseline objective value.</param>
    /// <param name="solverDescriptor">用于调度与来源记录的求解器描述 / Solver descriptor for dispatch and provenance.</param>
    /// <param name="capabilityMatrix">能力矩阵，未提供时从 descriptor 推导 / Capability matrix, derived from the descriptor when omitted.</param>
    public CriticalConstraintAnalysisSession(
        ConstraintProgrammingModelSnapshot baselineSnapshot,
        ConstraintProgrammingSolution? baselineSolution = null,
        Flt64? baselineObjectiveValue = null,
        SolverDescriptor? solverDescriptor = null,
        CapabilityMatrix? capabilityMatrix = null) {
        ArgumentNullException.ThrowIfNull(baselineSnapshot);

        BaselineSnapshot = baselineSnapshot;
        BaselineSolution = baselineSolution;
        BaselineObjectiveValue = baselineObjectiveValue;
        SolverDescriptor = solverDescriptor;
        CapabilityMatrix = capabilityMatrix
            ?? (solverDescriptor is null ? new CapabilityMatrix(new HashSet<SolverModelType>()) : CapabilityMatrix.From(solverDescriptor));

        _caches = Enum.GetValues<AnalysisCacheKind>()
            .ToDictionary(kind => kind, _ => new Dictionary<AnalysisCacheKey, object>());

        Validation = ValidateSessionInputs(baselineSnapshot, baselineSolution, baselineObjectiveValue);
    }

    /// <summary>不可变模型基线 / Immutable model baseline.</summary>
    public ConstraintProgrammingModelSnapshot BaselineSnapshot { get; }

    /// <summary>可选基线 incumbent / Optional baseline incumbent.</summary>
    public ConstraintProgrammingSolution? BaselineSolution { get; }

    /// <summary>可选基线目标值 / Optional baseline objective value.</summary>
    public Flt64? BaselineObjectiveValue { get; }

    /// <summary>用于调度与来源记录的求解器描述 / Solver descriptor for dispatch and provenance.</summary>
    public SolverDescriptor? SolverDescriptor { get; }

    /// <summary>分析能力矩阵 / Analysis capability matrix.</summary>
    public CapabilityMatrix CapabilityMatrix { get; }

    /// <summary>不可变基线及 session 输入的校验结果 / Validation result for the immutable baseline and session inputs.</summary>
    public Try Validation { get; }

    /// <summary>分析器使用的基线 snapshot 兼容别名 / Compatibility alias for analyzers that call the code simply snapshot.</summary>
    public ConstraintProgrammingModelSnapshot Snapshot => BaselineSnapshot;

    /// <summary>基线目标值兼容别名 / Compatibility alias for the code objective.</summary>
    public Flt64? BaselineObjective => BaselineObjectiveValue;

    /// <summary>session 是否已释放分析缓存 / Whether this session has released its analysis caches.</summary>
    public bool IsClosed {
        get {
            lock (_gate) {
                return _closed;
            }
        }
    }

    /// <summary>当前缓存大小，便于生命周期测试与诊断 / Current cache sizes, useful for lifecycle tests and diagnostics.</summary>
    public IReadOnlyDictionary<AnalysisCacheKind, int> CacheSizes {
        get {
            lock (_gate) {
                return _caches.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
            }
        }
    }

    /// <summary>删除所有派生分析缓存项 / Remove all derived analysis cache entries.</summary>
    public void ClearCaches() {
        lock (_gate) {
            foreach (Dictionary<AnalysisCacheKey, object> cache in _caches.Values) {
                cache.Clear();
            }
        }
    }

    /// <summary>读取一个缓存项 / Read one cache entry.</summary>
    /// <typeparam name="T">缓存值类型 / The cached value type.</typeparam>
    /// <param name="kind">缓存桶 / The cache bucket.</param>
    /// <param name="key">缓存键 / The cache key.</param>
    /// <returns>缓存值，未命中时为 <c>null</c> / The cached value, or <c>null</c> on a miss.</returns>
    internal T? Cached<T>(AnalysisCacheKind kind, AnalysisCacheKey key) where T : class {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate) {
            if (_closed) {
                return null;
            }
            return _caches[kind].TryGetValue(key, out object? value) ? value as T : null;
        }
    }

    /// <summary>写入一个缓存项 / Write one cache entry.</summary>
    /// <param name="kind">缓存桶 / The cache bucket.</param>
    /// <param name="key">缓存键 / The cache key.</param>
    /// <param name="value">缓存值 / The cached value.</param>
    /// <returns>结构化写入结果 / A structured write result.</returns>
    /// <remarks>
    /// **关闭后写入必须失败，而且要在持锁后再查一次 `_closed`**：只在锁外查，会在"查到未关闭"和"拿到锁"之间
    /// 被 `Dispose` 插进来，于是**一个已关闭的 session 又多出一条缓存**。
    ///
    /// **A write after close has to fail, and the `_closed` check has to happen again under the lock**: checking only outside lets `Dispose` slip in between the check and the
    /// lock, so **a closed session gains one more cache entry.**
    /// </remarks>
    internal Try Cache(AnalysisCacheKind kind, AnalysisCacheKey key, object value) {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate) {
            if (_closed) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationStopped,
                    "分析 session 已关闭 / Analysis session is closed"));
            }
            _caches[kind][key] = value;
        }
        return Results.OkInstance;
    }

    /// <summary>释放分析缓存 / Release the analysis caches.</summary>
    public void Dispose() => Close();

    /// <summary>释放分析缓存 / Release the analysis caches.</summary>
    public void Close() {
        lock (_gate) {
            if (_closed) {
                return;
            }
            foreach (Dictionary<AnalysisCacheKey, object> cache in _caches.Values) {
                cache.Clear();
            }
            _closed = true;
        }
    }

    /// <summary>对可变 CP model 创建 snapshot 并生成 session / Snapshot a mutable CP model and create a session.</summary>
    /// <param name="model">可变 CP model / Mutable CP model.</param>
    /// <param name="baselineSolution">可选基线解 / Optional baseline solution.</param>
    /// <param name="baselineObjectiveValue">可选基线目标值 / Optional baseline objective value.</param>
    /// <param name="solverDescriptor">可选求解器描述 / Optional solver descriptor.</param>
    /// <returns>分析 session 结果 / Analysis session result.</returns>
    public static SessionRet From(
        ConstraintProgrammingModel model,
        ConstraintProgrammingSolution? baselineSolution = null,
        Flt64? baselineObjectiveValue = null,
        SolverDescriptor? solverDescriptor = null) {
        ArgumentNullException.ThrowIfNull(model);

        Fuookami.Ospf.Utils.Functional.Result<ConstraintProgrammingModelSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            model.Snapshot();
        if (!snapshot.IsOk) {
            return Results.Failed<CriticalConstraintAnalysisSession>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "CP model 无法生成 snapshot / The CP model could not produce a snapshot"));
        }
        return Results.Ok(FromSnapshot(snapshot.Value, baselineSolution, baselineObjectiveValue, solverDescriptor));
    }

    /// <summary>从已校验的不可变 snapshot 创建 session / Create a session from an already validated immutable snapshot.</summary>
    /// <param name="snapshot">不可变 CP snapshot / Immutable CP snapshot.</param>
    /// <param name="baselineSolution">可选基线解 / Optional baseline solution.</param>
    /// <param name="baselineObjectiveValue">可选基线目标值 / Optional baseline objective value.</param>
    /// <param name="solverDescriptor">可选求解器描述 / Optional solver descriptor.</param>
    /// <param name="capabilityMatrix">能力矩阵 / Capability matrix.</param>
    /// <returns>分析 session / Analysis session.</returns>
    public static CriticalConstraintAnalysisSession FromSnapshot(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingSolution? baselineSolution = null,
        Flt64? baselineObjectiveValue = null,
        SolverDescriptor? solverDescriptor = null,
        CapabilityMatrix? capabilityMatrix = null) =>
        new(snapshot, baselineSolution, baselineObjectiveValue, solverDescriptor, capabilityMatrix);

    /// <summary>从不可变 snapshot 创建带校验结果的安全 session / Create a validated session result.</summary>
    /// <param name="snapshot">不可变 CP snapshot / Immutable CP snapshot.</param>
    /// <param name="baselineSolution">可选基线解 / Optional baseline solution.</param>
    /// <param name="baselineObjectiveValue">可选基线目标值 / Optional baseline objective value.</param>
    /// <param name="solverDescriptor">可选求解器描述 / Optional solver descriptor.</param>
    /// <param name="capabilityMatrix">能力矩阵 / Capability matrix.</param>
    /// <returns>带校验结果的分析 session / Validated analysis session result.</returns>
    public static SessionRet TryFromSnapshot(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingSolution? baselineSolution = null,
        Flt64? baselineObjectiveValue = null,
        SolverDescriptor? solverDescriptor = null,
        CapabilityMatrix? capabilityMatrix = null) {
        CriticalConstraintAnalysisSession session = FromSnapshot(
            snapshot, baselineSolution, baselineObjectiveValue, solverDescriptor, capabilityMatrix);

        if (!session.Validation.IsOk) {
            return Results.Failed<CriticalConstraintAnalysisSession>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "分析 session 基线无效 / Analysis session baseline is invalid"));
        }
        return Results.Ok(session);
    }

    /// <summary>
    /// 使用 solver descriptor 创建 session，但不保留 solver 句柄；solver 所有权仍由调用方持有 / Create a session using a solver descriptor without retaining the solver handle.
    /// </summary>
    /// <param name="snapshot">不可变 CP snapshot / Immutable CP snapshot.</param>
    /// <param name="solver">求解器实例，仅读取 descriptor / Solver instance, used only for its descriptor.</param>
    /// <param name="baselineSolution">可选基线解 / Optional baseline solution.</param>
    /// <param name="baselineObjectiveValue">可选基线目标值 / Optional baseline objective value.</param>
    /// <returns>分析 session / Analysis session.</returns>
    /// <remarks>
    /// **只读 `Descriptor`，不保存 solver 引用**：solver 的生命周期由调用方持有，
    /// 会话拿着它会让"关掉会话"看起来像"关掉求解器"。
    ///
    /// **Only `Descriptor` is read, and no solver reference is kept**: the caller owns the solver's lifetime, and holding it would make "close the session" look like
    /// "close the solver".
    /// </remarks>
    public static CriticalConstraintAnalysisSession FromSnapshot(
        ConstraintProgrammingModelSnapshot snapshot,
        IConstraintProgrammingSolver solver,
        ConstraintProgrammingSolution? baselineSolution = null,
        Flt64? baselineObjectiveValue = null) {
        ArgumentNullException.ThrowIfNull(solver);
        return FromSnapshot(snapshot, baselineSolution, baselineObjectiveValue, solver.Descriptor);
    }

    /// <summary>当 session 基线无效时返回结构化失败 / Return a structured failure when a session baseline is invalid.</summary>
    /// <param name="session">分析会话 / The analysis session.</param>
    /// <returns>失败结果，效时为 <c>null</c> / The failure, or <c>null</c> when the code is valid.</returns>
    internal static Try? InvalidSessionResult(CriticalConstraintAnalysisSession session) =>
        session.Validation.IsOk ? null : session.Validation;

    private static Try ValidateSessionInputs(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingSolution? baselineSolution,
        Flt64? baselineObjective) {
        if (!snapshot.Validate().IsOk) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "分析 session 需要有效的 CP snapshot 身份清单 / Analysis session requires a valid CP snapshot identity manifest"));
        }
        if (baselineObjective is { } objective && !IsFinite(objective)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "基线目标值必须有限 / Baseline objective value must be finite"));
        }
        // **只给目标值不给解必须拒**：一个数字无法校验，而"未校验的目标值"会被下游当成事实。
        // **A bare objective has to be refused**: a number cannot be checked, and an unverified objective is treated as fact downstream.
        if (baselineSolution is null) {
            return baselineObjective is null
                ? Results.OkInstance
                : Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "提供基线目标值时必须同时提供基线 CP 解 / A baseline CP solution is required when a baseline objective is supplied"));
        }

        IReadOnlyDictionary<VariableId, Int64> values = baselineSolution.Values;

        // **未知变量要先查**：一条多出来的值意味着这份解来自另一个模型，后面"缺少变量"的检查会先撞上它。
        // **Unknown variables are checked first**: an extra value means this solution belongs to another model, and the "missing variable" check would trip on it first.
        // **`FirstOrDefault` 对结构体键会返回默认值**，于是"没找到"和"找到 `VariableId("")`"分不开——
        // 而空 id 会让报错信息指向一个不存在的变量。改成显式查找。
        // **`FirstOrDefault` returns the default for a struct key**, so "not found" and "found `VariableId("")`" become the same thing — and the empty id makes the error point
        // at a variable that does not exist. Look it up explicitly instead.
        VariableId? unknown = values.Keys
            .Where(id => snapshot.Variable(id) is null)
            .Select(id => (VariableId?)id)
            .FirstOrDefault();
        if (unknown is { } unknownId) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.DataNotFound,
                "基线 CP 解包含未知变量 / Baseline CP solution contains an unknown variable: " + unknownId.Value));
        }

        ConstraintProgrammingVariableSnapshot? missing = snapshot.Variables
            .FirstOrDefault(variable => !values.ContainsKey(variable.Id));
        if (missing is not null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.DataNotFound,
                "基线 CP 解缺少变量 / Baseline CP solution misses variable: " + missing.Id.Value));
        }

        ConstraintProgrammingVariableSnapshot? outside = snapshot.Variables
            .FirstOrDefault(variable => !variable.Domain.Contains(values[variable.Id]));
        if (outside is not null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "基线 CP 解超出变量值域 / Baseline CP solution is outside variable domain: " + outside.Id.Value));
        }

        foreach (ConstraintProgrammingConstraintSnapshot definition in snapshot.Constraints) {
            Fuookami.Ospf.Utils.Functional.Result<bool, ErrorCode, Error<ErrorCode>> satisfied =
                definition.Constraint.IsSatisfied(values);
            if (!satisfied.IsOk) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "基线 CP 解的约束求值失败 / Constraint evaluation failed for the baseline CP solution: " + definition.Id.Value));
            }
            if (!satisfied.Value) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ORSolutionInvalid,
                    "基线 CP 解不满足约束 / Baseline CP solution violates constraint: " + definition.Id.Value));
            }
        }

        if (baselineObjective is not { } target) {
            return Results.OkInstance;
        }

        // **给了目标值就必须同时给解**：只给一个数字无法校验，而"未校验的目标值"会被下游当成事实。
        // **A supplied objective has to come with a solution**: a bare number cannot be checked, and an unverified objective is treated as fact downstream.
        if (snapshot.Objectives.Count != 1) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "基线目标值需要唯一的 CP objective / A baseline objective value requires exactly one CP objective"));
        }

        ConstraintProgrammingObjectiveSnapshot objectiveDefinition = snapshot.Objectives[0];
        Fuookami.Ospf.Utils.Functional.Result<Int64, ErrorCode, Error<ErrorCode>> evaluated =
            objectiveDefinition.Expression.Evaluate(values);
        if (!evaluated.IsOk) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "基线目标表达式求值失败 / Objective evaluation failed for the baseline CP solution"));
        }
        if (evaluated.Value.ToFlt64() != target) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "基线目标值与基线 CP 解不一致 / Baseline objective does not match the baseline CP solution: "
                + target.ToDouble() + " != " + evaluated.Value.ToFlt64().ToDouble()));
        }
        return Results.OkInstance;
    }

    private static bool IsFinite(Flt64 value) =>
        !double.IsNaN(value.ToDouble()) && !double.IsInfinity(value.ToDouble());
}
