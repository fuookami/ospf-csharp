#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 配载模型。Stowage model with binary assignment variables and intermediate symbols.
/// </summary>
public sealed class StowageModel
{
    /// <summary>货物列表 / Items list</summary>
    public IReadOnlyList<Item> Items { get; }
    /// <summary>位置列表 / Positions list</summary>
    public IReadOnlyList<StowagePosition> Positions { get; }

    public StowageModel(IReadOnlyList<Item> items, IReadOnlyList<StowagePosition> positions)
    {
        Items = items;
        Positions = positions;
    }

    // TODO: Port register() from Kotlin Stowage class
    // Creates BinVariable2 x[i,j], BTerVariable2 u[i,j],
    // LinearIntermediateSymbols2 stowage[i,j], LinearIntermediateSymbols1 loaded[i]
}
