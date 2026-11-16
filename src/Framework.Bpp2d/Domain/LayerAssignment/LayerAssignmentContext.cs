#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment;

/// <summary>
/// 列生成阶段的赋值构件 / Assignment artifacts of the column-generation phase.
/// </summary>
/// <param name="Assignment">非精确层分配 / The imprecise layer assignment.</param>
/// <param name="Load">非精确装载 / The imprecise load.</param>
/// <param name="DemandConstraint">需求约束 / The demand constraint.</param>
public sealed record RmpAssignmentArtifacts(
    ImpreciseAssignment Assignment,
    ImpreciseLoad Load,
    DemandConstraint DemandConstraint);

/// <summary>
/// 最终 MILP 阶段的赋值构件 / Assignment artifacts of the final MILP phase.
/// </summary>
/// <param name="Assignment">精确层分配 / The precise layer assignment.</param>
/// <param name="Load">非精确装载（最终阶段复用）/ The imprecise load, reused by the final phase.</param>
/// <param name="DemandConstraint">需求约束 / The demand constraint.</param>
/// <param name="Capacity">装载容量 / The load capacity.</param>
public sealed record FinalAssignmentArtifacts(
    PreciseAssignment Assignment,
    ImpreciseLoad Load,
    DemandConstraint DemandConstraint,
    PreciseLoadCapacity Capacity);

/// <summary>
/// 层分配上下文 / The layer-assignment context.
/// </summary>
/// <remarks>
/// 与对齐，**但这里只落 `RegisterRmp`**：
///
/// | 方法 | 状态 |
/// | --- | --- |
/// | `RegisterRmp` | 这里落地，三个前置（`ImpreciseAssignment` / `ImpreciseLoad` / `DemandConstraint`）都已就位 |
/// | `AddColumns` | 这里落地（一行：新列非空才追加装载项） |
/// | `RemoveColumns` | 这里落地（转给聚合） |
/// | `RegisterFinal` | **未落地**：要 `PreciseAssignment` / `PreciseLoad` 与五个尚未存在的约束（`AreaMinimization`、`SheetAmountMinimization`、`SheetCapacityConstraint`、`SheetTypeCostMinimization`、`SheetAreaMinimization`） |
/// | `SetSolution` 等求解器回写 | **未落地**：要列生成求解器主循环 |
///
/// 一处与形状差异：`registerRmp` 在 `registerLoad` 之后还调 `load.register(model)` 注册装载的符号层
/// （`AbstractLoad.register`）。本仓的 `ImpreciseLoad` **没有这一层**——`OverLoad` / `LessLoad` 仍是空表（记档），
/// 所以这里不调它。**等松弛变量落地后要补这一句**，否则松弛约束会找不到符号。
///
/// Aligned with its `LayerAssignmentContext`, **but only `RegisterRmp` lands here**: its three prerequisites (`ImpreciseAssignment`, `ImpreciseLoad`,
/// `DemandConstraint`) are all in place, while `RegisterFinal` needs `PreciseAssignment` / `PreciseLoad` plus five constraints that do not exist yet
/// (`AreaMinimization`, `SheetAmountMinimization`, `SheetCapacityConstraint`, `SheetTypeCostMinimization`, `SheetAreaMinimization`), and the solver write-back needs the column-generation
/// main loop. One structural difference: the code calls `load.register(model)` after `registerLoad` to register the load's symbol layer, but this repository's `ImpreciseLoad` has no such
/// layer — `OverLoad` / `LessLoad` are still empty lists (recorded) — so it is skipped here. **That call must be added once the slack variables land**, or the slack constraints will
/// not find their symbols.
/// </remarks>
public sealed class LayerAssignmentContext
{
    private readonly IReadOnlyDictionary<ItemId, ulong> _items;

    /// <summary>
    /// 构造层分配上下文 / Construct the layer-assignment context.
    /// </summary>
    /// <param name="items">物品需求量 / The item demands.</param>
    /// <param name="aggregation">层聚合 / The layer aggregation.</param>
    public LayerAssignmentContext(
        IReadOnlyDictionary<ItemId, ulong>? items = null,
        LayerAggregation<FltX>? aggregation = null)
    {
        _items = items ?? new Dictionary<ItemId, ulong>();
        Aggregation = aggregation ?? new LayerAggregation<FltX>();
    }

    /// <summary>层聚合 / The layer aggregation</summary>
    public LayerAggregation<FltX> Aggregation { get; }

