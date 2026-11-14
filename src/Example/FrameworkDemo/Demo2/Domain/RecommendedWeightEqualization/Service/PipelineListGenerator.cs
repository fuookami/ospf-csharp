#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service;

/// <summary>
/// 推荐重量均衡管线生成器 / The recommended-weight-equalisation pipeline generator.
/// </summary>
/// <remarks>
/// 与基线 `recommended_weight_equalization/service/PipelineListGenerator.kt` 对齐：**只建议打板模式**，四条管线顺序固定。
///
/// 四处必须照抄：
///
/// 1. **判定是 `!= WeightRecommendation`**，不是 `WithMacOptimization()` 那种谓词。这里只认一个模式，写成"是推荐打板或别的"
///    会让预配载也跑均衡约束。
/// 2. **四条的顺序**：货物顺序、优先级预约、重量均衡、偏差目标。前三条是约束、第四条是目标——顺序会影响约束在模型里的编号，
///    而下游按名字取值，所以照抄更安全。
/// 3. **`ItemOrderLimit` 由 `Invoke` 工厂构造**（它自己算有序对），其余三条直接 `new`。
/// 4. **偏差目标的系数来自 `parameter.weightRecommendationBalance`**，不是常数。
///
/// Aligned with the baseline: **weight recommendation only**, with four pipelines in a fixed order. Four things must be copied: **the test is `!= WeightRecommendation`**, not a
/// `WithMacOptimization()`-style predicate — writing "recommendation or anything else" would run the equalisation constraints under predistribution too; **the order** is item order,
/// priority appointment, weight equalisation, deviation objective, where the first three are constraints and the fourth an objective, and the order affects constraint numbering in
/// the model (downstream reads by name, so copying is safer); **`ItemOrderLimit` is built through its factory**, since it computes the ordered pairs itself, while the other three
/// are plain `new`; and **the deviation coefficient comes from `parameter.weightRecommendationBalance`**, not a constant.
/// </remarks>
internal sealed class PipelineListGenerator
{
    private readonly RecommendedWeightEqualizationAggregation _aggregation;

    /// <summary>
    /// 构造生成器 / Construct the generator.
    /// </summary>
    /// <param name="aggregation">聚合 / The aggregation.</param>
    public PipelineListGenerator(RecommendedWeightEqualizationAggregation aggregation)
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

        if (stowageMode != StowageMode.WeightRecommendation)
        {
            return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(
                new List<Func<LinearMetaModel<Flt64>, Try>>());
        }

        Flt64 balance = parameter.WeightRecommendationBalance;
        var pipelines = new List<Func<LinearMetaModel<Flt64>, Try>> {
            new ItemOrderLimit(_aggregation.Items, _aggregation.Positions, _aggregation.Stowage).Invoke,
            new PriorityAppointmentLimit(
                _aggregation.Items,
                _aggregation.Positions,
                _aggregation.Appointment,
                _aggregation.PriorityAppointment,
                _aggregation.Stowage).Invoke,
            new RecommendedWeightEqualizationLimit(_aggregation.Positions, _aggregation.Load).Invoke,
            new RecommendedWeightDeviationObjective(_aggregation.Load, () => balance).Invoke,
        };
        return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(pipelines);
    }
}
