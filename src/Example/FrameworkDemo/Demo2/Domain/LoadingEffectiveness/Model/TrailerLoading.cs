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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;

/// <summary>
/// 拖车换装与绕行指示 / The trailer-change and trailer-circling indicators.
/// </summary>
/// <remarks>
/// 与基线 `loading_effectiveness/model/TrailerLoading.kt` 对齐，两个矩阵：
///
/// | 矩阵 | 维度 | 含义 |
/// | --- | --- | --- |
/// | `trailer_change` | （有序拖车对 × 相邻舱位对） | 相邻两个舱位装的是**不同拖车**的货 |
/// | `trailer_circling` | （拖车内有序货物对 × 相邻舱位对） | 同一拖车的两件货被**反着**放 |
///
/// 七处必须照抄：
///
/// 1. **`orderedTrailers` 只收"至少有一件货在本次装载里"的拖车**（基线 `filter { trailer.items.any { it in items } }`），
///    再按 `顺序` 后按 `名字` 排序，最后 `zipWithNext` 取相邻对。
/// 2. **`orderedItemsInTrailers` 是每个拖车内部的货物全组合**，且**不排序、不按顺序过滤**（与 `SequentialLoading` 不同）。
/// 3. **`trailer_change` 的条件是交叉的**：`load(trailer2, pos1) + load(trailer1, pos2) − 2 + ε`——
///    第一项配的是**第二个**拖车。写成同序会让"换装"的语义整个反过来。
/// 4. **`trailer_change` 没有退化分支**，第一个舱位怎样都用 `IfFunction`。
/// 5. **`trailer_circling` 用配载变量而不是装载量**：`s[i2,j1] + s[i1,j2] − 2 + ε`。
/// 6. **`trailer_circling` 有恒零退化分支**（不可放置的组合），`trailer_change` 没有。
/// 7. `ε = NONZERO_TOLERANCE = 1e-10`，`strictBoundary` **也取这个 ε**（B620 实测：0.5 会被 `SafeLattice` 拒绝）。
///
/// Aligned with the baseline, two matrices: `trailer_change` over (ordered trailer pair × adjacent position pair) meaning the two adjacent positions carry **different trailers'** cargo, and
/// `trailer_circling` over (ordered item pair within a trailer × adjacent position pair) meaning two items of one trailer were placed **the wrong way round**. Seven things must be
/// copied: **`orderedTrailers` keeps only trailers with at least one item in this load**, sorted by order then name, then `zipWithNext`; **`orderedItemsInTrailers` is the full combination
/// within each trailer, neither sorted nor filtered by sequence** (unlike `SequentialLoading`); **`trailer_change`'s condition is crossed** — `load(trailer2, pos1) + load(trailer1, pos2) − 2
/// + ε`, the first term pairing the **second** trailer, and the same order would invert the meaning; **`trailer_change` has no degenerate branch**; **`trailer_circling` uses stowage
/// variables rather than loads**; **`trailer_circling` has a constant-zero branch** for unplaceable combinations while `trailer_change` does not; and `ε = NONZERO_TOLERANCE = 1e-10`,
/// with `strictBoundary` **also taking that ε** (measured in B620).
/// </remarks>
public sealed class TrailerLoading
{
    /// <summary>浮点边界余量：与基线的 `NONZERO_TOLERANCE` 一致 / The floating-point margin, matching the baseline's `NONZERO_TOLERANCE`.</summary>
    private const double NonzeroTolerance = 1e-10;

    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly IReadOnlyList<(StowagePosition First, StowagePosition Second)> _adjacentPositions;
    private readonly IReadOnlyList<Trailer> _trailers;
    private readonly StowageModel _stowage;
    private readonly Load _load;
    private readonly Dictionary<(int, int), ILinearIntermediateSymbol<Flt64>> _trailerChange = new();
    private readonly Dictionary<(int, int), ILinearIntermediateSymbol<Flt64>> _trailerCircling = new();

    /// <summary>
    /// 构造拖车指示 / Construct the trailer indicators.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="adjacentPositions">相邻舱位对 / The adjacent position pairs.</param>
    /// <param name="trailers">拖车 / The trailers.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="load">装载模型 / The load model.</param>
    public TrailerLoading(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyList<(StowagePosition First, StowagePosition Second)> adjacentPositions,
        IReadOnlyList<Trailer> trailers,
        StowageModel stowage,
        Load load)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(adjacentPositions);
        ArgumentNullException.ThrowIfNull(trailers);
        ArgumentNullException.ThrowIfNull(stowage);
        ArgumentNullException.ThrowIfNull(load);
        _items = items;
        _positions = positions;
        _adjacentPositions = adjacentPositions;
        _trailers = trailers;
        _stowage = stowage;
        _load = load;

