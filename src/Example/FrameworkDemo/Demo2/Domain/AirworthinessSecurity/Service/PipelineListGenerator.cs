#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service;

/// <summary>
/// 一条适航安全约束管线 / One airworthiness security constraint pipeline.
/// </summary>
/// <param name="Name">管线名（用于顺序与去重判据）/ The pipeline name, used by the order and duplicate criteria.</param>
/// <param name="Invoke">执行 / Invoke.</param>
public sealed record SecurityPipeline(string Name, Func<LinearMetaModel<Flt64>, Try> Invoke);

/// <summary>
/// 按装载模式生成适航安全约束管线 / Generate the airworthiness security constraint pipeline by stowage mode.
/// </summary>
/// <remarks>
/// 逐条对齐的 **14 条**（11 条无条件 + 3 条条件）。顺序是基线顺序，
/// 报告按序号引用约束时依赖它；**必须沿用，不能按类型名排序**：
///
/// | # | 管线 | 条件 |
/// | --- | --- | --- |
/// | 1 | `linear_density_limit` | |
/// | 2 | `surface_density_limit` | |
/// | 3 | `cumulative_load_weight_limit` | |
/// | 4 | `zone_load_weight_limit` | |
/// | 5 | `ballast_weight_limit` | `Ballast != null` |
/// | 6 | `low_payload_limit` | |
/// | 7 | `payload_limit` | |
/// | 8 | 目标纵向力矩（名字由构造参数给） | |
/// | 9 | `lateral_imbalance_limit` | |
/// | 10 | `total_weight_limit` | |
/// | 11 | `envelope_limit` | |
/// | 12 | `horizontal_stabilizer_limit` | |
/// | 13 | `max_clim_limit` | `MaxCLIM != null` |
/// | 14 | `adjacent_gap_limit` | `MaxAdjacentLoadGap != null` |
///
/// 三处容易做错：
///
/// 1. **两个条件分支在中间**（第 5 与第 13/14），不是全堆在末尾。把条件项挪到末尾会改变 6—12 的序号。
/// 2. **条件看的是聚合字段是否为 `null`**，不是装载模式；装载模式只传给 `horizontal_stabilizer_limit`。
/// 3. **`UnsymmetricalLinearDensityLimit` 不在这个列表里**。类存在，但基线没有把它接进管线——沿用，别顺手加。
///
/// 's **fourteen** pipelines (eleven unconditional plus three conditional) in its order, tabulated above; reports that reference
/// constraints by index depend on it, so it is copied verbatim rather than sorted by type name. Three easy mistakes: (1) **the conditional branches sit in the middle**
/// (row 5 and rows 13/14), not at the end — moving them renumbers rows 6 to 12; (2) **the conditions test whether the aggregation's field is `null`**, not the stowage mode,
/// which only reaches `horizontal_stabilizer_limit`; (3) **`UnsymmetricalLinearDensityLimit` is not in this list** — the class exists but the code never wires it in, so
/// it is copied as-is rather than added.
/// </remarks>
internal sealed class PipelineListGenerator
{
    /// <summary>纵向力矩区间管线的名字 / The longitudinal moment pipeline's name.</summary>
    public const string LongitudinalMomentName = "target_longitudinal_moment_limit";

    private readonly AirworthinessSecurityAggregation _aggregation;

    /// <summary>
    /// 构造函数。Constructor.
    /// </summary>
    /// <param name="aggregation">适航安全聚合 / Airworthiness security aggregation</param>
    public PipelineListGenerator(AirworthinessSecurityAggregation aggregation) {
        ArgumentNullException.ThrowIfNull(aggregation);
        _aggregation = aggregation;
    }

    /// <summary>
    /// 生成管线列表。Generate pipeline list.
    /// </summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <returns>管线列表 / Pipeline list</returns>
    public Result<IReadOnlyList<SecurityPipeline>, ErrorCode, Error<ErrorCode>> Invoke(StowageMode stowageMode) {
        var pipelines = new List<SecurityPipeline>();

        LinearDensityLimit linearDensity = new(_aggregation.LinearDensity, _aggregation.Positions);
        pipelines.Add(new SecurityPipeline("linear_density_limit", linearDensity.Invoke));

        SurfaceDensityLimit surfaceDensity = new(_aggregation.SurfaceDensity, _aggregation.Positions);
        pipelines.Add(new SecurityPipeline("surface_density_limit", surfaceDensity.Invoke));

        CumulativeLoadWeightLimit cumulative = new(_aggregation.MaxCumulativeLoadWeight, _aggregation.Positions, _aggregation.Load);
        pipelines.Add(new SecurityPipeline("cumulative_load_weight_limit", cumulative.Invoke));

        ZoneLoadWeightLimit zone = new(_aggregation.MaxZoneLoadWeight, _aggregation.Positions, _aggregation.Load);
        pipelines.Add(new SecurityPipeline("zone_load_weight_limit", zone.Invoke));

        if (_aggregation.Ballast is { } ballast) {
            BallastWeightLimit ballastLimit = new(ballast);
            pipelines.Add(new SecurityPipeline("ballast_weight_limit", ballastLimit.Invoke));
        }

        LowPayloadLimit lowPayload = new(_aggregation.Payload, _aggregation.MinLowPayload);
        pipelines.Add(new SecurityPipeline("low_payload_limit", lowPayload.Invoke));

        PayloadLimit payload = new(_aggregation.Payload, _aggregation.MinPayload);
        pipelines.Add(new SecurityPipeline("payload_limit", payload.Invoke));

        // 区间取 `target ± deviation`，两侧都用**同一个** deviation——不是 `min + deviation` / `max − deviation`。
        // The interval is `target ± deviation` with the **same** deviation on both sides, not `min + deviation` / `max − deviation`.
        LongitudinalMomentLimit longitudinal = new(
            _aggregation.Torque,
            _aggregation.TargetLongitudinalMoment - _aggregation.MaxLongitudinalMomentDeviation,
            _aggregation.TargetLongitudinalMoment + _aggregation.MaxLongitudinalMomentDeviation,
            LongitudinalMomentName);
        pipelines.Add(new SecurityPipeline(LongitudinalMomentName, longitudinal.Invoke));

        LateralImbalanceLimit lateral = new(_aggregation.Torque, _aggregation.MaxLateralImbalance);
        pipelines.Add(new SecurityPipeline(lateral.Name, lateral.Invoke));

        TotalWeightLimit totalWeight = new(_aggregation.TotalWeight);
        pipelines.Add(new SecurityPipeline("total_weight_limit", totalWeight.Invoke));

        EnvelopeLimit envelope = new(_aggregation.Torque, _aggregation.Envelopes);
        pipelines.Add(new SecurityPipeline("envelope_limit", envelope.Invoke));

        HorizontalStabilizerLimit stabilizer = new(_aggregation.HorizontalStabilizers, stowageMode);
        pipelines.Add(new SecurityPipeline("horizontal_stabilizer_limit", stabilizer.Invoke));

        if (_aggregation.MaxCLIM is { } maxClim) {
            CLIMLimit clim = new(_aggregation.Torque, maxClim);
            pipelines.Add(new SecurityPipeline("max_clim_limit", clim.Invoke));
        }

        if (_aggregation.MaxAdjacentLoadGap is { } gap) {
            AdjacentGapLimit adjacent = new(_aggregation.Positions, _aggregation.Load, gap);
            pipelines.Add(new SecurityPipeline("adjacent_gap_limit", adjacent.Invoke));
        }

        return Results.Ok<IReadOnlyList<SecurityPipeline>>(pipelines);
    }
}
