#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Model;
/// <summary>
/// 影子价格键 / Shadow price key.
/// </summary>
public class ShadowPriceKey(Type limit) {
    /// <summary>约束限制类型 / Constraint limit type.</summary>
    public Type Limit { get; } = limit;

    /// <inheritdoc/>
    public override string ToString() => $"ShadowPriceKey({Limit.Name})";

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ShadowPriceKey other && Limit == other.Limit;

    /// <inheritdoc/>
    public override int GetHashCode() => Limit.GetHashCode();
}

/// <summary>
/// 影子价格 / Shadow price.
/// </summary>
public sealed record ShadowPrice(ShadowPriceKey Key, Flt64 Price) {
    /// <inheritdoc/>
    public override string ToString() => $"{Key}: {Price}";
}

/// <summary>
/// 影子价格提取器函数类型 / Shadow price extractor function type.
/// </summary>
public delegate Flt64 ShadowPriceExtractor<Args, TMap>(TMap map, Args arg)
    where Args : class
    where TMap : AbstractShadowPriceMap<Args, TMap>;

/// <summary>
/// 抽象影子价格映射 / Abstract shadow price map.
/// </summary>
/// <typeparam name="Args">参数类型 / Argument type</typeparam>
/// <typeparam name="TSelf">映射自身类型 / Map self type</typeparam>
public abstract class AbstractShadowPriceMap<Args, TSelf>
    where Args : class
    where TSelf : AbstractShadowPriceMap<Args, TSelf> {
    private readonly Dictionary<ShadowPriceKey, ShadowPrice> _map = new();
    private readonly List<ShadowPriceExtractor<Args, TSelf>> _extractors = new();

    /// <summary>影子价格映射表 / Shadow price map.</summary>
    public IReadOnlyDictionary<ShadowPriceKey, ShadowPrice> Map => _map;

    /// <summary>通过参数计算影子价格总和 / Calculate shadow price sum via argument.</summary>
    public virtual Flt64 Invoke(Args arg) =>
        _extractors.Aggregate(Flt64.Zero, (acc, e) => acc + e((TSelf)this, arg));

    /// <summary>按键获取影子价格 / Get shadow price by key.</summary>
    public ShadowPrice? this[ShadowPriceKey key] => _map.TryGetValue(key, out ShadowPrice? v) ? v : null;

    /// <summary>按键设置影子价格 / Set shadow price by key.</summary>
    public void Set(ShadowPriceKey key, ShadowPrice value) => _map[key] = value;

    /// <summary>放置影子价格 / Put shadow price.</summary>
    public void Put(ShadowPrice price) => _map[price.Key] = price;

    /// <summary>放置或累加影子价格 / Put or add shadow price.</summary>
    public void PutOrAdd(ShadowPrice price) {
        _map[price.Key] = new ShadowPrice(
            price.Key,
            (_map.TryGetValue(price.Key, out ShadowPrice? cur) ? cur.Price : Flt64.Zero) + price.Price);
    }

    /// <summary>注册影子价格提取器 / Register shadow price extractor.</summary>
    public void Put(ShadowPriceExtractor<Args, TSelf> extractor) => _extractors.Add(extractor);

    /// <summary>按键移除影子价格 / Remove shadow price by key.</summary>
    public void Remove(ShadowPriceKey key) => _map.Remove(key);

    /// <summary>收缩：移除零值影子价格 / Shrink: remove zero-value shadow prices.</summary>
    public void Shrink() {
        foreach (ShadowPriceKey? key in _map.Where(kv => kv.Value.Price == Flt64.Zero).Select(kv => kv.Key).ToList()) {
            _map.Remove(key);
        }
    }
}

/// <summary>
/// 影子价格提取辅助方法 / Shadow price extraction helper methods.
/// </summary>
internal static class ShadowPriceHelpers {
    /// <summary>
    /// 从管线列表提取影子价格 / Extract shadow prices from pipeline list.
    /// </summary>
    internal static Try ExtractShadowPrice<Args, TMap>(
        TMap shadowPriceMap,
        IReadOnlyList<ICGPipeline<Args, object, TMap>> pipelineList,
        object model,
        MetaDualSolution shadowPrices)
        where Args : class
        where TMap : AbstractShadowPriceMap<Args, TMap> {
        foreach (ICGPipeline<Args, object, TMap> pipeline in pipelineList) {
            Try ret = pipeline.Refresh(shadowPriceMap, model, shadowPrices);
            if (ret.IsFailed) {
                return ret;
            }

            ShadowPriceExtractor<Args, TMap>? extractor = pipeline.Extractor();
            if (extractor is not null) {
                shadowPriceMap.Put(extractor);
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
