#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Service;

/// <summary>
/// MAC 聚合初始化器。MAC aggregation initializer (stub for P1).
/// </summary>
internal static class AggregationInitializer
{
    /// <summary>
    /// 从飞机和配载聚合初始化 MAC 聚合。Initialize MAC aggregation from aircraft and stowage aggregations.
    /// </summary>
    public static Result<MacAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        AircraftAggregation aircraftAggregation,
        StowageAggregation stowageAggregation,
        RequestDTO input)
    {
        // TODO: Port from Kotlin MAC AggregationInitializer
        return new Failed<MacAggregation, ErrorCode, Error<ErrorCode>>(
            ErrorCode.ApplicationFailed, "MAC AggregationInitializer not yet ported");
    }
}
