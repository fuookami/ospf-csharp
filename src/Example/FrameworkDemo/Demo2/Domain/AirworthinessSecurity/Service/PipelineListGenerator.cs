#nullable enable

using System;

using System.Collections.Generic;

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service;

/// <summary>
/// 适航安全管线列表生成器。Generates the pipeline of airworthiness security constraints based on the stowage mode.
/// </summary>
internal sealed class PipelineListGenerator
{
    private readonly AirworthinessSecurityAggregation _aggregation;

    /// <summary>
    /// 构造函数。Constructor.
    /// </summary>
    /// <param name="aggregation">适航安全聚合 / Airworthiness security aggregation</param>
    public PipelineListGenerator(AirworthinessSecurityAggregation aggregation)
    {
        _aggregation = aggregation;
    }

    /// <summary>
    /// 生成管线列表。Generate pipeline list.
    /// </summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <returns>管线列表 / Pipeline list</returns>
    public Utils.Functional.Result<IReadOnlyList<Action<dynamic>>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Invoke(
        StowageMode stowageMode)
    {
        // TODO: Port from Kotlin airworthiness_security PipelineListGenerator
        var pipelines = new List<Action<dynamic>>();

        // LinearDensityLimit
        // UnsymmetricalLinearDensityLimit (conditional)
        // SurfaceDensityLimit
        // CumulativeLoadWeightLimit
        // ZoneLoadWeightLimit
        // BallastWeightLimit (conditional)
        // LowPayloadLimit
        // PayloadLimit
        // TotalWeightLimit
        // EnvelopeLimit
        // HorizontalStabilizerLimit
        // CLIMLimit (conditional)
        // AdjacentGapLimit (conditional)

        return new Utils.Functional.Ok<IReadOnlyList<Action<dynamic>>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>(pipelines);
    }
}
