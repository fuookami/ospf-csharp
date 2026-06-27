#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;

/// <summary>
/// 飞机上下文。Context for managing aircraft model data.
/// </summary>
public sealed class AircraftContext
{
    /// <summary>飞机聚合 / Aircraft aggregation</summary>
    public AircraftAggregation Aggregation { get; private set; } = null!;

    /// <summary>
    /// 从请求初始化飞机上下文。Initialize aircraft context from request.
    /// </summary>
    /// <param name="input">请求 DTO / Request DTO</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Init(RequestDTO input)
    {
        var result = Service.AggregationInitializer.Initialize(input);
        if (result is not Ok<AircraftAggregation, ErrorCode, Error<ErrorCode>> ok)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, "Failed to initialize aircraft aggregation");
        }
        Aggregation = ok.Value;
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
