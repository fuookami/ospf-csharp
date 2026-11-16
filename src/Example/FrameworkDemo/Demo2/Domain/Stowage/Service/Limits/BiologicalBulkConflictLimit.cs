// 生物限制散货冲突 / The biological bulk-conflict rows.

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
/// 生物限制散货冲突 / The biological bulk-conflict rows.
/// </summary>
/// <remarks>
/// 对每个**可用散货舱位**与每条生物冲突类型对，**三支独立判定**
/// （注意：与 `AOGMATBulkConflictLimit` 的三支**互斥**不同，这里三支都跑）：
///
/// | 支 | 舱位已装 | 加的行 |
/// | --- | --- | --- |
/// | 1 | 有 `type1` 且**不全是** `type2` | `type2` 货物 `= 0` |
/// | 2 | 有 `type2` 且**不全是** `type1` | `type1` 货物 `= 0` |
/// | 3 | **两者都没有** | 一件 `type1` + 一件 `type2` 配对 `≤ 1` |
///
/// 两处极易写错：
///
/// 1. **`any …` 加 `!none …` 不是两个 `any`**。写法是"已装里**有一件**含 `type1`"与"已装里**不是全都不含** `type2`"两句的合取，
///    即"有 type1 且**至少有一件**含 type2"。把 `!none` 读成"没有 type2"（写成 `!any`）会**反过来**——那是第 3 支的条件。
/// 2. **三支都跑，不 `continue`**。同一个舱位可能同时满足第 1、2 支（已装里既有 type1 又有 type2），此时两个方向的货物都被禁。
///
/// For every **available bulk position** and every biological conflict pair, **three independently evaluated branches** (note: unlike
/// `AOGMATBulkConflictLimit`'s mutually exclusive three, all three run here), tabulated above. Two easy misreadings: **"`any` plus `!none`" is not "two `any`s"** —
/// The writing is "has a `type1` and **at least one** `type2`", and reading `!none` as "no `type2`" (i.e. `!any`) inverts it into branch three's condition; and **all three
/// branches run without `continue`**, so a position already holding both types satisfies branches one and two at once and both directions are forbidden.
/// </remarks>
public sealed class BiologicalBulkConflictLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly BiologicalLimit _biologicalLimit;
    private readonly StowageModel _stowage;

    /// <summary>
    /// 构造生物散货冲突 / Construct the biological bulk-conflict rows.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="biologicalLimit">生物限制 / The biological limit.</param>
    /// <param name="stowage">装载模型 / The stowage model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public BiologicalBulkConflictLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        BiologicalLimit biologicalLimit,
        StowageModel stowage,
        string name = "biological_bulk_conflict_limit") {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(biologicalLimit);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _biologicalLimit = biologicalLimit;
        _stowage = stowage;
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

            foreach ((CargoType type1, CargoType type2) in _biologicalLimit.BulkConflictLimit) {
                bool holdsType1 = false;
                bool holdsType2 = false;
                foreach (Item loaded in position.LoadedItems) {
                    if (Contains(loaded, type1)) {
                        holdsType1 = true;
                    }
                    if (Contains(loaded, type2)) {
                        holdsType2 = true;
                    }
                }

                // 第 1 支：有 type1 且至少有一件 type2 —— type2 货物一律 `= 0`。
                // Branch 1: holds type1 and at least one type2 — type2 cargo is `= 0`.
                if (holdsType1 && holdsType2) {
                    for (int i = 0; i < _items.Count; ++i) {
                        Item item = _items[i];
                        if (StowageModel.StowageNeeded(item, position) && Contains(item, type2)) {
                            Try added = AddZero(linear, i, j, item, position);
                            if (!added.IsOk) {
                                return added;
                            }
                        }
                    }
                }
                // 第 2 支：与第 1 支对称 —— type1 货物一律 `= 0`。
                // Branch 2: symmetric to branch 1 — type1 cargo is `= 0`.
                if (holdsType2 && holdsType1) {
                    for (int i = 0; i < _items.Count; ++i) {
                        Item item = _items[i];
                        if (StowageModel.StowageNeeded(item, position) && Contains(item, type1)) {
                            Try added = AddZero(linear, i, j, item, position);
                            if (!added.IsOk) {
                                return added;
                            }
                        }
                    }
                }
                // 第 3 支：两者都没有 —— 一件 type1 + 一件 type2 配对 `≤ 1`。
                // Branch 3: neither is present — one type1 plus one type2, `≤ 1` per pair.
                if (!holdsType1 && !holdsType2) {
                    for (int i1 = 0; i1 < _items.Count; ++i1) {
                        Item item1 = _items[i1];
                        if (!StowageModel.StowageNeeded(item1, position)) {
                            continue;
                        }
                        for (int i2 = i1 + 1; i2 < _items.Count; ++i2) {
                            Item item2 = _items[i2];
                            if (!StowageModel.StowageNeeded(item2, position)) {
                                continue;
                            }
                            bool forward = Contains(item1, type1) && Contains(item2, type2);
                            bool backward = Contains(item1, type2) && Contains(item2, type1);
                            if (!forward && !backward) {
                                continue;
                            }
                            Try added = AddPair(linear, i1, i2, j, item1, item2, position);
                            if (!added.IsOk) {
                                return added;
                            }
                        }
                    }
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    // `CargoType.Code` 可空：没有代码的类型**不匹配任何货物**，而不是抛。
    // `CargoType.Code` is nullable: a type with no code matches no item rather than throwing.
    private static bool Contains(Item item, CargoType type) =>
        type.Code is { } code && item.Contains(code);

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
            Comparison.EQ,
            rowName);
        return linear.AddConstraint(row, group: null, name: rowName);
    }

    private Try AddPair(LinearMetaModel<Flt64> linear, int firstIndex, int secondIndex, int positionIndex, Item first, Item second, StowagePosition position) {
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
