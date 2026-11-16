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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;

/// <summary>
/// 同来源 / 同目的地相邻 / The same-source and same-destination adjacency indicators.
/// </summary>
/// <remarks>
/// 与基线 `loading_effectiveness/model/TransferAdjacentLoading.kt` 对齐：为一个（来源, 相邻舱位对）与一个
/// （目的地, 相邻舱位对）各造一个指示符号，表示"这两个相邻舱位都装了同一来源（或目的地）的货"。
///
/// 六处必须照抄：
///
/// 1. **第一个舱位需要配载或调整时才用 `IfFunction`**（基线判 `position1.status.stowageNeeded || adjustmentNeeded`）。
///    判的是**第一个**舱位，不是两个都判。
/// 2. **条件多项式是 `loadAmount1 + loadAmount2 − 2 + ε`**，`ε = NONZERO_TOLERANCE = 1e-10`，
///    `strictBoundary` **也取这个 ε**（：取 0.5 会被 `SafeLattice` 拒绝）。
/// 3. **第一个舱位不需配载时走两条退化分支**：`loadAmount1` 恒为 0 就给恒零符号，否则给
///    "多项式就是 `loadAmount1` 本身"的符号。**不是一律给零**。
/// 4. **`loadAmount` 是"该舱位上满足谓词的货物的装载量之和"**，谓词分别按来源、按目的地。
/// 5. **两个矩阵的维度是（来源/目的地 × 相邻舱位对）**，不是反过来。
/// 6. 符号名带来源（或目的地）与两个舱位。
///
/// Aligned with the baseline: one indicator per (source, adjacent pair) and one per (destination, adjacent pair), meaning "both adjacent positions carry cargo of the same source (or
/// destination)". Six things must be copied: **the `IfFunction` branch fires only when the first position needs stowage or adjustment** — the baseline tests
/// `position1.status.stowageNeeded || adjustmentNeeded`, i.e. the **first** position, not both; **the condition is `loadAmount1 + loadAmount2 − 2 + ε`** with
/// `ε = NONZERO_TOLERANCE = 1e-10`, and `strictBoundary` **also takes that ε** (measured: 0.5 is rejected by `SafeLattice`); **when the first position needs no stowage there are two
/// degenerate branches** — a constant-zero symbol if `loadAmount1` is identically zero, otherwise a symbol whose polynomial **is** `loadAmount1`, so it is **not** always zero; **`loadAmount`
/// is the summed load of the items on that position matching the predicate**, by source and by destination respectively; **the matrices are (source/destination × adjacent pair)**, not the
/// reverse; and the names carry the source (or destination) and both positions.
/// </remarks>
public sealed class TransferAdjacentLoading
{
    /// <summary>浮点边界余量：与基线的 `NONZERO_TOLERANCE` 一致 / The floating-point margin, matching the baseline's `NONZERO_TOLERANCE`.</summary>
    private const double NonzeroTolerance = 1e-10;

    private readonly IReadOnlyList<(StowagePosition First, StowagePosition Second)> _adjacentPositions;
    private readonly IReadOnlyList<string> _sources;
    private readonly IReadOnlyList<string> _destinations;
    private readonly Load _load;
    private readonly Dictionary<(int, int), ILinearIntermediateSymbol<Flt64>> _sameSource = new();
    private readonly Dictionary<(int, int), ILinearIntermediateSymbol<Flt64>> _sameDestination = new();

    /// <summary>
    /// 构造相邻装载指示 / Construct the adjacency indicators.
    /// </summary>
    /// <param name="adjacentPositions">相邻舱位对 / The adjacent position pairs.</param>
    /// <param name="sources">来源集合 / The source set.</param>
    /// <param name="destinations">目的地集合 / The destination set.</param>
    /// <param name="load">装载模型 / The load model.</param>
    public TransferAdjacentLoading(
        IReadOnlyList<(StowagePosition First, StowagePosition Second)> adjacentPositions,
        IReadOnlyList<string> sources,
        IReadOnlyList<string> destinations,
        Load load)
    {
        ArgumentNullException.ThrowIfNull(adjacentPositions);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(load);
        _adjacentPositions = adjacentPositions;
        _sources = sources;
        _destinations = destinations;
        _load = load;
    }

    /// <summary>来源集合 / The source set</summary>
    public IReadOnlyList<string> Sources => _sources;

    /// <summary>目的地集合 / The destination set</summary>
    public IReadOnlyList<string> Destinations => _destinations;

    /// <summary>
    /// 取同来源指示 / Fetch a same-source indicator.
    /// </summary>
    /// <param name="sourceIndex">来源下标 / The source index.</param>
    /// <param name="positionPairIndex">舱位对下标 / The position-pair index.</param>
    /// <returns>指示符号 / The indicator symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? SameSource(int sourceIndex, int positionPairIndex) =>
        _sameSource.TryGetValue((sourceIndex, positionPairIndex), out ILinearIntermediateSymbol<Flt64>? symbol)
            ? symbol
            : null;

