#nullable enable
#pragma warning disable CS8714

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;

/// <summary>
/// 基于物理量的生产力，描述时间窗口内的生产能力，产出数量携带单位 /
/// Quantity-based productivity describing production capacity within a time window,
/// with production quantities carrying physical units.
/// 从 kotlin WorkingCalendar.kt QuantityProductivity 类移植。
/// </summary>
/// <typeparam name="V">数值类型 / The numeric value type.</typeparam>
/// <typeparam name="T">材料类型 / The material type.</typeparam>
/// <typeparam name="U">键类型 / The key type.</typeparam>
public class QuantityProductivity<V, T, U>
    where V : struct, IRealNumber<V> {
    /// <summary>时间窗口 / The time window.</summary>
    public TimeRange TimeWindow { get; }
    /// <summary>键提取器 / The key extractor.</summary>
    public Func<T, U> Extractor { get; }
    /// <summary>材料到生产单位所需时间的映射 / Mapping from material to time required per unit.</summary>
    public IReadOnlyDictionary<U, TimeSpan> Capacities { get; }
    /// <summary>材料到单位时间产量的映射（携带单位）/ Mapping from material to unit time production (with unit).</summary>
    public IReadOnlyDictionary<U, Quantity<V>> UnitYields { get; }
    /// <summary>条件产能列表 / The list of conditional capacities.</summary>
    public IReadOnlyList<(Func<T, bool> condition, TimeSpan capacity)> ConditionCapacities { get; }
    /// <summary>条件单位产量列表（携带单位）/ The list of conditional unit yields (with unit).</summary>
    public IReadOnlyList<(Func<T, bool> condition, Quantity<V> unitYield)> ConditionUnitYields { get; }

    private readonly Dictionary<T, TimeSpan?> _capacityCache = new();
    private readonly Dictionary<T, Quantity<V>?> _unitYieldCache = new();

    /// <summary>
    /// 构造基于物理量的生产力 / Construct a quantity-based productivity.
    /// </summary>
    public QuantityProductivity(
        TimeRange timeWindow,
        Func<T, U> extractor,
        IReadOnlyDictionary<U, TimeSpan>? capacities = null,
        IReadOnlyDictionary<U, Quantity<V>>? unitYields = null,
        IReadOnlyList<(Func<T, bool>, TimeSpan)>? conditionCapacities = null,
        IReadOnlyList<(Func<T, bool>, Quantity<V>)>? conditionUnitYields = null) {
        TimeWindow = timeWindow;
        Extractor = extractor;
        Capacities = capacities ?? new Dictionary<U, TimeSpan>();
        UnitYields = unitYields ?? new Dictionary<U, Quantity<V>>();
        ConditionCapacities = conditionCapacities ?? Array.Empty<(Func<T, bool>, TimeSpan)>();
        ConditionUnitYields = conditionUnitYields ?? Array.Empty<(Func<T, bool>, Quantity<V>)>();
    }

    /// <summary>获取指定材料的产能 / Get the capacity for the specified material.</summary>
    public TimeSpan? CapacityOf(T material) {
        if (Capacities.TryGetValue(Extractor(material), out TimeSpan cap)) {
            return cap;
        }

        if (_capacityCache.TryGetValue(material, out TimeSpan? cached)) {
            return cached;
        }

        (Func<T, bool> condition, TimeSpan capacity) result = ConditionCapacities.FirstOrDefault(c => c.condition(material));
        TimeSpan? value = result.capacity != default ? result.capacity : null;
        _capacityCache[material] = value;
        return value;
    }

    /// <summary>获取指定材料的单位产量（携带单位）/ Get the unit yield for the specified material (with unit).</summary>
    public Quantity<V>? UnitYieldOf(T material) {
        if (UnitYields.TryGetValue(Extractor(material), out Quantity<V>? yld)) {
            return yld;
        }

        if (_unitYieldCache.TryGetValue(material, out Quantity<V>? cached)) {
            return cached;
        }

        (Func<T, bool> condition, Quantity<V> unitYield) result = ConditionUnitYields.FirstOrDefault(c => c.condition(material));
        Quantity<V>? value = result.unitYield;
        _unitYieldCache[material] = value;
        return value;
    }

    /// <summary>创建新的生产力实例 / Create a new productivity instance.</summary>
    public QuantityProductivity<V, T, U> New(
        TimeRange? timeWindow = null,
        Func<T, U>? extractor = null,
        IReadOnlyDictionary<U, TimeSpan>? capacities = null,
        IReadOnlyDictionary<U, Quantity<V>>? unitYields = null,
        IReadOnlyList<(Func<T, bool>, TimeSpan)>? conditionCapacities = null,
        IReadOnlyList<(Func<T, bool>, Quantity<V>)>? conditionUnitYields = null) {
        return new QuantityProductivity<V, T, U>(
            timeWindow: timeWindow ?? this.TimeWindow,
            extractor: extractor ?? this.Extractor,
            capacities: capacities ?? this.Capacities,
            unitYields: unitYields ?? this.UnitYields,
            conditionCapacities: conditionCapacities ?? this.ConditionCapacities,
            conditionUnitYields: conditionUnitYields ?? this.ConditionUnitYields);
    }
}
#pragma warning restore CS8714
