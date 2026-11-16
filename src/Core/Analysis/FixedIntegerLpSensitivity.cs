#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Flt64 = Fuookami.Ospf.Math.Algebra.Number.Flt64;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>敏感性值的作用域 / Scope of a sensitivity value.</summary>
/// <remarks>
/// `LocalSensitivityScope` **只有一个成员，而这个成员本身是重点**：
/// 这里的对偶值**绝不是 MILP 全局影子价格**，它只在"整数变量固定为一个 incumbent 结构"下成立。
///
/// **It has one member, and that member is the point**: these duals are **never global MILP shadow prices**, they hold only under
/// "integer variables fixed to one incumbent pattern".
/// </remarks>
public enum LocalSensitivityScope
{
    /// <summary>整数变量固定为一个 incumbent 结构 / Integer variables are fixed to one incumbent pattern.</summary>
    FixedIntegerIncumbent,
}

/// <summary>
/// LP 后端可用时返回的有限 RHS 敏感性范围 / A finite RHS range reported by an LP backend, when available.
/// </summary>
/// <param name="Lower">已知时的范围下界 / Lower end of the valid range, if known.</param>
/// <param name="Upper">已知时的范围上界 / Upper end of the valid range, if known.</param>
public sealed record SensitivityRange(Flt64? Lower = null, Flt64? Upper = null)
{
    /// <summary>
    /// 校验范围本身成立 / Validate that the range is well formed.
    /// </summary>
    /// <remarks>
    /// **三条都要查**：端点必须有限（`Infinity` 当范围上界，等于没有范围），且下界不得超过上界。
    /// 只查有限性会让 `[5, 3]` 这种"范围"通过——**而它覆盖的区间是空的。**
    ///
    /// **All three are checked**: endpoints must be finite (an infinite upper end is no range at all), and the lower end must not exceed the upper. Checking finiteness alone
    /// lets `[5, 3]` through — **and the interval it covers is empty.**
    /// </remarks>
    /// <exception cref="ArgumentException">范围不成立 / The range is not well formed.</exception>
    public void Validate() {
        if (Lower is { } lower && !IsFinite(lower)) {
            throw new ArgumentException("敏感性范围下界必须有限 / Sensitivity range lower bound must be finite", nameof(Lower));
        }
        if (Upper is { } upper && !IsFinite(upper)) {
            throw new ArgumentException("敏感性范围上界必须有限 / Sensitivity range upper bound must be finite", nameof(Upper));
        }
        if (Lower is { } l && Upper is { } u && Compare(l, u) > 0) {
            throw new ArgumentException("敏感性范围下界不得大于上界 / Sensitivity range lower bound must not exceed upper bound", nameof(Upper));
        }
    }

    private static bool IsFinite(Flt64 value) => ((IRealNumber<Flt64>)value).IsSelfWithinBounds();

    private static int Compare(Flt64 left, Flt64 right) => left.CompareTo(right);
}

/// <summary>
/// 单个原始 CP 约束的局部敏感性 / Local sensitivity of one original CP constraint.
/// </summary>
/// <remarks>
/// `DualValue` 的注释在就是一句警告：**它是固定整数结构下的 LP 对偶，不是全局影子价格。**
/// 这个区别不写清楚，报告会被当成"MILP 灵敏度"来用——**而那正是它不成立的地方。**
///
/// The comment on `DualValue` is a warning: **it is the LP dual under a fixed integer pattern, not a global shadow price.** Without that distinction a report
/// gets used as "MILP sensitivity" — **exactly where it does not hold.**
/// </remarks>
/// <param name="ConstraintId">原始稳定约束 ID / Original stable constraint ID.</param>
/// <param name="DualValue">固定整数结构下的 LP 对偶值 / LP dual under the fixed-integer pattern.</param>
/// <param name="Activity">同一约束的活动性证据 / Activity evidence for that constraint.</param>
/// <param name="LocalEffective">局部对偶值是否超过阈值 / Whether the local dual exceeds the threshold.</param>
/// <param name="SensitivityRange">可选的有效 RHS 敏感性范围 / Optional valid RHS sensitivity range.</param>
/// <param name="Scope">敏感性值作用域 / Scope of the sensitivity value.</param>
public sealed record LocalConstraintSensitivity(
    ConstraintId ConstraintId,
    Flt64? DualValue = null,
    ConstraintActivity? Activity = null,
    bool? LocalEffective = null,
    SensitivityRange? SensitivityRange = null,
    LocalSensitivityScope Scope = LocalSensitivityScope.FixedIntegerIncumbent)
{
    /// <summary>适配器使用的简短别名 / Short alias used by adapters.</summary>
    public Flt64? Dual => DualValue;

    /// <summary>使用有效性术语的调用方别名 / Short alias for callers that use effectiveness terminology.</summary>
    public bool? Effective => LocalEffective;
}

