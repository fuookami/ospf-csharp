// 良率配置与超产上界约束 / Yield configuration and the over-production bound constraint.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.Yield;

/// <summary>
/// 良率配置 / Yield configuration.
/// </summary>
/// <param name="OverProductionUpperBounds">产品编号到超产上限的映射，未配置的产品不设上限 / Product identifier to over-production upper bound; unconfigured products are uncapped.</param>
public sealed record Csp2dYieldConfig(
    IReadOnlyDictionary<string, Flt64>? OverProductionUpperBounds = null) {
    /// <summary>超产上限表，为空即空表 / The bound table; null means an empty map.</summary>
    public IReadOnlyDictionary<string, Flt64> Bounds =>
        OverProductionUpperBounds ?? new Dictionary<string, Flt64>();
}

/// <summary>
/// 超产上界约束：`Σ_j 覆盖_ij · y_j ≤ 需求_i + 上限_i` / Over-production bound constraint.
/// </summary>
/// <remarks>
/// **与 CSP1D 的差别**：CSP1D 用松弛变量**同时惩罚欠产与超产**，而 CSP2D 把"需求必须满足"当成硬约束、
/// 只对超产设上界，所以**不需要双向松弛**。多一套双向松弛变量会让主问题的可行域比业务允许的更宽。
///
/// **The difference from CSP1D**: CSP1D penalises both under- and over-production with slack variables, while CSP2D treats "demand must be met" as a hard constraint and
/// only caps over-production, so **no two-sided slack is needed**. An extra pair of slack variables would widen the master problem's feasible region beyond what the
/// business allows.
/// </remarks>
public sealed class Csp2dOverProductionBoundConstraint {
    private readonly IReadOnlyList<CuttingPlan2<Flt64>> _plans;
    private readonly Csp2dPatternAssignment _assignment;
    private readonly IReadOnlyList<ProductDemand2<Flt64>> _demands;
    private readonly Csp2dYieldConfig _config;

    /// <summary>构造超产上界约束 / Build the over-production bound constraint.</summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="assignment">图案使用量变量 / The pattern usage variables.</param>
    /// <param name="demands">产品需求 / The product demands.</param>
    /// <param name="config">良率配置 / The yield configuration.</param>
    /// <param name="name">约束名称 / The constraint name.</param>
    public Csp2dOverProductionBoundConstraint(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        Csp2dPatternAssignment assignment,
        IReadOnlyList<ProductDemand2<Flt64>> demands,
        Csp2dYieldConfig config,
        string name = "csp2d_over_production") {
        _plans = plans;
        _assignment = assignment;
        _demands = demands;
        _config = config;
        Name = name;
    }

    /// <summary>约束名称 / The constraint name.</summary>
    public string Name { get; }

    /// <summary>
    /// 注册超产上界约束 / Register the over-production bound constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    /// <remarks>
    /// **整张表为空时直接返回**：没有配置任何上限就不该往模型里塞约束，否则"良率配置"与"没有良率配置"会写出同一个模型，
    /// 而调用方以为它配过了。**有上限但没有任何方案覆盖该产品时同样跳过**——零项的约束只是添乱。
    ///
    /// **An empty table returns immediately**: with no bound configured, no constraint belongs in the model, or "configured yield" and "no yield configuration" would
    /// produce the same model while the caller believes it was configured. **A bounded product that no plan covers is skipped too** — a constraint with no terms only
    /// Adds </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Invoke(LinearMetaModel<Flt64> model) {
        if (_config.Bounds.Count == 0) {
            return Results.OkInstance;
        }

        for (int demandIndex = 0; demandIndex < _demands.Count; demandIndex++) {
            ProductDemand2<Flt64> demand = _demands[demandIndex];
            if (!_config.Bounds.TryGetValue(demand.Product.Id.Value, out Flt64 bound)) {
                continue;
            }
            double required = Csp2dProduceSupport.RequiredValue(demand);
            List<LinearMonomial<Flt64>> monomials = new();
            for (int planIndex = 0; planIndex < _plans.Count; planIndex++) {
                double coverage = Csp2dProduceSupport.CoverageOf(_plans[planIndex], demand);
                if (coverage == 0.0) {
                    continue;
                }
                monomials.Add(new LinearMonomial<Flt64>(new Flt64(coverage), _assignment.Usage[planIndex]));
            }
            if (monomials.Count == 0) {
                continue;
            }
            MutableLinearPolynomial<Flt64> polynomial = Csp2dProduceSupport.SafeCsp2dPolynomial(monomials);
            Result<Success, ErrorCode, Error<ErrorCode>> added = model.AddConstraint(
                new LinearInequality<Flt64>(
                    polynomial.ToLinearPolynomial(),
                    Csp2dProduceSupport.ConstantPolynomial(new Flt64(required) + bound),
                    Comparison.LE),
                group: null,
                name: Name + "_" + demandIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }
        return Results.OkInstance;
    }
}
