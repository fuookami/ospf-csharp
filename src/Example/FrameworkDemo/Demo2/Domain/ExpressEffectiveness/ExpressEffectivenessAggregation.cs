#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness;

/// <summary>
/// 货物时效聚合 / The express-effectiveness aggregation.
/// </summary>
/// <remarks>
/// 与基线 `express_effectiveness/Aggregation.kt` 对齐：**按装载模式二选一**地建 `AbsoluteOrder` 或 `RelativeOrder`，
/// 两者互斥。
///
/// 四处必须照抄：
///
/// 1. **预配载 → `AbsoluteOrder`**（相对顺序为 `null`）；**全载 → `RelativeOrder`**（绝对顺序为 `null`）；
///    **建议打板 → 两个都是 `null`**。写成"两个都建"会让预配载多出一族反转符号。
/// 2. **`Register` 只注册 `RelativeOrder`**，不注册 `AbsoluteOrder`——后者是纯函数，没有符号。
///    这看起来不对称，但基线就是这样：绝对顺序只在目标系数里出现，从不进模型。
/// 3. **`MustShipIndices` 默认空**。
/// 4. **`RegisterForBendersSP` 是空操作**。
///
/// Aligned with the baseline: **exactly one of `AbsoluteOrder` or `RelativeOrder`** is built, depending on the stowage mode. Four things must be copied: **predistribution →
/// `AbsoluteOrder`** (relative null), **full load → `RelativeOrder`** (absolute null), **weight recommendation → both null**, since building both would give predistribution a family of
/// reversal symbols it should not have; **`Register` registers only `RelativeOrder`**, not `AbsoluteOrder`, which is a pure function carrying no symbols — this looks asymmetric but
/// is what the baseline does, the absolute order appearing only in objective coefficients and never in the model; **`MustShipIndices` defaults to empty**; and
/// **`RegisterForBendersSP` is a no-op**.
/// </remarks>
public sealed class ExpressEffectivenessAggregation
{
    /// <summary>
    /// 构造聚合 / Construct the aggregation.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="mustShipIndices">必须发货的货物下标 / The must-ship item indices.</param>
    public ExpressEffectivenessAggregation(
        StowageMode stowageMode,
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        StowageModel stowage,
        IReadOnlyList<int>? mustShipIndices = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);

        Items = items;
        Positions = positions;
        Stowage = stowage;
        MustShipIndices = mustShipIndices ?? Array.Empty<int>();
        AbsoluteOrder = stowageMode == StowageMode.Predistribution
            ? Model.AbsoluteOrder.From(items, positions)
            : null;
        RelativeOrder = stowageMode == StowageMode.FullLoad
            ? new RelativeOrder(items, positions, stowage)
            : null;
    }

    /// <summary>货物 / The items</summary>
    public IReadOnlyList<Item> Items { get; }

    /// <summary>舱位 / The positions</summary>
    public IReadOnlyList<StowagePosition> Positions { get; }

    /// <summary>配载矩阵 / The stowage matrix</summary>
    public StowageModel Stowage { get; }

    /// <summary>必须发货的货物下标 / The must-ship item indices</summary>
    public IReadOnlyList<int> MustShipIndices { get; }

    /// <summary>绝对优先级系数（仅预配载）/ The absolute priority coefficients (predistribution only)</summary>
    public AbsoluteOrder? AbsoluteOrder { get; }

    /// <summary>相对优先级反转（仅全载）/ The relative priority reversals (full load only)</summary>
    public RelativeOrder? RelativeOrder { get; }

    /// <summary>
    /// 注册到模型 / Register with the model.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        // **只注册 `RelativeOrder`**：`AbsoluteOrder` 没有符号。
        // **Only `RelativeOrder` registers**: `AbsoluteOrder` has no symbols.
        return RelativeOrder is { } relative
            ? relative.Register(model)
            : Results.Ok<Success>(Results.SuccessInstance);
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
    public Try RegisterForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<Flt64> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
