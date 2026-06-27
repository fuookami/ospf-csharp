#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task;

/// <summary>
/// 航班恢复调度演示中航班任务域操作的上下文。
/// Context for flight task domain operations in the flight recovery scheduling demo.
/// </summary>
public sealed class FlightTaskContext {
    /// <summary>聚合 / Aggregation.</summary>
    public required Aggregation Aggregation { get; set; }
}
