#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// FullLoad 和 Predistribution 模式的通用域管线上下文。
/// Common domain pipeline context for FullLoad and Predistribution modes.
/// Encapsulates the 3-domain init/register chain to reduce duplication.
/// </summary>
public sealed class DomainPipelineContext
{
    /// <summary>飞机上下文 / Aircraft context</summary>
    public AircraftContext AircraftContext { get; } = new();
    /// <summary>配载上下文 / Stowage context</summary>
    public StowageContext StowageContext { get; } = new();
    /// <summary>MAC 上下文 / MAC context</summary>
    public MacContext MacContext { get; } = new();

    /// <summary>
    /// 初始化所有域上下文。Initialize all domain contexts from request.
    /// </summary>
    /// <param name="request">请求 DTO / Request DTO</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Init(RequestDTO request)
    {
        Try r1 = AircraftContext.Init(request);
        if (r1.IsFailed) return r1;

        Try r2 = StowageContext.Init(AircraftContext, request);
        if (r2.IsFailed) return r2;

        Try r3 = MacContext.Init(AircraftContext, StowageContext, request);
        if (r3.IsFailed) return r3;

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
