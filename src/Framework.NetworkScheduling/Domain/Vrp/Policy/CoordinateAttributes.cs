// VRP 坐标约定 / VRP coordinate convention.
// 与固定 Kotlin 基线 `CoordinateAttributes.kt`（25 行）对应。
// Mirrors the pinned baseline's `CoordinateAttributes.kt`.
//
// 通用网络节点**不定义坐标语义**：节点的 `Attributes` 只是一张通用的物理量字典。VRP 在此之上约定 `x`／`y`／`z` 三个属性
// 表示坐标轴，把"坐标"这件事收在领域层而不是污染基础设施层。
// Generic network nodes **carry no coordinate semantics**: a node's `Attributes` is just a generic quantity dictionary. VRP
// reserves `x` / `y` / `z` on top of it for axes, keeping "coordinates" in the domain layer rather than polluting
// infrastructure.

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;

/// <summary>
/// 读取 VRP 约定的坐标属性 / Read coordinates using the VRP attribute convention.
/// </summary>
public static class CoordinateAttributes {
    /// <summary>约定的坐标轴名称，按此顺序读取 / Reserved axis names, read in this order.</summary>
    private static readonly string[] CoordinateKeys = ["x", "y", "z"];

    /// <summary>
    /// 读取 VRP 坐标 / Read the VRP coordinates.
    /// </summary>
    /// <remarks>
    /// **顺序固定为 `x`／`y`／`z`**，而不是字典的自然顺序：坐标是位置向量，顺序错乱会让点积、距离与朝向全部算错。缺失的轴
    /// 会被跳过，因此二维点返回两个分量、三维点返回三个——调用方按数量判断维度，不必传维度参数。
    /// **The order is fixed to `x` / `y` / `z`**, not the dictionary's natural order: coordinates are a position vector, and a
    /// shuffled order breaks the dot product, distance and orientation. Missing axes are skipped, so a 2-D point yields two
    /// components and a 3-D point three — callers infer dimensionality from the count instead of passing it.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="node">网络节点 / Network node.</param>
    /// <returns>坐标分量 / The coordinate components.</returns>
    public static IReadOnlyList<Quantity<V>> VrpCoordinates<V>(this NetworkNode<V> node)
        where V : struct, IRealNumber<V> =>
        node.VrpCoordinateAttributes().Select(pair => pair.Value).ToList();

    /// <summary>
    /// 读取带轴名称的 VRP 坐标快照 / Read a VRP coordinate snapshot retaining axis names.
    /// </summary>
    /// <remarks>
    /// 保留轴名让调用方能区分"缺了 z"和"z 为零"——两者在几何上完全不同，只返回数值列表时无法分辨。
    /// Keeping axis names lets callers tell "no z" from "z is zero" — geometrically different cases that a bare value list
    /// cannot distinguish.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="node">网络节点 / Network node.</param>
    /// <returns>轴名与分量的有序快照 / Ordered axis-name and component pairs.</returns>
    public static IReadOnlyList<(string Axis, Quantity<V> Value)> VrpCoordinateAttributes<V>(
        this NetworkNode<V> node) where V : struct, IRealNumber<V> {
        var result = new List<(string Axis, Quantity<V> Value)>();
        foreach (string key in CoordinateKeys) {
            if (node.Attributes.TryGetValue(key, out Quantity<V>? value)) {
                result.Add((key, value));
            }
        }
        return result;
    }
}
