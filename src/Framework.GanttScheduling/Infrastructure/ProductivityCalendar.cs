#nullable enable
#pragma warning disable CS8714

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;

/// <summary>
/// 生产力日历，结合工作日历和生产力信息 /
/// Productivity calendar combining working calendar and productivity information.
/// 从 kotlin WorkingCalendar.kt ProductivityCalendar 类移植。
/// Ported from kotlin WorkingCalendar.kt ProductivityCalendar class.
/// </summary>
/// <typeparam name="W">时间窗口数值类型 / The time-window numeric type.</typeparam>
/// <typeparam name="Q">产量类型 / The quantity type.</typeparam>
/// <typeparam name="P">生产力类型 / The productivity type.</typeparam>
/// <typeparam name="T">材料类型 / The material type.</typeparam>
/// <typeparam name="U">键类型 / The key type.</typeparam>
public class ProductivityCalendar<W, Q, P, T, U> : WorkingCalendar<W>
    where W : struct, IRealNumber<W>
    where Q : struct, IRealNumber<Q>
    where P : Productivity<Q, T, U> {
    private readonly INumericConstants<Q> _constants;
    private readonly Func<TimeSpan, Flt64> _durationToValue;
    private readonly Func<Flt64, TimeSpan> _valueToDuration;
    private readonly Func<Q, Flt64> _quantityToValue;
    private readonly Func<Flt64, Q> _valueToQuantity;

    private readonly Lazy<IReadOnlyList<P>> _productivity;
    private readonly Lazy<IReadOnlyDictionary<U, TimeSpan>> _averageCapacity;
    private readonly Lazy<IReadOnlyDictionary<U, Q>> _averageUnitYield;
    private readonly Lazy<IReadOnlyList<TimeRange>> _unavailableTimesLazy;

    /// <summary>
    /// 构造生产力日历 / Construct a productivity calendar.
    /// </summary>
    /// <param name="timeWindow">时间窗口 / The time window.</param>
    /// <param name="productivity">生产力列表 / The list of productivities.</param>
    /// <param name="constants">数值常量 / The numeric constants.</param>
    /// <param name="durationToValue">TimeSpan 转 Flt64 / TimeSpan to Flt64 converter.</param>
    /// <param name="valueToDuration">Flt64 转 TimeSpan / Flt64 to TimeSpan converter.</param>
    /// <param name="quantityToValue">Q 转 Flt64 / Q to Flt64 converter.</param>
    /// <param name="valueToQuantity">Flt64 转 Q / Flt64 to Q converter.</param>
    /// <param name="unavailableTimes">不可用时间列表 / The list of unavailable times.</param>
    public ProductivityCalendar(
        TimeWindow<W> timeWindow,
        IReadOnlyList<P> productivity,
        INumericConstants<Q> constants,
        Func<TimeSpan, Flt64> durationToValue,
        Func<Flt64, TimeSpan> valueToDuration,
        Func<Q, Flt64> quantityToValue,
        Func<Flt64, Q> valueToQuantity,
        IReadOnlyList<TimeRange>? unavailableTimes = null)
        : base(timeWindow, unavailableTimes) {
        _constants = constants;
        _durationToValue = durationToValue;
        _valueToDuration = valueToDuration;
        _quantityToValue = quantityToValue;
        _valueToQuantity = valueToQuantity;

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
                var entries = new List<(TimeSpan Duration, TimeSpan Capacity)>();
                foreach (P p in Productivity) {
                    if (p.Capacities.TryGetValue(material, out TimeSpan cap)) {
                        entries.Add((p.TimeWindow.Duration, cap));
                    }
                }
                double totalWeighted = entries.Aggregate(0.0, (acc, e) => acc + _durationToValue(e.Duration).ToDouble() * _durationToValue(e.Capacity).ToDouble());
                double totalDuration = entries.Aggregate(0.0, (acc, e) => acc + _durationToValue(e.Duration).ToDouble());
                result[material] = _valueToDuration(new Flt64(totalWeighted / totalDuration));
            }
            return result;
        });

        _averageUnitYield = new Lazy<IReadOnlyDictionary<U, Q>>(() => {
            IEnumerable<U> materials = Productivity.SelectMany(p => p.UnitYields.Keys).Distinct();
            var result = new Dictionary<U, Q>();
            foreach (U? material in materials) {
                var entries = Productivity
                    .Select(p => p.UnitYields.TryGetValue(material, out Q yld)
                        ? new (TimeSpan Duration, Q Yld)?((p.TimeWindow.Duration, yld))
                        : null)
                    .Where(e => e.HasValue)
                    .Select(e => e!.Value)
                    .ToList();
                Q totalWeighted = entries.Aggregate(_constants.Zero, (acc, e) => acc.Plus(Mul(e.Yld, e.Duration)));
                TimeSpan totalDuration = entries.Aggregate(TimeSpan.Zero, (acc, e) => acc + e.Duration);
                result[material] = Div(totalWeighted, totalDuration);
            }
            return result;
        });

        _unavailableTimesLazy = new Lazy<IReadOnlyList<TimeRange>>(() => {
            if (unavailableTimes != null) {
                return base.UnavailableTimes;
            }
            // Derive from productivity gaps
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

    /// <summary>平均单位产量映射 / Average unit yield mapping.</summary>
    public IReadOnlyDictionary<U, Q> AverageUnitYield => _averageUnitYield.Value;

    /// <summary>不可用时间列表 / Unavailable times.</summary>
    public new IReadOnlyList<TimeRange> UnavailableTimes => _unavailableTimesLazy.Value;

    // =========================================================================
    // Public API: actualStartTimeFrom, actualTimeFrom, actualTimeUntil, actualQuantity
    // =========================================================================

    /// <summary>
    /// 查找从指定时间开始的最早有效开始时间 /
    /// Find the earliest valid start time from the specified instant.
    /// </summary>
    public DateTimeOffset ActualStartTimeFrom(
        T material,
        DateTimeOffset startTime,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
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
                time: intersection,
                unavailableTimes: unavailableTimes,
                beforeConnectionTime: beforeConnectionTime,
                afterConnectionTime: afterConnectionTime,
                beforeConditionalConnectionTime: beforeConditionalConnectionTime,
                afterConditionalConnectionTime: afterConditionalConnectionTime,
                currentDuration: currentTime == startTime ? currentDuration : null,
                maxDuration: TimeSpan.MaxValue,
                breakTime: breakTime);

            foreach (TimeRange produceTime in validTimesResult.Times) {
                double thisQuantity = _durationToValue(produceTime.Duration).ToDouble() * currentProductivity.Value.ToDouble();
                if (thisQuantity > _durationToValue(TimeSpan.Zero).ToDouble()) {
                    return produceTime.Start;
                }
            }
            currentTime = MaxEndTime(validTimesResult.Times, validTimesResult.BreakTimes, validTimesResult.ConnectionTimes);
        }
        return TimeRange.DistantFuture;
    }

    /// <summary>
    /// 计算从指定时间开始生产指定数量所需的实际时间范围 /
    /// Calculate the actual time range needed to produce a specified quantity from a given start time.
    /// </summary>
    public ActualTimeResult ActualTimeFrom(
        T material,
        DateTimeOffset startTime,
        Q quantity,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
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

        return ActualTimeFromInternal(
            material, startTime, productivityCalendar, quantity,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime,
            currentDuration, breakTime);
    }

    /// <summary>
    /// 计算从指定时间开始生产指定数量所需的实际时间范围（可返回 null）/
    /// Calculate the actual time range (returns null if not possible).
    /// </summary>
    public ActualTimeResult? ActualTimeFromOrNull(
        T material,
        DateTimeOffset startTime,
        Q quantity,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<P> productivityCalendar = Productivity.FindFrom(startTime, p => p.TimeWindow);
        if (productivityCalendar.Count == 0) {
            return null;
        }

        return ActualTimeFromInternal(
            material, startTime, productivityCalendar, quantity,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime,
            currentDuration, breakTime);
    }

    /// <summary>
    /// 计算在指定结束时间之前生产指定数量的实际时间范围 /
    /// Calculate the actual time range to produce a quantity before the specified end time.
    /// </summary>
    public ActualTimeResult ActualTimeUntil(
        T material,
        DateTimeOffset endTime,
        Q quantity,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
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

        return ActualTimeUntilInternal(
            material, endTime, productivityCalendar, quantity,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime,
            breakTime);
    }

    /// <summary>
    /// 计算在指定结束时间之前生产指定数量的实际时间范围（可返回 null）/
    /// Calculate the actual time range (returns null if not possible).
    /// </summary>
    public ActualTimeResult? ActualTimeUntilOrNull(
        T material,
        DateTimeOffset endTime,
        Q quantity,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        var productivityCalendar = Productivity.FindUntil(endTime, p => p.TimeWindow).Reverse().ToList();
        if (productivityCalendar.Count == 0) {
            return null;
        }

        return ActualTimeUntilInternal(
            material, endTime, productivityCalendar, quantity,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime,
            breakTime);
    }

    /// <summary>
    /// 计算在指定时间范围内的实际产量 /
    /// Calculate the actual quantity within the specified time range.
    /// </summary>
    public Q ActualQuantity(
        T material,
        TimeRange time,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<P> productivityCalendar = Productivity.Find(time, p => p.TimeWindow);
        if (productivityCalendar.Count == 0) {
            return _constants.Zero;
        }

        return ActualQuantityInternal(
            material, time, productivityCalendar,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime,
            currentDuration, breakTime);
    }

    /// <summary>
    /// 计算在指定时间范围内的实际产量（可返回 null）/
    /// Calculate the actual quantity (returns null if no productivity found).
    /// </summary>
    public Q? ActualQuantityOrNull(
        T material,
        TimeRange time,
        IReadOnlyList<TimeRange>? unavailableTimes = null,
        DurationRange? beforeConnectionTime = null,
        DurationRange? afterConnectionTime = null,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime = null,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime = null,
        TimeSpan? currentDuration = null,
        (DurationRange unit, TimeSpan breakDuration)? breakTime = null) {
        IReadOnlyList<P> productivityCalendar = Productivity.Find(time, p => p.TimeWindow);
        if (productivityCalendar.Count == 0) {
            return null;
        }

        return ActualQuantityInternal(
            material, time, productivityCalendar,
            TimeRange.Merge(MergeUnavailableTimes(unavailableTimes)),
            beforeConnectionTime, afterConnectionTime,
            beforeConditionalConnectionTime, afterConditionalConnectionTime,
            currentDuration, breakTime);
    }

    // =========================================================================
    // Private implementation methods
    // =========================================================================

    private ActualTimeResult ActualTimeFromInternal(
        T material,
        DateTimeOffset startTime,
        IReadOnlyList<P> productivityCalendar,
        Q quantity,
        IReadOnlyList<TimeRange> mergedUnavailableTimes,
        DurationRange? beforeConnectionTime,
        DurationRange? afterConnectionTime,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime,
        TimeSpan? currentDuration,
        (DurationRange unit, TimeSpan breakDuration)? breakTime) {
        double produceQuantity = 0.0;
        DateTimeOffset currentTime = startTime;
        var workingTimes = new List<TimeRange>();
        var breakTimes = new List<TimeRange>();
        var connectionTimes = new List<TimeRange>();
        double quantityValue = _quantityToValue(quantity).ToDouble();

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
                time: intersection,
                unavailableTimes: mergedUnavailableTimes,
                beforeConnectionTime: beforeConnectionTime,
                afterConnectionTime: afterConnectionTime,
                beforeConditionalConnectionTime: beforeConditionalConnectionTime,
                afterConditionalConnectionTime: afterConditionalConnectionTime,
                currentDuration: currentTime == startTime ? currentDuration : null,
                maxDuration: maxDuration,
                breakTime: breakTime);

            workingTimes.AddRange(validTimesResult.Times);
            breakTimes.AddRange(validTimesResult.BreakTimes);
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
            return new ActualTimeResult {
                Time = new TimeRange(startTime, currentTime),
                WorkingTimes = workingTimes,
                BreakTimes = breakTimes,
                ConnectionTimes = connectionTimes
            };
        }
        return new ActualTimeResult {
            Time = new TimeRange(startTime, TimeRange.DistantFuture),
            WorkingTimes = workingTimes,
            BreakTimes = breakTimes,
            ConnectionTimes = connectionTimes
        };
    }

    private ActualTimeResult ActualTimeUntilInternal(
        T material,
        DateTimeOffset endTime,
        IReadOnlyList<P> productivityCalendar,
        Q quantity,
        IReadOnlyList<TimeRange> mergedUnavailableTimes,
        DurationRange? beforeConnectionTime,
        DurationRange? afterConnectionTime,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime,
        (DurationRange unit, TimeSpan breakDuration)? breakTime) {
        double produceQuantity = 0.0;
        DateTimeOffset currentTime = endTime;
        var workingTimes = new List<TimeRange>();
        var breakTimes = new List<TimeRange>();
        var connectionTimes = new List<TimeRange>();
        double quantityValue = _quantityToValue(quantity).ToDouble();

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
                time: intersection,
                unavailableTimes: mergedUnavailableTimes,
                beforeConnectionTime: beforeConnectionTime,
                afterConnectionTime: afterConnectionTime,
                beforeConditionalConnectionTime: beforeConditionalConnectionTime,
                afterConditionalConnectionTime: afterConditionalConnectionTime,
                maxDuration: maxDuration,
                breakTime: breakTime);

            workingTimes.AddRange(validTimesResult.Times);
            breakTimes.AddRange(validTimesResult.BreakTimes);
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
            return new ActualTimeResult {
                Time = new TimeRange(currentTime, endTime),
                WorkingTimes = workingTimes,
                BreakTimes = breakTimes,
                ConnectionTimes = connectionTimes
            };
        }
        return new ActualTimeResult {
            Time = new TimeRange(TimeRange.DistantPast, endTime),
            WorkingTimes = workingTimes,
            BreakTimes = breakTimes,
            ConnectionTimes = connectionTimes
        };
    }

    private Q ActualQuantityInternal(
        T material,
        TimeRange time,
        IReadOnlyList<P> productivityCalendar,
        IReadOnlyList<TimeRange> mergedUnavailableTimes,
        DurationRange? beforeConnectionTime,
        DurationRange? afterConnectionTime,
        Func<TimeRange, DurationRange?>? beforeConditionalConnectionTime,
        Func<TimeRange, DurationRange?>? afterConditionalConnectionTime,
        TimeSpan? currentDuration,
        (DurationRange unit, TimeSpan breakDuration)? breakTime) {
        ValidTimesResult validTimesResult = ValidTimesStatic(
            time: time,
            unavailableTimes: mergedUnavailableTimes,
            beforeConnectionTime: beforeConnectionTime,
            afterConnectionTime: afterConnectionTime,
            beforeConditionalConnectionTime: beforeConditionalConnectionTime,
            afterConditionalConnectionTime: afterConditionalConnectionTime,
            currentDuration: currentDuration,
            breakTime: breakTime);

        double quantity = 0.0;
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
                quantity += global::System.Math.Floor(_durationToValue(produceTimeSpan.Value).ToDouble() * currentProductivity.ToDouble());
            }
        }
        return _valueToQuantity(new Flt64(quantity));
    }

    // =========================================================================
    // Helper methods
    // =========================================================================

    private Flt64? ProductivityRateOf(P productivity, T material) {
        Q? unitYield = productivity.UnitYieldOf(material);
        if (unitYield != null) {
            return _quantityToValue(unitYield.Value);
        }
        TimeSpan? capacity = productivity.CapacityOf(material);
        if (capacity != null) {
            return new Flt64(1.0 / _durationToValue(capacity.Value).ToDouble());
        }
        return null;
    }

    private Q Mul(Q quantity, TimeSpan duration) {
        double result = _quantityToValue(quantity).ToDouble() * _durationToValue(duration).ToDouble();
        return _valueToQuantity(new Flt64(global::System.Math.Floor(result)));
    }

    private Q Div(Q quantity, TimeSpan duration) {
        double result = _quantityToValue(quantity).ToDouble() / _durationToValue(duration).ToDouble();
        return _valueToQuantity(new Flt64(global::System.Math.Floor(result)));
    }

    private TimeSpan CeilDuration(double valueInUnits) => TimeWindow.Ceil(_valueToDuration(new Flt64(valueInUnits)));

    private IReadOnlyList<TimeRange> MergeUnavailableTimes(IReadOnlyList<TimeRange>? extra) {
        if (extra == null || extra.Count == 0) {
            return UnavailableTimes;
        }

        return TimeRange.Merge(extra.Concat(UnavailableTimes).ToList());
    }
}

