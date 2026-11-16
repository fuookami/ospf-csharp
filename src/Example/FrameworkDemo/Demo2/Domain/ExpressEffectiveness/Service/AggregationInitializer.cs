#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service;

/// <summary>
/// 货物时效聚合初始化器 / The express-effectiveness aggregation initializer.
/// </summary>
/// <remarks>
/// **一条校验**（上游非空）+ **一条派生规则**（必须发货下标）。
///
/// 三处必须沿用：
///
/// 1. **`mustShipIndices` 的判据是 `cargo.priority >= 8`**，用**请求里的下标**（`mapIndexedNotNull`），不是货物名也不是 `Item`。
///    阈值 8 是硬编码。
/// 2. **聚合接收 `stowageMode`**，因为它决定建 `AbsoluteOrder` 还是 `RelativeOrder`。
/// 3. **`aircraftAggregation` 在收了但没用**——本仓不收，收了不读会让调用方以为有影响。
///
/// **one check** (upstream present) plus **one derivation rule** (the must-ship indices). Three things must be copied: **the must-ship criterion is
/// `cargo.priority >= 8`** using the **request's index** (its `mapIndexedNotNull`), not a name and not an `Item`, with 8 hard-coded; **the aggregation
/// takes the `stowageMode`**, since that decides between `AbsoluteOrder` and `RelativeOrder`; and **`aircraftAggregation` is accepted but unread in the implementation** — it is omitted
/// here, since accepting and ignoring it would suggest it matters.
/// </remarks>
internal static class AggregationInitializer
{
    /// <summary>必须发货的优先级阈值 / The must-ship priority threshold</summary>
    private const int MustShipPriorityThreshold = 8;

    /// <summary>
    /// 初始化聚合 / Initialize the aggregation.
    /// </summary>
    /// <param name="stowageAggregation">配载聚合 / The stowage aggregation.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <returns>聚合结果 / The aggregation result.</returns>
    public static Result<ExpressEffectivenessAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        StowageAggregation? stowageAggregation,
        RequestDTO input,
        StowageMode stowageMode)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (stowageAggregation is null)
        {
            return Results.Failed<ExpressEffectivenessAggregation>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "配载聚合未初始化 / the stowage aggregation is not initialised"));
        }

        return Results.Ok<ExpressEffectivenessAggregation>(new ExpressEffectivenessAggregation(
            stowageMode: stowageMode,
            items: stowageAggregation.Items,
            positions: stowageAggregation.Positions,
            stowage: stowageAggregation.Stowage,
            mustShipIndices: MustShipIndices(input)));
    }

    /// <summary>
    /// 派生必须发货下标 / Derive the must-ship indices.
    /// </summary>
    /// <remarks>
    /// **优先级不小于 8 的货物下标**（`mapIndexedNotNull { index, cargo -> index.takeIf { cargo.priority >= 8 } }`）。
    /// 单独成方法是**为了让它可被测**：上一版的判据自己重算了一遍期望值而没调用被测代码，结果"阈值改成 7"的变异存活。
    ///
    /// **The indices of items whose priority is at least 8**. It is a separate method **so that it can be tested**: the previous version's criterion
 /// recomputed the expectation itself without calling the implementation under test, and a mutant changing the threshold to 7 survived.
    /// </remarks>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>货物下标 / The item indices.</returns>
    internal static List<int> MustShipIndices(RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var indices = new List<int>();
        for (int index = 0; index < input.Cargos.Count; ++index)
        {
            // **优先级 >= 8 的必须发货**，阈值照此。
 // **A priority of 8 or more must ship**, the threshold.
            if (input.Cargos[index].Priority >= MustShipPriorityThreshold)
            {
                indices.Add(index);
            }
        }
        return indices;
    }
}
