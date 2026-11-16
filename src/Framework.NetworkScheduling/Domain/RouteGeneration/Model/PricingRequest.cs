// ESPPRC 标签、定价请求与定价结果 / ESPPRC label, pricing request and pricing result.
// 与固定 Kotlin 、（46 行）、（28 行）对应。
// , and.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Model;

/// <summary>
/// ESPPRC 标签 / ESPPRC label.
/// </summary>
/// <remarks>
/// 不可变资源标签，包含累计 reduced cost、时间、负载、当前节点、已访问客户、不可达客户、前驱索引，以及终点处才用得上的
/// 三个字段：沿途原始弧成本、逐弧累加的目标成本、按路线成本策略算出的实际路线成本。
///
/// **为什么要把弧成本与目标成本一路存下来**：非可加的成本策略（阶梯计费、距离折扣）无法在扩展时就把 reduced cost 累加正确，
/// 只有到终点拿着完整弧序列才能算准。若扩展时直接累加，定价会用偏低的 reduced cost 接受冗余列，而主问题最终被迫在
/// 这些列上收敛——结果是一个数值上"正确"却很差的解。
/// An immutable resource label with the accumulated reduced cost, time, load, current node, visited and forbidden customers, a
/// predecessor index, and three fields only used at the terminal: the raw arc costs along the route, the arc-decomposable
/// objective cost, and the actual route cost from the route-cost policy.
///
/// **Why arc costs and objective cost are carried all the way**: a non-additive cost policy (tiered tariffs, distance
/// discounts) cannot be accumulated correctly during extension — only at the terminal with the full arc sequence. Accumulating
/// during extension would let pricing accept redundant columns with an understated reduced cost, and the master would be forced
/// to converge on them: a numerically "correct" but poor solution.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="ReducedCost">累计 reduced cost / Accumulated reduced cost.</param>
/// <param name="Time">当前时间 / Current time.</param>
/// <param name="Load">当前负载 / Current load.</param>
/// <param name="CurrentNode">当前节点标识 / Current node identity.</param>
/// <param name="Visited">已访问客户 / Visited customers.</param>
/// <param name="Forbidden">不可达客户 / Forbidden customers.</param>
/// <param name="Predecessor">前驱标签索引（根为 -1）/ Predecessor label index (-1 for the root).</param>
/// <param name="RouteIndex">路线在迭代中的索引 / Route index within the iteration.</param>
/// <param name="ArcCosts">沿途原始弧成本 / Raw arc costs along the route.</param>
/// <param name="AdditiveObjectiveCost">逐弧累加的目标成本 / Accumulated arc-decomposable objective cost.</param>
/// <param name="RouteCost">终点处策略算出的实际路线成本 / Actual route cost at the terminal.</param>
public sealed record EspprcLabel<V>(
    V ReducedCost,
    V Time,
    V Load,
    NetworkNodeId CurrentNode,
    VisitedCustomers Visited,
    ForbiddenCustomers Forbidden,
    int Predecessor,
    int RouteIndex = -1,
    IReadOnlyList<Quantity<V>>? ArcCosts = null,
    V? AdditiveObjectiveCost = null,
    V? RouteCost = null) where V : struct, IRealNumber<V> {
    /// <summary>沿途原始弧成本，默认空 / Raw arc costs, empty by default.</summary>
    public IReadOnlyList<Quantity<V>> ArcCosts { get; init; } = ArcCosts ?? Array.Empty<Quantity<V>>();
}

/// <summary>
/// 定价中断检查器 / Pricing interruption checker.
/// </summary>
public interface IPricingInterruptionChecker {
    /// <summary>是否应立即停止定价 / Whether pricing should stop immediately.</summary>
    /// <returns>是否停止 / Whether to stop.</returns>
    bool ShouldStop();
}

