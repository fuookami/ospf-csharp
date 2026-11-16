#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
// **`Stowage` 既是命名空间又是类型**（`Domain.Stowage` 与 `Model.Stowage`），必须起别名。
// **`Stowage` is both a namespace and a type** (`Domain.Stowage` versus `Model.Stowage`), so an alias is mandatory.
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service.Limits;

/// <summary>
/// 货物顺序约束 / The item-order constraint.
/// </summary>
/// <remarks>
/// 与对齐：**优先级高与优先级低的一对货物，不能分别落在"后与前"的一对舱位上**。
///
/// 四处必须沿用：
///
/// 1. **两个排序键不同**：货物按 `优先级降序` 再按 `Id 升序`；舱位按 `装载顺序升序` 再按 `Id 字符串升序`。
///    第二级的 `Id` 是**打破平局**用的——只按主键排会让同优先级的货物顺序取决于列表原序。
/// 2. **有序对是"全组合"**：`orderedPairs` 生成所有满足谓词的 `(a, b)`，不是相邻对。少一半约束会让高优先级货物仍可后置。
/// 3. **下标用"首次出现"**：同一对象在列表里出现两次时取第一次。用 `IndexOf` 也行，但基线写的是显式的首次出现表。
/// 4. **约束是 `s[i1,j2] + s[i2,j1] 不超过 1`**：两个"反向放置"之和至多 1，即最多允许其中一个。
///
/// **a higher-priority and a lower-priority item must not land in a "later and earlier" pair of positions**. Four things are carried over: **the two sort keys
/// differ** — items by `priority descending` then `Id ascending`, positions by `loading order ascending` then `Id` as a **string** ascending, where the second level breaks ties and
/// without it the order of equal-priority items depends on the input list; **ordered pairs are all combinations**, not adjacent pairs, since halving the constraints would let a
/// high-priority item sit late; **indices come from first occurrence**, so a repeated object takes its first index; and **the constraint is `s[i1,j2] + s[i2,j1] 不超过 1`**, allowing at
/// most one of the two reversed placements.
/// </remarks>
internal sealed class ItemOrderLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly IReadOnlyList<(Item First, Item Second)> _orderedItems;
    private readonly IReadOnlyList<(StowagePosition First, StowagePosition Second)> _orderedPositions;
    private readonly StowageModel _stowage;

    /// <summary>
    /// 构造顺序约束 / Construct the order constraint.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public ItemOrderLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        StowageModel stowage,
        string name = "item_order_limit")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);

        _items = items;
        _positions = positions;
        _stowage = stowage;
        Name = name;

        var sortedItems = new List<Item>(items);
        sortedItems.Sort((left, right) =>
        {
            int byPriority = right.Priority.Priority.CompareTo(left.Priority.Priority);
            return byPriority != 0 ? byPriority : left.Id.CompareTo(right.Id);
        });
        _orderedItems = OrderedPairs(
            sortedItems,
            (item, other) => item.Priority.Priority > other.Priority.Priority);

        var sortedPositions = new List<StowagePosition>(positions);
        sortedPositions.Sort((left, right) =>
        {
            byte leftOrder = left.AircraftPosition.LoadingOrder?.Order ?? left.AircraftPosition.LinearLoadingOrder;
            byte rightOrder = right.AircraftPosition.LoadingOrder?.Order ?? right.AircraftPosition.LinearLoadingOrder;
            int byOrder = leftOrder.CompareTo(rightOrder);
            return byOrder != 0
                ? byOrder
                : string.CompareOrdinal(left.Name, right.Name);
        });
        _orderedPositions = OrderedPairs(
            sortedPositions,
            (position, other) =>
                (position.AircraftPosition.LoadingOrder?.Order ?? position.AircraftPosition.LinearLoadingOrder)
                < (other.AircraftPosition.LoadingOrder?.Order ?? other.AircraftPosition.LinearLoadingOrder));
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>有序货物对 / The ordered item pairs</summary>
    public IReadOnlyList<(Item First, Item Second)> OrderedItems => _orderedItems;

    /// <summary>有序舱位对 / The ordered position pairs</summary>
    public IReadOnlyList<(StowagePosition First, StowagePosition Second)> OrderedPositions => _orderedPositions;

    /// <summary>
    /// 施加约束 / Apply the constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        IReadOnlyDictionary<Item, int> itemIndices = FirstOccurrenceIndices(_items);
        IReadOnlyDictionary<StowagePosition, int> positionIndices = FirstOccurrenceIndices(_positions);

        foreach ((Item item1, Item item2) in _orderedItems)
        {
            int i1 = itemIndices.TryGetValue(item1, out int foundItem1) ? foundItem1 : -1;
            int i2 = itemIndices.TryGetValue(item2, out int foundItem2) ? foundItem2 : -1;
            foreach ((StowagePosition position1, StowagePosition position2) in _orderedPositions)
            {
                int j1 = positionIndices.TryGetValue(position1, out int foundPosition1) ? foundPosition1 : -1;
                int j2 = positionIndices.TryGetValue(position2, out int foundPosition2) ? foundPosition2 : -1;
                if (StowageModel.StowageNeeded(item1, position2) && StowageModel.StowageNeeded(item2, position1))
                {
                    LinearExpressionSymbol? first = _stowage.StowageSymbol(i1, j2);
                    LinearExpressionSymbol? second = _stowage.StowageSymbol(i2, j1);
                    if (first is null || second is null)
                    {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.ApplicationFailed,
                            "配载符号缺失 / the stowage symbols are missing: " + item1 + "," + item2 + "," + position1 + "," + position2));
                    }
                    // **左端是 `s[i1,j2] + s[i2,j1]`，右端是常数 1**。
                    // **The left side is `s[i1,j2] + s[i2,j1]` and the right side the constant 1.**
                    string rowName = Name + "_" + item1 + "_" + item2 + "_" + position1 + "_" + position2;
                    var polynomial = new LinearPolynomial<Flt64>(
                        new List<LinearMonomial<Flt64>>(first.Polynomial.Monomials)
                        {
                            new(new Flt64(1.0), second),
                        },
                        first.Polynomial.Constant + second.Polynomial.Constant);
                    var row = new LinearInequality<Flt64>(
                        polynomial,
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                        Comparison.LE,
                        rowName);
                    Try added = model.AddConstraint(row, group: null, name: rowName);
                    if (!added.IsOk)
                    {
                        return added;
                    }
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 生成满足谓词的全组合 / Generate every pair satisfying the predicate.
    /// </summary>
    /// <typeparam name="T">元素类型 / The element type.</typeparam>
    /// <param name="source">已排序列表 / The sorted list.</param>
    /// <param name="predicate">谓词 / The predicate.</param>
    /// <returns>有序对 / The ordered pairs.</returns>
    private static List<(T First, T Second)> OrderedPairs<T>(
        IReadOnlyList<T> source,
        Func<T, T, bool> predicate)
    {
        var pairs = new List<(T First, T Second)>();
        for (int i = 0; i < source.Count; ++i)
        {
            for (int j = 0; j < source.Count; ++j)
            {
                if (i != j && predicate(source[i], source[j]))
                {
                    pairs.Add((source[i], source[j]));
                }
            }
        }
        return pairs;
    }

    /// <summary>
    /// 首次出现下标 / First-occurrence indices.
    /// </summary>
    /// <typeparam name="T">元素类型 / The element type.</typeparam>
    /// <param name="source">列表 / The list.</param>
    /// <returns>元素到首次下标 / The element-to-first-index map.</returns>
    private static IReadOnlyDictionary<T, int> FirstOccurrenceIndices<T>(IReadOnlyList<T> source)
        where T : notnull
    {
        var indices = new Dictionary<T, int>();
        for (int index = 0; index < source.Count; ++index)
        {
            indices.TryAdd(source[index], index);
        }
        return indices;
    }
}
