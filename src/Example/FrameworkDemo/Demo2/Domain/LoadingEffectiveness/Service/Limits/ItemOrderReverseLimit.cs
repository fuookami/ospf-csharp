#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service.Limits;

/// <summary>
/// 顺序装载反转目标 / The sequential-loading-reversal objective.
/// </summary>
/// <remarks>
/// 与基线 `loading_effectiveness/service/limits/ItemOrderReverseLimit.kt` 对齐：把**每个（货物对, 舱位对）**的反转符号
/// 按系数加权求和，作为**最小化**目标，名为 `"item order reverse"`（带空格）。
///
/// 五点必须照抄：
///
/// 1. **是 `Minimum`**。
/// 2. **目标名带空格**：`"item order reverse"`，与管线名 `item_order_reverse_limit` 不同。
/// 3. **空集合时直接返回成功、不加目标**（基线的 `if (monomials.isEmpty) return ok`）。
/// 4. **系数按"两个（舱位, 货物）对"取，且顺序交叉**：`coefficient(position2, item1)`、`coefficient(position1, item2)`。
///    写成 `(position1, item1)` 会让同一对舱位的权重张冠李戴。
/// 5. **符号缺失即失败**，不跳过——那说明注册顺序错了。
///
/// 第 4 点的交叉顺序与 的 `ItemPriorityReverseLimit` **完全相同**，是基线的一贯写法。
///
/// Aligned with the baseline: the reversal symbols weighted by their coefficients are summed into a **minimisation** objective named `"item order reverse"` (with a space). Five things
/// must be copied: **it is `Minimum`**; **the name has a space**, unlike the pipeline name; **an empty set returns success without an objective** (the baseline's early return); **the
/// coefficient takes two (position, item) pairs with crossed ordering** — `(position2, item1)` and `(position1, item2)` — since `(position1, item1)` would misassign the weights; and **a
/// missing symbol fails rather than being skipped**, because it means the registration order is wrong. Point four's crossed ordering is **identical** to the earlier
/// `ItemPriorityReverseLimit`, the baseline being consistent.
/// </remarks>
internal sealed class ItemOrderReverseLimit
{
    private readonly IReadOnlyList<(Item First, Item Second)> _orderedItems;
    private readonly IReadOnlyList<(StowagePosition First, StowagePosition Second)> _orderedPositions;
    private readonly SequentialLoading _loading;
    private readonly Func<(StowagePosition, Item), (StowagePosition, Item), Flt64> _coefficient;

    /// <summary>
    /// 构造反转目标 / Construct the reversal objective.
    /// </summary>
    /// <param name="loading">顺序装载反转 / The sequential-loading reversals.</param>
    /// <param name="coefficient">按对的系数 / The per-pair coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public ItemOrderReverseLimit(
        SequentialLoading loading,
        Func<(StowagePosition, Item), (StowagePosition, Item), Flt64>? coefficient = null,
        string name = "item_order_reverse_limit")
    {
        ArgumentNullException.ThrowIfNull(loading);
        _loading = loading;
        _orderedItems = loading.OrderedItems;
        _orderedPositions = loading.OrderedPositions;
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
                ILinearIntermediateSymbol<Flt64>? reversal = _loading.Reversal(p1, p2);
                if (reversal is null)
                {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "反转符号未注册 / the reversal symbol is not registered at " + p1 + "," + p2));
                }
                // **系数取 `(position2, item1)` 与 `(position1, item2)`**——交叉顺序。
                // **The coefficient takes `(position2, item1)` and `(position1, item2)`** — crossed ordering.
                Flt64 coefficient = _coefficient((position2, item1), (position1, item2));
                if (reversal is LinearExpressionSymbol plain)
                {
                    foreach (LinearMonomial<Flt64> monomial in plain.Polynomial.Monomials)
                    {
                        monomials.Add(new LinearMonomial<Flt64>(coefficient * monomial.Coefficient, monomial.Symbol));
                    }
                    constant += coefficient * plain.Polynomial.Constant;
                }
                else if (reversal is Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64> function)
                {
                    // **函数符号的 `Polynomial` 是它自己 `ResultVar` 上的线性多项式**，可以直接进目标；
                    // 指示约束由适配器自己产出的行来绑定，这里只负责把系数乘上去。
                    //
                    // **A function symbol's `Polynomial` is linear over its own `ResultVar`**, so it can enter the objective directly; the indicator rows are emitted by the adapter, and
                    // this only has to apply the coefficient.
                    foreach (LinearMonomial<Flt64> monomial in function.Polynomial.Monomials)
                    {
                        monomials.Add(new LinearMonomial<Flt64>(coefficient * monomial.Coefficient, monomial.Symbol));
                    }
                    constant += coefficient * function.Polynomial.Constant;
                }
                else
                {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "符号无法展成线性目标 / the symbol cannot be expanded into a linear objective: " + reversal.Name));
                }
            }
        }

        // **空集合不加目标**。
        // **An empty set adds no objective.**
        if (monomials.Count == 0)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, constant),
            "item order reverse",
            displayName: null);
    }
}
