#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;

/// <summary>
/// 列生成的应用请求 / The column-generation application request.
/// </summary>
/// <param name="Items">物品与件数 / The items with their amounts.</param>
/// <param name="ItemCatalogue">物品目录 / The item catalogue.</param>
/// <param name="SheetTypes">板材类型 / The sheet types.</param>
/// <param name="IterationLimit">迭代上限 / The iteration limit.</param>
/// <param name="MaxColumnsPerIteration">每轮列数上限 / The per-round column limit.</param>
/// <param name="FinalMilpEnabled">是否执行最终 MILP / Whether to run the final MILP.</param>
/// <param name="Kerf">锯缝宽度 / The kerf.</param>
/// <param name="ExecutorConfig">执行器配置 / The executor configuration.</param>
/// <remarks>
/// 与基线 `ColumnGenerationApplicationRequest` 对齐，是应用层的入口数据。
/// 四处照抄：**件数上限用 `ulong`**（件数不会是负数）；**目录与物品分开**（物品清单只有 id 与件数，
/// 形状在目录里）；**两个上限来自调用方**（不是执行器配置）；**锯缝可以为 null**（零缝宽与"没配置"是两回事）。
///
/// Aligned with the baseline's `ColumnGenerationApplicationRequest`, the application layer's entry data. Four things are copied: **amounts are `ulong`**, since unit counts are never negative;
/// **the catalogue is separate from the item list**, the list carrying only ids and amounts while the shapes live in the catalogue; **both limits come from the caller** rather than the executor
/// config; and **the kerf may be null**, since zero kerf differs from an unconfigured one.
/// </remarks>
public sealed record ColumnGenerationApplicationRequest(
    IReadOnlyDictionary<ItemId, ulong> Items,
    IReadOnlyDictionary<ItemId, ActualItem<FltX>> ItemCatalogue,
    IReadOnlyList<SheetType<FltX>> SheetTypes,
    int IterationLimit = 32,
    int MaxColumnsPerIteration = 16,
    bool FinalMilpEnabled = true,
    Quantity<FltX>? Kerf = null,
    ColumnGenerationStandardExecutorConfig? ExecutorConfig = null)
{
    /// <summary>执行器配置（未给则默认）/ The executor config, defaulting when unset</summary>
    public ColumnGenerationStandardExecutorConfig ExecutorConfigValue =>
        ExecutorConfig ?? new ColumnGenerationStandardExecutorConfig();

    /// <summary>
    /// 产出需求项 / Build the demand entries.
    /// </summary>
    /// <param name="withAreaDemand">是否附带面积需求 / Whether to add area demands.</param>
    /// <returns>需求项 / The demand entries.</returns>
    /// <remarks>
    /// **按目录取形状**：物品清单只有 id 与件数，没有形状就算不出面积需求。目录里缺的物品**跳过**（B658 的同一口径：
    /// 未登记是数据准备问题，抛错会让整次求解白跑）。
    ///
    /// **Shapes come from the catalogue**: the item list carries only ids and amounts, and without a shape no area demand can be computed. Items missing from the catalogue are **skipped** —
    /// the same stance as B658, where an unregistered item is a data preparation problem and throwing would waste the whole run.
    /// </remarks>
    public IReadOnlyList<Bpp2dAssignmentDemandEntry> ToDemandEntries(bool withAreaDemand = false)
    {
        var resolved = new Dictionary<ActualItem<FltX>, ulong>(Items.Count);
        foreach (KeyValuePair<ItemId, ulong> pair in Items)
        {
            if (!ItemCatalogue.TryGetValue(pair.Key, out ActualItem<FltX>? item))
            {
                continue;
            }
            resolved[item] = pair.Value;
        }
        return DemandEntries.Build(resolved, withAreaDemand);
    }

    /// <summary>
    /// 产出算法配置 / Build the algorithm config.
    /// </summary>
    /// <returns>算法配置 / The algorithm config.</returns>
    /// <remarks>
    /// **三个上限都来自请求**，不在这里塞默认值——调用方的预算就是调用方的预算。
    ///
    /// **All three limits come from the request**, with no defaults inserted here: the caller's budget is the caller's.
    /// </remarks>
    public ColumnGenerationConfig ToAlgorithmConfig() => new(
        IterationLimit: IterationLimit,
        MaxColumnsPerIteration: MaxColumnsPerIteration,
        FinalMilpEnabled: FinalMilpEnabled);
}

/// <summary>
/// 标准执行器组 / The standard executor set.
/// </summary>
/// <remarks>
/// 与基线 `ColumnGenerationStandardExecutors` 的构造对齐：把请求里的物品、目录、需求项、板材、锯缝与配置
/// 装成"求解器 + 四个执行器"这一组。**这是装配，不是计算**。
///
/// Aligned with the baseline's `ColumnGenerationStandardExecutors` construction: it packs the request's items, catalogue, demand entries, sheets, kerf and config into "one solver plus four
/// executors". **This assembles; it does not compute.**
/// </remarks>
public sealed class ColumnGenerationStandardExecution
{
    /// <summary>
    /// 从请求装出一组执行器 / Assemble a set of executors from a request.
    /// </summary>
    /// <param name="request">应用请求 / The application request.</param>
    /// <param name="solver">求解器 / The solver.</param>
    /// <returns>执行器组 / The executor set.</returns>
    public ColumnGenerationStandardExecution(
        ColumnGenerationApplicationRequest request,
        IColumnGenerationSolver solver)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(solver);

        Config = request.ExecutorConfigValue;
        DemandEntries = request.ToDemandEntries();

        LayerGenerator = new LayerGenerationContext<FltX>();
        RmpSolver = new StandardRmpSolver(request.Items, DemandEntries, solver, Config);
        FinalSolver = new StandardFinalSolver(DemandEntries, request.SheetTypes, solver, Config);
        RequestBuilder = new StandardLayerRequestBuilder(
            request.Items, request.ItemCatalogue, request.SheetTypes, request.Kerf);
        ReducedCostFilter = new ReducedCostFilter(
            DemandEntries, Config.RmpAreaCoefficientValue, Config.ReducedCostToleranceValue);
    }

    /// <summary>执行器配置 / The executor config</summary>
    public ColumnGenerationStandardExecutorConfig Config { get; }

    /// <summary>层生成器 / The layer generator</summary>
    /// <remarks>**每次装一组新的**：生成器内部可能缓存，跨请求复用会让上一单的候选漏到下一单。</remarks>
    /// <remarks>**A fresh one per set**: the generator may cache internally, and reusing it across requests would leak the previous order's candidates into the next one.</remarks>
    public LayerGenerationContext<FltX> LayerGenerator { get; }

    /// <summary>需求项 / The demand entries</summary>
    public IReadOnlyList<Bpp2dAssignmentDemandEntry> DemandEntries { get; }

    /// <summary>RMP 求解器 / The RMP solver</summary>
    public StandardRmpSolver RmpSolver { get; }

    /// <summary>最终阶段求解器 / The final-stage solver</summary>
    public StandardFinalSolver FinalSolver { get; }

    /// <summary>建列请求构造器 / The layer-request builder</summary>
    public StandardLayerRequestBuilder RequestBuilder { get; }

    /// <summary>检验数筛选器 / The reduced-cost filter</summary>
    public ReducedCostFilter ReducedCostFilter { get; }
}
