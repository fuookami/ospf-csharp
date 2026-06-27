#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service;

/// <summary>
/// 适航安全聚合初始化器。Initializes the airworthiness security aggregation from aircraft, stowage, and MAC contexts.
/// </summary>
internal static class AggregationInitializer
{
    /// <summary>
    /// 从飞机、配载和 MAC 聚合初始化适航安全聚合。Initialize airworthiness security aggregation from aircraft, stowage, and MAC aggregations.
    /// </summary>
    /// <param name="aircraftAggregation">飞机聚合 / Aircraft aggregation</param>
    /// <param name="stowageAggregation">配载聚合 / Stowage aggregation</param>
    /// <param name="macAggregation">MAC 聚合 / MAC aggregation</param>
    /// <param name="input">请求 DTO / Request DTO</param>
    /// <returns>聚合结果 / Aggregation result</returns>
    public static Result<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        AircraftAggregation aircraftAggregation,
        StowageAggregation stowageAggregation,
        MacAggregation macAggregation,
        RequestDTO input)
    {
        // TODO: Port from Kotlin airworthiness_security AggregationInitializer
        return new Ok<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>>(
            new AirworthinessSecurityAggregation());
    }
}
