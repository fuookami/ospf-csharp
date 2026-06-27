#nullable enable

using System.Collections.Generic;

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;

/// <summary>
/// 适航安全上下文。Context for managing airworthiness and safety constraints across aircraft, stowage, and MAC domains.
/// </summary>
public sealed class AirworthinessSecurityContext
{
    /// <summary>适航安全聚合 / Airworthiness security aggregation</summary>
    public AirworthinessSecurityAggregation Aggregation { get; private set; } = null!;

    /// <summary>
    /// 从飞机、配载和 MAC 上下文初始化适航安全上下文。Initialize airworthiness security context from aircraft, stowage, and MAC contexts.
    /// </summary>
    /// <param name="aircraftContext">飞机上下文 / Aircraft context</param>
    /// <param name="stowageContext">配载上下文 / Stowage context</param>
    /// <param name="macContext">MAC 上下文 / MAC context</param>
    /// <param name="input">请求 DTO / Request DTO</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Init(AircraftContext aircraftContext, StowageContext stowageContext, MacContext macContext, RequestDTO input)
    {
        // TODO: Port from Kotlin AirworthinessSecurityContext
        var result = AggregationInitializer.Initialize(
            aircraftContext.Aggregation,
            stowageContext.Aggregation,
            macContext.Aggregation,
            input);
        if (result is Failed<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        Aggregation = ((Ok<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>>)result).Value!;

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册适航安全约束到模型。Register airworthiness security constraints with the model.
    /// </summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Register(StowageMode stowageMode, dynamic model)
    {
        // TODO: Port from Kotlin AirworthinessSecurityContext.register
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册 Benders 主问题约束。Register constraints for Benders master problem.
    /// </summary>
    public Try RegisterForBendersMP(dynamic model)
    {
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册 Benders 子问题约束。Register constraints for Benders sub problem.
    /// </summary>
    public Try RegisterForBendersSP(dynamic model)
    {
        return Register(StowageMode.FullLoad, model);
    }

    /// <summary>
    /// 刷新 Benders 子问题。Flush Benders sub problem.
    /// </summary>
    public Try FlushForBendersSP(dynamic model, IReadOnlyList<double> solution)
    {
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
