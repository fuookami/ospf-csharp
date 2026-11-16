#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Algebra.Number;

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
        // **上游聚合可能为 null**（起 `StowageContext.Aggregation` 可空），也可能飞机侧尚未初始化。
        // 取非空会抛 `NullReferenceException`，而那是崩溃不是失败——调用方拿不到任何可报告的原因。
        // **The upstream aggregation may be null** (nullable since then) and the aircraft side may be uninitialised. Taking non-null throws a `NullReferenceException`, which is a
        // crash rather than a failure — the caller gets no reportable reason.
        if (aircraftContext.Aggregation is not { } aircraftAggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "飞机上下文尚未初始化 / the aircraft context is not initialised"));
        }
        if (stowageContext.Aggregation is not { } stowageAggregation)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "配载上下文尚未初始化 / the stowage context is not initialised"));
        }

        var result = AggregationInitializer.Initialize(
            aircraftAggregation,
            stowageAggregation,
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
    public Try Register(StowageMode stowageMode, Parameter parameter, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(model);

        // 与基线一致：**先注册聚合的符号，再跑管线**。顺序反了会让管线找不到 `DivideEmptyLoading` 的中间符号。
        // As in the baseline: **register the aggregation's symbols, then run the pipelines**. The reverse order leaves the pipelines unable to find
        // `DivideEmptyLoading`'s intermediate symbols.
        Try registered = Aggregation.Register(stowageMode, model);
        if (!registered.IsOk)
        {
            return registered;
        }

        Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
            new Service.PipelineListGenerator(Aggregation).Invoke(stowageMode, parameter);
        if (generated is Failed<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> pipelines =
            ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)generated).Value!;

        foreach (Func<LinearMetaModel<Flt64>, Try> pipeline in pipelines)
        {
            Try applied = pipeline(model);
            if (!applied.IsOk)
            {
                return applied;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册 Benders 主问题约束。Register constraints for Benders master problem.
    /// </summary>
    public Try RegisterForBendersMP(LinearMetaModel<Flt64> model)
    {
        return Register(StowageMode.FullLoad, new Parameter(), model);
    }

    /// <summary>
    /// 注册 Benders 子问题约束。Register constraints for Benders sub problem.
    /// </summary>
    public Try RegisterForBendersSP(LinearMetaModel<Flt64> model)
    {
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 刷新 Benders 子问题。Flush Benders sub problem.
    /// </summary>
    public Try FlushForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<Flt64> solution)
    {
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