    /// <summary>
    /// 注册列生成阶段 / Register the column-generation phase.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="demandEntries">需求项 / The demand entries.</param>
    /// <param name="overEnabled">是否启用超额 / Whether over-load is enabled.</param>
    /// <returns>构件 / The artifacts.</returns>
    public Result<RmpAssignmentArtifacts, ErrorCode, Error<ErrorCode>> RegisterRmp(
        LinearMetaModel<Flt64> model,
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        bool overEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(demandEntries);

        var assignment = new ImpreciseAssignment(_items, Aggregation);
        Try registered = assignment.Register(model);
        if (!registered.IsOk)
        {
            return Fail(registered);
        }

        var load = new ImpreciseLoad(demandEntries, assignment, overEnabled: overEnabled, lessEnabled: true);
        Try loadRegistered = load.RegisterLoad(model);
        if (!loadRegistered.IsOk)
        {
            return Fail(loadRegistered);
        }

        // 基线此处还有 `load.register(model)` 注册装载的符号层（超额 / 欠额）； 起本仓也有这一层了。
        // The code also calls `load.register(model)` here to register the load's symbol layer (over- and under-load); as of this repository has that layer too.
        Try loadSymbols = load.Register(model);
        if (!loadSymbols.IsOk)
        {
            return Fail(loadSymbols);
        }

        var demandConstraint = new DemandConstraint(load, demandEntries);
        Try invoked = demandConstraint.Invoke(model);
        if (!invoked.IsOk)
        {
            return Fail(invoked);
        }

        return Results.Ok<RmpAssignmentArtifacts>(new RmpAssignmentArtifacts(assignment, load, demandConstraint));
    }

    /// <summary>
    /// 追加新列 / Append new columns.
    /// </summary>
    /// <param name="artifacts">列生成构件 / The column-generation artifacts.</param>
    /// <param name="iteration">迭代序号 / The iteration number.</param>
    /// <param name="newLayers">新层 / The new layers.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>去重后的新层 / The deduplicated new layers.</returns>
    /// <remarks>
    /// **新列非空才追加装载项**：空追加会把每条需求的装载多项式重写一遍，白做功。
    ///
    /// **Load terms are appended only when new columns exist**: appending on an empty batch would rewrite every demand's load polynomial for nothing.
    /// </remarks>
    public Result<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>> AddColumns(
        RmpAssignmentArtifacts artifacts,
        ulong iteration,
        IReadOnlyList<Layer<FltX>> newLayers,
        LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(newLayers);
        ArgumentNullException.ThrowIfNull(model);

        Result<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>> added =
            artifacts.Assignment.AddColumns(iteration, newLayers, model);
        if (added is Ok<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>> ok && ok.Value.Count > 0)
        {
            artifacts.Load.AppendColumnTerms();
        }
        return added;
    }

    /// <summary>
    /// 移除列 / Remove columns.
    /// </summary>
    /// <param name="layers">要移除的层 / The layers to remove.</param>
    public void RemoveColumns(IReadOnlyList<Layer<FltX>> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        Aggregation.RemoveColumns(layers);
    }

    /// <summary>
    /// 注册最终阶段 / Register the final phase.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="layers">候选层 / The candidate layers.</param>
    /// <param name="demandEntries">需求项 / The demand entries.</param>
    /// <param name="enableCapacityConstraint">是否启用容量约束 / Whether the capacity constraint is enabled.</param>
    /// <returns>构件 / The artifacts.</returns>
    /// <remarks>
    /// 与基线 `registerFinal` 对齐，**顺序不能换**：
    ///
    /// 1. **先建精确分配并注册 `y`**——后面三个构件都引用它。
    /// 2. **份数上界由 <see cref="MaxCopies.PerColumn"/> 算出**，不是常数：上界过小会让最终解欠需求，过大只是慢一点。
    /// 3. **容量约束可关**：`enableCapacityConstraint` 为假时仍注册容量符号（构件里要用），只是不加约束行。
    /// 4. **装载复用 `ImpreciseLoad`**：最终阶段用 `PreciseLoad`，但它与 `ImpreciseLoad` 的差别只在松弛变量的注册层，
    ///    而那一层本仓还没有（记档）。**用同一个类型而不是复制一份几乎相同的代码**，差别写在注释里。
    ///
    /// Aligned with its `registerFinal`, and **the order must not change**: **the precise assignment is built and `y` registered first**, since the other three artifacts reference it;
    /// **the copy bound comes from `MaxCopies.PerColumn`** rather than a constant, because a bound that is too small leaves the final solution short while a large one merely costs time; **the
    /// capacity constraint can be switched off** while the capacity symbols are still registered for the artifacts to use; and **the load reuses `ImpreciseLoad`** — the code uses `PreciseLoad`
    /// here, but the two differ only in the slack-variable registration layer that this repository does not have yet (recorded), so **one type is reused rather than duplicating nearly
    /// identical code**, with the difference noted here.
    /// </remarks>
    public Result<FinalAssignmentArtifacts, ErrorCode, Error<ErrorCode>> RegisterFinal(
        LinearMetaModel<Flt64> model,
        IReadOnlyList<Layer<FltX>> layers,
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        bool enableCapacityConstraint = true)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(demandEntries);

        var assignment = new PreciseAssignment(layers, MaxCopies.PerColumn(layers, demandEntries));
        Try registered = assignment.Register(model);
        if (!registered.IsOk)
        {
            return FailFinal(registered);
        }

