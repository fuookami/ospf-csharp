#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service.Limits;

/// <summary>
/// 货物优先级目标 / The item-priority objective.
/// </summary>
/// <remarks>
/// 与基线 `express_effectiveness/service/limits/ItemPriorityLimit.kt` 对齐：把**每个（货物, 舱位）**的
/// `系数 × 绝对优先级系数 × 配载变量` 求和，作为**最大化**目标，名为 `item priority`（带空格）。
///
/// 四处必须照抄：
///
/// 1. **是最大化目标**，不是最小化。
/// 2. **目标名带空格**：`"item priority"`，与管线名 `item_priority_limit` 不同。
/// 3. **两重求和**：外层货物、内层舱位，**每个组合都进目标**（不可放置的组合由配载变量本身为 0 处理）。
/// 4. **系数来自 `parameter.priority`**，且是**按货物**取，不是按优先级。
///
/// Aligned with the baseline: the sum over every (item, position) of `coefficient × absolutePriority × stowageVariable` becomes a **maximisation** objective named `item priority`
/// (with a space). Four things must be copied: **it is a maximisation**; **the objective name has a space**, unlike the pipeline name `item_priority_limit`; **there are two nested
/// sums** — items outside, positions inside, with **every combination entering the objective**, unplaceable ones handled by their stowage variable being zero; and **the coefficient
/// comes from `parameter.priority`**, taken **per item**, not per priority.
/// </remarks>
internal sealed class ItemPriorityLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly AbsoluteOrder _unloading;
    private readonly StowageModel _stowage;
    private readonly Func<Item, Flt64> _coefficient;

    /// <summary>
    /// 构造优先级目标 / Construct the priority objective.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="unloading">绝对优先级系数 / The absolute priority coefficients.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="coefficient">按货物的系数 / The per-item coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public ItemPriorityLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        AbsoluteOrder unloading,
        StowageModel stowage,
        Func<Item, Flt64>? coefficient = null,
        string name = "item_priority_limit")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(unloading);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _unloading = unloading;
        _stowage = stowage;
        _coefficient = coefficient ?? (_ => Flt64.One);
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加目标 / Apply the objective.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        for (int i = 0; i < _items.Count; ++i)
        {
            Item item = _items[i];
            Flt64 coefficient = _coefficient(item);
            for (int j = 0; j < _positions.Count; ++j)
            {
                if (_stowage.StowageSymbol(i, j) is not { } symbol)
                {
                    continue;
                }
                Flt64 weight = coefficient * _unloading.Invoke(item.Priority, _positions[j]);
                foreach (LinearMonomial<Flt64> monomial in symbol.Polynomial.Monomials)
                {
                    monomials.Add(new LinearMonomial<Flt64>(weight * monomial.Coefficient, monomial.Symbol));
                }
                constant += weight * symbol.Polynomial.Constant;
            }
        }
        var objective = new LinearPolynomial<Flt64>(monomials, constant);
        return model.AddObject(ObjectCategory.Maximum, objective, "item priority", displayName: null);
    }
}
