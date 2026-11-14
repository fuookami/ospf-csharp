#nullable enable

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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service.Limits;

/// <summary>
/// 同来源早装限制 / The same-source early-loading constraint.
/// </summary>
/// <remarks>
/// 与基线 `loading_effectiveness/service/limits/SourceEarlyLimit.kt` 对齐：**同一来源的多件货物至少有一件装进前段舱位**。
///
/// 五处必须照抄：
///
/// 1. **只有货物数大于 1 的来源才加约束**（基线的 `cargos.size` 不大于 1 时跳过）。
/// 2. **前段舱位按 `linearLoadingOrder` 排序后取前 `earlyEnd + 1` 个**，且**显式按装载顺序**，不依赖输入列表顺序。
/// 3. **`take` 的上界是 `(earlyEnd + 1).coerceIn(0, positions.size)`**——不夹取会越界。
/// 4. **关系是 `GE`，右端 1**（至少一件），不是等式。
/// 5. **约束名带来源**：`source_early_limit_{source}`。
///
/// Aligned with the baseline: **at least one of a source's items must go into an early position**. Five things must be copied: **only sources with more than one item are constrained**
/// (the baseline skips when `cargos.size` is at most 1); **the early positions are the first `earlyEnd + 1` in `linearLoadingOrder` order**, explicitly by loading order rather than
/// input-list order;
/// **the take bound is `(earlyEnd + 1).coerceIn(0, positions.size)`**, without which it would overrun; **the relation is `GE` with right-hand side 1** (at least one), not an equality;
/// and **the name carries the source**.
/// </remarks>
internal sealed class SourceEarlyLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly StowageModel _stowage;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<int>> _cargosBySource;
    private readonly int _earlyEnd;

    /// <summary>
    /// 构造同来源早装限制 / Construct the same-source early-loading constraint.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="cargosBySource">来源到货物下标的映射 / The source-to-item-index map.</param>
    /// <param name="earlyEnd">前段舱位的末位下标 / The index of the last early position.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public SourceEarlyLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        StowageModel stowage,
        IReadOnlyDictionary<string, IReadOnlyList<int>> cargosBySource,
        int earlyEnd,
        string name = "source_early_limit")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);
        ArgumentNullException.ThrowIfNull(cargosBySource);
        _items = items;
        _positions = positions;
        _stowage = stowage;
        _cargosBySource = cargosBySource;
        _earlyEnd = earlyEnd;
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 前段舱位下标 / The early-position indices.
    /// </summary>
    /// <returns>按装载顺序排列的下标 / The indices in loading order.</returns>
    public IReadOnlyList<int> EarlyPositions()
    {
        var indices = new List<int>(_positions.Count);
        for (int index = 0; index < _positions.Count; ++index)
        {
            indices.Add(index);
        }
        var original = new Dictionary<int, int>(indices.Count);
        for (int cursor = 0; cursor < indices.Count; ++cursor)
        {
            original[indices[cursor]] = cursor;
        }
        indices.Sort((left, right) =>
        {
            int byOrder = _positions[left].AircraftPosition.LinearLoadingOrder
                .CompareTo(_positions[right].AircraftPosition.LinearLoadingOrder);
            return byOrder != 0 ? byOrder : original[left].CompareTo(original[right]);
        });

        // **`(earlyEnd + 1).coerceIn(0, positions.size)`**，不夹取会越界。
        // **`(earlyEnd + 1).coerceIn(0, positions.size)`**; without the clamp it would overrun.
        int take = System.Math.Clamp(_earlyEnd + 1, 0, _positions.Count);
        return indices.GetRange(0, take);
    }

    /// <summary>
    /// 施加约束 / Apply the constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<int> earlyPositions = EarlyPositions();

        foreach (KeyValuePair<string, IReadOnlyList<int>> entry in _cargosBySource)
        {
            // **只有多于一件货物才加**。
            // **Only sources with more than one item are constrained.**
            if (entry.Value.Count <= 1)
            {
                continue;
            }
            var monomials = new List<LinearMonomial<Flt64>>();
            Flt64 constant = Flt64.Zero;
            foreach (int c in entry.Value)
            {
                if (c < 0 || c >= _items.Count)
                {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        "来源货物下标越界 / the source item index is out of range: " + c));
                }
                foreach (int p in earlyPositions)
                {
                    if (_stowage.StowageSymbol(c, p) is not { } symbol)
                    {
                        continue;
                    }
                    monomials.AddRange(symbol.Polynomial.Monomials);
                    constant += symbol.Polynomial.Constant;
                }
            }

            string rowName = Name + "_" + entry.Key;
            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(monomials, constant),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                Comparison.GE,
                rowName);
            Try added = model.AddConstraint(row, group: null, name: rowName);
            if (!added.IsOk)
            {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
