#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// 业务目标参数。Business objective parameters for all optimization contexts.
/// </summary>
public sealed record Parameter(
    // 重心优化上下文 / MAC optimization context
    Flt64 MacRangeC = default,
    Flt64 LongitudinalBalance = default,
    Flt64 B737LongitudinalBalance = default,
    Flt64 LateralBalance = default,
    Flt64 HorizontalStabilizerWarn = default,
    // 软性安全上下文 / Soft security context
    Flt64 BallastWeight = default,
    Flt64 EmptyHated = default,
    Flt64 BesideDoorMainPosition = default,
    Flt64 DividedEmpty = default,
    // 装卸效率上下文 / Loading effectiveness context
    Flt64 AdviceLoadAmount = default,
    Flt64 AdviceLoadWeight = default,
    Flt64 SameFlowTransferIn = default,
    Flt64 SameFlowTransferOut = default,
    Flt64 ItemOrder = default,
    Flt64 TrailerChange = default,
    Flt64 TrailerCircling = default,
    // 货物时效上下文 / Express effectiveness context
    Flt64 Priority = default,
    Flt64 PriorityCategory = default,
    // 余度上下文 / Redundancy context
    Flt64 ExperimentalLongitudinalBalance = default,
    Flt64 RedundancyRange = default);
