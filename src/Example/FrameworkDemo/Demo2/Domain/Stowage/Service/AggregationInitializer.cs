#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;

/// <summary>
/// 配载聚合初始化器。Stowage aggregation initializer (stub for P1).
/// </summary>
internal static class AggregationInitializer
{
    /// <summary>
    /// 从飞机聚合和请求初始化配载聚合。Initialize stowage aggregation from aircraft aggregation and request.
    /// </summary>
    public static Result<StowageAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        AircraftAggregation aircraftAggregation, RequestDTO input)
    {
        // TODO: Port from Kotlin Stowage AggregationInitializer
        return new Failed<StowageAggregation, ErrorCode, Error<ErrorCode>>(
            ErrorCode.ApplicationFailed, "Stowage AggregationInitializer not yet ported");
    }
}