/// <summary>
/// 固定整数 LP 后端返回的结果 / Result returned by a fixed-integer LP backend.
/// </summary>
/// <param name="Status">派生 LP 的求解结论 / Solver conclusion for the derived LP.</param>
/// <param name="ObjectiveValue">存在解时的派生 LP 目标值 / Derived LP objective value, when available.</param>
/// <param name="Duals">按原始稳定约束 ID 编索引的对偶值 / Duals keyed by original stable constraint ID.</param>
/// <param name="SensitivityRanges">按原始稳定约束 ID 编索引的可选范围 / Optional ranges keyed by original stable constraint ID.</param>
/// <param name="SolveTime">后端求解耗时 / Backend solve duration.</param>
/// <param name="Message">保留用于诊断的后端详情 / Backend detail retained for diagnostics.</param>
public sealed record FixedIntegerLpSolveResult(
    AnalysisStatus Status,
    Flt64? ObjectiveValue = null,
    IReadOnlyDictionary<ConstraintId, Flt64?>? Duals = null,
    IReadOnlyDictionary<ConstraintId, SensitivityRange?>? SensitivityRanges = null,
    TimeSpan? SolveTime = null,
    string? Message = null)
{
    /// <summary>对偶值，未给出时为空 / The duals, empty when absent.</summary>
    public IReadOnlyDictionary<ConstraintId, Flt64?> DualValues =>
        Duals ?? new Dictionary<ConstraintId, Flt64?>();

    /// <summary>使用 `DualValues` 的适配器兼容别名 / Compatibility alias for adapters using `DualValues`.</summary>
    public IReadOnlyDictionary<ConstraintId, Flt64?> DualMap => DualValues;

    /// <summary>
    /// 校验结果本身成立 / Validate that the result is well formed.
    /// </summary>
    /// <exception cref="ArgumentException">目标值或某个对偶值不是有限数 / The objective or one dual is not finite.</exception>
    public void Validate() {
        if (ObjectiveValue is { } objective && !((IRealNumber<Flt64>)objective).IsSelfWithinBounds()) {
            throw new ArgumentException("固定整数 LP 目标值必须有限 / Fixed-integer LP objective must be finite", nameof(ObjectiveValue));
        }
        foreach (KeyValuePair<ConstraintId, Flt64?> pair in DualValues) {
            if (pair.Value is { } dual && !((IRealNumber<Flt64>)dual).IsSelfWithinBounds()) {
                throw new ArgumentException(
                    "固定整数 LP 对偶值必须有限 / Fixed-integer LP dual values must be finite: " + pair.Key.Value,
                    nameof(Duals));
            }
        }
    }
}

/// <summary>
/// 分析器选项 / Analyzer options.
/// </summary>
/// <param name="DualTolerance">判定非零对偶值的绝对阈值 / Absolute threshold for classifying a non-zero dual.</param>
public sealed record FixedIntegerLpSensitivityOptions(double DualTolerance = FixedIntegerLpSensitivityOptions.DefaultDualTolerance)
{
    /// <summary>默认局部有效性阈值 / Default local-effectiveness threshold.</summary>
    public const double DefaultDualTolerance = 1e-9;

    /// <summary>
    /// 校验阈值本身成立 / Validate that the threshold is well formed.
    /// </summary>
    /// <remarks>
    /// **必须是有限且非负的**：`NaN` 会让每次比较都为假（于是没有一条被判为有效），**而报告看起来只是"都没超阈值"。**
    ///
    /// **It has to be finite and non-negative**: `NaN` makes every comparison false (so nothing is ever classified effective), **while the report simply looks like "none
    /// exceeded the threshold".**
    /// </remarks>
    /// <exception cref="ArgumentException">阈值不是有限非负数 / The threshold is not finite and non-negative.</exception>
    public void Validate() {
        if (double.IsNaN(DualTolerance) || double.IsInfinity(DualTolerance) || DualTolerance < 0.0) {
            throw new ArgumentException("对偶阈值必须有限且非负 / Dual tolerance must be finite and non-negative", nameof(DualTolerance));
        }
    }
}

