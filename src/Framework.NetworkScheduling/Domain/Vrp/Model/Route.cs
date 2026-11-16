// VRPTW 路线与解 / VRPTW route and solution.
// 与固定 Kotlin 、（19 行）对应。
// And.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;

/// <summary>
/// 单车 elementary 路线 / Elementary single-vehicle route.
/// </summary>
/// <remarks>
/// "elementary" 指每个客户至多出现一次。路线同时保存有序停靠点与有序弧标识，因为**同一节点序列可以对应不同的物理路线**
/// （平行弧），定价需要把它们当作不同的列。
/// "Elementary" means each customer appears at most once. The route keeps ordered stops and ordered arc identities side by
/// side because **one node sequence can correspond to different physical routes** (parallel arcs), and pricing must treat
/// them as distinct columns.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class Route<V> where V : struct, IRealNumber<V> {
    private readonly List<RouteStop<V>> _stops;
    private readonly List<NetworkArcId> _arcIds;

    private Route(
        VehicleTypeId vehicleTypeId,
        List<RouteStop<V>> stops,
        List<NetworkArcId> arcIds,
        Quantity<V> distance,
        Quantity<V> cost) {
        VehicleTypeId = vehicleTypeId;
        _stops = stops;
        _arcIds = arcIds;
        Distance = distance;
        Cost = cost;
        Signature = BuildSignature(vehicleTypeId, stops, arcIds);
    }

    /// <summary>车辆类型 / Vehicle type.</summary>
    public VehicleTypeId VehicleTypeId { get; }

    /// <summary>有序停靠点 / Ordered stops.</summary>
    public IReadOnlyList<RouteStop<V>> Stops => _stops;

    /// <summary>有序独立弧标识 / Ordered independent arc identities.</summary>
    public IReadOnlyList<NetworkArcId> ArcIds => _arcIds;

    /// <summary>总距离 / Total distance.</summary>
    public Quantity<V> Distance { get; }

    /// <summary>总成本 / Total cost.</summary>
    public Quantity<V> Cost { get; }

    /// <summary>
    /// 路线去重签名 / Route deduplication signature.
    /// </summary>
    /// <remarks>
    /// 签名同时编码**有序节点与稳定弧标识**，且每段都带长度前缀。只编码节点会让同端点的平行弧合并成同一列，丢掉
    /// "两条物理路线" 的区别；不带长度前缀则会让 `("ab","c")` 与 `("a","bc")` 拼出同一个签名，把不同路线误判为重复。
    /// The signature encodes **ordered nodes and stable arc identities**, each length-prefixed. Encoding nodes alone would merge
    /// parallel arcs between the same endpoints into one column and lose the "two physical routes" distinction; omitting the
    /// prefixes would let `("ab","c")` and `("a","bc")` produce the same signature and misjudge distinct routes as duplicates.
    /// </remarks>
    public string Signature { get; }

    /// <summary>
    /// 创建路线值对象 / Create a route value object.
    /// </summary>
    /// <remarks>
    /// 四条校验：至少两个停靠点 → 弧标识数量与内容有效 → 距离是非负长度量 → 成本非负。
    /// **距离必须校验量纲**：距离要参与路径累加与三角不等式，用错了量纲单位的路线会让"哪条路更短"的比较整体失效，而这种
    /// 错误不会抛异常，只会给出一个错误的最优解。
    /// Four checks: at least two stops → arc-identity count and values valid → distance is a non-negative length quantity →
    /// cost non-negative. **Distance must have its dimension checked**: it takes part in accumulation and the triangle
    /// inequality, and a route with the wrong dimension silently invalidates every "which route is shorter" comparison
    /// without throwing — it just yields a wrong optimum.
    /// </remarks>
    /// <param name="vehicleTypeId">车辆类型 / Vehicle type.</param>
    /// <param name="stops">有序停靠点 / Ordered stops.</param>
    /// <param name="distance">总距离 / Total distance.</param>
    /// <param name="cost">总成本 / Total cost.</param>
    /// <param name="arcIds">有序独立弧标识，省略时按端点派生兼容标识 / Ordered arc identities; defaults to endpoint-derived ones.</param>
    /// <returns>路线或基础校验失败 / The route, or a basic validation failure.</returns>
    public static Result<Route<V>, ErrorCode, Error<ErrorCode>> Invoke(
        VehicleTypeId vehicleTypeId,
        IReadOnlyList<RouteStop<V>> stops,
        Quantity<V> distance,
        Quantity<V> cost,
        IReadOnlyList<NetworkArcId>? arcIds = null) {
        ArgumentNullException.ThrowIfNull(vehicleTypeId);
        ArgumentNullException.ThrowIfNull(stops);
        ArgumentNullException.ThrowIfNull(distance);
        ArgumentNullException.ThrowIfNull(cost);

        List<NetworkArcId> resolvedArcIds = arcIds is null
            ? DeriveArcIds(stops)
            : arcIds.ToList();

        if (stops.Count < 2) {
            return NetworkSchedulingErrors.Failure<Route<V>>(
                "创建路线失败：路线至少包含起止仓库 / Failed to create route: start and end depots are required");
        }
        if (resolvedArcIds.Count != stops.Count - 1
            || resolvedArcIds.Any(arcId => string.IsNullOrWhiteSpace(arcId.Value))) {
            return NetworkSchedulingErrors.Failure<Route<V>>(
                "创建路线失败：弧 ID 数量或内容无效 / Failed to create route: invalid arc-ID count or value");
        }
        if (distance.Unit.Quantity != Dimensions.Length
            || distance.Value.CompareTo(distance.Value.Constants.Zero) < 0) {
            return NetworkSchedulingErrors.Failure<Route<V>>(
                "创建路线失败：总距离必须是非负长度量 / Failed to create route: total distance must be a non-negative "
                + "length quantity");
        }
        if (cost.Value.CompareTo(cost.Value.Constants.Zero) < 0) {
            return NetworkSchedulingErrors.Failure<Route<V>>(
                "创建路线失败：总成本不能为负 / Failed to create route: total cost cannot be negative");
        }
        return Results.Ok(new Route<V>(
            vehicleTypeId, stops.ToList(), resolvedArcIds, distance, cost));
    }

    /// <summary>按端点派生默认弧标识 / Derive default arc identities from endpoints.</summary>
    /// <param name="stops">有序停靠点 / Ordered stops.</param>
    /// <returns>派生标识 / The derived identities.</returns>
    private static List<NetworkArcId> DeriveArcIds(IReadOnlyList<RouteStop<V>> stops) {
        var derived = new List<NetworkArcId>();
        for (int position = 0; position + 1 < stops.Count; position++) {
            derived.Add(ResourceArcs.DefaultId(stops[position].NodeId, stops[position + 1].NodeId));
        }
        return derived;
    }

    /// <summary>构造去重签名 / Build the deduplication signature.</summary>
    /// <param name="vehicleTypeId">车辆类型 / Vehicle type.</param>
    /// <param name="stops">有序停靠点 / Ordered stops.</param>
    /// <param name="arcIds">有序弧标识 / Ordered arc identities.</param>
    /// <returns>签名 / The signature.</returns>
    private static string BuildSignature(
        VehicleTypeId vehicleTypeId,
        IReadOnlyList<RouteStop<V>> stops,
        IReadOnlyList<NetworkArcId> arcIds) {
        var builder = new StringBuilder();
        builder.Append(vehicleTypeId.Value.Length).Append(':').Append(vehicleTypeId.Value);
        foreach (RouteStop<V> stop in stops) {
            builder.Append("|n").Append(stop.NodeId.Value.Length).Append(':').Append(stop.NodeId.Value);
        }
        foreach (NetworkArcId arcId in arcIds) {
            builder.Append("|a").Append(arcId.Value.Length).Append(':').Append(arcId.Value);
        }
        return builder.ToString();
    }
}

/// <summary>
/// 已校验路线集合及其汇总指标 / Validated route set and aggregate metrics.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Routes">路线快照 / Route snapshot.</param>
/// <param name="TotalDistance">总距离 / Total distance.</param>
/// <param name="TotalCost">总成本 / Total cost.</param>
public sealed class VrptwSolution<V>(
    IReadOnlyList<Route<V>> Routes,
    Quantity<V> TotalDistance,
    Quantity<V> TotalCost) where V : struct, IRealNumber<V> {
    /// <summary>路线快照 / Route snapshot.</summary>
    public IReadOnlyList<Route<V>> Routes { get; } = Routes.ToList();

    /// <summary>总距离 / Total distance.</summary>
    public Quantity<V> TotalDistance { get; } = TotalDistance;

    /// <summary>总成本 / Total cost.</summary>
    public Quantity<V> TotalCost { get; } = TotalCost;
}
