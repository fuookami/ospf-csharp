#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits;

/// <summary>
/// 顺序约束 / Order constraint
/// </summary>
/// <remarks>
/// 每个顺序位置最多只能有一个动作不为0（仅用于 CapacityOrderCompilation）。
/// Each order position can have at most one action with non-zero allocation (only for CapacityOrderCompilation).
/// </remarks>
/// <remarks>
/// **第一版是 `Invoke =&gt; Ok`**：三族约束一条都没建，却对任何模型报成功。
///
/// 现在按三族真的建行（`x` 是数量、`b` 是选中，来自 `CapacityOrderVariables`）：
///
/// 1. **每顺序位至多一个动作**：对每个 (时隙, 顺序位) 加 `Σ_动作 b[a, t, o] ≤ 1`；
/// 2. **下界关联**：`x[a, t, o] ≥ b[a, t, o]`——选中就必须至少加工 1 件；
/// 3. **上界关联**：`x[a, t, o] ≤ M · b[a, t, o]`，`M` 取 `action.upperBound(slot, window)`——没选中就必须加工 0 件。
///
/// 第 2 与第 3 条缺一不可：只留第 2 条，`b` 可以是 1 而 `x` 很大（"选中了但无限加工"）；只留第 3 条，`x` 可以是 0
/// 而 `b` 是 1（"选中了却什么都没做"，白占一个顺序位）。
///
/// **The first version was `Invoke => Ok`**: none of the three families was built, yet every model was reported as fine. It now really builds its three families (`x` is the
/// amount, `b` the selection, both from `CapacityOrderVariables`): (1) **at most one action per position** — `sum over actions of b[a, t, o] &lt;= 1` for every (slot, position);
/// (2) **the lower linking bound** `x[a, t, o] &gt;= b[a, t, o]` — being selected means processing at least 1; (3) **the upper linking bound** `x[a, t, o] &lt;= M . b[a, t, o]` with
/// `M` taken from `action.upperBound(slot, window)` — not being selected means processing 0. Both (2) and (3) are needed: with only (2), `b` can be 1 while `x` is huge ("selected and
/// processing without limit"); with only (3), `x` can be 0 while `b` is 1 ("selected but doing nothing", wasting a position).
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class OrderConstraint<A> : IMetaConstraintGroup
    where A : IProductionAction {
    private readonly CapacityOrderVariables<A> _variables;

    /// <summary>
    /// 顺序约束构造 / Order constraint constructor
    /// </summary>
    /// <param name="variables">排序变量层 / The order variable layer</param>
    /// <param name="name">约束名称 / Constraint name</param>
    public OrderConstraint(
        CapacityOrderVariables<A> variables,
        string name = "order") {
        ArgumentNullException.ThrowIfNull(variables);
        _variables = variables;
        Name = name;
    }

    /// <inheritdoc/>
    public bool Lazy => false;

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>
    /// 应用约束到模型 / Apply constraint to model
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>成功或失败 / Success or failure</returns>
    public Try Invoke(object model) {
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "顺序约束需要线性元模型 / the order constraint needs a linear meta model");
        }
        if (_variables.Amounts is null || _variables.Selections is null) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "排序变量尚未注册，无法建顺序约束 / the order variables are not registered, so the order constraints cannot be built");
        }

        int actions = _variables.Actions.Count;
        int slots = _variables.Slots.Count;
        int positions = checked((int)_variables.MaxOrderPerSlot);

        for (int slot = 0; slot < slots; ++slot) {
            for (int position = 0; position < positions; ++position) {
                // **约束 1：每个顺序位至多一个动作** / Constraint 1: at most one action per position.
                var selections = new LinearMonomial<Flt64>[actions];
                for (int action = 0; action < actions; ++action) {
                    selections[action] = new LinearMonomial<Flt64>(
                        Flt64.One, _variables.SelectionOf(action, slot, position));
                }
                Try unique = linear.AddConstraint(
                    new LinearInequality<Flt64>(
                        new LinearPolynomial<Flt64>(selections, Flt64.Zero),
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                        Comparison.LE),
                    group: null,
                    name: $"{Name}_unique_{slot}_{position}",
                    args: null);
                if (!unique.IsOk) {
                    return unique;
                }

                for (int action = 0; action < actions; ++action) {
                    // **约束 2：`x ≥ b`**——选中就至少加工 1 件 / Constraint 2: `x >= b`, selected means at least one unit.
                    Try lower = linear.AddConstraint(
                        new LinearInequality<Flt64>(
                            new LinearPolynomial<Flt64>(
                                new[] { new LinearMonomial<Flt64>(Flt64.One, _variables.AmountOf(action, slot, position)) },
                                Flt64.Zero),
                            new LinearPolynomial<Flt64>(
                                new[] { new LinearMonomial<Flt64>(Flt64.One, _variables.SelectionOf(action, slot, position)) },
                                Flt64.Zero),
                            Comparison.GE),
                        group: null,
                        name: $"{Name}_link_lb_{action}_{slot}_{position}",
                        args: null);
                    if (!lower.IsOk) {
                        return lower;
                    }

                    // **约束 3：`x ≤ M · b`**——没选中就必须加工 0 件。`M` 取动作自己的上界：猜大了解松弛、猜小了切可行解。
                    // **Constraint 3: `x <= M . b`** — not selected means processing zero. `M` comes from the action's own bound: too large relaxes the model, too small cuts
                    // off feasible solutions.
                    var upper = new Flt64((double)_variables.Actions[action].UpperBound(_variables.Slots[slot], _variables.TimeWindow));
                    Try upperLinked = linear.AddConstraint(
                        new LinearInequality<Flt64>(
                            new LinearPolynomial<Flt64>(
                                new[] { new LinearMonomial<Flt64>(Flt64.One, _variables.AmountOf(action, slot, position)) },
                                Flt64.Zero),
                            new LinearPolynomial<Flt64>(
                                new[] { new LinearMonomial<Flt64>(upper, _variables.SelectionOf(action, slot, position)) },
                                Flt64.Zero),
                            Comparison.LE),
                        group: null,
                        name: $"{Name}_link_ub_{action}_{slot}_{position}",
                        args: null);
                    if (!upperLinked.IsOk) {
                        return upperLinked;
                    }
                }
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
