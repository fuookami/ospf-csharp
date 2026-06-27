#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;

/// <summary>
/// 表示阻止航班任务修改的约束的锁定模型。
/// Lock model representing constraints that prevent flight task modifications.
/// </summary>
/// <param name="LockedTasks">锁定的任务映射 / Locked tasks mapping.</param>
public sealed class Lock(IReadOnlyDictionary<FlightTask, DateTimeOffset>? LockedTasks = null) {
    private readonly IReadOnlyDictionary<FlightTask, DateTimeOffset> _lockedTasks = LockedTasks ?? new Dictionary<FlightTask, DateTimeOffset>();

    /// <summary>
    /// 返回给定航班任务的锁定时间，如果未锁定则返回 null。
    /// Returns the locked time for the given flight task, or null if not locked.
    /// </summary>
    public DateTimeOffset? LockedTime(FlightTask flightTask)
        => _lockedTasks.TryGetValue(flightTask, out var time) ? time : null;
}
