// 主问题的三个约束、目标与就绪状态 / The master problem's three constraints, objective and readiness record.
// 与固定 Kotlin 基线 `csp2d/domain/produce/MasterProblem.kt` 的后半段对应。
// Mirrors the tail of the pinned Kotlin baseline's `MasterProblem.kt`.

using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.Produce;

/// <summary>
/// 需求覆盖约束：`Σ_j 覆盖_ij · y_j ≥ 需求_i` / Demand coverage constraint `Σ_j coverage_ij · y_j ≥ demand_i`.
/// </summary>
/// <remarks>
/// **覆盖量的口径随需求口径变化**：件数按件数、面积按成品面积、重量按单件重量。三者都归一到同一个数值域，
/// 因为主问题只有一套变量——**为每种口径各做一套变量会让"需求满足了多少"变成三个互不相干的答案**。
///
/// **The coverage unit follows the demand unit**: counts for the amount unit, finished area for the area unit, and per-piece weight for the weight unit. All three map into
/// one numeric domain because the master problem has a single variable set — **one variable set per unit would turn "how much demand is met" into three unrelated
/// answers**.
/// </remarks>
public sealed class Csp2dDemandConstraint {
    private readonly IReadOnlyList<CuttingPlan2<Flt64>> _plans;
    private readonly Csp2dPatternAssignment _assignment;
    private readonly IReadOnlyList<ProductDemand2<Flt64>> _demands;

    /// <summary>构造需求覆盖约束 / Build the demand coverage constraint.</summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="assignment">图案使用量变量 / The pattern usage variables.</param>
    /// <param name="demands">产品需求 / The product demands.</param>
    /// <param name="name">约束名称 / The constraint name.</param>
    public Csp2dDemandConstraint(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        Csp2dPatternAssignment assignment,
        IReadOnlyList<ProductDemand2<Flt64>> demands,
        string name = "csp2d_demand") {
        _plans = plans;
        _assignment = assignment;
        _demands = demands;
        Name = name;
    }

    /// <summary>约束名称 / The constraint name.</summary>
    public string Name { get; }

    /// <summary>注册需求覆盖约束 / Register the demand coverage constraints.</summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Invoke(LinearMetaModel<Flt64> model) {
        for (int demandIndex = 0; demandIndex < _demands.Count; demandIndex++) {
            ProductDemand2<Flt64> demand = _demands[demandIndex];
            double required = Csp2dProduceSupport.RequiredValue(demand);
            List<LinearMonomial<Flt64>> monomials = new();
            for (int planIndex = 0; planIndex < _plans.Count; planIndex++) {
                double coverage = Csp2dProduceSupport.CoverageOf(_plans[planIndex], demand);
                if (coverage == 0.0) {
                    continue;
                }
                monomials.Add(new LinearMonomial<Flt64>(new Flt64(coverage), _assignment.Usage[planIndex]));
            }
            MutableLinearPolynomial<Flt64> polynomial = Csp2dProduceSupport.SafeCsp2dPolynomial(monomials);
            Result<Success, ErrorCode, Error<ErrorCode>> added = model.AddConstraint(
                new LinearInequality<Flt64>(
                    polynomial.ToLinearPolynomial(),
                    Csp2dProduceSupport.ConstantPolynomial(new Flt64(required)),
                    Comparison.GE),
                group: null,
                name: Name + "_" + demandIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }
        return Results.OkInstance;
    }

}

/// <summary>
/// 物料可用量约束：`Σ_j y_j ≤ 可用卷数/张数` / Material availability constraint `Σ_j y_j ≤ available count`.
/// </summary>
/// <remarks>
/// **同一物料的方案共享一个可用量**，不同物料各有一条约束。不限量的物料**不注册约束**——
/// 给一个 `ulong.MaxValue` 的上界等于给求解器添一道永远成立的大数约束，**除了数值病态什么也没带来**。
///
/// **Plans of the same material share one availability**, and each material gets its own constraint. Unlimited material gets **no constraint**: an upper bound of
/// `ulong.MaxValue` adds a constraint that always holds, **bringing nothing but numerical trouble**.
/// </remarks>
public sealed class Csp2dMaterialAvailabilityConstraint {
    private readonly IReadOnlyList<CuttingPlan2<Flt64>> _plans;
    private readonly Csp2dPatternAssignment _assignment;
    private readonly IReadOnlyList<Material2<Flt64>> _materials;

