#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model;

/// <summary>
/// 通过代码和工号标识的飞行员（具有池化实例管理）。
/// A pilot identified by code and worker number, with pooled instance management.
/// </summary>
/// <param name="Code">飞行员代码 / Pilot code.</param>
/// <param name="WorkerNo">工号 / Worker number.</param>
/// <param name="Name">姓名 / Name.</param>
/// <param name="Nationality">国籍 / Nationality.</param>
/// <param name="DisplayName">显示名称 / Display name.</param>
public sealed record Pilot(
    PilotCode Code,
    WorkerNo WorkerNo,
    string Name,
    string Nationality,
    string? DisplayName = null
) : IAbstractCrewMan {
    private static readonly Dictionary<PilotCode, Pilot> Pool = new();

    /// <summary>所有已注册的飞行员 / All registered pilots.</summary>
    public static IReadOnlyCollection<Pilot> Values => Pool.Values;

    WorkerNo? IAbstractCrewMan.WorkerNo => WorkerNo;

    /// <summary>
    /// 通过飞行员代码从池中获取飞行员。
    /// Retrieves a Pilot by pilot code from the pool.
    /// </summary>
    /// <param name="code">飞行员代码 / Pilot code.</param>
    /// <returns>飞行员，若不存在则为 null / The pilot, or null if not found.</returns>
    public static Pilot? Find(PilotCode code)
        => Pool.TryGetValue(code, out var pilot) ? pilot : null;

    /// <summary>
    /// 通过工号从池中获取飞行员。
    /// Retrieves a Pilot by worker number from the pool.
    /// </summary>
    /// <param name="workerNo">工号 / Worker number.</param>
    /// <returns>飞行员，若不存在则为 null / The pilot, or null if not found.</returns>
    public static Pilot? Find(WorkerNo workerNo)
        => Pool.Values.FirstOrDefault(p => p.WorkerNo == workerNo);

    /// <summary>
    /// 注册飞行员到池中。
    /// Registers a pilot into the pool.
    /// </summary>
    /// <param name="pilot">飞行员 / The pilot.</param>
    public static void Register(Pilot pilot) => Pool[pilot.Code] = pilot;

    /// <inheritdoc/>
    public override string ToString() => DisplayName ?? Name;
}
