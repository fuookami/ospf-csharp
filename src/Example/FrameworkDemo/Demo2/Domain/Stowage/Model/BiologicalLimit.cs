#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 生物货物兼容性规则：哪些货物类型不能相邻、不能共享同一散货位置，或完全禁止散货装载。
/// Biological cargo compatibility rules: which cargo types may not be adjacent, may not share a bulk position, or are barred from bulk stowage.
/// </summary>
/// <remarks>
/// 移植基线 `BiologicalLimit.kt`。三张表都是**声明**而非推导结果：判定逻辑在约束构造方，
/// 这里只负责承载规则。
///
/// Ports the baseline's `BiologicalLimit.kt`. All three lists are **declarations, not derivations**: the judging logic lives in the constraint builder,
/// and this type only carries the rules.
/// </remarks>
public sealed class BiologicalLimit
{
    /// <summary>创建生物兼容性规则 / Create the biological compatibility rules.</summary>
    /// <param name="adjacentLimit">不能放在相邻位置的货物类型对 / Pairs of cargo types that must not be placed in adjacent positions.</param>
    /// <param name="bulkConflictLimit">不能共享同一散货位置的货物类型对 / Pairs of cargo types that must not share the same bulk position.</param>
    /// <param name="bulkLimit">被限制进行散货装载的货物类型 / Cargo types that are restricted from bulk stowage.</param>
    public BiologicalLimit(
        IReadOnlyList<(CargoType First, CargoType Second)> adjacentLimit,
        IReadOnlyList<(CargoType First, CargoType Second)> bulkConflictLimit,
        IReadOnlyList<CargoType> bulkLimit)
    {
        AdjacentLimit = adjacentLimit;
        BulkConflictLimit = bulkConflictLimit;
        BulkLimit = bulkLimit;
    }

    /// <summary>不能放在相邻位置的货物类型对 / Pairs of cargo types that must not be placed in adjacent positions.</summary>
    public IReadOnlyList<(CargoType First, CargoType Second)> AdjacentLimit { get; }

    /// <summary>不能共享同一散货位置的货物类型对 / Pairs of cargo types that must not share the same bulk position.</summary>
    public IReadOnlyList<(CargoType First, CargoType Second)> BulkConflictLimit { get; }

    /// <summary>被限制进行散货装载的货物类型 / Cargo types that are restricted from bulk stowage.</summary>
    public IReadOnlyList<CargoType> BulkLimit { get; }
}
