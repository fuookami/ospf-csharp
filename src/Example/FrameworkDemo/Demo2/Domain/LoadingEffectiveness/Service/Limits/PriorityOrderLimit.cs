#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
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
/// 优先级顺序限制 / The priority-order constraint.
/// </summary>
/// <remarks>
/// 与对齐：**高优先级货物必须占到更靠前的舱位**，
/// 用 Big-M 包成一条线性不等式。
///
/// 五处必须沿用：
///
/// 1. **排序键是 `linearLoadingOrder`**，不是 `loadingOrder.order`（后者可能为空）。基线写 `it.base.linearLoadingOrder`。
/// 2. **排名是 `sortedBy(...).withIndex.associate { value to index }`**——`associate` 在**相等键上保留最后一次**。
///    写成 `TryAdd`（首次）会让并列舱位的排名不同。注释明确点出了这一点。
/// 3. **不等式是 `Σ (rank + M)·x[i,p] + Σ (−rank + M)·x[j,p] ≤ 2M`**。两个 `M` 都**加**，不是减。
/// 4. **只对严格更高优先级的（i, j）加**，同优先级跳过。
/// 5. **约束名带优先级值**：`priority_order_limit_{i}_{j}_{priority}`。
///
/// **a higher-priority item must occupy an earlier position**, wrapped in one Big-M linear inequality. Five things must be copied: **the sort key is
/// `linearLoadingOrder`**, not `loadingOrder.order`, which may be null; **the rank comes from `sortedBy(...).withIndex.associate { value to index }`**, where `associate` **keeps the
/// last entry for equal keys** — using `TryAdd` (first wins) would rank tied positions differently, a point its own comment calls out; **the inequality is
/// `Σ (rank + M)·x[i,p] + Σ (−rank + M)·x[j,p] ≤ 2M`**, with both `M`s **added**; **only strictly higher-priority (i, j) pairs are added**, equal priorities being skipped; and
/// **the name carries the priority value**.
/// </remarks>
internal sealed class PriorityOrderLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly StowageModel _stowage;
    private readonly double _bigM;

    /// <summary>
    /// 构造优先级顺序限制 / Construct the priority-order constraint.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="bigM">位置的 Big-M 上界 / The Big-M upper bound over positions.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public PriorityOrderLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        StowageModel stowage,
        double bigM,
        string name = "priority_order_limit")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _stowage = stowage;
        _bigM = bigM;
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 舱位排名 / The position ranks.
    /// </summary>
    /// <remarks>
    /// **`associate` 在相等键上保留最后一次**（基线语义）。这里用"后写覆盖"实现同一条规则。
    ///
    /// **`associate` keeps the last entry for equal keys** (its semantics); this overwrites to match.
    /// </remarks>
    public IReadOnlyDictionary<StowagePosition, int> Ranks()
    {
        var sorted = new List<StowagePosition>(_positions);
        // 稳定排序：`sortedBy` 是稳定的，`Sort` 不是，所以带上下标做次键。
        // A stable sort: `sortedBy` is stable while `Sort` is not, so the original index breaks ties.
        var indexed = new List<(StowagePosition Position, int Original)>(sorted.Count);
        for (int index = 0; index < sorted.Count; ++index)
        {
            indexed.Add((sorted[index], index));
        }
        indexed.Sort((left, right) =>
        {
            int byOrder = left.Position.AircraftPosition.LinearLoadingOrder
                .CompareTo(right.Position.AircraftPosition.LinearLoadingOrder);
            return byOrder != 0 ? byOrder : left.Original.CompareTo(right.Original);
        });

        var ranks = new Dictionary<StowagePosition, int>();
        for (int index = 0; index < indexed.Count; ++index)
        {
            // **后写覆盖**，与 `associate` 一致。
            // **Overwrite**, matching `associate`.
            ranks[indexed[index].Position] = index;
        }
        return ranks;
    }

    /// <summary>
    /// 施加约束 / Apply the constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyDictionary<StowagePosition, int> order = Ranks();

        for (int i = 0; i < _items.Count; ++i)
        {
            for (int j = 0; j < _items.Count; ++j)
            {
                if (_items[i].Priority.Priority <= _items[j].Priority.Priority)
                {
                    continue;
                }
                var monomials = new List<LinearMonomial<Flt64>>();
                Flt64 constant = Flt64.Zero;
                for (int p = 0; p < _positions.Count; ++p)
                {
                    double rank = order[_positions[p]];
                    Accumulate(monomials, ref constant, _stowage.StowageSymbol(i, p), rank + _bigM);
                    Accumulate(monomials, ref constant, _stowage.StowageSymbol(j, p), -rank + _bigM);
                }

                string rowName = Name + "_" + i + "_" + j + "_" + _items[i].Priority.Priority;
                var row = new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(monomials, constant),
                    new LinearPolynomial<Flt64>(
                        Array.Empty<LinearMonomial<Flt64>>(),
                        new Flt64(2.0 * _bigM)),
                    Comparison.LE,
                    rowName);
                Try added = model.AddConstraint(row, group: null, name: rowName);
                if (!added.IsOk)
                {
                    return added;
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 把一个符号按系数累加进单项式表 / Accumulate one symbol into the monomial list at a coefficient.
    /// </summary>
    /// <param name="monomials">单项式表 / The monomial list.</param>
    /// <param name="constant">常数累加器 / The constant accumulator.</param>
    /// <param name="symbol">配载符号 / The stowage symbol.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    private static void Accumulate(
        List<LinearMonomial<Flt64>> monomials,
        ref Flt64 constant,
        Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol? symbol,
        double coefficient)
    {
        if (symbol is null)
        {
            return;
        }
        var weight = new Flt64(coefficient);
        foreach (LinearMonomial<Flt64> monomial in symbol.Polynomial.Monomials)
        {
            monomials.Add(new LinearMonomial<Flt64>(weight * monomial.Coefficient, monomial.Symbol));
        }
        constant += weight * symbol.Polynomial.Constant;
    }
}
