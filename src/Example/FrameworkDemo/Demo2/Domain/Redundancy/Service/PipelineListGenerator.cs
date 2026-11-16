#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Service;

/// <summary>
/// 冗余管线生成器 / The redundancy pipeline generator.
/// </summary>
/// <remarks>
/// **只在预配载给管线，其余模式给空表**。
///
/// 两点必须沿用：
///
/// 1. **`stowageMode != Predistribution` 直接返回空表**——不是"少加几条"，是一条都不加。冗余只在预配载阶段有意义。
/// 2. **顺序是纵向平衡在前、冗余在后**，与`pipelines.add` 顺序一致；与 `Aggregation.Register` 的注册顺序**相反**，
///    这种不一致是既有设计。
///
/// **pipelines exist only under predistribution**, with an empty list otherwise. Two things must be copied: **anything but predistribution returns an empty
/// list** — not "fewer pipelines" but none, since redundancy only matters during predistribution; and **the balance comes before redundancy**, matching its `pipelines.add`
/// order, which is the **reverse** of `Aggregation.Register`'s order — an inconsistency kept as is.
/// </remarks>
internal sealed class PipelineListGenerator
{
    private readonly RedundancyAggregation _aggregation;

    /// <summary>
    /// 构造生成器 / Construct the generator.
    /// </summary>
    /// <param name="aggregation">聚合 / The aggregation.</param>
    public PipelineListGenerator(RedundancyAggregation aggregation)
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

        // **非预配载一律空表。**
        // **Anything but predistribution yields an empty list.**
        if (stowageMode != StowageMode.Predistribution)
        {
            return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(
                Array.Empty<Func<LinearMetaModel<Flt64>, Try>>());
        }

        var pipelines = new List<Func<LinearMetaModel<Flt64>, Try>>(2)
        {
            new ExperimentalLongitudinalBalanceLimit(
                _aggregation.ExperimentalLongitudinalBalance,
                () => parameter.ExperimentalLongitudinalBalance).Invoke,
            new RedundancyLimit(
                _aggregation.Redundancy,
                () => parameter.RedundancyRange).Invoke,
        };
        return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(pipelines);
    }
}