        var inLoad = new HashSet<Item>(items);
        var itemPairs = new List<(Item First, Item Second)>();
        foreach (Trailer trailer in trailers)
        {
            var trailerItems = new List<Item>();
            foreach (Item item in items)
            {
                if (Contains(trailer.Items, item))
                {
                    trailerItems.Add(item);
                }
            }
            // **全组合，不排序、不按顺序过滤。**
            // **Full combinations, neither sorted nor filtered by sequence.**
            for (int i = 0; i < trailerItems.Count; ++i)
            {
                for (int j = i + 1; j < trailerItems.Count; ++j)
                {
                    itemPairs.Add((trailerItems[i], trailerItems[j]));
                }
            }
        }
        OrderedItemsInTrailers = itemPairs;

        var eligible = new List<Trailer>();
        foreach (Trailer trailer in trailers)
        {
            foreach (Item candidate in trailer.Items)
            {
                if (inLoad.Contains(candidate))
                {
                    eligible.Add(trailer);
                    break;
                }
            }
        }
        eligible.Sort((left, right) =>
        {
            int byOrder = left.Order.CompareTo(right.Order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(left.Name, right.Name);
        });
        var trailerPairs = new List<(Trailer First, Trailer Second)>();
        for (int index = 0; index + 1 < eligible.Count; ++index)
        {
            trailerPairs.Add((eligible[index], eligible[index + 1]));
        }
        OrderedTrailers = trailerPairs;
    }

    /// <summary>相邻舱位对 / The adjacent position pairs</summary>
    public IReadOnlyList<(StowagePosition First, StowagePosition Second)> AdjacentPositions => _adjacentPositions;

    /// <summary>拖车内有序货物对 / The ordered item pairs within trailers</summary>
    public IReadOnlyList<(Item First, Item Second)> OrderedItemsInTrailers { get; }

    /// <summary>相邻拖车对 / The adjacent trailer pairs</summary>
    public IReadOnlyList<(Trailer First, Trailer Second)> OrderedTrailers { get; }

    /// <summary>
    /// 取换装指示 / Fetch a trailer-change indicator.
    /// </summary>
    /// <param name="trailerPairIndex">拖车对下标 / The trailer-pair index.</param>
    /// <param name="positionPairIndex">舱位对下标 / The position-pair index.</param>
    /// <returns>指示符号 / The indicator symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? TrailerChange(int trailerPairIndex, int positionPairIndex) =>
        _trailerChange.TryGetValue((trailerPairIndex, positionPairIndex), out ILinearIntermediateSymbol<Flt64>? symbol)
            ? symbol
            : null;

    /// <summary>
    /// 取绕行指示 / Fetch a trailer-circling indicator.
    /// </summary>
    /// <param name="itemPairIndex">货物对下标 / The item-pair index.</param>
    /// <param name="positionPairIndex">舱位对下标 / The position-pair index.</param>
    /// <returns>指示符号 / The indicator symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? TrailerCircling(int itemPairIndex, int positionPairIndex) =>
        _trailerCircling.TryGetValue((itemPairIndex, positionPairIndex), out ILinearIntermediateSymbol<Flt64>? symbol)
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
        if (_trailerChange.Count > 0)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        var requiredPositions = new List<int>();
        foreach ((StowagePosition first, StowagePosition second) in _adjacentPositions)
        {
            requiredPositions.Add(IndexOf(first));
            requiredPositions.Add(IndexOf(second));
        }

        // `groupedAmounts`：每辆（被相邻对用到的）拖车在每个所需舱位上的装载量。
        // `groupedAmounts`: each trailer used by the adjacent pairs, its load at every required position.
        var usedTrailers = new List<Trailer>();
        var seenTrailers = new HashSet<string>(StringComparer.Ordinal);
        foreach ((Trailer first, Trailer second) in OrderedTrailers)
        {
            if (seenTrailers.Add(first.Name))
            {
                usedTrailers.Add(first);
            }
            if (seenTrailers.Add(second.Name))
            {
                usedTrailers.Add(second);
            }
        }
        var grouped = new Dictionary<string, IReadOnlyDictionary<int, LinearExpressionSymbol>>(StringComparer.Ordinal);
        foreach (Trailer trailer in usedTrailers)
        {
            grouped[trailer.Name] = _load.LoadAmountsOf(
                requiredPositions, _load.ItemIndicesOf(item => Contains(trailer.Items, item)));
        }