/// <summary>
/// 固定整数 LP 后端请求 / Fixed-integer LP backend request.
/// </summary>
/// <param name="Snapshot">不可变源 snapshot / Immutable source snapshot.</param>
/// <param name="FixedValues">为每个原始整数变量固定的值 / Values fixed for every original integer variable.</param>
/// <param name="BaselineSolution">用作热启动和证据来源的基线解 / Baseline solution used as warm-start and evidence source.</param>
/// <param name="Options">分析器选项 / Analyzer options.</param>
public sealed record FixedIntegerLpRequest(
    ConstraintProgrammingModelSnapshot Snapshot,
    IReadOnlyDictionary<VariableId, Int64> FixedValues,
    ConstraintProgrammingSolution BaselineSolution,
    FixedIntegerLpSensitivityOptions Options);

/// <summary>
/// 固定整数 LP 后端 / A fixed-integer LP backend.
/// </summary>
/// <remarks>
/// **这是 `fun interface FixedIntegerLpBackend` 的 C# 版。**
///
/// **一个方法，一个职责：收到请求、回一个结果或一个错误。** 转储、松弛、对偶提取全部属于实现方，
/// **接口本身不知道"敏感性"是什么**——所以换一个求解器不需要改这里的任何一个字。
///
/// **One method, one responsibility: take a request, return a result or an error.** Dumping, relaxation and dual extraction all belong to the implementation, **and the interface does not know what
/// "sensitivity" is** — so swapping a solver changes nothing here.
///
/// **它也是"需要真实后端"那类缺口的分界线。** 有了这个接口，编排逻辑可以用一个假后端跑通；
/// **没有它，唯一能测的路径必须挂一个真求解器。**
///
/// **It is also the line between "needs a real backend" and "needs a fake one".** With this interface the orchestration runs against a stub; **without it, the only testable path needs a live
/// solver.**
/// </remarks>
public interface IFixedIntegerLpBackend
{
    /// <summary>
    /// 求解一个固定整数 LP / Solve one fixed-integer LP.
    /// </summary>
    /// <param name="request">请求 / The request.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>结果或错误 / The result, or an error.</returns>
    Task<Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        FixedIntegerLpRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 固定整数 LP 敏感性报告 / Fixed-integer LP sensitivity report.
/// </summary>
/// <remarks>
/// **`Status` 里 `Unsupported` 与 `Unknown` 必须分开**：前者是"这个后端做不到"，后者是"试了但没结果"。
/// 合成一个会让"换后端能解决"和"模型本身有问题"看起来一样。
///
/// **`Unsupported` and `Unknown` have to stay distinct** in `Status`: the first means "this backend cannot", the second "tried and got nothing". Merging them makes "another
/// backend would work" look like "the model is broken".
/// </remarks>
/// <param name="Status">分析结论 / Analysis conclusion.</param>
/// <param name="Scope">报告中所有对偶值的作用域 / Scope of all dual values in the report.</param>
/// <param name="BaselineObjective">调用方提供的基线目标值 / Baseline objective supplied by the caller.</param>
/// <param name="FixedValues">固定的 incumbent 值 / Fixed incumbent values.</param>
/// <param name="Sensitivities">按 snapshot 顺序排列的原始约束敏感性 / Original constraint sensitivities in snapshot order.</param>
/// <param name="ObjectiveValue">可用时的派生 LP 目标值 / Derived LP objective, if available.</param>
/// <param name="SolveTime">派生 LP 求解耗时 / Derived LP solve duration.</param>
/// <param name="Message">调度或后端诊断详情 / Diagnostics from dispatch or backend.</param>
public sealed record FixedIntegerLpSensitivityReport(
    AnalysisStatus Status,
    LocalSensitivityScope Scope = LocalSensitivityScope.FixedIntegerIncumbent,
    Flt64? BaselineObjective = null,
    IReadOnlyDictionary<VariableId, Int64>? FixedValues = null,
    IReadOnlyList<LocalConstraintSensitivity>? Sensitivities = null,
    Flt64? ObjectiveValue = null,
    TimeSpan? SolveTime = null,
    string? Message = null)
{
    /// <summary>局部敏感性，未给出时为空 / The local sensitivities, empty when absent.</summary>
    public IReadOnlyList<LocalConstraintSensitivity> LocalSensitivities =>
        Sensitivities ?? Array.Empty<LocalConstraintSensitivity>();

    /// <summary>
    /// 查找一个原始约束的敏感性 / Find one original constraint's sensitivity.
    /// </summary>
    /// <param name="id">要查找的约束 ID / Constraint ID to find.</param>
    /// <returns>匹配的敏感性记录，找不到时为 <c>null</c> / Matching sensitivity, or <c>null</c> when absent.</returns>
    public LocalConstraintSensitivity? Constraint(ConstraintId id) =>
        LocalSensitivities.FirstOrDefault(sensitivity => sensitivity.ConstraintId == id);
}

/// <summary>
/// 先降阶再委托的后端适配器 / A backend adapter that lowers first and then delegates.
/// </summary>
/// <remarks>
/// **这是 `LoweredFixedIntegerLpBackend` 的实现，而它此前在 C# 侧完全没有对应物**——
/// `IFixedIntegerLpBackend` 和 `ConstraintProgrammingToLinearModelLowerer` 都在，
/// **缺的正是把两者接起来的那一层。**
///
/// **`finally` 里的 `Dispose` 不是清理习惯，是这条路径的正确性前提。** 降阶会物化一个线性模型；
/// **不关它，每次敏感性分析都在进程里留一份完整模型**——而敏感性分析按约束逐个跑，
/// **一个千约束的模型会留下千份。**
///
/// **This is the implementation of `LoweredFixedIntegerLpBackend`, and it had no counterpart here at all** — `IFixedIntegerLpBackend` and the lowerer both exist, **and the layer joining them did not.**
/// **The `Dispose` in `finally` is not tidiness; it is the precondition of this path being usable at all.** Lowering materialises a linear model; **not closing it leaves one full model per sensitivity run, and sensitivity
/// runs once per constraint — a thousand-constraint model leaves a thousand behind.**
/// </remarks>
public sealed class LoweredFixedIntegerLpBackend : IFixedIntegerLpBackend
{
    private readonly ConstraintProgrammingToLinearModelLowerer _lowerer;
    private readonly ILoweredFixedIntegerLpSolver _delegate;

