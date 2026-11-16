// 一维装箱物理结果提取 / One-dimensional packing physical result extraction.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;

/// <summary>
/// 箱内自由区间 / Free interval in a bin.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="BinId">箱体 ID / Bin ID.</param>
/// <param name="Start">起点 / Start.</param>
/// <param name="Length">可用长度 / Available length.</param>
public sealed record Remnant<V>(string BinId, Quantity<V> Start, Quantity<V> Length)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 一维装箱物理结果 / Physical packing result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Plans">放置结果 / Placements.</param>
/// <param name="Remnants">尾端余量，不将物品间距算为可用余料 / Tail remnants, excluding spacing.</param>
/// <param name="Materials">物料汇总 / Material totals.</param>
/// <param name="Utilization">长度利用率 / Length utilization.</param>
public sealed record PackingResult<V>(
    IReadOnlyList<PackingPlan<V>> Plans,
    IReadOnlyList<Remnant<V>> Remnants,
    IReadOnlyDictionary<string, ulong> Materials,
    V Utilization)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 最终需求、库存、几何复核与物理结果提取 / Final demand, stock, geometry verification and extraction.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class PackingContext<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>默认输出展开预算 / Default output expansion budget.</summary>
    public const int DefaultExpansionLimit = 1000000;

    /// <summary>零值 / The additive zero.</summary>
    private static V Zero => NumericConstantsRegistry.For<V>().Zero;

    /// <summary>
    /// 按确定顺序展开方案并复核精确需求 / Expand patterns deterministically and verify exact demand.
    /// </summary>
    /// <param name="problem">输入 / Input.</param>
    /// <param name="usages">整数份数 / Integer usages.</param>
    /// <param name="expansionLimit">输出展开预算 / Output expansion budget.</param>
    /// <returns>物理结果或失败 / Physical result or failure.</returns>
    /// <remarks>
    /// **本函数有四道校验，缺一道就会静默给出错结果**：
    ///
    /// | 校验 | 漏掉的后果 |
    /// | --- | --- |
    /// | 精确需求 `totals == amount` | 少切或多切都算通过 |
    /// | 箱型库存 `stock &lt;= inventory` | 超用库存无人发现 |
    /// | 展开预算 | 大件数把内存打爆 |
    /// | 物料汇总溢出 | `ulong` 回绕成小数 |
    ///
    /// **余量只到箱尾，不含物品间距**：`cursor` 在放完最后一件事物后停在其右端，
    /// 间距只加在件与件之间，所以余量不会凭空多出一段。
    ///
    /// **Four checks here, and missing any one silently yields a wrong result** (table above). **The remnant reaches the bin tail only and never includes spacing**:
    /// `cursor` stops at the right edge of the last piece, and gaps are added between pieces only, so no phantom gap inflates the remnant.
    /// </remarks>
    public Result<PackingResult<V>, ErrorCode, Error<ErrorCode>> Pack(
        PackingProblem<V> problem,
        IReadOnlyDictionary<PackingPattern<V>, ulong> usages,
        int expansionLimit = DefaultExpansionLimit) {
        Try validation = problem.Validate();
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<PackingResult<V>>(validation);
        }
        if (expansionLimit < 0) {
            return Results.Failed<PackingResult<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "展开预算不得为负 / Expansion budget must not be negative"));
        }

        var totals = new Dictionary<string, ulong>(problem.Items.Count);
        foreach (Item<V> item in problem.Items) {
            totals[item.Id] = 0;
        }
        var stock = new Dictionary<string, ulong>(problem.BinTypes.Count);
        foreach (BinType<V> binType in problem.BinTypes) {
            stock[binType.Id] = 0;
        }

        ulong expanded = 0;
        foreach (KeyValuePair<PackingPattern<V>, ulong> usage in usages) {
            Try patternResult = problem.ValidatePattern(usage.Key);
            if (patternResult is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<PackingResult<V>>(patternResult);
            }
            foreach (KeyValuePair<string, ulong> pair in usage.Key.Amounts) {
                ulong copies = usage.Value;
                if (copies > 0 && pair.Value > (ulong)expansionLimit / copies) {
                    return Results.Failed<PackingResult<V>>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "输出展开预算超限 / Output expansion budget exceeded"));
                }
                ulong pieces = pair.Value * copies;
                expanded += pieces;
                if (expanded > (ulong)expansionLimit) {
                    return Results.Failed<PackingResult<V>>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "输出展开预算超限 / Output expansion budget exceeded"));
                }
                totals[pair.Key] = totals[pair.Key] + pieces;
            }
            stock[usage.Key.BinTypeId] = stock[usage.Key.BinTypeId] + usage.Value;
        }

        foreach (Item<V> item in problem.Items) {
            if (totals[item.Id] != item.Amount) {
                return Results.Failed<PackingResult<V>>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError, "需求或库存复核失败 / Demand or inventory verification failed"));
            }
        }
        foreach (BinType<V> binType in problem.BinTypes) {
            if (binType.Inventory is { } inventory && stock[binType.Id] > inventory) {
                return Results.Failed<PackingResult<V>>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError, "需求或库存复核失败 / Demand or inventory verification failed"));
            }
        }

        var plans = new List<PackingPlan<V>>();
        var remnants = new List<Remnant<V>>();
        var materials = new Dictionary<string, ulong>();
        V totalLength = Zero;
        V packedLength = Zero;

        foreach (KeyValuePair<PackingPattern<V>, ulong> usage in usages) {
            BinType<V> type = problem.BinTypes[0];
            foreach (BinType<V> candidate in problem.BinTypes) {
                if (candidate.Id == usage.Key.BinTypeId) {
                    type = candidate;
                    break;
                }
            }

            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> gapResult = problem.Spacing.To(type.Length.Unit);
            if (gapResult is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> gapOk) {
                return Results.Failed<PackingResult<V>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "间距量纲与箱体长度不一致 / Spacing unit differs from the bin length unit"));
            }
            V gap = gapOk.Value.Value;

            var itemOrder = new List<string>(usage.Key.Amounts.Keys);
            itemOrder.Sort(global::System.StringComparer.Ordinal);

            for (ulong copy = 0; copy < usage.Value; copy++) {
                V cursor = Zero;
                var placements = new List<Placement<V>>();

                foreach (string id in itemOrder) {
                    Item<V> item = problem.Items[0];
                    foreach (Item<V> candidate in problem.Items) {
                        if (candidate.Id == id) {
                            item = candidate;
                            break;
                        }
                    }
                    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> lengthResult = item.Length.To(type.Length.Unit);
                    if (lengthResult is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> lengthOk) {
                        return Results.Failed<PackingResult<V>>(new Err<ErrorCode>(
                            ErrorCode.IllegalArgument, $"物品长度量纲不一致 / Item length unit differs: {id}"));
                    }
                    Quantity<V> length = lengthOk.Value;

                    for (ulong piece = 0; piece < usage.Key.Amounts[id]; piece++) {
                        if (placements.Count > 0) {
                            cursor = cursor.Plus(gap);
                        }
                        placements.Add(new Placement<V>(id, new Quantity<V>(cursor, type.Length.Unit), length));
                        cursor = cursor.Plus(length.Value);

                        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> metre = item.Length.To(SIBaseUnits.Meter);
                        if (metre is Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> metreOk) {
                            packedLength = packedLength.Plus(metreOk.Value.Value);
                        }

                        foreach (KeyValuePair<string, ulong> material in item.MaterialsValue) {
                            ulong previous = materials.TryGetValue(material.Key, out ulong value) ? value : 0;
                            if (material.Value > ulong.MaxValue - previous) {
                                return Results.Failed<PackingResult<V>>(new Err<ErrorCode>(
                                    ErrorCode.IllegalArgument, "物料汇总溢出 / Material summary overflow"));
                            }
                            materials[material.Key] = previous + material.Value;
                        }
                    }
                }

                var bin = new Bin<V>($"bin_{plans.Count}", type);
                plans.Add(new PackingPlan<V>(bin, placements));
                if (cursor.CompareTo(type.Length.Value) < 0) {
                    remnants.Add(new Remnant<V>(
                        BinId: bin.Id,
                        Start: new Quantity<V>(cursor, type.Length.Unit),
                        Length: new Quantity<V>(type.Length.Value.Minus(cursor), type.Length.Unit)));
                }

                Result<Quantity<V>, ErrorCode, Error<ErrorCode>> binMetre = type.Length.To(SIBaseUnits.Meter);
                if (binMetre is Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> binMetreOk) {
                    totalLength = totalLength.Plus(binMetreOk.Value.Value);
                }
            }
        }

        V utilization = totalLength.CompareTo(Zero) == 0
            ? Zero
            : packedLength.Div(totalLength);

        return Results.Ok(new PackingResult<V>(
            Plans: plans,
            Remnants: remnants,
            Materials: materials,
            Utilization: utilization));
    }

    /// <summary>把同码型的失败/致命结果转成本函数的返回类型 / Re-shape a failure or fatal result.</summary>
    /// <typeparam name="T">目标载荷类型 / Target payload type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T>(
        Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal => new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Ok(default(T)!),
        };
}