    /// <summary>构造物料可用量约束 / Build the material availability constraint.</summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="assignment">图案使用量变量 / The pattern usage variables.</param>
    /// <param name="materials">物料目录 / The material catalogue.</param>
    /// <param name="name">约束名称 / The constraint name.</param>
    public Csp2dMaterialAvailabilityConstraint(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        Csp2dPatternAssignment assignment,
        IReadOnlyList<Material2<Flt64>> materials,
        string name = "csp2d_material") {
        _plans = plans;
        _assignment = assignment;
        _materials = materials;
        Name = name;
    }

    /// <summary>约束名称 / The constraint name.</summary>
    public string Name { get; }

    /// <summary>注册物料可用量约束 / Register the material availability constraints.</summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Invoke(LinearMetaModel<Flt64> model) {
        for (int materialIndex = 0; materialIndex < _materials.Count; materialIndex++) {
            Material2<Flt64> material = _materials[materialIndex];
            if (material.AvailableBatches == ulong.MaxValue) {
                continue;
            }
            List<LinearMonomial<Flt64>> monomials = new();
            for (int planIndex = 0; planIndex < _plans.Count; planIndex++) {
                if (_plans[planIndex].Material.Id.Value != material.Id.Value) {
                    continue;
                }
                monomials.Add(new LinearMonomial<Flt64>(Flt64.One, _assignment.Usage[planIndex]));
            }
            if (monomials.Count == 0) {
                continue;
            }
            MutableLinearPolynomial<Flt64> polynomial = Csp2dProduceSupport.SafeCsp2dPolynomial(monomials);
            Result<Success, ErrorCode, Error<ErrorCode>> added = model.AddConstraint(
                new LinearInequality<Flt64>(
                    polynomial.ToLinearPolynomial(),
                    Csp2dProduceSupport.ConstantPolynomial(new Flt64(material.AvailableBatches)),
                    Comparison.LE),
                group: null,
                name: Name + "_" + materialIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }
        return Results.OkInstance;
    }
}

/// <summary>
/// 设备产能约束：`Σ_j 产能消耗_j · y_j ≤ 产能` / Machine capacity constraint.
/// </summary>
/// <remarks>
/// 未配置产能的设备**不注册约束**；未配置产能消耗的方案**不计入**。
///
/// Machines without a capacity get **no constraint**, and plans without a consumption **do not contribute**.
/// </remarks>
public sealed class Csp2dMachineCapacityConstraint {
    private readonly IReadOnlyList<CuttingPlan2<Flt64>> _plans;
    private readonly Csp2dPatternAssignment _assignment;
    private readonly IReadOnlyList<Machine2<Flt64>> _machines;

    /// <summary>构造设备产能约束 / Build the machine capacity constraint.</summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="assignment">图案使用量变量 / The pattern usage variables.</param>
    /// <param name="machines">设备目录 / The machine catalogue.</param>
    /// <param name="name">约束名称 / The constraint name.</param>
    public Csp2dMachineCapacityConstraint(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        Csp2dPatternAssignment assignment,
        IReadOnlyList<Machine2<Flt64>> machines,
        string name = "csp2d_capacity") {
        _plans = plans;
        _assignment = assignment;
        _machines = machines;
        Name = name;
    }

    /// <summary>约束名称 / The constraint name.</summary>
    public string Name { get; }

    /// <summary>注册设备产能约束 / Register the machine capacity constraints.</summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Invoke(LinearMetaModel<Flt64> model) {
        for (int machineIndex = 0; machineIndex < _machines.Count; machineIndex++) {
            Machine2<Flt64> machine = _machines[machineIndex];
            if (machine.Capacity is not { } capacity) {
                continue;
            }
            List<LinearMonomial<Flt64>> monomials = new();
            for (int planIndex = 0; planIndex < _plans.Count; planIndex++) {
                CuttingPlan2<Flt64> plan = _plans[planIndex];
                if (plan.CapacityConsumption is not { } consumption) {
                    continue;
                }
                if (plan.MachineId is not { } planMachine || planMachine.Value != machine.Id.Value) {
                    continue;
                }
                monomials.Add(new LinearMonomial<Flt64>(consumption.Value, _assignment.Usage[planIndex]));
            }
            if (monomials.Count == 0) {
                continue;
            }
            MutableLinearPolynomial<Flt64> polynomial = Csp2dProduceSupport.SafeCsp2dPolynomial(monomials);
            Result<Success, ErrorCode, Error<ErrorCode>> added = model.AddConstraint(
                new LinearInequality<Flt64>(
                    polynomial.ToLinearPolynomial(),
                    Csp2dProduceSupport.ConstantPolynomial(capacity.Value),
                    Comparison.LE),
                group: null,
                name: Name + "_" + machineIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }
        return Results.OkInstance;
    }
}

