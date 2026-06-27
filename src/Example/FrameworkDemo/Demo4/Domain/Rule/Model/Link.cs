#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;

/// <summary>
/// 表示两个连续航班任务之间具有分割成本的链接的基类。
/// Base class representing a link between two consecutive flight tasks with a split cost.
/// </summary>
public abstract class Link {
    /// <summary>构造函数 / Constructor.</summary>
    protected Link(string type, FlightTask prevTask, FlightTask succTask, Fuookami.Ospf.Math.Algebra.Number.Flt64 splitCost) {
        Type = type;
        PrevTask = prevTask;
        SuccTask = succTask;
        SplitCost = splitCost;
    }

    /// <summary>链接类型 / Link type.</summary>
    public string Type { get; }

    /// <summary>前驱任务 / Previous task.</summary>
    public FlightTask PrevTask { get; }

    /// <summary>后继任务 / Successor task.</summary>
    public FlightTask SuccTask { get; }

    /// <summary>分割成本 / Split cost.</summary>
    public Fuookami.Ospf.Math.Algebra.Number.Flt64 SplitCost { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Type}_{PrevTask}_{SuccTask}";
}

/// <summary>
/// 两个未恢复航段之间的连接链接。
/// A connecting link between two unrecovered flight legs.
/// </summary>
public sealed class ConnectingLink : Link {
    /// <summary>构造函数 / Constructor.</summary>
    public ConnectingLink(FlightTask prevTask, FlightTask succTask, Fuookami.Ospf.Math.Algebra.Number.Flt64 splitCost)
        : base("connecting", prevTask, succTask, splitCost) { }

    /// <inheritdoc/>
    public override int GetHashCode() => PrevTask.GetHashCode() ^ SuccTask.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is ConnectingLink other && PrevTask == other.PrevTask && SuccTask == other.SuccTask;
}

/// <summary>
/// 具有计算连接时间的两个未恢复航段之间的经停链接。
/// A stopover link between two unrecovered flight legs with computed connection time.
/// </summary>
public sealed class StopoverLink : Link {
    /// <summary>构造函数 / Constructor.</summary>
    public StopoverLink(FlightTask prevTask, FlightTask succTask, Fuookami.Ospf.Math.Algebra.Number.Flt64 splitCost)
        : base("stopover", prevTask, succTask, splitCost) {
        ConnectionTime = succTask.ScheduledTime!.Start - prevTask.ScheduledTime!.End;
    }

    /// <summary>连接时间 / Connection time.</summary>
    public TimeSpan ConnectionTime { get; }

    /// <inheritdoc/>
    public override int GetHashCode() => PrevTask.GetHashCode() ^ SuccTask.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is StopoverLink other && PrevTask == other.PrevTask && SuccTask == other.SuccTask;
}

/// <summary>
/// 忽略连接时间约束的两个未恢复航段之间的链接。
/// A link between two unrecovered flight legs that ignores connection time constraints.
/// </summary>
public sealed class ConnectionTimeIgnoringLink : Link {
    /// <summary>构造函数 / Constructor.</summary>
    public ConnectionTimeIgnoringLink(FlightTask prevTask, FlightTask succTask, Fuookami.Ospf.Math.Algebra.Number.Flt64 splitCost)
        : base("connection_time_ignoring", prevTask, succTask, splitCost) { }

    /// <inheritdoc/>
    public override int GetHashCode() => PrevTask.GetHashCode() ^ SuccTask.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is ConnectionTimeIgnoringLink other && PrevTask == other.PrevTask && SuccTask == other.SuccTask;
}

/// <summary>
/// 提供按前驱和后继任务查找的所有链接类型映射。
/// A map of all link types providing lookup by predecessor and successor tasks.
/// </summary>
public sealed class LinkMap(
    IReadOnlyList<ConnectingLink> ConnectingLinks,
    IReadOnlyList<StopoverLink> StopoverLinks,
    IReadOnlyList<ConnectionTimeIgnoringLink> ConnectionTimeIgnoringLinks
) {
    /// <summary>所有链接 / All links.</summary>
    public IReadOnlyList<Link> Links { get; } = BuildLinks(ConnectingLinks, StopoverLinks, ConnectionTimeIgnoringLinks);

    /// <summary>
    /// 返回给定任务作为前驱的所有链接。
    /// Returns all links where the given task is the predecessor.
    /// </summary>
    public IReadOnlyList<Link> LinksAfter(FlightTask task)
        => LeftMapper.TryGetValue(task, out var links) ? links : Array.Empty<Link>();

    /// <summary>
    /// 返回给定任务作为后继的所有链接。
    /// Returns all links where the given task is the successor.
    /// </summary>
    public IReadOnlyList<Link> LinksBefore(FlightTask task)
        => RightMapper.TryGetValue(task, out var links) ? links : Array.Empty<Link>();

    private IReadOnlyDictionary<FlightTask, IReadOnlyList<Link>> LeftMapper { get; } = BuildMapper(ConnectingLinks, StopoverLinks, ConnectionTimeIgnoringLinks, left: true);
    private IReadOnlyDictionary<FlightTask, IReadOnlyList<Link>> RightMapper { get; } = BuildMapper(ConnectingLinks, StopoverLinks, ConnectionTimeIgnoringLinks, left: false);

    private static IReadOnlyList<Link> BuildLinks(
        IReadOnlyList<ConnectingLink> a, IReadOnlyList<StopoverLink> b, IReadOnlyList<ConnectionTimeIgnoringLink> c) {
        var result = new List<Link>(a.Count + b.Count + c.Count);
        result.AddRange(a); result.AddRange(b); result.AddRange(c);
        return result;
    }

    private static IReadOnlyDictionary<FlightTask, IReadOnlyList<Link>> BuildMapper(
        IReadOnlyList<ConnectingLink> a, IReadOnlyList<StopoverLink> b, IReadOnlyList<ConnectionTimeIgnoringLink> c, bool left) {
        var dict = new Dictionary<FlightTask, List<Link>>();
        void Add(Link l) {
            var key = left ? l.PrevTask : l.SuccTask;
            if (!dict.TryGetValue(key, out var list)) { list = new(); dict[key] = list; }
            list.Add(l);
        }
        foreach (var l in a) Add(l);
        foreach (var l in b) Add(l);
        foreach (var l in c) Add(l);
        var result = new Dictionary<FlightTask, IReadOnlyList<Link>>();
        foreach (var kv in dict) result[kv.Key] = kv.Value;
        return result;
    }
}
