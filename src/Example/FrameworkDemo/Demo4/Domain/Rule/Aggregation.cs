#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule;

/// <summary>
/// 航班恢复调度演示的规则域对象聚合。
/// Aggregation of rule domain objects for the flight recovery scheduling demo.
/// </summary>
public sealed class Aggregation {
}

/// <summary>
/// 航班恢复调度演示中规则域操作的上下文。
/// Context for rule domain operations in the flight recovery scheduling demo.
/// </summary>
public sealed class RuleContext {
    /// <summary>聚合 / Aggregation.</summary>
    public required Aggregation Aggregation { get; set; }
}
