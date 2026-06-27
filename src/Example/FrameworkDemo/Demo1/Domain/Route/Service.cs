#nullable enable

using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Route;

/// <summary>
/// 可以通过网络路由的服务（具有容量限制和每次使用成本）。A service that can be routed through the network, with a capacity limit and per-use cost.
/// </summary>
public sealed class SspService {
    /// <summary>服务唯一标识符 / Unique service identifier</summary>
    public UInt64 Id { get; }
    /// <summary>在服务列表中的索引 / Index within the service list</summary>
    public int Index { get; }
    /// <summary>服务容量 / Service capacity</summary>
    public UInt64 Capacity { get; }
    /// <summary>服务成本 / Service cost</summary>
    public UInt64 Cost { get; }

    public SspService(UInt64 id, int index, UInt64 capacity, UInt64 cost) {
        Id = id;
        Index = index;
        Capacity = capacity;
        Cost = cost;
    }

    public override string ToString() => $"S{Id}";
}
