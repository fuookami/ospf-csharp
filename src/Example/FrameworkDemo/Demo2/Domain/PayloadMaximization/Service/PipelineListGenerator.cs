#nullable enable

using System;

using System.Collections.Generic;

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.Service;

/// <summary>
/// 业载最大化管线列表生成器。Generates the pipeline of payload maximization objective for model construction.
/// </summary>
internal sealed class PipelineListGenerator
{
    private readonly PayloadMaximizationAggregation _aggregation;

    /// <summary>
    /// 构造函数。Constructor.
    /// </summary>
    /// <param name="aggregation">业载最大化聚合 / Payload maximization aggregation</param>
    public PipelineListGenerator(PayloadMaximizationAggregation aggregation)
    {
        _aggregation = aggregation;
    }

    /// <summary>
    /// 生成管线列表。Generate pipeline list.
    /// </summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="parameter">参数 / Parameter</param>
    /// <returns>管线列表 / Pipeline list</returns>
    public Utils.Functional.Result<IReadOnlyList<Action<dynamic>>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Invoke(
        StowageMode stowageMode, Parameter parameter)
    {
        // TODO: Port from Kotlin payload_maximization PipelineListGenerator
        var pipelines = new List<Action<dynamic>>();

        // MaxPayloadLimit

        return new Utils.Functional.Ok<IReadOnlyList<Action<dynamic>>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>(pipelines);
    }
}
