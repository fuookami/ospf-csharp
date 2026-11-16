#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.Service;

/// <summary>
/// 业载最大化管线列表生成器 / The payload-maximization pipeline list generator.
/// </summary>
/// <remarks>
/// **恒返回一条**管线，**不看装载模式**。
///
/// 三处必须沿用：
///
/// 1. **没有 `withPayloadMaximization` 判断**。软安全与重心优化都有"模式关了就不加"的守卫，业载最大化**没有**——
///    它只在建议打板模式下被上层调用，所以生成器自己不做判断。照着别的子域加一个守卫会让预配载模式少一条目标。
/// 2. **恒一条**，没有条件分支。写成"有业载才加"会漏掉"业载为 0 也要最大化"的情形。
/// 3. **系数来自 `parameter.weightRecommendationPayload`**，由生成器注入，不是硬编码。
///
/// **always one pipeline**, **ignoring the stowage mode**. Three things must be copied: **there is no `withPayloadMaximization` check** — soft security and
/// MAC optimisation both guard on the mode, payload maximization does **not**, because the caller only invokes it in weight-recommendation mode, so the generator itself does not
/// test; adding a guard for symmetry would drop an objective in predistribution. **Always one**, no conditional branch — "add only when payload is positive" would miss
/// "maximise even a zero payload". And **the coefficient comes from `parameter.weightRecommendationPayload`**, injected rather than hard-coded.
/// </remarks>
internal sealed class PipelineListGenerator
{
    private readonly PayloadMaximizationAggregation _aggregation;

    /// <summary>
    /// 构造生成器 / Construct the generator.
    /// </summary>
    /// <param name="aggregation">业载最大化聚合 / The payload-maximization aggregation.</param>
    public PipelineListGenerator(PayloadMaximizationAggregation aggregation)
    {
        ArgumentNullException.ThrowIfNull(aggregation);
        _aggregation = aggregation;
    }

    /// <summary>
    /// 生成管线列表 / Generate the pipeline list.
    /// </summary>
    /// <param name="stowageMode">配载模式 / The stowage mode.</param>
    /// <param name="parameter">参数 / The parameter.</param>
    /// <returns>管线列表 / The pipeline list.</returns>
    public Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> Invoke(
        StowageMode stowageMode,
        Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        Flt64 coefficient = parameter.WeightRecommendationPayload;
        var pipelines = new List<Func<LinearMetaModel<Flt64>, Try>> {
            new MaxPayloadLimit(_aggregation.Payload, () => coefficient).Invoke,
        };
        return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(pipelines);
    }
}
