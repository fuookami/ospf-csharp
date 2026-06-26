#nullable enable
#pragma warning disable CS8714

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;

/// <summary>
/// 基于物理量的生产力日历，结合工作日历和生产力信息 /
/// Quantity-based productivity calendar combining working calendar and productivity information.
/// 从 kotlin WorkingCalendar.kt QuantityProductivityCalendar 类移植。
/// </summary>
/// <typeparam name="W">时间窗口数值类型 / The time-window numeric type.</typeparam>
/// <typeparam name="V">产量数值类型 / The quantity value type.</typeparam>
/// <typeparam name="P">生产力类型 / The productivity type.</typeparam>
/// <typeparam name="T">材料类型 / The material type.</typeparam>
/// <typeparam name="U">键类型 / The key type.</typeparam>
public class QuantityProductivityCalendar<W, V, P, T, U> : WorkingCalendar<W>
    where W : struct, IRealNumber<W>
    where V : struct, IRealNumber<V>
    where P : QuantityProductivity<V, T, U> {
    private readonly PhysicalUnit _quantityUnit;
    private readonly INumericConstants<V> _constants;
    private readonly Func<TimeSpan, Flt64> _durationToValue;
    private readonly Func<Flt64, TimeSpan> _valueToDuration;
    private readonly Func<V, Flt64> _quantityValueToFlt64;
    private readonly Func<Flt64, V> _flt64ToQuantityValue;
    private readonly Func<Flt64, Quantity<V>> _quantityFloor;

    private readonly Lazy<IReadOnlyList<P>> _productivity;
    private readonly Lazy<IReadOnlyDictionary<U, TimeSpan>> _averageCapacity;
    private readonly Lazy<IReadOnlyDictionary<U, Quantity<V>>> _averageUnitYield;
    private readonly Lazy<IReadOnlyList<TimeRange>> _unavailableTimesLazy;

    /// <summary>
    /// 构造基于物理量的生产力日历 / Construct a quantity-based productivity calendar.
    /// </summary>
    public QuantityProductivityCalendar(
        TimeWindow<W> timeWindow,
        IReadOnlyList<P> productivity,
        PhysicalUnit quantityUnit,
        INumericConstants<V> constants,
        Func<TimeSpan, Flt64> durationToValue,
        Func<Flt64, TimeSpan> valueToDuration,
        Func<V, Flt64> quantityValueToFlt64,
        Func<Flt64, V> flt64ToQuantityValue,
        Func<Flt64, Quantity<V>> quantityFloor,
        IReadOnlyList<TimeRange>? unavailableTimes = null)
        : base(timeWindow, unavailableTimes) {
        _quantityUnit = quantityUnit;
        _constants = constants;
        _durationToValue = durationToValue;
        _valueToDuration = valueToDuration;
        _quantityValueToFlt64 = quantityValueToFlt64;
        _flt64ToQuantityValue = flt64ToQuantityValue;
        _quantityFloor = quantityFloor;

        _productivity = new Lazy<IReadOnlyList<P>>(() => {
            if (unavailableTimes != null) {
                return productivity
                    .SelectMany(p => {
                        IReadOnlyList<TimeRange> timeRanges = p.TimeWindow.DifferenceWith(unavailableTimes);
                        return timeRanges.Select(tr => (P)p.New(timeWindow: tr));
                    })
                    .OrderBy(p => p.TimeWindow.Start)
                    .ToList();
            }
            return productivity.OrderBy(p => p.TimeWindow.Start).ToList();
        });

        _averageCapacity = new Lazy<IReadOnlyDictionary<U, TimeSpan>>(() => {
            IEnumerable<U> materials = Productivity.SelectMany(p => p.Capacities.Keys).Distinct();
            var result = new Dictionary<U, TimeSpan>();
            foreach (U? material in materials) {
                var rawEntries = new List<(TimeSpan Duration, TimeSpan Capacity)>();
                foreach (P p in Productivity) {
                    if (p.Capacities.TryGetValue(material, out TimeSpan cap)) {
                        rawEntries.Add((p.TimeWindow.Duration, cap));
                    }
                }
                List<(TimeSpan Duration, TimeSpan Capacity)> entries = rawEntries;
                double totalWeighted = entries.Aggregate(0.0, (acc, e) => acc + _durationToValue(e.Duration).ToDouble() * _durationToValue(e.Capacity).ToDouble());
                double totalDuration = entries.Aggregate(0.0, (acc, e) => acc + _durationToValue(e.Duration).ToDouble());
                result[material] = _valueToDuration(new Flt64(totalWeighted / totalDuration));
            }
            return result;
        });

        _averageUnitYield = new Lazy<IReadOnlyDictionary<U, Quantity<V>>>(() => {
            IEnumerable<U> materials = Productivity.SelectMany(p => p.UnitYields.Keys).Distinct();
            var result = new Dictionary<U, Quantity<V>>();
            foreach (U? material in materials) {
                var entries = Productivity
                    .Select(p => p.UnitYields.TryGetValue(material, out Quantity<V>? qty)
                        ? new (TimeSpan Duration, Quantity<V> Qty)?((p.TimeWindow.Duration, qty))
                        : null)
                    .Where(e => e.HasValue)
                    .Select(e => e!.Value)
                    .ToList();
                if (entries.Count == 0) {
                    result[material] = new Quantity<V>(_constants.Zero, _quantityUnit);
                }
                else {
                    var units = entries.Select(e => e.Qty.Unit).Distinct().ToList();
                    if (units.Count != 1) {
                        throw new InvalidOperationException($"Inconsistent unitYield units for material '{material}': [{string.Join(", ", units)}]");
                    }
                    PhysicalUnit yieldUnit = units[0];
                    double totalWeighted = entries.Aggregate(0.0, (acc, e) => acc + _quantityValueToFlt64(e.Qty.Value).ToDouble() * _durationToValue(e.Duration).ToDouble());
                    TimeSpan totalDuration = entries.Aggregate(TimeSpan.Zero, (acc, e) => acc + e.Duration);
                    double avgValue = totalWeighted / _durationToValue(totalDuration).ToDouble();
                    Quantity<V> floored = _quantityFloor(new Flt64(avgValue));
                    result[material] = new Quantity<V>(ExtractQuantityValue(floored), yieldUnit);
                }
            }
            return result;
        });

        _unavailableTimesLazy = new Lazy<IReadOnlyList<TimeRange>>(() => {
            if (unavailableTimes != null) {
                return base.UnavailableTimes;
            }

            var result = new List<TimeRange>();
            IReadOnlyList<P> prods = Productivity;
            for (int i = 0; i < prods.Count; i++) {
                if (i == 0) {
                    TimeRange? front = prods[i].TimeWindow.Front;
                    if (front != null) {
                        result.Add(front);
                    }
                }
                else {
                    TimeRange? gap = prods[i].TimeWindow.FrontBetween(prods[i - 1].TimeWindow);
                    if (gap != null) {
                        result.Add(gap);
                    }
                }
                if (i == prods.Count - 1) {
                    TimeRange? back = prods[i].TimeWindow.Back;
                    if (back != null) {
                        result.Add(back);
                    }
                }
            }
            return result;
        });
    }

    /// <summary>生产力列表（已处理不可用时间）/ Productivity list (with unavailable times processed).</summary>
    public IReadOnlyList<P> Productivity => _productivity.Value;

    /// <summary>平均产能映射 / Average capacity mapping.</summary>
    public IReadOnlyDictionary<U, TimeSpan> AverageCapacity => _averageCapacity.Value;

    /// <summary>平均单位产量映射（携带单位）/ Average unit yield mapping (with unit).</summary>
    public IReadOnlyDictionary<U, Quantity<V>> AverageUnitYield => _averageUnitYield.Value;

    /// <summary>不可用时间列表 / Unavailable times.</summary>
    public new IReadOnlyList<TimeRange> UnavailableTimes => _unavailableTimesLazy.Value;

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>查找从指定时间开始的最早有效开始时间 / Find the earliest valid start time.</summary>
    public DateTimeOffset ActualStartTimeFrom(
        T material, DateTimeOffset startTime,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null, DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<P> productivityCalendar = Productivity.FindFrom(startTime, p => p.TimeWindow);
        if (productivityCalendar.Count == 0) {
            return TimeRange.DistantFuture;
        }

        DateTimeOffset currentTime = startTime;
        foreach (P? calendar in productivityCalendar) {
            Flt64? currentProductivity = ProductivityRateOf(calendar, material);
            if (currentProductivity == null) {
                continue;
            }

            TimeRange? intersection = calendar.TimeWindow.IntersectionWith(new TimeRange(currentTime));
            if (intersection == null) {
                continue;
            }

            ValidTimesResult validTimesResult = ValidTimes(
                time: intersection, unavailableTimes: unavailableTimes,
                beforeConnectionTime: beforeConnectionTime, afterConnectionTime: afterConnectionTime,
                beforeConditionalConnectionTime: beforeConditionalConnectionTime,
                afterConditionalConnectionTime: afterConditionalConnectionTime,
                currentDuration: currentTime == startTime ? currentDuration : null,
                maxDuration: TimeSpan.MaxValue, breakTime: breakTime);
            foreach (TimeRange produceTime in validTimesResult.Times) {
                double thisQuantity = _durationToValue(produceTime.Duration).ToDouble() * currentProductivity.Value.ToDouble();
                if (thisQuantity > 0.0) {
                    return produceTime.Start;
                }
            }
            currentTime = MaxEndTime(validTimesResult.Times, validTimesResult.BreakTimes, validTimesResult.ConnectionTimes);
        }
        return TimeRange.DistantFuture;
    }

    /// <summary>计算从指定时间开始的实际时间范围 / Calculate the actual time range from a start time.</summary>
    public ActualTimeResult ActualTimeFrom(
        T material, DateTimeOffset startTime, Quantity<V> quantity,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null, DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<P> productivityCalendar = Productivity.FindFrom(startTime, p => p.TimeWindow);
        if (productivityCalendar.Count == 0) {
            return new ActualTimeResult {
                Time = new TimeRange(startTime, TimeRange.DistantFuture),
                WorkingTimes = Array.Empty<TimeRange>(),
                BreakTimes = Array.Empty<TimeRange>(),
                ConnectionTimes = Array.Empty<TimeRange>()
            };
        }
        return ActualTimeFromInternal(material, startTime, productivityCalendar, quantity,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime,
            currentDuration, breakTime);
    }

    /// <summary>计算从指定时间开始的实际时间范围（可返回 null）/ Returns null if not possible.</summary>
    public ActualTimeResult? ActualTimeFromOrNull(
        T material, DateTimeOffset startTime, Quantity<V> quantity,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null, DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<P> productivityCalendar = Productivity.FindFrom(startTime, p => p.TimeWindow);
        if (productivityCalendar.Count == 0) {
            return null;
        }

        return ActualTimeFromInternal(material, startTime, productivityCalendar, quantity,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime,
            currentDuration, breakTime);
    }

    /// <summary>计算在指定结束时间之前的实际时间范围 / Calculate actual time range until end time.</summary>
    public ActualTimeResult ActualTimeUntil(
        T material, DateTimeOffset endTime, Quantity<V> quantity,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null, DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        var productivityCalendar = Productivity.FindUntil(endTime, p => p.TimeWindow).Reverse().ToList();
        if (productivityCalendar.Count == 0) {
            return new ActualTimeResult {
                Time = new TimeRange(TimeRange.DistantPast, endTime),
                WorkingTimes = Array.Empty<TimeRange>(),
                BreakTimes = Array.Empty<TimeRange>(),
                ConnectionTimes = Array.Empty<TimeRange>()
            };
        }
        return ActualTimeUntilInternal(material, endTime, productivityCalendar, quantity,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime, breakTime);
    }

    /// <summary>计算在指定结束时间之前的实际时间范围（可返回 null）/ Returns null if not possible.</summary>
    public ActualTimeResult? ActualTimeUntilOrNull(
        T material, DateTimeOffset endTime, Quantity<V> quantity,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null, DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        var productivityCalendar = Productivity.FindUntil(endTime, p => p.TimeWindow).Reverse().ToList();
        if (productivityCalendar.Count == 0) {
            return null;
        }

        return ActualTimeUntilInternal(material, endTime, productivityCalendar, quantity,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime, breakTime);
    }

    /// <summary>计算在指定时间范围内的实际产量 / Calculate actual quantity within a time range.</summary>
    public Quantity<V> ActualQuantity(
        T material, TimeRange time,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null, DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<P> productivityCalendar = Productivity.Find(time, p => p.TimeWindow);
        if (productivityCalendar.Count == 0) {
            return new Quantity<V>(_constants.Zero, ResolveQuantityUnit(material));
        }
        return ActualQuantityInternal(material, time, productivityCalendar,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime,
            currentDuration, breakTime);
    }

    /// <summary>计算在指定时间范围内的实际产量（可返回 null）/ Returns null if no productivity found.</summary>
    public Quantity<V>? ActualQuantityOrNull(
        T material, TimeRange time,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null, DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<P> productivityCalendar = Productivity.Find(time, p => p.TimeWindow);
        if (productivityCalendar.Count == 0) {
            return null;
        }

        return ActualQuantityInternal(material, time, productivityCalendar,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime,
            currentDuration, breakTime);
    }

    // =========================================================================
    // Private implementation
    // =========================================================================

    private PhysicalUnit ResolveQuantityUnit(T material, IReadOnlyList<P>? productivityList = null) {
        IReadOnlyList<P> list = productivityList ?? Productivity;
        var units = list
            .Select(p => p.UnitYieldOf(material))
            .Where(q => q != null)
            .Select(q => q!.Unit)
            .Distinct()
            .ToList();
        if (units.Count > 1) {
            throw new InvalidOperationException($"Inconsistent unitYield units for material '{material}': [{string.Join(", ", units)}]");
        }
        return units.FirstOrDefault() ?? _quantityUnit;
    }

    private void ValidateQuantityUnit(T material, IReadOnlyList<P> productivityList, Quantity<V> quantity) {
        PhysicalUnit expected = ResolveQuantityUnit(material, productivityList);
        if (quantity.Unit != expected) {
            throw new InvalidOperationException($"Quantity unit '{quantity.Unit}' does not match productivity unit '{expected}'");
        }
    }

    private Flt64? ProductivityRateOf(P productivity, T material) {
        Quantity<V>? unitYield = productivity.UnitYieldOf(material);
        if (unitYield is Quantity<V> qty) {
            V qtyValue = ExtractQuantityValue(qty);
            return _quantityValueToFlt64(qtyValue);
        }
        TimeSpan? capacity = productivity.CapacityOf(material);
        if (capacity != null) {
            return new Flt64(1.0 / _durationToValue(capacity.Value).ToDouble());
        }

        return null;
    }

    private ActualTimeResult ActualTimeFromInternal(
        T material, DateTimeOffset startTime, IReadOnlyList<P> productivityCalendar,
        Quantity<V> quantity, IReadOnlyList<TimeRange> mergedUnavailableTimes,
        DurationRange? beforeConnectionTime, DurationRange? afterConnectionTime,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime,
        TimeSpan? currentDuration, (DurationRange unit, TimeSpan breakDuration)? breakTime) {
        ValidateQuantityUnit(material, productivityCalendar, quantity);
        double produceQuantity = 0.0;
        DateTimeOffset currentTime = startTime;
        var workingTimes = new List<TimeRange>();
        var breakTimesList = new List<TimeRange>();
        var connectionTimes = new List<TimeRange>();
        double quantityValue = _quantityValueToFlt64(quantity.Value).ToDouble();

        foreach (P calendar in productivityCalendar) {
            Flt64? currentProductivity = ProductivityRateOf(calendar, material);
            if (currentProductivity == null) {
                continue;
            }

            TimeSpan maxDuration = CeilDuration((quantityValue - produceQuantity) / currentProductivity.Value.ToDouble());
            TimeRange? intersection = calendar.TimeWindow.IntersectionWith(new TimeRange(currentTime));
            if (intersection == null) {
                continue;
            }

            ValidTimesResult validTimesResult = ValidTimesStatic(
                time: intersection, unavailableTimes: mergedUnavailableTimes,
                beforeConnectionTime: beforeConnectionTime, afterConnectionTime: afterConnectionTime,
                beforeConditionalConnectionTime: beforeConditionalConnectionTime,
                afterConditionalConnectionTime: afterConditionalConnectionTime,
                currentDuration: currentTime == startTime ? currentDuration : null,
                maxDuration: maxDuration, breakTime: breakTime);

            workingTimes.AddRange(validTimesResult.Times);
            breakTimesList.AddRange(validTimesResult.BreakTimes);
            connectionTimes.AddRange(validTimesResult.ConnectionTimes);

            foreach (TimeRange produceTime in validTimesResult.Times) {
                double thisQuantity = _durationToValue(produceTime.Duration).ToDouble() * currentProductivity.Value.ToDouble();
                produceQuantity += global::System.Math.Min(quantityValue - produceQuantity, thisQuantity);
            }
            currentTime = MaxEndTime(validTimesResult.Times, validTimesResult.BreakTimes, validTimesResult.ConnectionTimes);
            if (global::System.Math.Abs(produceQuantity - quantityValue) < 1e-10) {
                break;
            }
        }

        if (global::System.Math.Abs(produceQuantity - quantityValue) < 1e-10) {
            return new ActualTimeResult { Time = new TimeRange(startTime, currentTime), WorkingTimes = workingTimes, BreakTimes = breakTimesList, ConnectionTimes = connectionTimes };
        }
        return new ActualTimeResult { Time = new TimeRange(startTime, TimeRange.DistantFuture), WorkingTimes = workingTimes, BreakTimes = breakTimesList, ConnectionTimes = connectionTimes };
    }

    private ActualTimeResult ActualTimeUntilInternal(
        T material, DateTimeOffset endTime, IReadOnlyList<P> productivityCalendar,
        Quantity<V> quantity, IReadOnlyList<TimeRange> mergedUnavailableTimes,
        DurationRange? beforeConnectionTime, DurationRange? afterConnectionTime,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime,
        (DurationRange unit, TimeSpan breakDuration)? breakTime) {
        ValidateQuantityUnit(material, productivityCalendar, quantity);
        double produceQuantity = 0.0;
        DateTimeOffset currentTime = endTime;
        var workingTimes = new List<TimeRange>();
        var breakTimesList = new List<TimeRange>();
        var connectionTimes = new List<TimeRange>();
        double quantityValue = _quantityValueToFlt64(quantity.Value).ToDouble();

        foreach (P calendar in productivityCalendar) {
            Flt64? currentProductivity = ProductivityRateOf(calendar, material);
            if (currentProductivity == null) {
                continue;
            }

            TimeSpan maxDuration = CeilDuration((quantityValue - produceQuantity) / currentProductivity.Value.ToDouble());
            TimeRange? intersection = calendar.TimeWindow.IntersectionWith(new TimeRange(TimeRange.DistantPast, currentTime));
            if (intersection == null) {
                continue;
            }

            ValidTimesResult validTimesResult = ReversedValidTimesStatic(
                time: intersection, unavailableTimes: mergedUnavailableTimes,
                beforeConnectionTime: beforeConnectionTime, afterConnectionTime: afterConnectionTime,
                beforeConditionalConnectionTime: beforeConditionalConnectionTime,
                afterConditionalConnectionTime: afterConditionalConnectionTime,
                maxDuration: maxDuration, breakTime: breakTime);

            workingTimes.AddRange(validTimesResult.Times);
            breakTimesList.AddRange(validTimesResult.BreakTimes);
            connectionTimes.AddRange(validTimesResult.ConnectionTimes);

            foreach (TimeRange produceTime in validTimesResult.Times) {
                double thisQuantity = _durationToValue(produceTime.Duration).ToDouble() * currentProductivity.Value.ToDouble();
                produceQuantity += global::System.Math.Min(quantityValue - produceQuantity, thisQuantity);
            }
            currentTime = MinStartTime(validTimesResult.Times, validTimesResult.BreakTimes, validTimesResult.ConnectionTimes);
            if (global::System.Math.Abs(produceQuantity - quantityValue) < 1e-10) {
                break;
            }
        }

        if (global::System.Math.Abs(produceQuantity - quantityValue) < 1e-10) {
            return new ActualTimeResult { Time = new TimeRange(currentTime, endTime), WorkingTimes = workingTimes, BreakTimes = breakTimesList, ConnectionTimes = connectionTimes };
        }
        return new ActualTimeResult { Time = new TimeRange(TimeRange.DistantPast, endTime), WorkingTimes = workingTimes, BreakTimes = breakTimesList, ConnectionTimes = connectionTimes };
    }

    private Quantity<V> ActualQuantityInternal(
        T material, TimeRange time, IReadOnlyList<P> productivityCalendar,
        IReadOnlyList<TimeRange> mergedUnavailableTimes,
        DurationRange? beforeConnectionTime, DurationRange? afterConnectionTime,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime,
        TimeSpan? currentDuration, (DurationRange unit, TimeSpan breakDuration)? breakTime) {
        PhysicalUnit resolvedUnit = ResolveQuantityUnit(material, productivityCalendar);
        ValidTimesResult validTimesResult = ValidTimesStatic(
            time: time, unavailableTimes: mergedUnavailableTimes,
            beforeConnectionTime: beforeConnectionTime, afterConnectionTime: afterConnectionTime,
            beforeConditionalConnectionTime: beforeConditionalConnectionTime,
            afterConditionalConnectionTime: afterConditionalConnectionTime,
            currentDuration: currentDuration, breakTime: breakTime);

        double totalFlt64 = 0.0;
        foreach (P calendar in productivityCalendar) {
            foreach (TimeRange validTime in validTimesResult.Times) {
                if (validTime.End <= calendar.TimeWindow.Start) {
                    continue;
                }

                if (validTime.Start >= calendar.TimeWindow.End) {
                    break;
                }

                TimeSpan? produceTimeSpan = validTime.IntersectionWith(calendar.TimeWindow)?.Duration;
                if (produceTimeSpan == null) {
                    continue;
                }

                Flt64 currentProductivity = ProductivityRateOf(calendar, material) ?? new Flt64(0.0);
                totalFlt64 += _durationToValue(produceTimeSpan.Value).ToDouble() * currentProductivity.ToDouble();
            }
        }
        Quantity<V> floored = _quantityFloor(new Flt64(totalFlt64));
        return new Quantity<V>(ExtractQuantityValue(floored), resolvedUnit);
    }

    private TimeSpan CeilDuration(double valueInUnits) => TimeWindow.Ceil(_valueToDuration(new Flt64(valueInUnits)));

    /// <summary>从 Quantity 提取 V 值（避免 record Value 属性与 IInvariant.Value() 方法冲突）/
    /// Extract V value from Quantity (avoids record Value property vs IInvariant.Value() method conflict).</summary>
    private static V ExtractQuantityValue(Quantity<V> qty) {
        (V val, PhysicalUnit _) = qty;
        return val;
    }

    private IReadOnlyList<TimeRange> MergeUnavailableTimes(IReadOnlyList<TimeRange>? extra) {
        if (extra == null || extra.Count == 0) {
            return UnavailableTimes;
        }

        return TimeRange.Merge(extra.Concat(UnavailableTimes).ToList());
    }
}

