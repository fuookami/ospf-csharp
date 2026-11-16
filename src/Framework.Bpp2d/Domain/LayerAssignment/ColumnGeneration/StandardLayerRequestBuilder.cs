#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;

/// <summary>
/// 标准执行器的建列请求构造器 / The standard executors' layer-request builder.
/// </summary>
/// <remarks>
/// 与基线 `ColumnGenerationStandardExecutors.requestBuilder` 对齐。**它只装配，不计算**：把物品、板材、已有列、
/// 影子价格与锯齿缝宽装进一个请求对象，交给生成器。
///
/// 四处必须照抄：
///
/// 1. **候选板材取主板材**（`IsMain`），没有主板材才退到第一块。主板材缺失时取第一块是基线的兜底，
///    不是"随便选一块"——没有兜底会让单板材配置直接失败。
/// 2. **物品清单按 `_itemCatalogue` 过滤**：目录里没有的物品**跳过**而不是抛错。物品未登记是数据准备问题，
///    生成器仍能用其余物品产出候选；抛错会让整次列生成白跑。
/// 3. **数量转 `int` 时截断**：基线的 `amount.toULong.toInt` 在超出 `int` 范围时会回绕，
///    这是基线行为，本实现照抄并记档（见 `ponytail:` 注释）。
/// 4. **`MaxCandidates` 来自列生成配置**，不是执行器配置——两者是不同的东西，混用会让每轮列数失控。
///
/// Aligned with the baseline's `requestBuilder`. **It only assembles, never computes**: it packs items, sheets, existing columns, shadow prices and kerf into a request for the generator. Four
/// things must be copied: **the candidate sheet is the main one** (`IsMain`), falling back to the first only when none is main, since that fallback is the baseline's and without it a
/// single-sheet configuration fails outright; **the item list is filtered by `_itemCatalogue`**, skipping items the catalogue lacks rather than throwing, because an unregistered item is a data
/// preparation problem and throwing would waste the whole generation pass; **the amount truncates when cast to `int`**, which is the baseline's `toULong.toInt` behaviour and is recorded as a
/// `ponytail:` note below; and **`MaxCandidates` comes from the column-generation config**, not the executor config, since mixing them lets the per-round column count run away.
/// </remarks>
public sealed class StandardLayerRequestBuilder : IColumnGenerationLayerRequestBuilder
{
    private readonly IReadOnlyDictionary<ItemId, ulong> _items;
    private readonly IReadOnlyDictionary<ItemId, ActualItem<FltX>> _itemCatalogue;
    private readonly IReadOnlyList<SheetType<FltX>> _sheetTypes;
    private readonly Quantity<FltX>? _kerf;

    /// <summary>
    /// 构造请求构造器 / Construct the request builder.
    /// </summary>
    /// <param name="items">物品需求量 / The item demands.</param>
    /// <param name="itemCatalogue">物品目录 / The item catalogue.</param>
    /// <param name="sheetTypes">板材类型 / The sheet types.</param>
    /// <param name="kerf">锯缝宽度 / The kerf.</param>
    public StandardLayerRequestBuilder(
        IReadOnlyDictionary<ItemId, ulong> items,
        IReadOnlyDictionary<ItemId, ActualItem<FltX>> itemCatalogue,
        IReadOnlyList<SheetType<FltX>> sheetTypes,
        Quantity<FltX>? kerf = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(itemCatalogue);
        ArgumentNullException.ThrowIfNull(sheetTypes);
        _items = items;
        _itemCatalogue = itemCatalogue;
        _sheetTypes = sheetTypes;
        _kerf = kerf;
    }

    /// <summary>候选板材 / The candidate sheet</summary>
    /// <remarks>主板材优先，没有主板材时取第一块；两者都没有则为 null / The main sheet first, else the first one; null when there are none.</remarks>
    public SheetType<FltX>? CandidateSheet =>
        _sheetTypes.FirstOrDefault(sheet => sheet.IsMain) ?? _sheetTypes.FirstOrDefault();

    /// <summary>
    /// 物品生成清单 / The item list for generation.
    /// </summary>
    /// <returns>物品与数量的清单 / The items with their amounts.</returns>
    /// <remarks>
    /// **目录里没有的物品跳过**，不抛错。数量转 `int` 时截断——见类注释的 `ponytail:` 记档。
    ///
    /// **Items absent from the catalogue are skipped**, not thrown on. The amount truncates when cast to `int` — see the `ponytail:` note on the class.
    /// </remarks>
    public IReadOnlyList<(ActualItem<FltX> Item, int Amount)> GenerationItems()
    {
        var result = new List<(ActualItem<FltX> Item, int Amount)>(_items.Count);
        foreach (KeyValuePair<ItemId, ulong> entry in _items)
        {
            if (!_itemCatalogue.TryGetValue(entry.Key, out ActualItem<FltX>? item))
            {
                continue;
            }
            // ponytail: 数量按 `int` 截断，与基线的 `toULong.toInt` 一致；单个物品需求超过 `int.MaxValue`
            // 在板材排版里不可能出现，真出现时截断也比抛错更接近基线行为。
            // ponytail: the amount truncates to `int` like the baseline's `toULong.toInt`. A single-item demand past `int.MaxValue` cannot occur in sheet nesting, and truncation matches
            // the baseline more closely than throwing.
            result.Add((item, unchecked((int)entry.Value)));
        }
        return result;
    }

    /// <inheritdoc/>
    public Bpp2dLayerGenerationRequest<FltX> Build(
        ColumnGenerationState state,
        ColumnGenerationConfig config)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(config);
        return new Bpp2dLayerGenerationRequest<FltX>(
            Iteration: state.Iteration,
            Sheet: CandidateSheet,
            Sheets: _sheetTypes,
            Items: GenerationItems(),
            ExistingLayers: state.Columns,
            ShadowPrices: state.ShadowPrices,
            Kerf: _kerf,
            // **每轮列数来自列生成配置**，不是执行器配置。
            // **The per-round column count comes from the column-generation config**, not the executor config.
            MaxCandidates: config.MaxColumnsPerIteration);
    }
}
