#nullable enable

using System;

using System.Collections.Generic;

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service;

/// <summary>
/// 软性安全管线列表生成器。Generates the pipeline of soft security constraints based on the stowage mode.
/// </summary>
internal sealed class PipelineListGenerator
{
    private readonly SoftSecurityAggregation _aggregation;

    /// <summary>
    /// 构造函数。Constructor.
    /// </summary>
    /// <param name="aggregation">软性安全聚合 / Soft security aggregation</param>
    public PipelineListGenerator(SoftSecurityAggregation aggregation)
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
        // TODO: Port from Kotlin soft_security PipelineListGenerator
        var pipelines = new List<Action<dynamic>>();

        // AdviceBallastWeightLimit (conditional on ballast)
        // EmptyHatedLimit (conditional on aircraft type and item count)
        // MainDeckDoorEmptyLimit (conditional on mainDeckDoorEmptyPrefer)
        // DivideEmptyLoadingLimit

        return new Utils.Functional.Ok<IReadOnlyList<Action<dynamic>>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>(pipelines);
    }
}
