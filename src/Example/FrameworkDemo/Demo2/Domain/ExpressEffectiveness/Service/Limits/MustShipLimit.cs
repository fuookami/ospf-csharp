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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service.Limits;

/// <summary>
/// 必须发货约束 / The must-ship constraint.
/// </summary>
/// <remarks>
/// 与对齐：**每个必须发货的货物**，其在**全部舱位**上的配载变量之和等于 1。
///
/// 四处必须沿用：
///
/// 1. **等式约束，右端是 1**（不是 `≥ 1`，也不是 `≤ 1`）。用不等式会允许重复装载或漏装。
/// 2. **求和覆盖全部舱位**，不是只覆盖"可放置"的舱位。不可放置的舱位变量本身为 0，加进去不影响等式。
/// 3. **`mustShipIndices` 是货物下标**（`Int`），不是货物对象。
/// 4. **约束名带货物名**：`must_ship_limit_{item}`。
///
/// For **each must-ship item**, the sum of its stowage variables over **all positions** equals 1. Four things are carried over: **it is an equality with
/// right-hand side 1**, not `≥` or `≤`, which would allow double loading or a miss; **the sum covers every position**, not only the placeable ones, since an unplaceable position's
/// variable is already zero and adding it does not change the equality; **`mustShipIndices` are item indices**, not item objects; and **the name carries the item**.
/// </remarks>
internal sealed class MustShipLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly StowageModel _stowage;
    private readonly IReadOnlyList<int> _mustShipIndices;

    /// <summary>
    /// 构造必须发货约束 / Construct the must-ship constraint.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="mustShipIndices">必须发货的货物下标 / The must-ship item indices.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public MustShipLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        StowageModel stowage,
        IReadOnlyList<int> mustShipIndices,
        string name = "must_ship_limit")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);
        ArgumentNullException.ThrowIfNull(mustShipIndices);
        _items = items;
        _positions = positions;
        _stowage = stowage;
        _mustShipIndices = mustShipIndices;
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束 / Apply the constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        foreach (int c in _mustShipIndices)
        {
            if (c < 0 || c >= _items.Count)
            {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "必须发货下标越界 / the must-ship index is out of range: " + c));
            }
            var monomials = new List<LinearMonomial<Flt64>>();
            Flt64 constant = Flt64.Zero;
            for (int p = 0; p < _positions.Count; ++p)
            {
                LinearExpressionSymbol? symbol = _stowage.StowageSymbol(c, p);
                if (symbol is null)
                {
                    continue;
                }
                foreach (LinearMonomial<Flt64> monomial in symbol.Polynomial.Monomials)
                {
                    monomials.Add(monomial);
                }
                constant += symbol.Polynomial.Constant;
            }
            string rowName = Name + "_" + _items[c];
            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(monomials, constant),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                Comparison.EQ,
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
