#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 装载顺序。Loading order with precedence and succession constraints.
/// </summary>
public sealed class LoadingOrder
{
    /// <summary>甲板位置 / Deck location</summary>
    public DeckLocation Location { get; }
    /// <summary>顺序号 / Order number</summary>
    public byte Order { get; }
    /// <summary>直接前驱集 / Direct predecessor set</summary>
    public IReadOnlySet<Position> DirectPrec { get; }
    /// <summary>直接后继集 / Direct successor set</summary>
    public IReadOnlySet<Position> DirectSucc { get; }

    public LoadingOrder(
        DeckLocation location,
        byte order,
        IReadOnlySet<Position> directPrec,
        IReadOnlySet<Position> directSucc)
    {
        Location = location;
        Order = order;
        DirectPrec = directPrec;
        DirectSucc = directSucc;
    }

    // Note: PrecDepth, SuccDepth, Prec, Succ are lazy-computed in Kotlin
    // but require circular reference resolution. Stubs for now.
}
