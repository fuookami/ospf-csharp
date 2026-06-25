#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// CSP1D 领域计算上下文 / CSP1D domain calculation context
///
/// 提供单条记录级别的领域判断所需信息。
/// Provides information for domain-level judgment on individual records.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dDomainCalculationContext<V> where V : struct {
    /// <summary>当前切割方案 / Current cutting plan.</summary>
    CuttingPlan<V> Plan { get; }

    /// <summary>方案索引 / Plan index.</summary>
    int PlanIndex { get; }

    /// <summary>方案所属物料 / Plan material.</summary>
    Material<V> Material => Plan.Material;

    /// <summary>方案所属设备 ID / Plan machine ID.</summary>
    string? MachineId => Plan.MachineId;

    /// <summary>方案切片列表 / Plan slices.</summary>
    IReadOnlyList<CuttingPlanSlice<V>> Slices => Plan.Slices;

    /// <summary>方案需求贡献 / Plan demand contributions.</summary>
    IReadOnlyList<CuttingPlanDemandContribution<V>> DemandContributions => Plan.DemandContributions;

    /// <summary>领域数值样本 / Domain value sample.</summary>
    V DomainValueSample { get; }

    /// <summary>转换为领域数值 / Convert to domain value.</summary>
    V ToDomainValue(Flt64 value);

    /// <summary>计算指定产品的需求贡献量 / Compute demand contribution for specified product.</summary>
    Flt64? ContributionFor(string productId);
}

/// <summary>
/// 简单领域计算上下文 / Simple domain calculation context
///
/// Csp1dDomainCalculationContext 的默认实现，直接包装 CuttingPlan。
/// Default implementation of Csp1dDomainCalculationContext, wrapping a CuttingPlan directly.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class SimpleDomainCalculationContext<V> : ICsp1dDomainCalculationContext<V>
    where V : struct {
    private readonly Func<Flt64, V> _domainValueConverter;

    /// <summary>
    /// 构造简单领域计算上下文 / Construct a simple domain calculation context
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <param name="planIndex">方案索引 / Plan index.</param>
    /// <param name="domainValueSample">领域数值样本 / Domain value sample.</param>
    /// <param name="domainValueConverter">值转换函数 / Value converter.</param>
    public SimpleDomainCalculationContext(
        CuttingPlan<V> plan,
        int planIndex,
        V domainValueSample,
        Func<Flt64, V>? domainValueConverter = null) {
        Plan = plan;
        PlanIndex = planIndex;
        DomainValueSample = domainValueSample;
        _domainValueConverter = domainValueConverter ?? (value => {
            Result<V, ErrorCode, Error<ErrorCode>> result = DomainValueConversion.ConvertSolverValue(domainValueSample, value);
            return result.IsOk ? result.Value : default;
        });
    }

    /// <inheritdoc/>
    public CuttingPlan<V> Plan { get; }

    /// <inheritdoc/>
    public int PlanIndex { get; }

    /// <inheritdoc/>
    public V DomainValueSample { get; }

    /// <inheritdoc/>
    public V ToDomainValue(Flt64 value) => _domainValueConverter(value);

    /// <inheritdoc/>
    public Flt64? ContributionFor(string productId) {
        CuttingPlanDemandContribution<V>? contribution = Plan.DemandContributions.FirstOrDefault(c => c.Product.Id == productId);
        if (contribution == null) {
            return null;
        }

        V val = contribution.Quantity.Value;
        if (val is Flt64 f64) {
            return f64;
        }

        if (val is FltX fx) {
            return fx.ToFlt64();
        }

        return null;
    }
}

