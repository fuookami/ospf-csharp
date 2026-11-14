#nullable enable

using System;
using System.Collections.Generic;

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness;

/// <summary>
/// 货物时效上下文 / The express-effectiveness context.
/// </summary>
/// <remarks>
/// 与基线 `express_effectiveness/ExpressEffectivenessContext.kt` 对齐：**初始化幂等**，**`Init` 收 `stowageMode`**。
///
/// 与其它子域不同，这里 `Init` 必须知道装载模式——聚合按模式二选一地建子模型，而模式是请求的属性，不是注册时的参数。
///
/// Aligned with the baseline: **initialisation is idempotent** and **`Init` takes the `stowageMode`**. Unlike the other sub-domains, `Init` must know the mode here, because the
/// aggregation builds one of two sub-models by mode, and the mode is a property of the request rather than a registration-time argument.
/// </remarks>
public sealed class ExpressEffectivenessContext
{
    /// <summary>聚合 / The aggregation</summary>
    public ExpressEffectivenessAggregation Aggregation { get; private set; } = null!;

    /// <summary>
    /// 初始化 / Initialize.
    /// </summary>
    /// <param name="stowageContext">配载上下文 / The stowage context.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Init(StowageContext stowageContext, RequestDTO input, StowageMode stowageMode)
    {
        ArgumentNullException.ThrowIfNull(stowageContext);

        if (Aggregation is not null)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        Result<ExpressEffectivenessAggregation, ErrorCode, Error<ErrorCode>> initialized =
            Service.AggregationInitializer.Initialize(stowageContext.Aggregation, input, stowageMode);
        if (initialized is Failed<ExpressEffectivenessAggregation, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        Aggregation = ((Ok<ExpressEffectivenessAggregation, ErrorCode, Error<ErrorCode>>)initialized).Value!;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册到模型 / Register with the model.
    /// </summary>
    /// <param name="stowageMode">配载模式 / The stowage mode.</param>
    /// <param name="parameter">参数 / The parameter.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Register(StowageMode stowageMode, Parameter parameter, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(model);
        if (Aggregation is null)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "货物时效上下文尚未初始化 / the express-effectiveness context is not initialised"));
        }

        // **必须先走聚合的 `Register`**：聚合的 `Register` 才建出松弛符号等子模型，而管线表里有一半是"读这些子模型"的。
        // 跳过它，模型不会报错——管线会在读到空属性时以"未注册"失败，**而失败点指向了错的那个上下文**。
        // **Go through the aggregation's `Register` first**: that is what builds the slack symbols and the rest of the sub-models, and half the pipeline list reads them.
        // Skipping it raises nothing in the model — the pipelines fail on an empty property with "not registered", **pointing at the wrong context**.
        Try registered = Aggregation.Register(stowageMode, model);
        if (!registered.IsOk)
        {
            return registered;
        }

        Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
            new PipelineListGenerator(Aggregation).Invoke(stowageMode, parameter);
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
    /// 注册 Benders 主问题约束 / Register the Benders master-problem constraints.
    /// </summary>
    /// <param name="stowageMode">配载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try RegisterForBendersMP(StowageMode stowageMode, LinearMetaModel<Flt64> model) =>
        Register(stowageMode, new Parameter(), model);

    /// <summary>
    /// 注册 Benders 子问题约束 / Register the Benders sub-problem constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try RegisterForBendersSP(LinearMetaModel<Flt64> model) =>
        Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>
    /// 刷新 Benders 子问题 / Flush the Benders sub-problem.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="solution">子问题解 / The sub-problem solution.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try FlushForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<Flt64> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
