#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model;

/// <summary>
/// 枚举预定义的飞行员职级类别及其职级编号。
/// Enumerates the predefined pilot rank classes with their rank numbers.
/// </summary>
public enum PilotRankClass {
    /// <summary>机长 / Captain.</summary>
    Captain,
    /// <summary>副驾驶指挥 / Second in command.</summary>
    SecondInCommand,
    /// <summary>巡航机长 / Cruise captain.</summary>
    CruiseCaptain,
    /// <summary>副驾驶 / First officer.</summary>
    FirstOfficer,
    /// <summary>学员机长 / Student pilot in command.</summary>
    StudentPilotInCommand,
    /// <summary>飞行监控员 / Pilot monitor.</summary>
    PilotMonitor,
    /// <summary>飞行观察员 / Pilot observer.</summary>
    PilotObserver
}

/// <summary>
/// 飞行员职级类别到编号的映射辅助类。
/// Helper class for pilot rank class to rank number mapping.
/// </summary>
public static class PilotRankClassExtensions {
    /// <summary>
    /// 获取职级类别的编号。
    /// Gets the rank number for the rank class.
    /// </summary>
    /// <param name="cls">职级类别 / The rank class.</param>
    /// <returns>职级编号 / The rank number.</returns>
    public static PilotRankNo ToRankNo(this PilotRankClass cls) => cls switch {
        PilotRankClass.Captain => new PilotRankNo("A001"),
        PilotRankClass.SecondInCommand => new PilotRankNo("A002"),
        PilotRankClass.CruiseCaptain => new PilotRankNo("B001"),
        PilotRankClass.FirstOfficer => new PilotRankNo("C001"),
        PilotRankClass.StudentPilotInCommand => new PilotRankNo("J001"),
        PilotRankClass.PilotMonitor => new PilotRankNo("F001"),
        PilotRankClass.PilotObserver => new PilotRankNo("K001"),
        _ => throw new ArgumentOutOfRangeException(nameof(cls))
    };
}

/// <summary>
/// 具有可选类别、编号、名称和池化实例管理的飞行员职级。
/// A pilot rank with optional class, number, name, and pooled instance management.
/// </summary>
/// <param name="No">职级编号 / Rank number.</param>
/// <param name="Name">名称 / Name.</param>
/// <param name="Cls">职级类别 / Rank class.</param>
/// <param name="DisplayName">显示名称 / Display name.</param>
public sealed record PilotRank(
    PilotRankNo No,
    string Name,
    PilotRankClass? Cls = null,
    string? DisplayName = null
) {
    private static readonly Dictionary<PilotRankNo, PilotRank> Pool = new();

    /// <summary>所有已注册的职级 / All registered ranks.</summary>
    public static IReadOnlyCollection<PilotRank> Values => Pool.Values;

    /// <summary>
    /// 通过类别从池中获取职级。
    /// Retrieves a PilotRank by class from the pool.
    /// </summary>
    /// <param name="cls">职级类别 / Rank class.</param>
    /// <returns>职级，若不存在则为 null / The rank, or null if not found.</returns>
    public static PilotRank? Find(PilotRankClass cls)
        => Pool.TryGetValue(cls.ToRankNo(), out var rank) ? rank : null;

    /// <summary>
    /// 通过职级编号从池中获取职级。
    /// Retrieves a PilotRank by rank number from the pool.
    /// </summary>
    /// <param name="no">职级编号 / Rank number.</param>
    /// <returns>职级，若不存在则为 null / The rank, or null if not found.</returns>
    public static PilotRank? Find(PilotRankNo no)
        => Pool.TryGetValue(no, out var rank) ? rank : null;

    /// <summary>
    /// 注册职级到池中。
    /// Registers a rank into the pool.
    /// </summary>
    /// <param name="rank">职级 / The rank.</param>
    public static void Register(PilotRank rank) => Pool[rank.No] = rank;

    /// <inheritdoc/>
    public override string ToString() => DisplayName ?? Name;
}
