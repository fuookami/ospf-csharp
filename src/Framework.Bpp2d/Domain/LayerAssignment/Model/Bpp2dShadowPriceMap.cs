#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;

/// <summary>
/// 二维层分配影子价格的参数 / Arguments for a 2D layer-assignment shadow price.
/// </summary>
/// <param name="Mode">需求种类 / The demand kind.</param>
/// <param name="ItemId">物品编号 / The item identifier.</param>
public sealed record Bpp2dShadowPriceArguments(
    Bpp2dAssignmentDemandMode Mode,
    ItemId ItemId);

/// <summary>
/// 二维层分配影子价格映射 / The 2D layer-assignment shadow price map.
/// </summary>
/// <remarks>
/// **不继承共享的 `AbstractShadowPriceMap`**，理由记在 ：那份基类的键 `ShadowPriceKey` 只存一个 `Type`、
/// 没有载荷位，`ToKey` 会把模式与物品编号折叠掉（实测：加两条只剩一条）。本类于是自己持有
/// `Dictionary&lt;Bpp2dDemandShadowPriceKey, Flt64&gt;`，键是**自带相等性的值对象**。
///
/// 三处沿用语义：
///
/// 1. **同一个键累加**，不是覆盖——同一条需求在多轮迭代里被反复定价；
/// 2. **没登记的键取回 null**，不是零（零会被读成"已知影子价格为 0"）；
/// 3. `Invoke` 把已登记提取器的结果**求和**。
///
/// **Deliberately does not derive from the shared `AbstractShadowPriceMap`** for the reason recorded: that base class's key holds only a `Type` with no room for a payload, so
/// `ToKey` collapses the mode and item id (measured: two additions collapsed to one). This class keeps its own `Dictionary&lt;Bpp2dDemandShadowPriceKey, Flt64&gt;` keyed by a **value
/// object with its own equality**. Three baseline semantics are copied: **the same key accumulates** rather than overwriting, since one demand is priced repeatedly across iterations;
/// **an unregistered key returns null** rather than zero, which would read as "a known shadow price of zero"; and `Invoke` **sums** the registered extractors.
/// </remarks>
public sealed class Bpp2dShadowPriceMap
{
    private readonly Dictionary<Bpp2dDemandShadowPriceKey, Flt64> _map = new();
    private readonly List<Func<Bpp2dShadowPriceArguments, Flt64>> _extractors = new();

    /// <summary>影子价格映射表 / The shadow price map.</summary>
    public IReadOnlyDictionary<Bpp2dDemandShadowPriceKey, Flt64> Map => _map;

    /// <summary>已登记的提取器个数 / The number of registered extractors.</summary>
    public int ExtractorCount => _extractors.Count;

    /// <summary>
    /// 按键取影子价格 / Fetch a shadow price by key.
    /// </summary>
    /// <param name="key">键 / The key.</param>
    /// <returns>影子价格，未登记时为 null / The shadow price, or null when unregistered.</returns>
    public Flt64? this[Bpp2dDemandShadowPriceKey key] =>
        _map.TryGetValue(key, out Flt64 value) ? value : null;

    /// <summary>
    /// 按模式与物品取影子价格 / Fetch a shadow price by mode and item.
    /// </summary>
    /// <param name="mode">需求种类 / The demand kind.</param>
    /// <param name="itemId">物品编号 / The item identifier.</param>
    /// <returns>影子价格，未登记时为 null / The shadow price, or null when unregistered.</returns>
    public Flt64? At(Bpp2dAssignmentDemandMode mode, ItemId itemId) => this[new(mode, itemId)];

    /// <summary>
    /// 设置一条影子价格 / Set one shadow price.
    /// </summary>
    /// <param name="mode">需求种类 / The demand kind.</param>
    /// <param name="itemId">物品编号 / The item identifier.</param>
    /// <param name="value">数值 / The value.</param>
    public void Put(Bpp2dAssignmentDemandMode mode, ItemId itemId, Flt64 value) =>
        _map[new(mode, itemId)] = value;

    /// <summary>
    /// 累加一条影子价格 / Accumulate one shadow price.
    /// </summary>
    /// <param name="mode">需求种类 / The demand kind.</param>
    /// <param name="itemId">物品编号 / The item identifier.</param>
    /// <param name="value">数值 / The value.</param>
    /// <remarks>
    /// **累加而不是覆盖**：同一条需求在多轮迭代里被反复定价，覆盖会把前几轮的定价丢掉。
    ///
    /// **Accumulates rather than overwrites**: one demand is priced repeatedly across iterations, and overwriting would discard the earlier rounds.
    /// </remarks>
    public void Add(Bpp2dAssignmentDemandMode mode, ItemId itemId, Flt64 value)
    {
        Bpp2dDemandShadowPriceKey key = new(mode, itemId);
        _map[key] = _map.TryGetValue(key, out Flt64 existing) ? existing + value : value;
    }

    /// <summary>
    /// 登记一个提取器 / Register one extractor.
    /// </summary>
    /// <param name="extractor">提取器 / The extractor.</param>
    public void Register(Func<Bpp2dShadowPriceArguments, Flt64> extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        _extractors.Add(extractor);
    }

    /// <summary>
    /// 求和 / Sum.
    /// </summary>
    /// <param name="arguments">参数 / The arguments.</param>
    /// <returns>影子价格之和 / The sum of the shadow prices.</returns>
    public Flt64 Invoke(Bpp2dShadowPriceArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Flt64 sum = Flt64.Zero;
        foreach (Func<Bpp2dShadowPriceArguments, Flt64> extractor in _extractors)
        {
            sum += extractor(arguments);
        }
        return sum;
    }
}
