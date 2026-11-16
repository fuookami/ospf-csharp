#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization;

/// <summary>
/// 重心优化聚合 / The MAC-optimisation aggregation.
/// </summary>
/// <remarks>
/// **自建三个模型**（`MACRange`、`LongitudinalBalance`、`LateralBalance`），
/// 上游只取飞机型号、公式、总重、扭矩与安定面。
///
/// 三处必须沿用：
///
/// 1. **`MACRange` 在这里算出来，不是从 `Mac` 域传进来**。它依赖 `TotalWeight` 的起飞重量，而那是配载域的产物——
///    在 `Mac` 域里算会用到还没注册的符号。
/// 2. **横向平衡只对宽体机存在**。窄体机 `LateralBalance` 是 `null`，生成器据此跳过 `LateralBalanceLimit`。
///    给窄体机也建一个会让目标里多一个恒零项。
/// 3. **`Register` 先纵后横，任一失败即短路**。横向失败被吞掉会让宽体机少一个目标而看不出来。
///
/// **three models are built here** (`MACRange`, `LongitudinalBalance`, `LateralBalance`), taking only the aircraft model, formula, total weight, torque and
/// stabilizers from upstream. Three things must be copied: **`MACRange` is computed here**, not passed in from the `Mac` domain — it depends on the take-off total weight, a stowage
/// domain product, and computing it in the `Mac` domain would use symbols that are not registered yet; **lateral balance exists only for wide-body aircraft** (`null` otherwise, so the
/// generator skips `LateralBalanceLimit`; building one for a narrow-body would add a constant-zero objective term); and **`Register` does longitudinal then lateral and
/// short-circuits on either failure** — swallowing a lateral failure would leave a wide-body missing an objective, invisibly.
/// </remarks>
public sealed class MacOptimizationAggregation
{
    /// <summary>
    /// 构造聚合 / Construct the aggregation.
    /// </summary>
    /// <param name="aircraftModel">飞机型号 / The aircraft model.</param>
    /// <param name="formula">公式 / The formula.</param>
    /// <param name="totalWeight">总重 / The total weight.</param>
    /// <param name="torque">扭矩 / The torque.</param>
    /// <param name="horizontalStabilizers">水平安定面 / The horizontal stabilizers.</param>
    public MacOptimizationAggregation(
        AircraftModel aircraftModel,
        Formula formula,
        TotalWeight totalWeight,
        Torque torque,
        IReadOnlyDictionary<HorizontalStabilizer.Key, HorizontalStabilizer> horizontalStabilizers)
    {
        ArgumentNullException.ThrowIfNull(aircraftModel);
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(totalWeight);
        ArgumentNullException.ThrowIfNull(torque);
        ArgumentNullException.ThrowIfNull(horizontalStabilizers);

        AircraftModel = aircraftModel;
        HorizontalStabilizers = horizontalStabilizers;
        MacRange = MACRange.From(aircraftModel, formula, totalWeight);
        LongitudinalBalance = new LongitudinalBalance(MacRange, torque);
        // **只有宽体机才有横向平衡**。
        // **Only a wide-body has lateral balance**.
        LateralBalance = aircraftModel.WideBody ? new LateralBalance(torque) : null;
    }

    /// <summary>飞机型号 / The aircraft model</summary>
    public AircraftModel AircraftModel { get; }

    /// <summary>MAC 范围 / The MAC range</summary>
    public MACRange MacRange { get; }

    /// <summary>纵向平衡 / The longitudinal balance</summary>
    public LongitudinalBalance LongitudinalBalance { get; }

    /// <summary>横向平衡（窄体机为 null）/ The lateral balance (null for narrow-body)</summary>
    public LateralBalance? LateralBalance { get; }

    /// <summary>水平安定面 / The horizontal stabilizers</summary>
    public IReadOnlyDictionary<HorizontalStabilizer.Key, HorizontalStabilizer> HorizontalStabilizers { get; }

    /// <summary>
    /// 注册纵向与横向平衡符号 / Register the longitudinal and lateral balance symbols.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Try longitudinal = LongitudinalBalance.Register(stowageMode, model);
        if (!longitudinal.IsOk)
        {
            return longitudinal;
        }
        if (LateralBalance is { } lateral)
        {
            Try applied = lateral.Register(model);
            if (!applied.IsOk)
            {
                return applied;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 为 Benders 主问题注册 / Register for the Benders master problem.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try RegisterForBendersMP(StowageMode stowageMode, LinearMetaModel<Flt64> model) =>
        Register(stowageMode, model);

    /// <summary>
    /// 为 Benders 子问题注册（空操作）/ Register for the Benders sub-problem (a no-op).
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="solution">主问题解 / The master-problem solution.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 这里**什么都不做**，且 `flushForBendersSP` 是 `private` 且从未被调用。沿用成空操作，而不是"顺手实现"——
    /// 子问题不消费这些松弛符号。
    ///
 /// This implementation does **nothing** here, and its `flushForBendersSP` is `private` and never called. Copied as a no-op rather than "implemented in passing": the sub-problem does not
    /// consume these slack symbols.
    /// </remarks>
    public Try RegisterForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<Flt64> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