/// <summary>
/// 方案使用量目标：`min Σ_j 系数_j · y_j` / Plan usage objective.
/// </summary>
/// <remarks>
/// 系数由调用方给出（板材张数、消耗面积、段高、余料），所以**一个目标类表达多种口径**，不必为每种口径各写一个目标。
///
/// The coefficient is supplied by the caller (sheet count, consumed area, segment height, waste), so **one objective class expresses many measures** instead of one class
/// per measure.
/// </remarks>
public sealed class Csp2dUsageObjective {
    private readonly IReadOnlyList<CuttingPlan2<Flt64>> _plans;
    private readonly Csp2dPatternAssignment _assignment;
    private readonly System.Func<CuttingPlan2<Flt64>, Flt64> _coefficient;

    /// <summary>构造目标 / Build the objective.</summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="assignment">图案使用量变量 / The pattern usage variables.</param>
    /// <param name="coefficient">方案到系数 / The plan-to-coefficient function.</param>
    /// <param name="name">目标名称 / The objective name.</param>
    public Csp2dUsageObjective(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        Csp2dPatternAssignment assignment,
        System.Func<CuttingPlan2<Flt64>, Flt64> coefficient,
        string name = "csp2d_usage") {
        _plans = plans;
        _assignment = assignment;
        _coefficient = coefficient;
        Name = name;
    }

    /// <summary>目标名称 / The objective name.</summary>
    public string Name { get; }

    /// <summary>注册目标 / Register the objective.</summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    /// <remarks>
    /// **系数为零的方案不进目标**，且**全为零时不注册目标**：一个空目标会让求解器只满足可行域，
    /// 而调用方会以为"已经按这个口径优化过了"。
    ///
    /// **Plans with a zero coefficient are left out**, and **no objective is registered when everything is zero**: an empty objective makes the solver satisfy only
    /// feasibility while the caller believes "this measure has already been optimised".
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Invoke(LinearMetaModel<Flt64> model) {
        List<LinearMonomial<Flt64>> monomials = new();
        for (int index = 0; index < _plans.Count; index++) {
            Flt64 value = _coefficient(_plans[index]);
            if (value == Flt64.Zero) {
                continue;
            }
            monomials.Add(new LinearMonomial<Flt64>(value, _assignment.Usage[index]));
        }
        if (monomials.Count == 0) {
            return Results.OkInstance;
        }
        MutableLinearPolynomial<Flt64> polynomial = Csp2dProduceSupport.SafeCsp2dPolynomial(monomials);
        return model.AddObject(
            ObjectCategory.Minimum,
            polynomial.ToLinearPolynomial(),
            Name,
            Name);
    }
}

/// <summary>
/// 主问题就绪状态：变量、需求、物料、产能与目标的注册结果 / Master problem readiness: the registration results of variables, demand, material, capacity and objective.
/// </summary>
/// <param name="Model">线性元模型 / The linear meta model.</param>
/// <param name="Assignment">图案使用量变量 / The pattern usage variables.</param>
/// <param name="DemandRegistered">需求约束注册结果 / The demand constraint registration result.</param>
/// <param name="MaterialRegistered">物料约束注册结果 / The material constraint registration result.</param>
/// <param name="CapacityRegistered">产能约束注册结果 / The capacity constraint registration result.</param>
/// <param name="ObjectiveRegistered">目标注册结果 / The objective registration result.</param>
public sealed record Csp2dMasterProblem(
    LinearMetaModel<Flt64> Model,
    Csp2dPatternAssignment Assignment,
    Result<Success, ErrorCode, Error<ErrorCode>> DemandRegistered,
    Result<Success, ErrorCode, Error<ErrorCode>> MaterialRegistered,
    Result<Success, ErrorCode, Error<ErrorCode>> CapacityRegistered,
    Result<Success, ErrorCode, Error<ErrorCode>> ObjectiveRegistered) {
    /// <summary>
    /// 全部注册是否成功 / Whether every registration succeeded.
    /// </summary>
    /// <returns>是否全部成功 / Whether everything succeeded.</returns>
    /// <remarks>
    /// **四项都要查**：只看最后一项会让前三项的失败在"目标注册成功"的假象下溜过去。
    ///
    /// **All four are checked**: looking only at the last one lets earlier failures slip by under the illusion that "the objective registered".
    /// </remarks>
    public bool AllSucceeded() =>
        DemandRegistered.IsOk && MaterialRegistered.IsOk && CapacityRegistered.IsOk && ObjectiveRegistered.IsOk;
}
