#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service;

/// <summary>
/// 货物时效管线生成器 / The express-effectiveness pipeline generator.
/// </summary>
/// <remarks>
/// **三条条件管线**，且**没有总的模式开关**。
///
/// 四处必须沿用：
///
/// 1. **`AbsoluteOrder` 非空才加 `ItemPriorityLimit`**（即仅预配载）。
/// 2. **`RelativeOrder` 非空才加 `ItemPriorityReverseLimit`**（即仅全载）。
/// 3. **`MustShipLimit` 的条件是"非预配载且下标非空"**。这里只判 `!= Predistribution`，不判 `WithMacOptimization` 之类的谓词——
///    写成那种谓词会让建议打板少一条约束。
/// 4. **顺序**：优先级、反转、必须发货。
///
/// **three conditional pipelines and no overall mode switch**. Four things are carried over: **`ItemPriorityLimit` only when `AbsoluteOrder` is non-null**
/// (predistribution only); **`ItemPriorityReverseLimit` only when `RelativeOrder` is non-null** (full load only); **`MustShipLimit` requires "not predistribution and a non-empty index
/// list"**, testing only `!= Predistribution` rather than a `WithMacOptimization`-style predicate, which would drop a constraint under weight recommendation; and **the order** is
/// priority, reversal, must-ship.
/// </remarks>
internal sealed class PipelineListGenerator
{
    private readonly ExpressEffectivenessAggregation _aggregation;

    /// <summary>
    /// 构造生成器 / Construct the generator.
    /// </summary>
    /// <param name="aggregation">聚合 / The aggregation.</param>
    public PipelineListGenerator(ExpressEffectivenessAggregation aggregation)
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
        if (_aggregation.AbsoluteOrder is { } absolute)
        {
            pipelines.Add(new ItemPriorityLimit(
                _aggregation.Items,
                _aggregation.Positions,
                absolute,
                _aggregation.Stowage,
                _ => parameter.Priority).Invoke);
        }
        if (_aggregation.RelativeOrder is { } relative)
        {
            pipelines.Add(new ItemPriorityReverseLimit(
                relative,
                (left, right) => left.Item2.Priority.Category == right.Item2.Priority.Category
                    ? parameter.Priority
                    : parameter.PriorityCategory).Invoke);
        }
        if (stowageMode != StowageMode.Predistribution && _aggregation.MustShipIndices.Count > 0)
        {
            pipelines.Add(new MustShipLimit(
                _aggregation.Items,
                _aggregation.Positions,
                _aggregation.Stowage,
                _aggregation.MustShipIndices).Invoke);
        }
        return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(pipelines);
    }
}
