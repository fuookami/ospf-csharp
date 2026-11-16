#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service;

/// <summary>
/// 装载效能管线生成器 / The loading-effectiveness pipeline generator.
/// </summary>
/// <remarks>
/// 与基线 `loading_effectiveness/service/PipelineListGenerator.kt` 对齐：**十二条管线的条件与顺序**，这里已全部落地。
///
/// 顺序（基线原样）：提前装载、预留 → 复重（条件） → 同来源相邻、同目的地相邻 → 建议数量、建议重量（条件） →
/// 顺序反转（条件） → 拖车换装、拖车绕行（条件） → 优先级顺序（无条件） → 同来源早装（条件）。
///
/// 四条加成条件最容易写错：
///
/// | 管线 | 条件 |
/// | --- | --- |
/// | 提前装载 / 预留 / 建议数量 / 建议重量 / 顺序反转 | **仅预配载** |
/// | 拖车换装 / 拖车绕行 | **仅全载** |
/// | 同来源相邻 / 同目的地相邻 / 优先级顺序 | **无条件** |
/// | 同来源早装 | **非预配载且来源映射非空** |
///
/// Aligned with the baseline: the **conditions and order of all twelve pipelines**, fully landed. The order is the baseline's own. Four gating conditions are easy to get wrong:
/// **ahead-load, reserve, both advice objectives and order reversal are predistribution-only**; **both trailer objectives are full-load-only**; **both adjacencies plus priority order are
/// unconditional**; and **source-early needs "not predistribution and a non-empty source map"**.
/// </remarks>
internal sealed class PipelineListGenerator
{
    private readonly LoadingEffectivenessAggregation _aggregation;

    /// <summary>
    /// 构造生成器 / Construct the generator.
    /// </summary>
    /// <param name="aggregation">聚合 / The aggregation.</param>
    public PipelineListGenerator(LoadingEffectivenessAggregation aggregation)
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

        var pipelines = new List<Func<LinearMetaModel<Flt64>, Try>>();

        // 1、2：**仅预配载**。
        // 1, 2: **predistribution only**.
        if (stowageMode == StowageMode.Predistribution)
        {
            pipelines.Add(new ItemAheadLoadLimit(
                _aggregation.Items, _aggregation.Stowage, _ => Flt64.One).Invoke);
            pipelines.Add(new ItemReserveLimit(
                _aggregation.Items, _aggregation.Stowage, _ => Flt64.One).Invoke);
        }

        // 3：`flight.reweighNeeded` 为真时才加。基线那个站点集合是空的，所以实际永不触发。
        // 3: only when `flight.reweighNeeded`. That station set is empty in the baseline, so it never fires in practice.
        if (_aggregation.Flight is { } flight && flight.ReweighNeeded(stowageMode))
        {
            pipelines.Add(new ItemReweighNeededLimit(
                _aggregation.Items, _aggregation.Stowage, _ => Flt64.One).Invoke);
        }

        // 4、5：相邻同来源 / 同目的地，**无条件**。
        // 4, 5: adjacency, **unconditional**.
        if (_aggregation.TransferAdjacentLoading is { } adjacency)
        {
            pipelines.Add(new SameSourceAdjacentLimit(
                _aggregation.AdjacentPositions, _aggregation.Sources, adjacency,
                (_, _, _) => parameter.SameFlowTransferIn).Invoke);
            pipelines.Add(new SameDestinationAdjacent(
                _aggregation.AdjacentPositions, _aggregation.Destinations, adjacency,
                (_, _, _) => parameter.SameFlowTransferOut).Invoke);
        }

        // 6、7：建议数量 / 建议重量，**仅预配载**。
        // 6, 7: advice objectives, **predistribution only**.
        if (_aggregation.AdviceLoading is { } advice)
        {
            pipelines.Add(new AdviceLoadAmountLimit(
                _aggregation.Positions, advice, _ => parameter.AdviceLoadAmount).Invoke);
            pipelines.Add(new AdviceLoadWeightLimit(
                _aggregation.Positions, advice, _ => parameter.AdviceLoadWeight).Invoke);
        }

        // 8：顺序反转，**仅预配载**。
        // 8: order reversal, **predistribution only**.
        if (_aggregation.SequentialLoading is { } sequential)
        {
            pipelines.Add(new ItemOrderReverseLimit(
                sequential, (_, _) => parameter.ItemOrder).Invoke);
        }

        // 9、10：拖车换装 / 绕行，**仅全载**。
        // 9, 10: trailer objectives, **full load only**.
        if (_aggregation.TrailerLoading is { } trailers)
        {
            pipelines.Add(new TrailerChangeLimit(
                trailers, (_, _) => parameter.TrailerChange).Invoke);
            pipelines.Add(new TrailerCirclingLimit(
                trailers, (_, _) => parameter.TrailerCircling).Invoke);
        }

        // 11：优先级顺序，**无条件**。
        // 11: priority order, **unconditional**.
        pipelines.Add(new PriorityOrderLimit(
            _aggregation.Items, _aggregation.Positions, _aggregation.Stowage, _aggregation.BigM).Invoke);

        // 12：同来源早装，**非预配载且来源映射非空**。
        // 12: source-early, **not predistribution and a non-empty source map**.
        if (stowageMode != StowageMode.Predistribution && _aggregation.CargosBySource.Count > 0)
        {
            pipelines.Add(new SourceEarlyLimit(
                _aggregation.Items, _aggregation.Positions, _aggregation.Stowage,
                _aggregation.CargosBySource, _aggregation.EarlyEnd).Invoke);
        }

        return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(pipelines);
    }
}
