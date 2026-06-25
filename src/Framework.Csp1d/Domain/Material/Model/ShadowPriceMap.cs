#nullable enable

using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// CSP1D 影子价格索引键 / CSP1D shadow price key
/// </summary>
public abstract class Csp1dShadowPriceKey : ShadowPriceKey {
    protected Csp1dShadowPriceKey(Type limit) : base(limit) { }

    /// <summary>键名称 / Key name.</summary>
    public abstract string Name { get; }
}

/// <summary>
/// 产品需求影子价格键 / Product demand shadow price key
/// </summary>
public sealed class ProductDemandShadowPriceKey : Csp1dShadowPriceKey {
    public ProductDemandShadowPriceKey(string productId, string unitSymbol) : base(typeof(ProductDemandShadowPriceKey)) {
        ProductId = productId;
        UnitSymbol = unitSymbol;
    }

    public string ProductId { get; }
    public string UnitSymbol { get; }

    /// <inheritdoc/>
    public override string Name => $"product-demand:{ProductId}:{UnitSymbol}";

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ProductDemandShadowPriceKey other && ProductId == other.ProductId && UnitSymbol == other.UnitSymbol;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(ProductId, UnitSymbol);
}

/// <summary>
/// 物料用量影子价格键 / Material usage shadow price key
/// </summary>
public sealed class MaterialUsageShadowPriceKey : Csp1dShadowPriceKey {
    public MaterialUsageShadowPriceKey(string materialId) : base(typeof(MaterialUsageShadowPriceKey)) {
        MaterialId = materialId;
    }

    public string MaterialId { get; }

    /// <inheritdoc/>
    public override string Name => $"material-usage:{MaterialId}";

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is MaterialUsageShadowPriceKey other && MaterialId == other.MaterialId;

    /// <inheritdoc/>
    public override int GetHashCode() => MaterialId.GetHashCode();
}

/// <summary>
/// 设备批次数影子价格键 / Machine batch count shadow price key
/// </summary>
public sealed class MachineBatchShadowPriceKey : Csp1dShadowPriceKey {
    public MachineBatchShadowPriceKey(string machineId) : base(typeof(MachineBatchShadowPriceKey)) {
        MachineId = machineId;
    }

    public string MachineId { get; }

    /// <inheritdoc/>
    public override string Name => $"machine-batch:{MachineId}";

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is MachineBatchShadowPriceKey other && MachineId == other.MachineId;

    /// <inheritdoc/>
    public override int GetHashCode() => MachineId.GetHashCode();
}

/// <summary>
/// 设备业务产能影子价格键 / Machine business capacity shadow price key
/// </summary>
public sealed class MachineCapacityShadowPriceKey : Csp1dShadowPriceKey {
    public MachineCapacityShadowPriceKey(string machineId) : base(typeof(MachineCapacityShadowPriceKey)) {
        MachineId = machineId;
    }

    public string MachineId { get; }

    /// <inheritdoc/>
    public override string Name => $"machine-capacity:{MachineId}";

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is MachineCapacityShadowPriceKey other && MachineId == other.MachineId;

    /// <inheritdoc/>
    public override int GetHashCode() => MachineId.GetHashCode();
}

/// <summary>
/// 产出超产上限影子价格键 / Yield over-production bound shadow price key
/// </summary>
public sealed class YieldOverProductionBoundShadowPriceKey : Csp1dShadowPriceKey {
    public YieldOverProductionBoundShadowPriceKey(string productId, string unitSymbol) : base(typeof(YieldOverProductionBoundShadowPriceKey)) {
        ProductId = productId;
        UnitSymbol = unitSymbol;
    }

    public string ProductId { get; }
    public string UnitSymbol { get; }

    /// <inheritdoc/>
    public override string Name => $"yield-over-production-bound:{ProductId}:{UnitSymbol}";

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is YieldOverProductionBoundShadowPriceKey other && ProductId == other.ProductId && UnitSymbol == other.UnitSymbol;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(ProductId, UnitSymbol);
}

/// <summary>
/// CSP1D 影子价格参数接口 / CSP1D shadow price arguments interface
///
/// 用于列生成管线计算 reduced cost 时传递参数。
/// Used for passing arguments when computing reduced cost in column generation pipelines.
/// </summary>
public interface ICsp1dShadowPriceArguments { }

/// <summary>
/// CSP1D 影子价格参数基类（别名）/ CSP1D shadow price arguments base class (alias).
/// </summary>
public abstract class AbstractCsp1dShadowPriceArguments : ICsp1dShadowPriceArguments { }

/// <summary>
/// 切割方案影子价格参数 / Cutting plan shadow price arguments
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dCuttingPlanShadowPriceArguments<V> : AbstractCsp1dShadowPriceArguments where V : struct {
    public Csp1dCuttingPlanShadowPriceArguments(CuttingPlan<V> plan) { Plan = plan; }
    public CuttingPlan<V> Plan { get; }
}

/// <summary>
/// 切割方案影子价格表（轻量级结果容器） / Cutting plan shadow price map (lightweight result container)
///
/// 用于 pricing 阶段消费影子价格，不依赖框架 AbstractShadowPriceMap 的完整生命周期。
/// Used for consuming shadow prices during pricing, without depending on the full AbstractShadowPriceMap lifecycle.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class ShadowPriceMap<V> where V : struct {
    private readonly IReadOnlyDictionary<Csp1dShadowPriceKey, V> _values;

    /// <summary>
    /// 构造影子价格映射 / Construct a shadow price map
    /// </summary>
    /// <param name="values">影子价格值映射 / Shadow price value map.</param>
    public ShadowPriceMap(IReadOnlyDictionary<Csp1dShadowPriceKey, V>? values = null) {
        _values = values ?? new Dictionary<Csp1dShadowPriceKey, V>();
    }

    /// <summary>
    /// 查询影子价格 / Get shadow price by key
    /// </summary>
    /// <param name="key">影子价格键 / Shadow price key.</param>
    /// <returns>对应影子价格 / Matched shadow price.</returns>
    public V? this[Csp1dShadowPriceKey key] =>
        _values.TryGetValue(key, out V value) ? value : default;
}

/// <summary>
/// 抽象 CSP1D 影子价格映射 / Abstract CSP1D shadow price map
/// </summary>
/// <typeparam name="Args">参数类型 / Arguments type.</typeparam>
public class AbstractCsp1dShadowPriceMap<Args>
    : AbstractShadowPriceMap<Args, AbstractCsp1dShadowPriceMap<Args>>
    where Args : class {
}

/// <summary>
/// ShadowPriceMap 工厂扩展 / ShadowPriceMap factory extensions
/// </summary>
public static class ShadowPriceMapExtensions {
    /// <summary>
    /// 从框架 AbstractShadowPriceMap 提取轻量级 ShadowPriceMap / Extract lightweight ShadowPriceMap from framework AbstractShadowPriceMap
    /// </summary>
    public static ShadowPriceMap<V> ToShadowPriceMap<V>(
        this AbstractCsp1dShadowPriceMap<AbstractCsp1dShadowPriceArguments> shadowPriceMap,
        Func<Flt64, V> converter) where V : struct {
        var values = new Dictionary<Csp1dShadowPriceKey, V>();
        foreach ((ShadowPriceKey? key, ShadowPrice? price) in shadowPriceMap.Map) {
            if (key is Csp1dShadowPriceKey cspKey) {
                values[cspKey] = converter(price.Price);
            }
        }
        return new ShadowPriceMap<V>(values);
    }
}