        var load = new ImpreciseLoad(demandEntries, new ImpreciseAssignment(_items));
        Try loadRegistered = load.RegisterLoad(model);
        if (!loadRegistered.IsOk)
        {
            return FailFinal(loadRegistered);
        }

        var demandConstraint = new DemandConstraint(load, demandEntries);
        Try invoked = demandConstraint.Invoke(model);
        if (!invoked.IsOk)
        {
            return FailFinal(invoked);
        }

        var capacity = new PreciseLoadCapacity(layers, assignment);
        Try capacityRegistered = capacity.Register(model);
        if (!capacityRegistered.IsOk)
        {
            return FailFinal(capacityRegistered);
        }
        if (enableCapacityConstraint)
        {
            Try capacityInvoked = new SheetCapacityConstraint(capacity).Invoke(model);
            if (!capacityInvoked.IsOk)
            {
                return FailFinal(capacityInvoked);
            }
        }

        return Results.Ok<FinalAssignmentArtifacts>(
            new FinalAssignmentArtifacts(assignment, load, demandConstraint, capacity));
    }

    /// <summary>
    /// 注册板材面积最小化 / Register the sheet-area minimisation.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="assignment">精确层分配 / The precise layer assignment.</param>
    /// <param name="layers">候选层 / The candidate layers.</param>
    /// <param name="weight">面积权重 / The area weight.</param>
    /// <returns>注册结果 / The registration result.</returns>
    /// <remarks>
    /// 与基线 `registerSheetAreaMinimization` 对齐：**只是构造目标再施加**，不额外校验。
    /// 权重默认 1（面积为**主**目标）——最终阶段"余料为主、板数与类型成本为次"，
    /// 所以这条必须最先注册，且权重不能被后面的目标盖过。
    ///
    /// Aligned with its `registerSheetAreaMinimization`: **it constructs the objective and applies it**, nothing more. The weight defaults to 1 because area is the **primary** objective
    /// in the final stage ("waste first, sheet count and per-type cost second"), so this must be registered first and must not be outweighed by the objectives after it.
    /// </remarks>
    public Try RegisterSheetAreaMinimization(
        LinearMetaModel<Flt64> model,
        PreciseAssignment assignment,
        IReadOnlyList<Layer<FltX>> layers,
        FltX? weight = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(layers);
        return new SheetAreaMinimization(layers, assignment, weight).Invoke(model);
    }

    /// <summary>
    /// 注册板材数量最小化 / Register the sheet-count minimisation.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="assignment">精确层分配 / The precise layer assignment.</param>
    /// <param name="layers">候选层 / The candidate layers.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <returns>注册结果 / The registration result.</returns>
    /// <remarks>
    /// 与基线 `registerSheetAmountMinimization` 对齐。标量系数包成**常量函数** `{ coefficient }`，
    /// 这里同样包一层——`SheetAmountMinimization` 的系数是函数是为了区分"缺失数据"（null）与"零权重"，
    /// 传常量函数正好落在"有数据、权重为常数"。
    ///
    /// Aligned with its `registerSheetAmountMinimization`. The code wraps a scalar coefficient in a **constant function** `{ coefficient }`, and this does the same: the objective takes
    /// a function precisely to distinguish **missing data** (null) from **zero weight**, and a constant function lands on "data present, weight constant".
    /// </remarks>
    public Try RegisterSheetAmountMinimization(
        LinearMetaModel<Flt64> model,
        PreciseAssignment assignment,
        IReadOnlyList<Layer<FltX>> layers,
        FltX? coefficient = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(layers);
        FltX value = coefficient ?? new FltX(1.0);
        return new SheetAmountMinimization(layers, assignment, _ => value).Invoke(model);
    }

    /// <summary>
    /// 把一次失败转成目标结果类型 / Carry a failure into the target result type.
    /// </summary>
    /// <param name="failed">失败结果 / The failure.</param>
    /// <returns>同样的失败 / The same failure.</returns>
    private static Result<RmpAssignmentArtifacts, ErrorCode, Error<ErrorCode>> Fail(Try failed) =>
        failed is Failed<Success, ErrorCode, Error<ErrorCode>> failure
            ? Results.Failed<RmpAssignmentArtifacts>(failure.Error)
            : throw new InvalidOperationException("unreachable");

    /// <summary>
    /// 把一次失败转成最终阶段的结果类型 / Carry a failure into the final-phase result type.
    /// </summary>
    /// <param name="failed">失败结果 / The failure.</param>
    /// <returns>同样的失败 / The same failure.</returns>
    private static Result<FinalAssignmentArtifacts, ErrorCode, Error<ErrorCode>> FailFinal(Try failed) =>
        failed is Failed<Success, ErrorCode, Error<ErrorCode>> failure
            ? Results.Failed<FinalAssignmentArtifacts>(failure.Error)
            : throw new InvalidOperationException("unreachable");
}
