// 路线列池 / Route column pool.
// 与固定 Kotlin 基线 `model/RouteColumnPool.kt`（65 行）对应。
// Mirrors the pinned baseline's `RouteColumnPool.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;

/// <summary>
/// 路线列池 / Route column pool.
/// </summary>
/// <remarks>
/// 按 <see cref="Route{V}.Signature"/>（车辆类型 + 有序节点 + 稳定弧标识）去重。
/// **重复变量会破坏整数性判断**：同一个签名进了两次池，主问题里就有两个描述同一物理路线的变量，LP 松弛会把解拆到两者之间，
/// 取整之后得到一条既不完整也不可行的路线，而求解器本身不会报错。
/// Deduplicates by <see cref="Route{V}.Signature"/> (vehicle type + ordered nodes + stable arc identities).
/// **Duplicate variables break integrality reasoning**: the same signature entering the pool twice puts two variables describing
/// one physical route into the master, the LP relaxation splits the solution between them, and rounding yields a route that is
/// neither complete nor feasible — with nothing reported by the solver.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class RouteColumnPool<V> where V : struct, IRealNumber<V> {
    private readonly List<Route<V>> _routes = new();
    private readonly HashSet<string> _signatures = new(StringComparer.Ordinal);

    /// <summary>当前池中路线快照 / A snapshot of the routes currently in the pool.</summary>
    public IReadOnlyList<Route<V>> Routes => _routes.ToList();

    /// <summary>当前池中路线数量 / The number of routes currently in the pool.</summary>
    public int Size => _routes.Count;

    /// <summary>
    /// 添加路线列，去重 / Add route columns with deduplication.
    /// </summary>
    /// <param name="newRoutes">待添加路线 / Routes to add.</param>
    /// <returns>去重后实际添加的路线 / The routes actually added after deduplication.</returns>
    public IReadOnlyList<Route<V>> AddColumns(IReadOnlyList<Route<V>> newRoutes) {
        ArgumentNullException.ThrowIfNull(newRoutes);
        var added = new List<Route<V>>();
        foreach (Route<V> route in newRoutes) {
            if (_signatures.Add(route.Signature)) {
                _routes.Add(route);
                added.Add(route);
            }
        }
        return added.ToList();
    }

    /// <summary>
    /// 移除指定路线 / Remove the specified routes.
    /// </summary>
    /// <param name="routesToRemove">待移除路线 / Routes to remove.</param>
    public void RemoveColumns(IReadOnlyCollection<Route<V>> routesToRemove) {
        ArgumentNullException.ThrowIfNull(routesToRemove);
        var signatures = routesToRemove.Select(route => route.Signature).ToHashSet(StringComparer.Ordinal);
        _routes.RemoveAll(route => signatures.Contains(route.Signature));
        _signatures.ExceptWith(signatures);
    }

    /// <summary>
    /// 检查路线签名是否已存在 / Check whether a route signature is already present.
    /// </summary>
    /// <param name="signature">路线签名 / Route signature.</param>
    /// <returns>是否存在 / Whether it is present.</returns>
    public bool Contains(string signature) {
        ArgumentNullException.ThrowIfNull(signature);
        return _signatures.Contains(signature);
    }

    /// <summary>
    /// 过滤与分支遮罩兼容的路线 / Filter routes compatible with a branch mask.
    /// </summary>
    /// <param name="predicate">兼容性谓词 / The compatibility predicate.</param>
    /// <returns>兼容路线 / The compatible routes.</returns>
    public IReadOnlyList<Route<V>> FilterCompatible(Func<Route<V>, bool> predicate) {
        ArgumentNullException.ThrowIfNull(predicate);
        return _routes.Where(predicate).ToList();
    }
}
