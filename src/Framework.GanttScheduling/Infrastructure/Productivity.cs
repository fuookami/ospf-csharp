#nullable enable
#pragma warning disable CS8714

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;

/// <summary>
/// 生产力，描述时间窗口内的生产能力 /
/// Productivity describing production capacity within a time window.
/// 从 kotlin WorkingCalendar.kt Productivity 类移植。Ported from kotlin WorkingCalendar.kt Productivity class.
/// </summary>
/// <typeparam name="Q">产量类型 / The quantity type.</typeparam>
/// <typeparam name="T">材料类型 / The material type.</typeparam>
/// <typeparam name="U">键类型 / The key type.</typeparam>
public class Productivity<Q, T, U> {
    /// <summary>时间窗口 / The time window.</summary>
    public TimeRange TimeWindow { get; }
    /// <summary>键提取器 / The key extractor.</summary>
    public Func<T, U> Extractor { get; }
    /// <summary>材料到生产单位所需时间的映射 / Mapping from material to time required per unit.</summary>
    public IReadOnlyDictionary<U, TimeSpan> Capacities { get; }
    /// <summary>材料到单位时间产量的映射 / Mapping from material to unit time production.</summary>
    public IReadOnlyDictionary<U, Q> UnitYields { get; }
    /// <summary>条件产能列表 / The list of conditional capacities.</summary>
    public IReadOnlyList<(Func<T, bool> condition, TimeSpan capacity)> ConditionCapacities { get; }
    /// <summary>条件单位产量列表 / The list of conditional unit yields.</summary>
    public IReadOnlyList<(Func<T, bool> condition, Q unitYield)> ConditionUnitYields { get; }

    private readonly Dictionary<T, TimeSpan?> _capacityCache = new();
    private readonly Dictionary<T, Q?> _unitYieldCache = new();

    /// <summary>
    /// 构造生产力 / Construct a productivity.
    /// </summary>
    public Productivity(
        TimeRange timeWindow,
        Func<T, U> extractor,
        IReadOnlyDictionary<U, TimeSpan>? capacities = null,
        IReadOnlyDictionary<U, Q>? unitYields = null,
        IReadOnlyList<(Func<T, bool>, TimeSpan)>? conditionCapacities = null,
        IReadOnlyList<(Func<T, bool>, Q)>? conditionUnitYields = null) {
        TimeWindow = timeWindow;
        Extractor = extractor;
        Capacities = capacities ?? new Dictionary<U, TimeSpan>();
        UnitYields = unitYields ?? new Dictionary<U, Q>();
        ConditionCapacities = conditionCapacities ?? Array.Empty<(Func<T, bool>, TimeSpan)>();
        ConditionUnitYields = conditionUnitYields ?? Array.Empty<(Func<T, bool>, Q)>();
    }

    /// <summary>
    /// 获取指定材料的产能 / Get the capacity for the specified material.
    /// </summary>
    /// <param name="material">材料 / The material.</param>
    /// <returns>产能，若无则为 null / The capacity, or null if none.</returns>
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

    /// <summary>
    /// 获取指定材料的单位产量 / Get the unit yield for the specified material.
    /// </summary>
    /// <param name="material">材料 / The material.</param>
    /// <returns>单位产量，若无则为 null / The unit yield, or null if none.</returns>
    public Q? UnitYieldOf(T material) {
        if (UnitYields.TryGetValue(Extractor(material), out Q? yld)) {
            return yld;
        }
        if (_unitYieldCache.TryGetValue(material, out Q? cached)) {
            return cached;
        }
        (Func<T, bool> condition, Q unitYield) result = ConditionUnitYields.FirstOrDefault(c => c.condition(material));
        Q? value = result.unitYield;
        _unitYieldCache[material] = value;
        return value;
    }

    /// <summary>
    /// 创建新的生产力实例 / Create a new productivity instance.
    /// </summary>
    public Productivity<Q, T, U> New(
        TimeRange? timeWindow = null,
        Func<T, U>? extractor = null,
        IReadOnlyDictionary<U, TimeSpan>? capacities = null,
        IReadOnlyDictionary<U, Q>? unitYields = null,
        IReadOnlyList<(Func<T, bool>, TimeSpan)>? conditionCapacities = null,
        IReadOnlyList<(Func<T, bool>, Q)>? conditionUnitYields = null) {
        return new Productivity<Q, T, U>(
            timeWindow: timeWindow ?? this.TimeWindow,
            extractor: extractor ?? this.Extractor,
            capacities: capacities ?? this.Capacities,
            unitYields: unitYields ?? this.UnitYields,
            conditionCapacities: conditionCapacities ?? this.ConditionCapacities,
            conditionUnitYields: conditionUnitYields ?? this.ConditionUnitYields);
    }
}
#pragma warning restore CS8714
