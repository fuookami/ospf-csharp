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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service.Limits;

/// <summary>
/// 相对优先级反转目标 / The relative-priority-reversal objective.
/// </summary>
/// <remarks>
/// 与基线 `express_effectiveness/service/limits/ItemPriorityReverseLimit.kt` 对齐：把**每个（货物对, 舱位对）**的反转符号按系数加权求和，
/// 作为**最小化**目标，名为 `item priority reverse`。
///
/// 五处必须照抄：
///
/// 1. **是 `Minimum`**，与 `ItemPriorityLimit` 的 `Maximum` 相反。
/// 2. **目标名带空格**：`"item priority reverse"`。
/// 3. **空集合时直接返回成功，不加目标**。基线写 `if (monomials.isEmpty()) return ok`——这一条不能省，
///    否则零多项式目标会占掉一个目标位。
/// 4. **系数按"两个（舱位, 货物）对"取**：`coefficient(position2 to item1, position1 to item2)`。注意**顺序是交叉的**——
///    第一项是 `(position2, item1)`，不是 `(position1, item1)`。
/// 5. **只收有符号的组合**：符号缺失说明注册顺序错了，直接失败，而不是跳过。
///
/// Aligned with the baseline: the reversal symbols weighted by their coefficients are summed into a **minimisation** objective named `item priority reverse`. Five things must be copied:
/// **it is `Minimum`**, the opposite of `ItemPriorityLimit`'s `Maximum`; **the objective name has a space**; **an empty set returns success without adding an objective**, since the
/// baseline's `if (monomials.isEmpty()) return ok` must not be dropped, or a zero-polynomial objective would take up a slot; **the coefficient is taken per pair of (position, item)** —
/// `coefficient(position2 to item1, position1 to item2)`, with the **crossed** ordering, the first being `(position2, item1)` rather than `(position1, item1)`; and **only pairs with
/// symbols are collected**, where a missing symbol means the registration order is wrong and fails outright rather than skipping.
/// </remarks>
internal sealed class ItemPriorityReverseLimit
{
    private readonly IReadOnlyList<(Item First, Item Second)> _orderedItems;
    private readonly IReadOnlyList<(StowagePosition First, StowagePosition Second)> _orderedPositions;
    private readonly RelativeOrder _unloading;
    private readonly Func<(StowagePosition, Item), (StowagePosition, Item), Flt64> _coefficient;

    /// <summary>
    /// 构造反转目标 / Construct the reversal objective.
    /// </summary>
    /// <param name="unloading">相对优先级反转 / The relative priority reversals.</param>
    /// <param name="coefficient">按对的系数 / The per-pair coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public ItemPriorityReverseLimit(
        RelativeOrder unloading,
        Func<(StowagePosition, Item), (StowagePosition, Item), Flt64>? coefficient = null,
        string name = "item_priority_reverse_limit")
    {
        ArgumentNullException.ThrowIfNull(unloading);
        _unloading = unloading;
        _orderedItems = unloading.OrderedItems;
        _orderedPositions = unloading.OrderedPositions;
        _coefficient = coefficient ?? ((_, _) => Flt64.One);
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
        for (int p1 = 0; p1 < _orderedItems.Count; ++p1)
        {
            (Item item1, Item item2) = _orderedItems[p1];
            for (int p2 = 0; p2 < _orderedPositions.Count; ++p2)
            {
                (StowagePosition position1, StowagePosition position2) = _orderedPositions[p2];
                if (_unloading.Reversal(p1, p2) is not { } reversal)
                {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "反转符号未注册 / the reversal symbol is not registered at " + p1 + "," + p2));
                }
                // **系数取 `(position2, item1)` 与 `(position1, item2)`**——顺序是交叉的。
                // **The coefficient takes `(position2, item1)` and `(position1, item2)`** — the ordering is crossed.
                Flt64 coefficient = _coefficient((position2, item1), (position1, item2));
                // **函数符号的 `Polynomial` 就是它 `ResultVar` 上的线性多项式**，两种符号都能展。
                // **A function symbol's `Polynomial` is linear over its `ResultVar`**, so both symbol kinds expand.
                LinearPolynomial<Flt64> polynomial = reversal switch
                {
                    LinearExpressionSymbol plain => plain.Polynomial,
                    Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64> function => function.Polynomial,
                    _ => throw new NotSupportedException("unknown symbol kind"),
                };
                foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials)
                {
                    monomials.Add(new LinearMonomial<Flt64>(coefficient * monomial.Coefficient, monomial.Symbol));
                }
                constant += coefficient * polynomial.Constant;
            }
        }

        // **空集合不加目标**。
        // **An empty set adds no objective.**
        if (monomials.Count == 0)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        var objective = new LinearPolynomial<Flt64>(monomials, constant);
        return model.AddObject(ObjectCategory.Minimum, objective, "item priority reverse", displayName: null);
    }

    /// <summary>
    /// 取符号的单项式 / Fetch the symbol's monomials.
    /// </summary>
    /// <param name="symbol">符号 / The symbol.</param>
    /// <returns>单项式 / The monomials.</returns>
    private static IReadOnlyList<LinearMonomial<Flt64>> MonomialsOf(ILinearIntermediateSymbol<Flt64> symbol) =>
        symbol switch
        {
            LinearExpressionSymbol plain => plain.Polynomial.Monomials,
            // 函数符号（`IfFunction` 的适配器）没有可直接展成的多项式；基线的 `sum(...)` 会走符号自己的展开。
            // 本仓尚未实现那条展开路径，所以在这里**明确失败**，而不是当成零。
            _ => throw new NotSupportedException("function symbols cannot be flattened here"),
        };

    /// <summary>
    /// 取符号的常数 / Fetch the symbol's constant.
    /// </summary>
    /// <param name="symbol">符号 / The symbol.</param>
    /// <returns>常数 / The constant.</returns>
    private static Flt64 ConstantOf(ILinearIntermediateSymbol<Flt64> symbol) =>
        symbol switch
        {
            LinearExpressionSymbol plain => plain.Polynomial.Constant,
            _ => throw new NotSupportedException("function symbols cannot be flattened here"),
        };
}