    /// <summary>
    /// 取同目的地指示 / Fetch a same-destination indicator.
    /// </summary>
    /// <param name="destinationIndex">目的地下标 / The destination index.</param>
    /// <param name="positionPairIndex">舱位对下标 / The position-pair index.</param>
    /// <returns>指示符号 / The indicator symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? SameDestination(int destinationIndex, int positionPairIndex) =>
        _sameDestination.TryGetValue((destinationIndex, positionPairIndex), out ILinearIntermediateSymbol<Flt64>? symbol)
            ? symbol
            : null;

    /// <summary>
    /// 注册到模型 / Register with the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="positions">舱位列表，用来定位下标 / The position list, for indices.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model, IReadOnlyList<StowagePosition> positions)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(positions);
        if (_sameSource.Count > 0)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        var indices = new Dictionary<StowagePosition, int>();
        for (int index = 0; index < positions.Count; ++index)
        {
            indices.TryAdd(positions[index], index);
        }

        for (int s = 0; s < _sources.Count; ++s)
        {
            string source = _sources[s];
            for (int p = 0; p < _adjacentPositions.Count; ++p)
            {
                (StowagePosition position1, StowagePosition position2) = _adjacentPositions[p];
                string name = "same_source_adjacent_" + source + "_" + position1 + "_" + position2;
                Try added = Build(
                    model, indices, position1, position2,
                    item => item.Source == source, name,
                    _sameSource, s, p);
                if (!added.IsOk)
                {
                    return added;
                }
            }
        }

        for (int d = 0; d < _destinations.Count; ++d)
        {
            string destination = _destinations[d];
            for (int p = 0; p < _adjacentPositions.Count; ++p)
            {
                (StowagePosition position1, StowagePosition position2) = _adjacentPositions[p];
                string name = "same_destination_adjacent_" + destination + "_" + position1 + "_" + position2;
                Try added = Build(
                    model, indices, position1, position2,
                    item => item.Destination == destination, name,
                    _sameDestination, d, p);
                if (!added.IsOk)
                {
                    return added;
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 构造一个指示符号并注册 / Build and register one indicator.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="indices">舱位下标 / The position indices.</param>
    /// <param name="position1">第一个舱位 / The first position.</param>
    /// <param name="position2">第二个舱位 / The second position.</param>
    /// <param name="predicate">货物谓词 / The item predicate.</param>
    /// <param name="name">符号名 / The symbol name.</param>
    /// <param name="sink">符号表 / The symbol map.</param>
    /// <param name="row">矩阵行下标 / The matrix row index.</param>
    /// <param name="column">矩阵列下标 / The matrix column index.</param>
    /// <returns>操作结果 / Operation result.</returns>
    private Try Build(
        LinearMetaModel<Flt64> model,
        IReadOnlyDictionary<StowagePosition, int> indices,
        StowagePosition position1,
        StowagePosition position2,
        Func<Item, bool> predicate,
        string name,
        Dictionary<(int, int), ILinearIntermediateSymbol<Flt64>> sink,
        int row,
        int column)
    {
        LinearExpressionSymbol first = _load.LoadAmountOf(indices[position1], predicate);
        LinearExpressionSymbol second = _load.LoadAmountOf(indices[position2], predicate);

        ILinearIntermediateSymbol<Flt64> symbol;
        // **只看第一个舱位是否需要配载或调整**。
        // **Only the first position's stowage-or-adjustment need is tested.**
        if (position1.Status.StowageNeeded || position1.Status.AdjustmentNeeded)
        {
            var terms = new List<LinearMonomial<Flt64>>(first.Polynomial.Monomials);
            terms.AddRange(second.Polynomial.Monomials);
            var condition = new LinearPolynomial<Flt64>(
                terms,
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
        else if (IsIdenticallyZero(first))
        {
            // **`loadAmount1` 恒为 0 → 恒零符号**。
            // **`loadAmount1` identically zero → a constant-zero symbol.**
            symbol = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero), name: name);
        }
        else
        {
            // **否则多项式就是 `loadAmount1` 本身**，不是零。
            // **Otherwise the polynomial is `loadAmount1` itself**, not zero.
            symbol = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(first.Polynomial.Monomials, first.Polynomial.Constant),
                name: name);
        }

        Try added = model.Add(symbol);
        if (!added.IsOk)
        {
            return added;
        }
        sink[(row, column)] = symbol;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 多项式是否恒为 0 / Whether the polynomial is identically zero.
    /// </summary>
    /// <remarks>
    /// 判**单项式表为空**，不是判 `Range.FixedValue`——后者对携带单项式的符号也可能返回 0。
    ///
    /// Tests whether the **monomial list is empty**, not `Range.FixedValue`, which can return 0 even for a symbol carrying monomials (measured).
    /// </remarks>
    /// <param name="symbol">符号 / The symbol.</param>
    /// <returns>是否恒零 / Whether it is identically zero.</returns>
    private static bool IsIdenticallyZero(LinearExpressionSymbol symbol) =>
        symbol.Polynomial.Monomials.Count == 0
        && symbol.Polynomial.Constant.ToDouble() == 0.0;
}
