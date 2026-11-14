// 锂电池相邻约束 / The lithium-battery adjacency rows.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
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
/// 锂电池相邻约束 / The lithium-battery adjacency rows.
/// </summary>
/// <remarks>
/// 对齐基线 `stowage/service/limits/ELDAdjacentLimit.kt`：**两件 ELD 货物不得落在相邻的两个舱位**（无论哪个方向）。
/// 每条邻接加**两行**——`(i1→j1, i2→j2) ≤ 1` 与 `(i1→j2, i2→j1) ≤ 1`——覆盖两种朝向。
///
/// 三处容易做错：
///
/// 1. **两行都要有**。只加第一行只挡住"同向"，交叉朝向仍可相邻。
/// 2. **条件是"或"不是"且"**：基线要求两件货中**至少一件**能放这一侧。用"且"会漏掉"只有一件能放这一侧"的组合，
///    而那正是交叉朝向最常见的情形。
/// 3. **配对用 `i2 > i1`**：写成全排列会让每一对出现两次，约束表翻倍。
///
/// Aligned with the baseline: **two ELD items must not sit in adjacent positions** in either direction, so every adjacency adds **two rows** — `(i1→j1, i2→j2) ≤ 1` and
/// `(i1→j2, i2→j1) ≤ 1`. Three easy mistakes: **both rows are required** (one row blocks only the aligned orientation while the crossed one still allows adjacency); the guard is
/// **"or", not "and"** as the baseline writes it (with "and" a pair where only one item fits that side is skipped, which is exactly the common crossed case); and pairs use
/// `i2 > i1`, since permutations would emit every pair twice and double the table.
/// </remarks>
public sealed class ELDAdjacentLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly IReadOnlyList<Neighbour> _neighbours;
    private readonly StowageModel _stowage;

    /// <summary>
    /// 构造锂电池相邻约束 / Construct the lithium-battery adjacency rows.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="neighbours">物理邻接 / The physical neighbours.</param>
    /// <param name="stowage">装载模型 / The stowage model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public ELDAdjacentLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyList<Neighbour> neighbours,
        StowageModel stowage,
        string name = "eld_adjacent_limit") {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(neighbours);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _neighbours = neighbours;
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

        for (int i1 = 0; i1 < _items.Count; ++i1) {
            Item item1 = _items[i1];
            if (!item1.Contains(CargoCode.ELD)) {
                continue;
            }
            for (int i2 = i1 + 1; i2 < _items.Count; ++i2) {
                Item item2 = _items[i2];
                if (!item2.Contains(CargoCode.ELD)) {
                    continue;
                }
                foreach (Neighbour neighbour in _neighbours) {
                    int j1 = IndexOfPosition(neighbour.Pair.First);
                    int j2 = IndexOfPosition(neighbour.Pair.Second);
                    if (j1 < 0 || j2 < 0) {
                        return Unknown(neighbour);
                    }
                    StowagePosition position1 = _positions[j1];
                    StowagePosition position2 = _positions[j2];

                    if (StowageModel.StowageNeeded(item1, position1) || StowageModel.StowageNeeded(item2, position2)) {
                        Try added = AddPair(linear, i1, j1, i2, j2, item1, item2, position1, position2);
                        if (!added.IsOk) {
                            return added;
                        }
                    }
                    if (StowageModel.StowageNeeded(item1, position2) || StowageModel.StowageNeeded(item2, position1)) {
                        Try added = AddPair(linear, i1, j2, i2, j1, item1, item2, position2, position1);
                        if (!added.IsOk) {
                            return added;
                        }
                    }
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private Try AddPair(
        LinearMetaModel<Flt64> linear,
        int firstItem,
        int firstPosition,
        int secondItem,
        int secondPosition,
        Item item1,
        Item item2,
        StowagePosition position1,
        StowagePosition position2) {
        if (_stowage.StowageSymbol(firstItem, firstPosition) is not { } first) {
            return Missing(item1, position1);
        }
        if (_stowage.StowageSymbol(secondItem, secondPosition) is not { } second) {
            return Missing(item2, position2);
        }
        string rowName = Name + "_" + item1.Name + "_" + item2.Name + "_" + position1.Name + "_" + position2.Name;
        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, first),
                    new LinearMonomial<Flt64>(Flt64.One, second),
                },
                Flt64.Zero),
            new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
            Comparison.LE,
            rowName);
        return linear.AddConstraint(row, group: null, name: rowName);
    }

    private int IndexOfPosition(Domain.Aircraft.Model.Position position) {
        for (int j = 0; j < _positions.Count; ++j) {
            if (string.Equals(_positions[j].Name, position.SpaceName, StringComparison.Ordinal)) {
                return j;
            }
        }
        return -1;
    }

    private Try Unknown(Neighbour neighbour) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "相邻约束引用了未知舱位 / the adjacency names an unknown position"));

    private Try Missing(Item item, StowagePosition position) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            "货物 " + item.Name + " 与舱位 " + position.Name +
            " 的装载符号未注册 / the stowage symbol is not registered"));
}
