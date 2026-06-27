#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 配载方案。Stowage solution with cargo-position assignments.
/// </summary>
public sealed class Solution
{
    /// <summary>货物分配 / Item assignments</summary>
    public IReadOnlyList<ItemAssignment> Assignments { get; }

    public Solution(IReadOnlyList<ItemAssignment> assignments)
    {
        Assignments = assignments;
    }
}

/// <summary>
/// 货物分配。Item assignment to a position.
/// </summary>
/// <param name="Item">货物 / Item</param>
/// <param name="Position">位置 / Position</param>
/// <param name="Stowed">是否已配载 / Whether stowed</param>
public sealed record ItemAssignment(Item Item, StowagePosition Position, bool Stowed);
