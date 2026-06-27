#nullable enable

using System;

using System.Collections.Generic;

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service;

/// <summary>
/// MAC 优化管线列表生成器。Generates the pipeline of MAC optimization constraints for longitudinal balance, lateral balance, and stabilizers.
/// </summary>
internal sealed class PipelineListGenerator
{
    private readonly MacOptimizationAggregation _aggregation;

    /// <summary>
    /// 构造函数。Constructor.
    /// </summary>
    /// <param name="aggregation">MAC 优化聚合 / MAC optimization aggregation</param>
    public PipelineListGenerator(MacOptimizationAggregation aggregation)
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
        // TODO: Port from Kotlin mac_optimization PipelineListGenerator
        var pipelines = new List<Action<dynamic>>();

        // LongitudinalBalanceLimit
        // LateralBalanceLimit (conditional on wideBody)
        // HorizontalStabilizerLimit

        return new Utils.Functional.Ok<IReadOnlyList<Action<dynamic>>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>(pipelines);
    }
}
