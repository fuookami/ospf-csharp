// 生物限制相邻约束 / The biological adjacency rows.

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
/// 生物限制相邻约束 / The biological adjacency rows.
/// </summary>
/// <remarks>
/// 对齐基线 `stowage/service/limits/BiologicalAdjacentLimit.kt`：对每条**生物限制类型对**（如活体与干冰），
/// 该类型对的两件货物不得落在相邻舱位。结构与 `ELDAdjacentLimit` 同形——每条邻接**两行**，条件是 `||`，配对 `i2 > i1`。
///
/// 差异只在**配对判定**：ELD 是"两件都是 ELD"，这里是"一件含 `type1` 且另一件含 `type2`，**或反过来**"。
/// 两个朝向都要试：只写 `item1` 含 `type1` 且 `item2` 含 `type2` 会漏掉货物顺序相反的情形，而限制表里的类型对**没有方向**。
///
/// Aligned with the baseline: for every **biological type pair** (live animals versus dry ice, say), two items of that pair must not sit in adjacent positions. The structure
/// mirrors `ELDAdjacentLimit` — **two rows** per adjacency, an `||` guard, pairs with `i2 > i1`. The only difference is **the pair test**: ELD means "both are ELD", while this is
/// "one contains `type1` and the other `type2`, **or the reverse**". Both orientations must be tried: writing only "`item1` has `type1` and `item2` has `type2`" misses the reversed cargo
/// order, and the type pairs in the limit table are **undirected**.
/// </remarks>
public sealed class BiologicalAdjacentLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly IReadOnlyList<Neighbour> _neighbours;
    private readonly BiologicalLimit _biologicalLimit;
    private readonly StowageModel _stowage;

    /// <summary>
    /// 构造生物相邻约束 / Construct the biological adjacency rows.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="neighbours">间接物理邻接 / The indirect physical neighbours.</param>
    /// <param name="biologicalLimit">生物限制 / The biological limit.</param>
    /// <param name="stowage">装载模型 / The stowage model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public BiologicalAdjacentLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyList<Neighbour> neighbours,
        BiologicalLimit biologicalLimit,
        StowageModel stowage,
        string name = "biological_adjacent_limit") {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(neighbours);
        ArgumentNullException.ThrowIfNull(biologicalLimit);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _neighbours = neighbours;
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

        foreach ((CargoType type1, CargoType type2) in _biologicalLimit.AdjacentLimit) {
            for (int i1 = 0; i1 < _items.Count; ++i1) {
                Item item1 = _items[i1];
                for (int i2 = i1 + 1; i2 < _items.Count; ++i2) {
                    Item item2 = _items[i2];
                    // **两个朝向都试**：限制表里的类型对没有方向。
                    // **Both orientations are tried**: the type pairs in the limit table are undirected.
                    bool forward = Contains(item1, type1) && Contains(item2, type2);
                    bool backward = Contains(item1, type2) && Contains(item2, type1);
                    if (!forward && !backward) {
                        continue;
                    }

                    foreach (Neighbour neighbour in _neighbours) {
                        int j1 = IndexOfPosition(neighbour.Pair.First);
                        int j2 = IndexOfPosition(neighbour.Pair.Second);
                        if (j1 < 0 || j2 < 0) {
                            return Unknown();
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

    // `CargoType.Code` 可空：没有代码的类型**不匹配任何货物**，而不是抛。`Contains` 因此集中在这里。
    // `CargoType.Code` is nullable: a type with no code matches no item rather than throwing, so the check is centralised here.
    private static bool Contains(Item item, CargoType type) =>
        type.Code is { } code && item.Contains(code);

    private int IndexOfPosition(Domain.Aircraft.Model.Position position) {
        for (int j = 0; j < _positions.Count; ++j) {
            if (string.Equals(_positions[j].Name, position.SpaceName, StringComparison.Ordinal)) {
                return j;
            }
        }
        return -1;
    }

    private Try Unknown() =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "相邻约束引用了未知舱位 / the adjacency names an unknown position"));

    private Try Missing(Item item, StowagePosition position) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            "货物 " + item.Name + " 与舱位 " + position.Name +
            " 的装载符号未注册 / the stowage symbol is not registered"));
}
