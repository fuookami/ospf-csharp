#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

/// <summary>
/// 装载顺序应用。Loading order application: generates loading order from aircraft context.
/// Port of Kotlin LoadingOrderAlgorithm / LoadingOrderAlgorithmImpl.
/// </summary>
public sealed class LoadingOrderApplication
{
    private readonly AircraftContext _aircraftContext = new();

    /// <summary>
    /// 执行装载顺序算法。Execute the loading order algorithm.
    /// </summary>
    /// <param name="request">请求 DTO / Request DTO</param>
    /// <returns>装载顺序响应 DTO / Loading order response DTO</returns>
    public LoadingOrderResponseDTO Execute(RequestDTO request)
    {
        // 1. Initialize aircraft context
        var r1 = _aircraftContext.Init(request);
        if (r1 is Failed<Utils.Functional.Success, ErrorCode, Error<ErrorCode>> failed)
        {
            return new LoadingOrderResponseDTO(Succeed: false, Status: "InitFailed",
                Notes: new[] { failed.Error.Message });
        }

        // 2. Export loading orders (stub)
        // TODO: Port aircraftContext.exportLoadingOrders from Kotlin
        var orders = new List<string>();

        return LoadingOrderResponseDTO.Success(orders);
    }
}
