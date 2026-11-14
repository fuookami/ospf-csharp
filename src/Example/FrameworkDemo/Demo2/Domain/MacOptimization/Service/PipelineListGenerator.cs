#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service;

/// <summary>
/// 重心优化管线生成器 / The MAC-optimisation pipeline generator.
/// </summary>
/// <remarks>
/// 与基线 `mac_optimization/service/PipelineListGenerator.kt` 对齐：**先看装载模式**，再按"纵 → 横（宽体才）→ 安定面"的顺序装配。
///
/// 五处必须照抄：
///
/// 1. **`WithMacOptimization()` 为假时返回空列表**（不是失败）。基线 `return Ok(emptyList())`。
/// 2. **顺序固定**：纵向、横向、安定面。改顺序会改变目标在 `MetaSubObjects` 里的位置，而下游按位置取值。
/// 3. **横向只对宽体机加**：`LateralBalance` 为 `null` 时跳过。
/// 4. **纵向系数三分支**：`C` 点 → `MacRangeC`；B737 → `B737LongitudinalBalance`；其余 → `LongitudinalBalance`。
///    第二分支的机型判定用的是 `aggregation.AircraftModel.Type == B737`，**不是**"窄体机"——B757 走第三分支。
/// 5. **安定面恒加**，即使字典为空（那时是零个目标，但管线本身在列表里）。
///
/// Aligned with the baseline: **the stowage mode is checked first**, then the pipelines are assembled longitudinal → lateral (wide-body only) → stabilizers. Five things must be copied:
/// **a false `WithMacOptimization()` yields an empty list**, not a failure; **the order is fixed**, since changing it moves the objectives within `MetaSubObjects` and downstream reads
/// them by position; **lateral is added only for a wide-body** (`LateralBalance` null is skipped); **the longitudinal coefficient has three branches** — `C` → `MacRangeC`, B737 →
/// `B737LongitudinalBalance`, everything else → `LongitudinalBalance`, and the middle branch tests the type for **B737 specifically**, not "narrow-body", so a B757 takes the third
/// branch; and **stabilizers are always added**, even when the dictionary is empty (zero objectives, but the pipeline is still in the list).
/// </remarks>
internal sealed class PipelineListGenerator
{
    private readonly MacOptimizationAggregation _aggregation;

    /// <summary>
    /// 构造生成器 / Construct the generator.
    /// </summary>
    /// <param name="aggregation">重心优化聚合 / The MAC-optimisation aggregation.</param>
    public PipelineListGenerator(MacOptimizationAggregation aggregation)
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

        if (!stowageMode.WithMacOptimization())
        {
            return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(
                new List<Func<LinearMetaModel<Flt64>, Try>>());
        }

        AircraftType? type = _aggregation.AircraftModel.Type;
        var pipelines = new List<Func<LinearMetaModel<Flt64>, Try>> {
            new LongitudinalBalanceLimit(
                _aggregation.LongitudinalBalance,
                rangeType => rangeType == MACRangeType.C
                    ? parameter.MacRangeC
                    : type == AircraftType.B737
                        ? parameter.B737LongitudinalBalance
                        : parameter.LongitudinalBalance).Invoke,
        };
        if (_aggregation.LateralBalance is { } lateral)
        {
            pipelines.Add(new LateralBalanceLimit(lateral, () => parameter.LateralBalance).Invoke);
        }
        pipelines.Add(new HorizontalStabilizerLimit(
            _aggregation.HorizontalStabilizers, _ => parameter.HorizontalStabilizerWarn).Invoke);

        return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(pipelines);
    }
}
