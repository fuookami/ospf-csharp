#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model;

/// <summary>
/// 机组人员实体的接口。
/// Interface for crew man entities.
/// </summary>
public interface IAbstractCrewMan {
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
/// 通过工号标识的非飞行员机组成员（具有池化实例管理）。
/// A non-pilot crew member identified by worker number, with pooled instance management.
/// </summary>
/// <param name="WorkerNo">工号 / Worker number.</param>
/// <param name="Name">姓名 / Name.</param>
/// <param name="Nationality">国籍 / Nationality.</param>
/// <param name="DisplayName">显示名称 / Display name.</param>
public sealed record CrewMan(
    WorkerNo WorkerNo,
    string Name,
    string Nationality,
    string? DisplayName = null
) : IAbstractCrewMan {
    private static readonly Dictionary<WorkerNo, CrewMan> Pool = new();

    /// <summary>所有已注册的机组成员 / All registered crew members.</summary>
    public static IReadOnlyCollection<CrewMan> Values => Pool.Values;

    WorkerNo? IAbstractCrewMan.WorkerNo => WorkerNo;

    /// <summary>
    /// 通过工号从池中获取机组成员。
    /// Retrieves a CrewMan by worker number from the pool.
    /// </summary>
    /// <param name="workerNo">工号 / Worker number.</param>
    /// <returns>机组成员，若不存在则为 null / The crew man, or null if not found.</returns>
    public static CrewMan? Find(WorkerNo workerNo)
        => Pool.TryGetValue(workerNo, out var crewMan) ? crewMan : null;

    /// <summary>
    /// 注册机组成员到池中。
    /// Registers a crew man into the pool.
    /// </summary>
    /// <param name="crewMan">机组成员 / The crew man.</param>
    public static void Register(CrewMan crewMan) => Pool[crewMan.WorkerNo] = crewMan;

    /// <inheritdoc/>
    public override string ToString() => DisplayName ?? Name;
}