    /// <summary>构造适配器 / Construct the adapter.</summary>
    /// <param name="delegate">降阶模型上的求解操作 / The solve operation on a lowered model.</param>
    /// <param name="lowerer">降阶器；<c>null</c> 表示用默认实现 / The lowerer, or <c>null</c> for the default one.</param>
    public LoweredFixedIntegerLpBackend(
        ILoweredFixedIntegerLpSolver @delegate,
        ConstraintProgrammingToLinearModelLowerer? lowerer = null) {
        ArgumentNullException.ThrowIfNull(@delegate);

        _delegate = @delegate;
        _lowerer = lowerer ?? new ConstraintProgrammingToLinearModelLowerer();
    }

    /// <summary>降阶后委托求解 / Lower, then delegate.</summary>
    /// <param name="request">求解请求 / The solve request.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>求解结果 / The solve result.</returns>
    public async Task<Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        FixedIntegerLpRequest request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        Result<ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>> lowered =
            _lowerer.Lower(request.Snapshot, fixedValues: request.FixedValues);

        if (lowered is Failed<ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<FixedIntegerLpSolveResult>(failed.Error);
        }

        ConstraintProgrammingLoweredLinearModel model = lowered.Value!;
        try {
            return await _delegate.SolveAsync(model, request, cancellationToken).ConfigureAwait(false);
        } finally {
            // **`finally` 而不是 `using`：这里必须关，哪怕委托抛了异常。**
            // **`finally` rather than `using`: this has to close even when the delegate throws.**
            model.Dispose();
        }
    }
}

/// <summary>
/// 一个已降阶模型上的求解操作 / The solve operation on one already-lowered model.
/// </summary>
/// <remarks>
/// **单独抽出来，是为了让"降阶"和"求解"能分别被测。**
/// **拆成两个接口之后，适配器的正确性就不再依赖某个具体求解器**——**那正是它此前无法被测试的原因。**
///
/// **Separated so lowering and solving can be tested apart.** **Once split, the adapter's correctness no longer depends on a concrete solver** — **which is exactly why it could not be tested before.**
/// </remarks>
public interface ILoweredFixedIntegerLpSolver
{
    /// <summary>在一个降阶模型上求解 / Solve on a lowered model.</summary>
    /// <param name="lowered">降阶模型 / The lowered model.</param>
    /// <param name="request">原始请求 / The original request.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>求解结果 / The solve result.</returns>
    Task<Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ConstraintProgrammingLoweredLinearModel lowered,
        FixedIntegerLpRequest request,
        CancellationToken cancellationToken = default);
}
