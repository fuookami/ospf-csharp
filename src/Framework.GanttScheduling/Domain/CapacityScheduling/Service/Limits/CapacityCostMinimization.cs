#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits;

/// <summary>
/// 产能成本最小化目标 / Capacity cost minimization objective
/// </summary>
/// <remarks>
/// 最小化产能调度的总成本。成本 = sum(action.unitCost * x[action, slot])。
/// Minimizes total cost of capacity scheduling.
/// </remarks>
/// <remarks>
/// **第一版是 `Invoke =&gt; Ok`**：什么目标都没设，却报成功。没有目标的模型会被当成"随便找一个可行解"，
/// 而调用方以为它在最小化成本——**解是可行的，数字是错的**，这种错误在事后对账时最难发现。
///
/// 现在真的建目标：`Σ_{动作, 时隙, 顺序位} unitCost(action, 时隙起点) · x[a, s, o]`，以
/// <see cref="ObjectCategory.Minimum"/> 挂到模型上。
///
/// **零成本项不写进目标**：系数为 0 的单项式只是白占内存，且让"目标里到底有几项"这种判据读不出真实结构。
///
/// **The first version was `Invoke =&gt; Ok`**: no objective was set while success was reported. A model without an objective is understood as "find any feasible solution" while the
/// caller believes cost is being minimised — **the solution is feasible and the number is wrong**, the hardest kind of error to find after the fact. It now really builds the
/// objective `sum over (action, slot, position) of unitCost(action, slot start) · x[a, s, o]` and attaches it with <see cref="ObjectCategory.Minimum"/>.
/// **Zero-cost terms are left out**: a monomial with coefficient 0 only costs memory and makes "how many terms does the objective really have" impossible to read.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class CapacityCostMinimization<A> : IMetaConstraintGroup
    where A : IProductionAction {
    private readonly CapacityOrderVariables<A> _variables;

    /// <summary>
    /// 产能成本最小化构造 / Capacity cost minimization constructor
    /// </summary>
    /// <param name="variables">排序变量层 / The order variable layer</param>
    /// <param name="name">目标名称 / Objective name</param>
    public CapacityCostMinimization(
        CapacityOrderVariables<A> variables,
        string name = "capacity_cost_minimization") {
        ArgumentNullException.ThrowIfNull(variables);
        _variables = variables;
        Name = name;
    }

    /// <inheritdoc/>
    public bool Lazy => false;

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>
    /// 应用目标到模型 / Apply the objective to a model
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>成功或失败 / Success or failure</returns>
    public Try Invoke(object model) {
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "产能成本目标需要线性元模型 / the capacity cost objective needs a linear meta model");
        }
        if (_variables.Amounts is null) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "排序变量尚未注册，没有数量可以计成本 / the order variables are not registered, so there is no amount to cost");
        }

        int positions = checked((int)_variables.MaxOrderPerSlot);
        var monomials = new List<LinearMonomial<Flt64>>();
        for (int action = 0; action < _variables.Actions.Count; ++action) {
            for (int slot = 0; slot < _variables.Slots.Count; ++slot) {
                Flt64 unitCost = _variables.Actions[action].UnitCost(
                    _variables.Slots[slot].Start, value => new Flt64(value));
                if (unitCost.ToDouble() == 0.0) {
                    continue;
                }
                for (int position = 0; position < positions; ++position) {
                    monomials.Add(new LinearMonomial<Flt64>(
                        unitCost, _variables.AmountOf(action, slot, position)));
                }
            }
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new LinearFlattenData<Flt64>(monomials.ToArray(), Flt64.Zero),
            Name,
            displayName: null);
    }
}
