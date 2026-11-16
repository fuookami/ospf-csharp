#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Model;

/// <summary>
/// 相对优先级反转 / The relative priority reversals.
/// </summary>
/// <remarks>
/// 为**每个（有序货物对, 有序舱位对）组合**造一个中间符号，
/// 表示"这一对是否被反着放"。
///
/// 五处必须沿用：
///
/// 1. **两个排序键**：货物按 `优先级降序` 再按 `Id`；舱位按 `装载顺序升序` 再按 `Id` 字符串。
/// 2. **有序对是全组合**，不是相邻对。
/// 3. **条件多项式是 `s[i1,j2] + s[i2,j1] − 2 + ε`**，用 `IfFunction` 的**默认关系 `GT`**（严格大于 0），
///    即"两个反向放置之和达到 2"。`ε` 是来避免浮点边界落在 0 上的。
///    写成 `GE` 或把 `−2` 写成 `−1` 会改变判定阈值。
/// 4. **不可放置的组合给恒零符号**，不是跳过。跳过会让符号矩阵少一格，下游按 `(p1,p2)` 取值时对不上。
/// 5. **符号名带三个元素**：`item_priority_reverse_{item1}_{item2}_{position1}_{position2}`。
///
/// One intermediate symbol per (ordered item pair, ordered position pair), meaning "this pair was placed the wrong way round". Five things must be copied: **the
/// two sort keys** — items by priority descending then `Id`, positions by loading order ascending then `Id` as a string; **ordered pairs are all combinations**, not adjacent pairs;
/// **the condition polynomial is `s[i1,j2] + s[i2,j1] − 2 + ε`** using `IfFunction`'s **default relation `GT`** (strictly above zero), i.e. "the two reversed placements sum to 2",
/// where `ε` keeps floating-point boundaries off zero and writing `GE` or `−1` changes the threshold; **an unplaceable combination gets a constant-zero symbol** rather than being
/// skipped, since skipping leaves a hole in the matrix and downstream indexing by `(p1,p2)` would misalign; and **the name carries three elements**.
/// </remarks>
public sealed class RelativeOrder
{
    /// <summary>
    /// 浮点边界余量，`1e-10` / The floating-point boundary margin, `1e-10`.
    /// </summary>
    private const double NonzeroTolerance = 1e-10;

    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly IReadOnlyList<(Item First, Item Second)> _orderedItems;
    private readonly IReadOnlyList<(StowagePosition First, StowagePosition Second)> _orderedPositions;
    private readonly StowageModel _stowage;
    private readonly Dictionary<(int, int), ILinearIntermediateSymbol<Flt64>> _reversals = new();

