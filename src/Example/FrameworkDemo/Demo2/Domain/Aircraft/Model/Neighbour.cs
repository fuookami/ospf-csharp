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

    /// <summary>转成字符串 / Render as a string.</summary>
    /// <returns>位置对文本 / The pair text.</returns>
    /// <remarks>
    /// **必须显式重写**。`record` 的编译器生成 `PrintMembers` 会打印**全部公开可读属性**，包括 `Symmetrical`——而它又是
    /// `PositionPair`，于是 `ToString` 无限递归，实测抛 `InsufficientExecutionStackException`（不是 `StackOverflowException`，
    /// 因为 .NET 的 record 打印会先调用 `EnsureSufficientExecutionStack`）。只要有任何断言失败并尝试打印这一对，测试进程就崩。
    ///
    /// **This override is mandatory.** A `record`'s compiler-generated `PrintMembers` prints **every public readable property**, including `Symmetrical`, which is itself a
    /// `PositionPair` — so `ToString` recurses forever and throws `InsufficientExecutionStackException` (not `StackOverflowException`, since .NET's record printing calls
    /// `EnsureSufficientExecutionStack` first). Any assertion that fails and tries to print a pair crashes the test process.
    /// </remarks>
    public override string ToString() => "(" + First.SpaceName + ", " + Second.SpaceName + ")";
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

    /// <summary>转成字符串 / Render as a string.</summary>
    /// <returns>邻接文本 / The neighbour text.</returns>
    /// <remarks>
    /// 同 <see cref="PositionPair"/>：`record` 的 `PrintMembers` 会打印 `Symmetrical`，而它是 `Neighbour` 本身，不重写就无限递归。
    ///
    /// As with <see cref="PositionPair"/>: the record's `PrintMembers` prints `Symmetrical`, which is a `Neighbour`, so without this override it recurses forever.
    /// </remarks>
    public override string ToString() => Type + " " + Pair;
}
