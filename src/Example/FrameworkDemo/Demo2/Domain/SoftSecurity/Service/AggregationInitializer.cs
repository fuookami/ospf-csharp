#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service;

/// <summary>
/// 软性安全聚合初始化器。Initializes the soft security aggregation from aircraft and stowage contexts.
/// </summary>
internal static class AggregationInitializer
{
    /// <summary>
    /// 从飞机和配载聚合初始化软性安全聚合。Initialize soft security aggregation from aircraft and stowage aggregations.
    /// </summary>
    /// <param name="aircraftAggregation">飞机聚合 / Aircraft aggregation</param>
    /// <param name="stowageAggregation">配载聚合 / Stowage aggregation</param>
    /// <param name="input">请求 DTO / Request DTO</param>
    /// <returns>聚合结果 / Aggregation result</returns>
    public static Result<SoftSecurityAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        AircraftAggregation aircraftAggregation,
        StowageAggregation stowageAggregation,
        RequestDTO input)
    {
        // TODO: Port from Kotlin soft_security AggregationInitializer
        return new Ok<SoftSecurityAggregation, ErrorCode, Error<ErrorCode>>(
            new SoftSecurityAggregation());
    }
}
