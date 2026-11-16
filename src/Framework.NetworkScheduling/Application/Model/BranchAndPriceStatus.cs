// Branch-and-Price 应用模型 / Branch-and-Price application models.
//（521 字节）、
// ,,
// and.

using System;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;

/// <summary>
/// Branch-and-Price 终止状态 / Branch-and-Price termination status.
/// </summary>
/// <remarks>
/// 正常算法终态返回 <see cref="VrptwSolveResult{V}"/>；输入、单位转换、solver 调用或对偶证书失败返回失败结果。
/// Normal terminal states return a <see cref="VrptwSolveResult{V}"/>; input, unit-conversion, solver-call or dual-certificate
/// failures return a failure result.
/// **`Feasible` 与 `Optimal` 的区别是 gap 是否闭合，而不是"有没有解"**。两者都带 incumbent；把 `Feasible` 报成 `Optimal` 会让调用方
/// 以为拿到了最优解，而实际只是"队列耗尽且还有 gap"。
/// **The difference between `Feasible` and `Optimal` is whether the gap closed, not whether a solution exists.** Both carry an
/// incumbent; reporting `Feasible` as `Optimal` tells the caller they hold an optimum when it is really "queue exhausted with a gap
/// still open".
/// </remarks>
public enum BranchAndPriceStatus {
    /// <summary>活动节点为空且有 incumbent / No active nodes and an incumbent exists.</summary>
    Optimal,

    /// <summary>队列耗尽且存在 incumbent，但由有效节点下界算出的 gap 尚未闭合 / An incumbent exists after the queue is exhausted, but
    /// the gap computed from valid node lower bounds is not closed.</summary>
    Feasible,

    /// <summary>活动节点为空且无 incumbent / No active nodes and no incumbent.</summary>
    Infeasible,

    /// <summary>达到时间上限终止 / Terminated at the time limit.</summary>
    TimeLimit,

    /// <summary>达到节点上限终止 / Terminated at the node limit.</summary>
    NodeLimit,

    /// <summary>外部 solver 携带有效 bound/incumbent 的正常停止 / An external solver's normal stop carrying a valid
    /// bound/incumbent.</summary>
    SolverStopped,
}

/// <summary>
/// Branch-and-Price 执行轨迹 / Branch-and-Price execution trace.
/// </summary>
/// <param name="NodesExplored">已探索节点数 / Explored node count.</param>
/// <param name="NodesPruned">已剪枝节点数 / Pruned node count.</param>
/// <param name="GlobalLowerBound">全局下界 / Global lower bound.</param>
/// <param name="GlobalUpperBound">全局上界（incumbent 目标值）/ Global upper bound (the incumbent objective).</param>
/// <param name="RelativeGap">相对 gap / Relative gap.</param>
/// <param name="TotalTime">总运行时间 / Total running time.</param>
/// <param name="TotalIterations">总列生成迭代次数 / Total column-generation iterations.</param>
public sealed record BranchAndPriceTrace(
    int NodesExplored,
    int NodesPruned,
    Flt64 GlobalLowerBound,
    Flt64 GlobalUpperBound,
    Flt64 RelativeGap,
    TimeSpan TotalTime,
    int TotalIterations);

/// <summary>
/// Branch-and-Price 轨迹监听器 / Branch-and-Price trace listener.
/// </summary>
/// <remarks>
/// `fun interface`；C# 侧用委托表达同一语义，并用静态成员提供默认空实现。
/// This implementation is a `fun interface`; the C# side expresses the same with a delegate and exposes the default no-op as a static
/// member.
/// **监听器失败会中止求解**，这是有意的：轨迹是诊断手段，如果它静默失败，调用方会以为"没有轨迹"就是"没有事件"。
/// **A listener failure aborts the solve** deliberately: the trace is a diagnostic, and if it failed silently the caller would read
/// "no trace" as "no events".
/// </remarks>
/// <param name="trace">最新轨迹快照 / The latest trace snapshot.</param>
/// <returns>操作结果 / The operation result.</returns>
public delegate Try BranchAndPriceTraceListener(BranchAndPriceTrace trace);

/// <summary>
/// 轨迹监听器默认值 / Default trace-listener values.
/// </summary>
public static class BranchAndPriceTraceListeners {
    /// <summary>默认空监听器 / The default no-op listener.</summary>
    /// <param name="trace">被忽略的轨迹 / The ignored trace.</param>
    /// <returns>始终成功 / Always success.</returns>
    public static Try None(BranchAndPriceTrace trace) => Results.Ok(Results.SuccessInstance);
}

/// <summary>
/// VRPTW 求解结果 / VRPTW solve result.
/// </summary>
/// <remarks>
/// 面向调用方的公开应用模型，与算法内部的 `BranchAndPriceResult` 解耦。
/// Public application model for callers, decoupled from the internal `BranchAndPriceResult`.
/// **`Solution` 为 null 只在 `Infeasible` 与资源上限终止且无 incumbent 时成立**。其它状态带 null 解说明有一条成功路径没写解，
/// 调用方会把它当成"无解"——这类错误不会报错，只会让上层以为问题不可行。
/// **`Solution` is null only for `Infeasible`, or for a resource-limit stop with no incumbent.** A null solution under any other
/// status means some success path never wrote it, and the caller reads that as "no solution" — an error that never raises, it just
/// makes the layer above believe the problem is infeasible.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Status">求解终止状态 / Solve termination status.</param>
/// <param name="Solution">可行解（不可行时为 null）/ Feasible solution (null when infeasible).</param>
/// <param name="LowerBound">全局下界 / Global lower bound.</param>
/// <param name="UpperBound">全局上界（incumbent 成本或 +∞）/ Global upper bound (incumbent cost, or +∞).</param>
/// <param name="RelativeGap">相对间隙 / Relative gap.</param>
/// <param name="Trace">求解过程轨迹 / The solve trace.</param>
public sealed class VrptwSolveResult<V>(
    BranchAndPriceStatus Status,
    VrptwSolution<V>? Solution,
    Flt64 LowerBound,
    Flt64 UpperBound,
    Flt64 RelativeGap,
    BranchAndPriceTrace Trace) where V : struct, IRealNumber<V> {
    /// <summary>求解终止状态 / Solve termination status.</summary>
    public BranchAndPriceStatus Status { get; } = Status;

    /// <summary>可行解（不可行时为 null）/ Feasible solution (null when infeasible).</summary>
    public VrptwSolution<V>? Solution { get; } = Solution;

    /// <summary>全局下界 / Global lower bound.</summary>
    public Flt64 LowerBound { get; } = LowerBound;

    /// <summary>全局上界（incumbent 成本或 +∞）/ Global upper bound (incumbent cost, or +∞).</summary>
    public Flt64 UpperBound { get; } = UpperBound;

    /// <summary>相对间隙 / Relative gap.</summary>
    public Flt64 RelativeGap { get; } = RelativeGap;

    /// <summary>求解过程轨迹 / The solve trace.</summary>
    public BranchAndPriceTrace Trace { get; } = Trace;
}
