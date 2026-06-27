#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model;

/// <summary>
/// 枚举预定义的机组人员职级类别及其职级编号。
/// Enumerates the predefined crew man rank classes with their rank numbers.
/// </summary>
public enum CrewManRankClass {
    /// <summary>私人乘务员 / Private attendant.</summary>
    PrivateAttendant,
    /// <summary>乘务员 / Attendant.</summary>
    Attendant,
    /// <summary>维护员 / Maintainer.</summary>
    Maintainer,
    /// <summary>副维护员 / Co-maintainer.</summary>
    CoMaintainer
}

/// <summary>
/// 机组人员职级映射辅助类。
/// Helper class for crew man rank class to rank number mapping.
/// </summary>
public static class CrewManRankClassExtensions {
    /// <summary>
    /// 获取职级类别的编号。
    /// Gets the rank number for the rank class.
    /// </summary>
    /// <param name="cls">职级类别 / The rank class.</param>
    /// <returns>职级编号 / The rank number.</returns>
    public static CrewManRankNo ToRankNo(this CrewManRankClass cls) => cls switch {
        CrewManRankClass.PrivateAttendant => new CrewManRankNo("S002"),
        CrewManRankClass.Attendant => new CrewManRankNo("S001"),
        CrewManRankClass.Maintainer => new CrewManRankNo("M001"),
        CrewManRankClass.CoMaintainer => new CrewManRankNo("M002"),
        _ => throw new System.ArgumentOutOfRangeException(nameof(cls))
    };
}

/// <summary>
/// 具有可选类别、编号、名称和池化实例管理的机组人员职级。
/// A crew man rank with optional class, number, name, and pooled instance management.
/// </summary>
/// <param name="No">职级编号 / Rank number.</param>
/// <param name="Name">名称 / Name.</param>
/// <param name="Cls">职级类别 / Rank class.</param>
/// <param name="DisplayName">显示名称 / Display name.</param>
public sealed record CrewManRank(
    CrewManRankNo No,
    string Name,
    CrewManRankClass? Cls = null,
    string? DisplayName = null
) {
    private static readonly Dictionary<CrewManRankNo, CrewManRank> Pool = new();

    /// <summary>所有已注册的职级 / All registered ranks.</summary>
    public static IReadOnlyCollection<CrewManRank> Values => Pool.Values;

    /// <summary>
    /// 通过类别从池中获取职级。
    /// Retrieves a CrewManRank by class from the pool.
    /// </summary>
    /// <param name="cls">职级类别 / Rank class.</param>
    /// <returns>职级，若不存在则为 null / The rank, or null if not found.</returns>
    public static CrewManRank? Find(CrewManRankClass cls)
        => Pool.TryGetValue(cls.ToRankNo(), out var rank) ? rank : null;

    /// <summary>
    /// 通过职级编号从池中获取职级。
    /// Retrieves a CrewManRank by rank number from the pool.
    /// </summary>
    /// <param name="no">职级编号 / Rank number.</param>
    /// <returns>职级，若不存在则为 null / The rank, or null if not found.</returns>
    public static CrewManRank? Find(CrewManRankNo no)
        => Pool.TryGetValue(no, out var rank) ? rank : null;

    /// <summary>
    /// 注册职级到池中。
    /// Registers a rank into the pool.
    /// </summary>
    /// <param name="rank">职级 / The rank.</param>
    public static void Register(CrewManRank rank) => Pool[rank.No] = rank;

    /// <inheritdoc/>
    public override string ToString() => DisplayName ?? Name;
}