/// <summary>
/// CSP1D 领域策略接口 / CSP1D domain policy interface
///
/// 允许下游注入领域计算/判断逻辑，如物料可行性、宽度判断、余料计算等。
/// Allows downstream to inject domain calculation/judgment logic.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public abstract class ICsp1dDomainPolicy<V> where V : struct {
    /// <summary>策略名称 / Policy name.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// 是否接管宽度可行性判断 / Whether this policy takes over width feasibility judgment
    /// </summary>
    public virtual bool OverridesWidthFeasibility => false;

    /// <summary>
    /// 判断切割方案在给定上下文中是否可行 / Check if cutting plan is feasible in the given context
    /// </summary>
    /// <param name="context">领域计算上下文 / Domain calculation context.</param>
    /// <returns>true 表示可行 / true if feasible.</returns>
    public virtual bool IsFeasible(ICsp1dDomainCalculationContext<V> context) => true;

    /// <summary>
    /// 判断切割方案在给定上下文中宽度是否可切 / Check if cutting plan width is feasible in the given context
    /// </summary>
    /// <param name="context">领域计算上下文 / Domain calculation context.</param>
    /// <returns>true 表示可切 / true if cuttable.</returns>
    public virtual bool IsWidthFeasible(ICsp1dDomainCalculationContext<V> context) => true;
}

/// <summary>
/// 默认 CSP1D 领域策略 / Default CSP1D domain policy
///
/// 保持现有硬编码行为：所有方案都可行，所有宽度都可切。
/// Preserves existing hard-coded behavior: all plans are feasible, all widths are cuttable.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class DefaultCsp1dDomainPolicy<V> : ICsp1dDomainPolicy<V> where V : struct {
    /// <inheritdoc/>
    public override string Name => "default";

    /// <inheritdoc/>
    public override bool IsFeasible(ICsp1dDomainCalculationContext<V> context) => true;

    /// <inheritdoc/>
    public override bool IsWidthFeasible(ICsp1dDomainCalculationContext<V> context) => true;
}

/// <summary>
/// CSP1D 领域策略辅助方法 / CSP1D domain policy helper methods
/// </summary>
public static class Csp1dDomainPolicyHelpers {
    /// <summary>
    /// 判断一组领域策略是否全部认为方案可行 / Check if all domain policies consider the plan feasible
    /// </summary>
    public static bool AllFeasible<V>(IReadOnlyList<ICsp1dDomainPolicy<V>> policies, ICsp1dDomainCalculationContext<V> context)
        where V : struct
        => policies.All(p => p.IsFeasible(context));

    /// <summary>
    /// 判断一组领域策略是否全部认为宽度可切 / Check if all domain policies consider the width cuttable
    /// </summary>
    public static bool AllWidthFeasible<V>(IReadOnlyList<ICsp1dDomainPolicy<V>> policies, ICsp1dDomainCalculationContext<V> context)
        where V : struct
        => policies.All(p => p.IsWidthFeasible(context));

    /// <summary>
    /// 从领域策略列表生成宽度可行性判断函数 / Generate width feasibility check function from domain policy list
    ///
    /// 只有声明 OverridesWidthFeasibility = true 的 policy 才会参与宽度判断，替代原始 CanCut。
    /// When no policy declares OverridesWidthFeasibility = true, returns null (use default CanCut logic).
    /// </summary>
    public static Func<Material<V>, Product<V>, Quantity<V>, bool>? WidthFeasibilityCheckFromPolicies<V>(
        IReadOnlyList<ICsp1dDomainPolicy<V>> domainPolicies,
        V domainValueSample) where V : struct {
        var widthOverridingPolicies = domainPolicies.Where(p => p.OverridesWidthFeasibility).ToList();
        if (widthOverridingPolicies.Count == 0) {
            return null;
        }

        return (material, product, productWidth) => {
            var ctx = new SimpleDomainCalculationContext<V>(
                plan: new CuttingPlan<V>(
                    id: $"width-check-{material.Id}-{product.Id}",
                    material: material,
                    slices: new[] { new CuttingPlanSlice<V>(product, productWidth) },
                    demandContributions: Array.Empty<CuttingPlanDemandContribution<V>>()
                ),
                planIndex: -1,
                domainValueSample: domainValueSample
            );
            return AllWidthFeasible(widthOverridingPolicies, ctx);
        };
    }
}
