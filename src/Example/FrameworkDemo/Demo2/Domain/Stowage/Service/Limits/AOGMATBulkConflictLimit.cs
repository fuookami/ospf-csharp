// 航材与散货互斥约束 / The AOG/MAT versus bulk exclusivity rows.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;

/// <summary>
/// 航材与散货互斥约束 / The AOG/MAT versus bulk exclusivity rows.
/// </summary>
/// <remarks>
/// 对齐。**三支互斥**，按顺序判定，每支处理完就 `continue`：
///
/// | 支 | 舱位条件 | 加的行 |
/// | --- | --- | --- |
/// | 1 | 航材指定 **或** 已装航材 | 普通散货 `stowage = 0` |
/// | 2 | 普通散货指定 **或** 已装普通散货且无航材 | 航材 `stowage = 0` |
/// | 3 | 其余 | 一件航材 + 一件普通散货 `≤ 1`（两两组合） |
///
/// 三支的**顺序**是硬要求：第 1、2 支各自已经把一个方向封死，第 3 支再补"两件最多一件"。把顺序调换会让第 3 支先加一批行、
/// 第 1 支再加一批，行数变多而语义重复；把 `continue` 去掉则会同时触发两支，对同一个舱位同时禁止两个方向——模型直接不可行。
///
/// 外层只看**可用**散货舱位（`!unavailable` 与 `location.bulk` 同时成立）。少了 `location.bulk` 会给主甲板舱位也加行，而那些行的语义完全不同。
///
/// **Three mutually exclusive branches**, judged in order, each ending in `continue`: tabulated above. The **order** is a hard requirement — branches
/// one and two each close one direction and branch three adds "at most one of a pair". Swapping the order emits branch three's rows first and branch one's after, more rows with
/// duplicated meaning; dropping a `continue` fires two branches at once and forbids both directions for the same position, making the model infeasible. The outer loop covers
/// **available bulk** positions only; without `location.bulk` the main-deck positions get rows whose meaning is entirely different.
/// </remarks>
public sealed class AOGMATBulkConflictLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly StowageModel _stowage;

    /// <summary>
    /// 构造互斥约束 / Construct the exclusivity rows.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">装载模型 / The stowage model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public AOGMATBulkConflictLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        StowageModel stowage,
        string name = "aot_mat_bulk_conflict_limit") {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _stowage = stowage;
        // 的名字就是 `aot_mat_bulk_conflict_limit`（`AOG` 打成了 `AOT`）。沿用，不"顺手改正"——改名会让按名字定位的调用方找不到它。
        // The code's name literally is `aot_mat_bulk_conflict_limit` (`AOG` typed as `AOT`). Copied as-is rather than "fixed": renaming breaks any caller that locates the
        // pipeline by name.
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束 / Apply the rows.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> linear) {
        ArgumentNullException.ThrowIfNull(linear);

        for (int j = 0; j < _positions.Count; ++j) {
            StowagePosition position = _positions[j];
            if (!position.Status.Available || !position.Location.Bulk) {
                continue;
            }

            // 第 1 支：航材指定舱位，或已装航材 —— 普通散货一律 `= 0`。
            // Branch 1: an AOG/MAT-appointed position, or one already holding AOG/MAT — normal bulk is `= 0`.
            bool holdsAogMat = false;
            foreach (Item loaded in position.LoadedItems) {
                if (IsAogMat(loaded)) {
                    holdsAogMat = true;
                    break;
                }
            }
            if (position.Type.Contains(PositionTypeCode.AOGMATAppointed) || holdsAogMat) {
                for (int i = 0; i < _items.Count; ++i) {
                    Item item = _items[i];
                    if (StowageModel.StowageNeeded(item, position) && item.Location.IsBulk && !IsAogMat(item)) {
                        Try added = AddZero(linear, i, j, item, position);
                        if (!added.IsOk) {
                            return added;
                        }
                    }
                }
                continue;
            }

            // 第 2 支：普通散货指定舱位，或已装普通散货且不含航材 —— 航材一律 `= 0`。
            // Branch 2: a normal-bulk-appointed position, or one holding normal bulk and no AOG/MAT — AOG/MAT is `= 0`.
            bool holdsNormalBulk = false;
            bool holdsAnyAogMat = false;
            foreach (Item loaded in position.LoadedItems) {
                if (IsAogMat(loaded)) {
                    holdsAnyAogMat = true;
                } else if (loaded.Location.IsBulk) {
                    holdsNormalBulk = true;
                }
            }
            if (position.Type.Contains(PositionTypeCode.NormalBulkAppointed) || (holdsNormalBulk && !holdsAnyAogMat)) {
                for (int i = 0; i < _items.Count; ++i) {
                    Item item = _items[i];
                    if (StowageModel.StowageNeeded(item, position) && IsAogMat(item)) {
                        Try added = AddZero(linear, i, j, item, position);
                        if (!added.IsOk) {
                            return added;
                        }
                    }
                }
                continue;
            }

            // 第 3 支：其余舱位 —— 一件航材 + 一件普通散货，两两 `≤ 1`。
            // Branch 3: everything else — one AOG/MAT plus one normal bulk, `≤ 1` per pair.
            for (int i1 = 0; i1 < _items.Count; ++i1) {
                Item item1 = _items[i1];
                if (!StowageModel.StowageNeeded(item1, position) || !IsAogMat(item1)) {
                    continue;
                }
                for (int i2 = i1 + 1; i2 < _items.Count; ++i2) {
                    Item item2 = _items[i2];
                    if (!StowageModel.StowageNeeded(item2, position) || !item2.Location.IsBulk || IsAogMat(item2)) {
                        continue;
                    }
                    Try added = AddPair(linear, i1, i2, j, item1, item2, position);
                    if (!added.IsOk) {
                        return added;
                    }
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static bool IsAogMat(Item item) => item.Contains(CargoCode.AOG) || item.Contains(CargoCode.MAT);

    private Try AddZero(LinearMetaModel<Flt64> linear, int itemIndex, int positionIndex, Item item, StowagePosition position) {
        if (_stowage.StowageSymbol(itemIndex, positionIndex) is not { } symbol) {
            return Missing(item, position);
        }
        string rowName = Name + "_" + item.Name + "_" + position.Name;
        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) },
                Flt64.Zero),
            new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            Comparison.LE,
            rowName);
        return linear.AddConstraint(row, group: null, name: rowName);
    }

    private Try AddPair(
        LinearMetaModel<Flt64> linear,
        int firstIndex,
        int secondIndex,
        int positionIndex,
        Item first,
        Item second,
        StowagePosition position) {
        if (_stowage.StowageSymbol(firstIndex, positionIndex) is not { } firstSymbol) {
            return Missing(first, position);
        }
        if (_stowage.StowageSymbol(secondIndex, positionIndex) is not { } secondSymbol) {
            return Missing(second, position);
        }
        string rowName = Name + "_" + first.Name + "_" + second.Name + "_" + position.Name;
        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, firstSymbol),
                    new LinearMonomial<Flt64>(Flt64.One, secondSymbol),
                },
                Flt64.Zero),
            new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
            Comparison.LE,
            rowName);
        return linear.AddConstraint(row, group: null, name: rowName);
    }

    private Try Missing(Item item, StowagePosition position) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            "货物 " + item.Name + " 与舱位 " + position.Name +
            " 的装载符号未注册 / the stowage symbol is not registered"));
}
