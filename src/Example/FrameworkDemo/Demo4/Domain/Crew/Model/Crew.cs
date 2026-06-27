#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model;

/// <summary>
/// 枚举航班恢复系统中的机组成员类型。
/// Enumerates the types of crew members in the flight recovery system.
/// </summary>
public enum CrewType {
    /// <summary>操作员 / Operator.</summary>
    Operator,
    /// <summary>乘务员 / Attendant.</summary>
    Attendant,
    /// <summary>其他 / Other.</summary>
    Other
}

/// <summary>
/// 表示具有身份和国籍信息的机组成员的接口。
/// Interface representing a crew member with identity and nationality information.
/// </summary>
public interface ICrewMember {
    /// <summary>类型 / Type.</summary>
    CrewType Type { get; }

    /// <summary>工号 / Worker number.</summary>
    WorkerNo? WorkerNo { get; }

    /// <summary>姓名 / Name.</summary>
    string Name { get; }

    /// <summary>显示名称 / Display name.</summary>
    string? DisplayName { get; }

    /// <summary>国籍 / Nationality.</summary>
    string Nationality { get; }
}

/// <summary>
/// 作为飞行员的机组成员（将身份字段委托给底层 Pilot）。
/// A crew member who is a pilot, delegating identity fields to the underlying Pilot.
/// </summary>
/// <param name="Type">类型 / Type.</param>
/// <param name="Rank">职级 / Rank.</param>
/// <param name="Pilot">飞行员 / Pilot.</param>
public sealed record CrewPilotMember(
    CrewType Type,
    PilotRank Rank,
    Pilot Pilot
) : ICrewMember {
    /// <inheritdoc/>
    public WorkerNo? WorkerNo => Pilot.WorkerNo;

    /// <inheritdoc/>
    public string Name => Pilot.Name;

    /// <inheritdoc/>
    public string? DisplayName => Pilot.DisplayName;

    /// <inheritdoc/>
    public string Nationality => Pilot.Nationality;

    /// <inheritdoc/>
    public override string ToString() => $"{Rank}_{Pilot}";
}

/// <summary>
/// 非飞行员的机组成员（将身份字段委托给底层 CrewMan）。
/// A crew member who is not a pilot, delegating identity fields to the underlying CrewMan.
/// </summary>
/// <param name="Type">类型 / Type.</param>
/// <param name="Rank">职级 / Rank.</param>
/// <param name="CrewMan">机组人员 / Crew man.</param>
public sealed record CrewNotPilotMember(
    CrewType Type,
    CrewManRank Rank,
    CrewMan CrewMan
) : ICrewMember {
    /// <inheritdoc/>
    public WorkerNo? WorkerNo => CrewMan.WorkerNo;

    /// <inheritdoc/>
    public string Name => CrewMan.Name;

    /// <inheritdoc/>
    public string? DisplayName => CrewMan.DisplayName;

    /// <inheritdoc/>
    public string Nationality => CrewMan.Nationality;

    /// <inheritdoc/>
    public override string ToString() => $"{Rank}_{CrewMan}";
}

/// <summary>
/// 分配给航班任务的机组（由飞行员和非飞行员成员组成）。
/// A crew assigned to a flight task, composed of pilot and non-pilot members.
/// </summary>
/// <param name="Flight">航班任务 / Flight task.</param>
/// <param name="Members">成员列表 / Members.</param>
public sealed record Crew(
    FlightTask Flight,
    IReadOnlyList<ICrewMember> Members
) {
    /// <summary>按职级分组的飞行员成员 / Pilot members grouped by rank.</summary>
    public IReadOnlyDictionary<PilotRank, IReadOnlyList<Pilot>> PilotMembers => Members
        .OfType<CrewPilotMember>()
        .GroupBy(m => m.Rank)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<Pilot>)g.Select(m => m.Pilot).ToArray());

    /// <summary>按职级分组的非飞行员成员 / Non-pilot members grouped by rank.</summary>
    public IReadOnlyDictionary<CrewManRank, IReadOnlyList<CrewMan>> NotPilotMembers => Members
        .OfType<CrewNotPilotMember>()
        .GroupBy(m => m.Rank)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<CrewMan>)g.Select(m => m.CrewMan).ToArray());
}