/// <summary>
/// 永不中断的默认检查器 / Default checker that never interrupts pricing.
/// </summary>
public sealed class NeverInterruptChecker : IPricingInterruptionChecker {
    /// <summary>共享实例 / Shared instance.</summary>
    public static NeverInterruptChecker Instance { get; } = new NeverInterruptChecker();

    /// <inheritdoc/>
    public bool ShouldStop() => false;
}

/// <summary>
/// 定价请求 / Pricing request.
/// </summary>
/// <remarks>
/// 不可变输入合同，由应用层构造后传给定价器。**与 route compilation 解耦**，只依赖 VRP 侧的 `PricingDuals`：定价不该知道
/// 主问题是怎么编译出来的，否则换一种编译方式就要改定价。
/// An immutable input contract built by the application layer and handed to the pricer. **Decoupled from route compilation**,
/// depending only on the VRP-side `PricingDuals`: pricing should not know how the master was compiled, or every compilation
/// change would force a pricing change.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Instance">VRPTW 实例 / VRPTW instance.</param>
/// <param name="Duals">定价对偶 / Pricing duals.</param>
/// <param name="BranchMask">分支遮罩，无分支时为 null / Branch mask, null when not branching.</param>
/// <param name="PricingTolerance">solver 边界上的定价容差 / Pricing tolerance at the solver boundary.</param>
/// <param name="MaxColumnsPerPricing">每次定价最大返回列数 / Max columns returned per pricing call.</param>
/// <param name="VehicleTypeId">当前定价的车辆类型 / Vehicle type being priced.</param>
/// <param name="InterruptionChecker">定价中断检查器 / Pricing interruption checker.</param>
public sealed record PricingRequest<V>(
    VrptwInstance<V> Instance,
    PricingDuals Duals,
    BranchMask<VehicleTypeId>? BranchMask,
    Flt64 PricingTolerance,
    VehicleTypeId VehicleTypeId,
    int MaxColumnsPerPricing = int.MaxValue,
    IPricingInterruptionChecker? InterruptionChecker = null)
    where V : struct, IRealNumber<V> {
    /// <summary>定价中断检查器，默认永不中断 / The interruption checker, never interrupting by default.</summary>
    public IPricingInterruptionChecker InterruptionChecker { get; init; } =
        InterruptionChecker ?? NeverInterruptChecker.Instance;
}

/// <summary>
/// 定价结果 / Pricing result.
/// </summary>
/// <remarks>
/// **列数截断不能冒充收敛**：截断只限制"加入 RMP 的列数"，绝不等于"没有更多负列"。把"找到但未返回的负列"报成
/// `ExactPricingComplete = true`，会让主问题过早停止定价并收敛到一个并非最优的解，而且整个流程不会有任何报错。
/// 因此 `Interrupted` 与 `ExactPricingComplete` 是两个独立字段。
/// **Truncation must not masquerade as convergence**: it only bounds the columns added to the RMP, never means "no more negative
/// columns exist". Reporting "found but unreturned negative columns" as `ExactPricingComplete = true` stops pricing early and
/// converges on a suboptimal solution with no error anywhere. `Interrupted` and `ExactPricingComplete` are therefore two
/// separate fields.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Routes">找到的负 reduced-cost 路线 / Found negative reduced-cost routes.</param>
/// <param name="MinReducedCost">本次最小 reduced cost / Minimum reduced cost in this call.</param>
/// <param name="ExactPricingComplete">精确定价是否完成 / Whether exact pricing is complete.</param>
/// <param name="Interrupted">定价是否被资源上限中断 / Whether pricing was interrupted by a resource limit.</param>
public sealed record PricingResult<V>(
    IReadOnlyList<Route<V>> Routes,
    V MinReducedCost,
    bool ExactPricingComplete,
    bool Interrupted = false) where V : struct, IRealNumber<V> {
    /// <summary>找到的负 reduced-cost 路线，构造时复制 / The found routes, copied at construction.</summary>
    public IReadOnlyList<Route<V>> Routes { get; init; } = Routes.ToList();
}