/// <summary>
/// 离散生产力日历，使用整数除法进行产量计算 /
/// Discrete productivity calendar using integer division for quantity calculation.
/// 从 kotlin WorkingCalendar.kt DiscreteProductivityCalendar 类移植。
/// </summary>
public class DiscreteProductivityCalendar<W, P, T, U> : ProductivityCalendar<W, Fuookami.Ospf.Math.Algebra.Number.UInt64, P, T, U>
    where W : struct, IRealNumber<W>
    where P : Productivity<Fuookami.Ospf.Math.Algebra.Number.UInt64, T, U> {
    private static readonly Fuookami.Ospf.Math.Algebra.Number.INumericConstants<Fuookami.Ospf.Math.Algebra.Number.UInt64> UInt64Constants =
        Fuookami.Ospf.Math.Algebra.Number.NumericConstantsRegistry.For<Fuookami.Ospf.Math.Algebra.Number.UInt64>();

    /// <summary>
    /// 构造离散生产力日历 / Construct a discrete productivity calendar.
    /// </summary>
    public DiscreteProductivityCalendar(
        TimeWindow<W> timeWindow,
        IReadOnlyList<P> productivity,
        IReadOnlyList<TimeRange>? unavailableTimes = null)
        : base(
            timeWindow: timeWindow,
            productivity: productivity,
            constants: UInt64Constants,
            durationToValue: duration => new Flt64(duration.TotalSeconds / timeWindow.Interval.TotalSeconds),
            valueToDuration: value => TimeSpan.FromSeconds(global::System.Math.Floor(value.ToDouble()) * timeWindow.Interval.TotalSeconds),
            quantityToValue: q => q.ToFlt64(),
            valueToQuantity: value => new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)global::System.Math.Floor(value.ToDouble())),
            unavailableTimes: unavailableTimes) {
    }
}
#pragma warning restore CS8714