/// <summary>
/// 离散物理量生产力日历，使用 UInt64 作为产量数值类型 /
/// Discrete quantity-based productivity calendar using UInt64 as the quantity value type.
/// </summary>
public class DiscreteQuantityProductivityCalendar<W, P, T, U>
    : QuantityProductivityCalendar<W, Fuookami.Ospf.Math.Algebra.Number.UInt64, P, T, U>
    where W : struct, IRealNumber<W>
    where P : QuantityProductivity<Fuookami.Ospf.Math.Algebra.Number.UInt64, T, U> {
    private static readonly Fuookami.Ospf.Math.Algebra.Number.INumericConstants<Fuookami.Ospf.Math.Algebra.Number.UInt64> UInt64Constants =
        Fuookami.Ospf.Math.Algebra.Number.NumericConstantsRegistry.For<Fuookami.Ospf.Math.Algebra.Number.UInt64>();

    public DiscreteQuantityProductivityCalendar(
        TimeWindow<W> timeWindow,
        IReadOnlyList<P> productivity,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        PhysicalUnit? quantityUnit = null)
        : base(
            timeWindow: timeWindow,
            productivity: productivity,
            quantityUnit: quantityUnit ?? NoneUnit.Instance,
            constants: UInt64Constants,
            durationToValue: duration => new Flt64(duration.TotalSeconds / timeWindow.Interval.TotalSeconds),
            valueToDuration: value => TimeSpan.FromSeconds(global::System.Math.Floor(value.ToDouble()) * timeWindow.Interval.TotalSeconds),
            quantityValueToFlt64: v => v.ToFlt64(),
            flt64ToQuantityValue: value => new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)global::System.Math.Floor(value.ToDouble())),
            quantityFloor: value => new Quantity<Fuookami.Ospf.Math.Algebra.Number.UInt64>(
                new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)global::System.Math.Floor(value.ToDouble())),
                quantityUnit ?? NoneUnit.Instance),
            unavailableTimes: unavailableTimes) {
    }
}

