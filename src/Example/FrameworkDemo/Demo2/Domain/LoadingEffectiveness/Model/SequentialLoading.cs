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
/// 顺序装载反转 / The sequential-loading reversals.
/// </summary>
/// <remarks>
/// 与基线 `loading_effectiveness/model/SequentialLoading.kt` 对齐：为**每个（有序货物对, 有序舱位对）**造一个中间符号，
/// 表示"这一对的装载先后被反了"。
///
/// 六处必须照抄：
///
/// 1. **只收"有装载顺序"的货物**（基线 `filter { it.order?.order != null }`），并按 `顺序升序` 再按 `Id` 排序。
/// 2. **有序对是全组合**，谓词是**严格小于**（顺序小的在前）。
/// 3. **有序舱位对由调用方传入**（聚合算好的），不在本类里重算。
/// 4. **条件多项式是 `s[i1,j2] + s[i2,j1] − 2 + ε`**，`ε = NONZERO_TOLERANCE = 1e-10`，
///    `strictBoundary` **也取这个 ε**——它是真假分支的间隔，不是阈值。取 0.5 会被 `SafeLattice` 拒绝。
/// 5. **不可放置的组合给恒零符号**，不是跳过。
/// 6. **符号名带三个元素**：`item_order_reverse_{item1}_{item2}_{position1}_{position2}`。
///    这依赖 `Item` 与 `StowagePosition` 的 `ToString` 覆写（补上），否则全部重名。
///
/// Aligned with the baseline: one intermediate symbol per (ordered item pair, ordered position pair), meaning "this pair was loaded the wrong way round". Six things must be copied:
/// **only items carrying a loading sequence are kept** (the baseline's filter), sorted by sequence then `Id`; **ordered pairs are all combinations** under a strict less-than
/// predicate; **the ordered position pairs come from the caller** rather than being recomputed here; **the condition is `s[i1,j2] + s[i2,j1] − 2 + ε`** with `ε = NONZERO_TOLERANCE
/// = 1e-10`, and `strictBoundary` **also takes that ε** — it is the true/false gap, not a threshold, and 0.5 is rejected by `SafeLattice` (measured); **an unplaceable combination
/// gets a constant-zero symbol** rather than being skipped; and **the name carries three elements**, which relies on the `ToString` overrides added.
/// </remarks>
public sealed class SequentialLoading
{
    /// <summary>浮点边界余量：与基线的 `NONZERO_TOLERANCE` 一致 / The floating-point margin, matching the baseline's `NONZERO_TOLERANCE`.</summary>
    private const double NonzeroTolerance = 1e-10;

    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly IReadOnlyList<(Item First, Item Second)> _orderedItems;
    private readonly IReadOnlyList<(StowagePosition First, StowagePosition Second)> _orderedPositions;
    private readonly StowageModel _stowage;
    private readonly Dictionary<(int, int), ILinearIntermediateSymbol<Flt64>> _reversals = new();

    /// <summary>
    /// 构造顺序装载反转 / Construct the sequential-loading reversals.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="orderedPositions">有序舱位对 / The ordered position pairs.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    public SequentialLoading(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyList<(StowagePosition First, StowagePosition Second)> orderedPositions,
        StowageModel stowage)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(orderedPositions);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _orderedPositions = orderedPositions;
        _stowage = stowage;

        // **只收有装载顺序的货物**。
        // **Only items carrying a loading sequence.**
        var sorted = new List<Item>();
        foreach (Item item in items)
        {
            if (SequenceOf(item) is not null)
            {
                sorted.Add(item);
            }
        }
        sorted.Sort((left, right) =>
        {
            int bySequence = SequenceOf(left)!.Value.CompareTo(SequenceOf(right)!.Value);
            return bySequence != 0 ? bySequence : left.Id.CompareTo(right.Id);
        });

        var pairs = new List<(Item First, Item Second)>();
        for (int i = 0; i < sorted.Count; ++i)
        {
            for (int j = 0; j < sorted.Count; ++j)
            {
                if (i != j && SequenceOf(sorted[i])!.Value < SequenceOf(sorted[j])!.Value)
                {
                    pairs.Add((sorted[i], sorted[j]));
                }
            }
        }
        _orderedItems = pairs;
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
                string name = "item_order_reverse_" + item1 + "_" + item2 + "_" + position1 + "_" + position2;

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
                    var condition = new LinearPolynomial<Flt64>(
                        new List<LinearMonomial<Flt64>>(first.Polynomial.Monomials)
                        {
                            new(new Flt64(1.0), second),
                        },
                        first.Polynomial.Constant + second.Polynomial.Constant
                            - new Flt64(2.0) + new Flt64(NonzeroTolerance));
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
                    // **不可放置的组合给恒零符号**。
                    // **An unplaceable combination gets a constant-zero symbol.**
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
    /// 取货物的装载顺序 / The item's loading sequence.
    /// </summary>
    /// <param name="item">货物 / The item.</param>
    /// <returns>装载顺序，或 `null` / The sequence, or null.</returns>
    private static byte? SequenceOf(Item item) => item.Order?.Sequence;

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