    /// <summary>
    /// 构造相对优先级反转 / Construct the relative priority reversals.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    public RelativeOrder(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        StowageModel stowage)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _stowage = stowage;

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
            int byOrder = OrderOf(left).CompareTo(OrderOf(right));
            return byOrder != 0 ? byOrder : string.CompareOrdinal(left.Name, right.Name);
        });
        _orderedPositions = OrderedPairs(
            sortedPositions,
            (position, other) => OrderOf(position) < OrderOf(other));
    }

    /// <summary>有序货物对 / The ordered item pairs</summary>
    public IReadOnlyList<(Item First, Item Second)> OrderedItems => _orderedItems;

    /// <summary>有序舱位对 / The ordered position pairs</summary>
    public IReadOnlyList<(StowagePosition First, StowagePosition Second)> OrderedPositions => _orderedPositions;

    /// <summary>
    /// 取反转符号 / Fetch the reversal symbol.
    /// </summary>
    /// <param name="itemPairIndex">货物对下标 / The item-pair index.</param>
    /// <param name="positionPairIndex">舱位对下标 / The position-pair index.</param>
    /// <returns>反转符号 / The reversal symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? Reversal(int itemPairIndex, int positionPairIndex) =>
        _reversals.TryGetValue((itemPairIndex, positionPairIndex), out ILinearIntermediateSymbol<Flt64>? symbol)
            ? symbol
            : null;

    /// <summary>
    /// 注册到模型 / Register with the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_reversals.Count > 0)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        IReadOnlyDictionary<Item, int> itemIndices = FirstOccurrenceIndices(_items);
        IReadOnlyDictionary<StowagePosition, int> positionIndices = FirstOccurrenceIndices(_positions);

        for (int p1 = 0; p1 < _orderedItems.Count; ++p1)
        {
            (Item item1, Item item2) = _orderedItems[p1];
            int i1 = itemIndices.TryGetValue(item1, out int foundItem1) ? foundItem1 : -1;
            int i2 = itemIndices.TryGetValue(item2, out int foundItem2) ? foundItem2 : -1;
            for (int p2 = 0; p2 < _orderedPositions.Count; ++p2)
            {
                (StowagePosition position1, StowagePosition position2) = _orderedPositions[p2];
                int j1 = positionIndices.TryGetValue(position1, out int foundPosition1) ? foundPosition1 : -1;
                int j2 = positionIndices.TryGetValue(position2, out int foundPosition2) ? foundPosition2 : -1;
                string name = "item_priority_reverse_" + item1 + "_" + item2 + "_" + position1 + "_" + position2;

                ILinearIntermediateSymbol<Flt64> symbol;
                if (StowageModel.StowageNeeded(item2, position1) && StowageModel.StowageNeeded(item1, position2))
                {
                    LinearExpressionSymbol? first = _stowage.StowageSymbol(i1, j2);
                    LinearExpressionSymbol? second = _stowage.StowageSymbol(i2, j1);
                    if (first is null || second is null)
                    {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.ApplicationFailed,
                            "配载符号缺失 / the stowage symbols are missing: " + name));
                    }
                    // **条件 = `s[i1,j2] + s[i2,j1] − 2 + ε`，默认关系 `GT`**。
                    // **The condition is `s[i1,j2] + s[i2,j1] − 2 + ε` with the default `GT`.**
                    var condition = new LinearPolynomial<Flt64>(
                        new List<LinearMonomial<Flt64>>(first.Polynomial.Monomials)
                        {
                            new(new Flt64(1.0), second),
                        },
                        first.Polynomial.Constant + second.Polynomial.Constant - new Flt64(2.0) + new Flt64(NonzeroTolerance));
                    // **`strictBoundary` 取默认值 `NONZERO_TOLERANCE = 1e-10`**，不是 0.5。
                    //
                    // 构造 `IfFunction` 时这里传入 `converter.intoValue(Flt64(NONZERO_TOLERANCE))`——
                    // 语义是**真假分支之间的业务间隔**（Big-M 的 gap），不是判定阈值。取 0.5 会把整个取值区间
                    // `{−2+ε, −1+ε, ε}` 压到边界下方，`SafeLattice` 因此拒绝。实测 1e-12/1e-11/1e-10 都能构造，
                    // 1e-9 及以上全部失败——即上界就是 `ε = NONZERO_TOLERANCE` 本身。
                    //
                    // **`strictBoundary` here is `NONZERO_TOLERANCE = 1e-10`**, not 0.5. The call passes
                    // `converter.intoValue(Flt64(NONZERO_TOLERANCE))` and means the **business gap between the true and false branches** (the Big-M gap), not a decision threshold. A value
                    // of 0.5 pushes the whole value range `{−2+ε, −1+ε, ε}` below the boundary, so `SafeLattice` rejects it. Measured, 1e-12, 1e-11 and 1e-10 all construct while 1e-9 and
                    // above all fail — the ceiling is `ε = NONZERO_TOLERANCE` itself.
                    Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
                        condition: condition,
                        converter: IIntoValue<Flt64>.Identity,
                        strictBoundary: new Flt64(NonzeroTolerance),
                        name: name);
                    if (created is not Ok<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> ok)
                    {
                        return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                            ((Failed<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>>)created).Error);
                    }
                    symbol = new LinearFunctionSymbolAdapter<Flt64>(ok.Value!);
                }
                else
                {
                    // **不可放置的组合给恒零符号**，不是跳过。
                    // **An unplaceable combination gets a constant-zero symbol**, not skipped.
                    symbol = new LinearExpressionSymbol(
                        new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                        name: name);
                }
                Try added = model.Add(symbol);
                if (!added.IsOk)
                {
                    return added;
                }
                _reversals[(p1, p2)] = symbol;
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
    private static List<(T First, T Second)> OrderedPairs<T>(IReadOnlyList<T> source, Func<T, T, bool> predicate)
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

    /// <summary>
    /// 取舱位的装载顺序 / The position's loading order.
    /// </summary>
    /// <param name="position">舱位 / The position.</param>
    /// <returns>装载顺序 / The loading order.</returns>
    private static byte OrderOf(StowagePosition position) =>
        position.AircraftPosition.LoadingOrder?.Order ?? position.AircraftPosition.LinearLoadingOrder;
}