        for (int p1 = 0; p1 < OrderedTrailers.Count; ++p1)
        {
            (Trailer trailer1, Trailer trailer2) = OrderedTrailers[p1];
            for (int p2 = 0; p2 < _adjacentPositions.Count; ++p2)
            {
                (StowagePosition position1, StowagePosition position2) = _adjacentPositions[p2];
                // **交叉**：第一个拖车配**第二个**舱位。
                // **Crossed**: the first trailer pairs the **second** position.
                LinearExpressionSymbol first = grouped[trailer2.Name][IndexOf(position1)];
                LinearExpressionSymbol second = grouped[trailer1.Name][IndexOf(position2)];
                string name = "trailer_change_" + trailer1 + "_" + trailer2 + "_" + position1 + "_" + position2;
                Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> created =
                    CreateIndicator(model, first.Polynomial, second.Polynomial, name);
                if (created is not Ok<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> builtOk)
                {
                    return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                        ((Failed<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>>)created).Error);
                }
                _trailerChange[(p1, p2)] = builtOk.Value!;
            }
        }

        for (int q1 = 0; q1 < OrderedItemsInTrailers.Count; ++q1)
        {
            (Item item1, Item item2) = OrderedItemsInTrailers[q1];
            int i1 = IndexOf(item1);
            int i2 = IndexOf(item2);
            for (int p2 = 0; p2 < _adjacentPositions.Count; ++p2)
            {
                (StowagePosition position1, StowagePosition position2) = _adjacentPositions[p2];
                int j1 = IndexOf(position1);
                int j2 = IndexOf(position2);
                string name = "trailer_circling_" + item1 + "_" + item2 + "_" + position1 + "_" + position2;
                ILinearIntermediateSymbol<Flt64> symbol;
                if (StowageModel.StowageNeeded(item2, position1) && StowageModel.StowageNeeded(item1, position2))
                {
                    // **用配载变量，不是装载量。**
                    // **Uses stowage variables, not loads.**
                    LinearExpressionSymbol? a = _stowage.StowageSymbol(i2, j1);
                    LinearExpressionSymbol? b = _stowage.StowageSymbol(i1, j2);
                    if (a is null || b is null)
                    {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.ApplicationFailed,
                            "配载符号缺失 / the stowage symbols are missing: " + name));
                    }
                    var terms = new List<LinearMonomial<Flt64>>(a.Polynomial.Monomials);
                    terms.AddRange(b.Polynomial.Monomials);
                    var condition = new LinearPolynomial<Flt64>(
                        terms,
                        a.Polynomial.Constant + b.Polynomial.Constant
                            - new Flt64(2.0) + new Flt64(NonzeroTolerance));
                    Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> built = IfFunction<Flt64>.Create(
                        condition: condition,
                        converter: IIntoValue<Flt64>.Identity,
                        strictBoundary: new Flt64(NonzeroTolerance),
                        name: name);
                    if (built is not Ok<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> ok)
                    {
                        return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                            ((Failed<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>>)built).Error);
                    }
                    symbol = new LinearFunctionSymbolAdapter<Flt64>(ok.Value!);
                }
                else
                {
                    symbol = new LinearExpressionSymbol(
                        new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero), name: name);
                }
                Try added = model.Add(symbol);
                if (!added.IsOk)
                {
                    return added;
                }
                _trailerCircling[(q1, p2)] = symbol;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 构造一个换装指示并注册 / Build and register a trailer-change indicator.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="first">第一项多项式 / The first polynomial.</param>
    /// <param name="second">第二项多项式 / The second polynomial.</param>
    /// <param name="name">符号名 / The symbol name.</param>
    /// <returns>操作结果 / Operation result.</returns>
    private Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> CreateIndicator(
        LinearMetaModel<Flt64> model,
        LinearPolynomial<Flt64> first,
        LinearPolynomial<Flt64> second,
        string name)
    {
        var terms = new List<LinearMonomial<Flt64>>(first.Monomials);
        terms.AddRange(second.Monomials);
        var condition = new LinearPolynomial<Flt64>(
            terms,
            first.Constant + second.Constant - new Flt64(2.0) + new Flt64(NonzeroTolerance));
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(NonzeroTolerance),
            name: name);
        if (created is not Ok<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> ok)
        {
            return new Failed<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>>(
                ((Failed<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>>)created).Error);
        }
        var symbol = new LinearFunctionSymbolAdapter<Flt64>(ok.Value!);
        Try added = model.Add(symbol);
        if (!added.IsOk)
        {
            return new Failed<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.ApplicationFailed, "指示符号注册失败 / the indicator failed to register: " + name));
        }
        return Results.Ok<ILinearIntermediateSymbol<Flt64>>(symbol);
    }

    /// <summary>
    /// 取货物下标 / The item index.
    /// </summary>
    /// <param name="item">货物 / The item.</param>
    /// <returns>下标 / The index.</returns>
    private int IndexOf(Item item)
    {
        for (int index = 0; index < _items.Count; ++index)
        {
            if (ReferenceEquals(_items[index], item))
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>
    /// 取舱位下标 / The position index.
    /// </summary>
    /// <param name="position">舱位 / The position.</param>
    /// <returns>下标 / The index.</returns>
    private int IndexOf(StowagePosition position)
    {
        for (int index = 0; index < _positions.Count; ++index)
        {
            if (ReferenceEquals(_positions[index], position))
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>
    /// 判断拖车货物里是否含给定货物 / Whether the trailer's items contain the given item.
    /// </summary>
    /// <param name="source">拖车货物 / The trailer items.</param>
    /// <param name="item">货物 / The item.</param>
    /// <returns>是否包含 / Whether it contains it.</returns>
    private static bool Contains(IReadOnlyList<Item> source, Item item)
    {
        foreach (Item candidate in source)
        {
            if (ReferenceEquals(candidate, item))
            {
                return true;
            }
        }
        return false;
    }
}
