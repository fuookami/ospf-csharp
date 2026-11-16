#nullable enable

using System;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Service;

/// <summary>
/// 冗余聚合初始化器 / The redundancy aggregation initializer.
/// </summary>
/// <remarks>
/// **无校验**，全部字段直取配载聚合。
///
/// 这里一条校验都没有（不像装载效能那边有六条拖车校验），所以本仓也不加。加校验会让合法请求被拒，
/// 而条路径从来不会拒。
///
/// **no validation**, every field taken straight from the stowage aggregation. The implementation performs no check here (unlike the six trailer validations in
/// loading effectiveness), so this implementation adds none — adding one would reject requests the implementation never rejects.
/// </remarks>
internal static class AggregationInitializer
{
    /// <summary>
    /// 初始化聚合 / Initialize the aggregation.
    /// </summary>
    /// <param name="stowageAggregation">配载聚合 / The stowage aggregation.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public static Result<RedundancyAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        StowageAggregation? stowageAggregation,
        RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (stowageAggregation is null)
        {
            return Results.Failed<RedundancyAggregation>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "配载聚合未初始化 / the stowage aggregation is not initialised"));
        }

        return Results.Ok<RedundancyAggregation>(new RedundancyAggregation(
            items: stowageAggregation.Items,
            positions: stowageAggregation.Positions,
            load: stowageAggregation.Load));
    }
}
