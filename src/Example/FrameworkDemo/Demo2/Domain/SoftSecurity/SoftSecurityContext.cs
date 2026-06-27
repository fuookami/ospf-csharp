#nullable enable

using System.Collections.Generic;

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity;

/// <summary>
/// 软性安全上下文。Context for managing soft security constraints across aircraft and stowage domains.
/// </summary>
public sealed class SoftSecurityContext
{
    /// <summary>软性安全聚合 / Soft security aggregation</summary>
    public SoftSecurityAggregation Aggregation { get; private set; } = null!;

    /// <summary>
    /// 从飞机和配载上下文初始化软性安全上下文。Initialize soft security context from aircraft and stowage contexts.
    /// </summary>
    /// <param name="aircraftContext">飞机上下文 / Aircraft context</param>
    /// <param name="stowageContext">配载上下文 / Stowage context</param>
    /// <param name="input">请求 DTO / Request DTO</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Init(AircraftContext aircraftContext, StowageContext stowageContext, RequestDTO input)
    {
        // TODO: Port from Kotlin SoftSecurityContext
        var result = AggregationInitializer.Initialize(
            aircraftContext.Aggregation,
            stowageContext.Aggregation,
            input);
        if (result is Failed<SoftSecurityAggregation, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        Aggregation = ((Ok<SoftSecurityAggregation, ErrorCode, Error<ErrorCode>>)result).Value!;

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册软性安全约束到模型。Register soft security constraints with the model.
    /// </summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="parameter">参数 / Parameter</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Register(StowageMode stowageMode, Parameter parameter, dynamic model)
    {
        // TODO: Port from Kotlin SoftSecurityContext.register
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册 Benders 主问题约束。Register constraints for Benders master problem.
    /// </summary>
    public Try RegisterForBendersMP(dynamic model)
    {
        return Register(StowageMode.FullLoad, new Parameter(), model);
    }

    /// <summary>
    /// 注册 Benders 子问题约束。Register constraints for Benders sub problem.
    /// </summary>
    public Try RegisterForBendersSP(dynamic model)
    {
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 刷新 Benders 子问题。Flush Benders sub problem.
    /// </summary>
    public Try FlushForBendersSP(dynamic model, IReadOnlyList<double> solution)
    {
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