/// <summary>
/// 连续物理量生产力日历，产量数值类型由调用方提供 /
/// Continuous quantity-based productivity calendar with caller-provided quantity value type.
/// </summary>
public class ContinuousQuantityProductivityCalendar<W, V, P, T, U>
    : QuantityProductivityCalendar<W, V, P, T, U>
    where W : struct, IRealNumber<W>
    where V : struct, IRealNumber<V>
    where P : QuantityProductivity<V, T, U> {
    public ContinuousQuantityProductivityCalendar(
        TimeWindow<W> timeWindow,
        IReadOnlyList<P> productivity,
        PhysicalUnit quantityUnit,
        INumericConstants<V> constants,
        Func<TimeSpan, Flt64> durationToValue,
        Func<Flt64, TimeSpan> valueToDuration,
        Func<V, Flt64> quantityValueToFlt64,
        Func<Flt64, V> flt64ToQuantityValue,
        Func<Flt64, Quantity<V>> quantityFloor,
        IReadOnlyList<TimeRange>? unavailableTimes = null)
        : base(
            timeWindow: timeWindow,
            productivity: productivity,
            quantityUnit: quantityUnit,
            constants: constants,
            durationToValue: durationToValue,
            valueToDuration: valueToDuration,
            quantityValueToFlt64: quantityValueToFlt64,
            flt64ToQuantityValue: flt64ToQuantityValue,
            quantityFloor: quantityFloor,
            unavailableTimes: unavailableTimes) {
    }
}
#pragma warning restore CS8714
