#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 邻接类型。Neighbour type describing adjacency relationship.
/// </summary>
public enum NeighbourType
{
    /// <summary>物理邻接 / Physical adjacency</summary>
    Physics,
    /// <summary>间接物理邻接 / Indirect physical adjacency</summary>
    IndirectPhysics,
    /// <summary>线性装载顺序 / Linear loading order</summary>
    LinearLoadingOrder,
    /// <summary>拓扑装载顺序 / Topological loading order</summary>
    TopologicalLoadingOrder
}

/// <summary>
/// 邻接类型扩展方法。Extension methods for NeighbourType.
/// </summary>
public static class NeighbourTypeExtensions
{
    /// <summary>是否有序 / Whether ordered</summary>
    public static bool IsOrdered(this NeighbourType type) =>
        type == NeighbourType.LinearLoadingOrder || type == NeighbourType.TopologicalLoadingOrder;
}

/// <summary>
/// 位置对。Position pair for adjacency relationships.
/// </summary>
/// <param name="First">第一个位置 / First position</param>
/// <param name="Second">第二个位置 / Second position</param>
public sealed record PositionPair(Position First, Position Second)
{
    /// <summary>对称位置对 / Symmetrical position pair</summary>
    public PositionPair Symmetrical => new(Second, First);
}

/// <summary>
/// 邻接关系。Neighbour relationship between two positions.
/// </summary>
/// <param name="Type">邻接类型 / Neighbour type</param>
/// <param name="Pair">位置对 / Position pair</param>
public sealed record Neighbour(NeighbourType Type, PositionPair Pair)
{
    /// <summary>是否有序 / Whether ordered</summary>
    public bool Ordered => Type.IsOrdered();
    /// <summary>对称邻接关系 / Symmetrical neighbour</summary>
    public Neighbour Symmetrical => new(Type, Pair.Symmetrical);
}
