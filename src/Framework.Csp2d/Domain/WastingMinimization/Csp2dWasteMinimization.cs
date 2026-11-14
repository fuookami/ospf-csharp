// 余料最小化与余料汇总 / Waste minimisation and remnant summarisation.
// 与固定 Kotlin 基线 `csp2d/domain/wasting_minimization/WasteContext.kt` 对应。
// Mirrors the pinned Kotlin baseline's `WasteContext.kt`.

using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.WastingMinimization;

/// <summary>
/// 余料最小化目标：`min weight · Σ_j 余料面积_j · y_j` / Waste minimisation objective.
/// </summary>
/// <remarks>
/// **为什么"消耗面积"与"余料面积"两种目标都要有**：需求固定时两者只差一个常数，但当料域尺寸可变（卷材段高待定）时，
/// 余料口径鼓励**把段高收紧**，消耗面积口径鼓励**少开料**——两者并不等价，所以两个都提供，由调用方选用或加权。
///
/// **Why both a consumed-area and a waste-area objective exist**: with a fixed demand they differ by a constant, but when the region size varies (an undetermined coil
/// segment height) the waste measure rewards **tightening the segment** while the consumed measure rewards **opening less material** — they are not equivalent, so
/// both are provided for the caller to choose or weight.
/// </remarks>
public sealed class Csp2dWasteMinimization {
    private readonly IReadOnlyList<CuttingPlan2<Flt64>> _plans;
    private readonly Csp2dPatternAssignment _assignment;
    private readonly Flt64 _weight;

    /// <summary>构造余料最小化目标 / Build the waste minimisation objective.</summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="assignment">图案使用量变量 / The pattern usage variables.</param>
    /// <param name="weight">目标权重 / The objective weight.</param>
    /// <param name="name">目标名称 / The objective name.</param>
    public Csp2dWasteMinimization(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        Csp2dPatternAssignment assignment,
        Flt64? weight = null,
        string name = "csp2d_waste_minimization") {
        _plans = plans;
        _assignment = assignment;
        _weight = weight ?? Flt64.One;
        Name = name;
    }

    /// <summary>目标名称 / The objective name.</summary>
    public string Name { get; }

    /// <summary>注册余料最小化目标 / Register the waste minimisation objective.</summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    /// <remarks>
    /// 系数取不到（图案量纲不可加）时**按零处理**，与 `Csp2dPatternAssignment` 的约定一致：
    /// 主问题必须能注册，代价是一个坏图案的贡献静默消失。**调用方必须先 `Pattern2.Validate`。**
    ///
    /// A coefficient that cannot be read (the pattern's dimensions are not addable) **counts as zero**, consistent with `Csp2dPatternAssignment`: the master problem must
    /// register, at the price of a broken pattern's contribution silently vanishing. **Callers must run `Pattern2.Validate` first.**
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Invoke(LinearMetaModel<Flt64> model) =>
        new Csp2dUsageObjective(
            _plans,
            _assignment,
            Coefficient,
            Name).Invoke(model);

    /// <summary>单个方案的余料系数 / The waste coefficient of one plan.</summary>
    /// <param name="plan">方案 / The plan.</param>
    /// <returns>系数 / The coefficient.</returns>
    private Flt64 Coefficient(CuttingPlan2<Flt64> plan) =>
        plan.WasteArea() is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> waste
            ? _weight * waste.Value.Value
            : Flt64.Zero;
}

/// <summary>
/// 余料汇总条目 / A remnant summary entry.
/// </summary>
/// <param name="PatternId">图案编号 / The pattern identifier.</param>
/// <param name="MaterialId">物料编号 / The material identifier.</param>
/// <param name="WasteArea">余料面积 / The waste area.</param>
/// <param name="Usable">余料是否达到可复用阈值 / Whether the remnant meets the reusable threshold.</param>
/// <param name="Utilization">利用率 / The utilization.</param>
public sealed record Csp2dRemnantSummary(
    string PatternId,
    string MaterialId,
    Quantity<Flt64> WasteArea,
    bool Usable,
    double Utilization);

/// <summary>
/// 余料可用性汇总器 / Remnant usability summariser.
/// </summary>
/// <remarks>
/// **物料目录里找不到时回退到图案自带的物料**：图案本身已经带着物料与阈值，
/// 因为目录缺一条就丢掉判定，会让**同一块料在汇总里"不知道能不能复用"**。
///
/// **A missing catalogue entry falls back to the pattern's own material**: the pattern already carries its material and threshold, and dropping the decision over one
/// missing catalogue row would leave **"no idea whether this remnant is reusable"** in the summary.
/// </remarks>
public sealed class Csp2dRemnantSummarizer {
    private readonly IReadOnlyList<Material2<Flt64>> _materials;

    /// <summary>构造汇总器 / Build the summariser.</summary>
    /// <param name="materials">物料目录，用于读取余料阈值 / The material catalogue providing the remnant threshold.</param>
    public Csp2dRemnantSummarizer(IReadOnlyList<Material2<Flt64>> materials) => _materials = materials;

    /// <summary>汇总图案的余料 / Summarise the remnants of patterns.</summary>
    /// <param name="patterns">图案列表 / The pattern list.</param>
    /// <returns>余料汇总 / The remnant summaries.</returns>
    /// <remarks>
    /// 余料面积算不出来（量纲不可加）时**跳过该图案**而不是记一条零面积：一条零面积的汇总会声称"这块料没剩什么"，
    /// 而真实情况是**这笔账算不成**。
    ///
    /// A pattern whose waste area cannot be computed (incompatible dimensions) is **skipped rather than recorded with zero area**: a zero-area entry would claim
    /// "nothing is left over", while the truth is that **the arithmetic failed**.
    /// </remarks>
    public IReadOnlyList<Csp2dRemnantSummary> Summarize(IReadOnlyList<Pattern2<Flt64>> patterns) {
        var byId = new Dictionary<string, Material2<Flt64>>();
        foreach (Material2<Flt64> material in _materials) {
            byId[material.Id.Value] = material;
        }

        var summaries = new List<Csp2dRemnantSummary>();
        foreach (Pattern2<Flt64> pattern in patterns) {
            if (pattern.WasteArea() is not Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> waste) {
                continue;
            }
            Material2<Flt64> material = byId.TryGetValue(pattern.Material.Id.Value, out Material2<Flt64>? found)
                ? found
                : pattern.Material;
            summaries.Add(new Csp2dRemnantSummary(
                pattern.Id.Value,
                pattern.Material.Id.Value,
                waste.Value,
                material.IsUsableRemnant(waste.Value),
                pattern.Utilization() is Ok<Flt64, ErrorCode, Error<ErrorCode>> utilization
                    ? utilization.Value.ToDouble()
                    : 0.0));
        }
        return summaries;
    }

    /// <summary>
    /// 统计可复用余料的总面积 / Total area of the reusable remnants.
    /// </summary>
    /// <param name="summaries">汇总列表 / The summary list.</param>
    /// <param name="sample">提供零值单位的**面积**样本 / An **area** sample supplying the zero unit.</param>
    /// <returns>可复用余料面积 / The reusable remnant area.</returns>
    public Quantity<Flt64> ReusableArea(IReadOnlyList<Csp2dRemnantSummary> summaries, Quantity<Flt64> sample) {
        Quantity<Flt64> total = new(Flt64.Zero, sample.Unit);
        foreach (Csp2dRemnantSummary summary in summaries) {
            if (!summary.Usable) {
                continue;
            }
            if (total.Add(summary.WasteArea) is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> added) {
                total = added.Value;
            }
        }
        return total;
    }
}
