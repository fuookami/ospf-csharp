// 解提取的取整判据 / Criteria for the rounding of extracted solution amounts.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **解提取必须四舍五入，不是截断** / Extracted solution amounts must round, not truncate.
/// </summary>
/// <remarks>
/// 发现的偏离**：基线是 `doubleValue.roundToLong.coerceAtLeast(0)`，
/// C# 原本是 `(long)doubleValue`——**截断**。
///
/// 两个后果，第二个更严重：
///
/// | 解值 | 基线（四舍五入） | C#（截断） |
/// | --- | --- | --- |
/// | `2.7` | 3 | 2 |
/// | `0.6` | **1** | **0** |
///
/// `0.6` 那一行说明问题：**一条用了 0.6 次的方案会被当成"没用"而整条丢弃**，
/// 物料用量、设备用量、欠产判定全部跟着少算。
///
/// **the earlier deviation**: the baseline is `doubleValue.roundToLong.coerceAtLeast(0)` while C# was `(long)doubleValue` — **truncation**. The table above shows the two
/// consequences; the `0.6` row is the serious one — **a plan used 0.6 times is discarded as "unused"**, and the material, machine and deficit figures all under-count with it.
/// </remarks>
public class Csp1dSolutionRoundingTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static (Csp1dProduceContext<Flt64> Context, LinearMetaModel<Flt64> Model, IVariableItem Variable) MakeModel() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(50.0)),
            });
        var input = new ProduceInput<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>> { plan },
            demands: new List<ProductDemand<Flt64>> { new(product, Millimetres(50.0)) },
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>>());
        var model = new LinearMetaModel<Flt64>();
        Csp1dProduceContext<Flt64> context = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.MILP)
            .Build();
        context.Register(model).IsOk.Should().BeTrue();
        return (context, model, context.Produce[0]!);
    }

    /// <summary>**`0.6` 必须提成 1 次，不能整条丢弃** / `0.6` must extract as 1, not be dropped.</summary>
    /// <remarks>
    /// 这是截断与四舍五入**差别最大**的输入：截断得到 0，方案被 `doubleValue <= 0.0` 之后
    /// 的取整过滤掉，`SelectedPlans` 里**一条都没有**。
    ///
    /// This is the input where truncation and rounding **differ most**: truncation yields 0, the plan is filtered out after the `doubleValue <= 0.0` check, and
    /// `CuttingPlans` ends up **empty**.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldRoundPointSixUpToOne() {
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model, IVariableItem variable) = MakeModel();
        model.Tokens.Find(variable)!.SetResult(new Flt64(0.6));

        Result<Produce<Flt64>, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsOk.Should().BeTrue();
        Produce<Flt64> produce = result.Value;
        produce.CuttingPlans.Should().HaveCount(1, "0.6 rounds to 1, so the plan was used");
        produce.CuttingPlans[0].Amount.ToULong().Should().Be(1UL);
    }

    /// <summary>**`2.5` 必须提成 3 次——这条钉住"逢半向上"** / `2.5` must extract as 3 — this pins half-away-from-zero.</summary>
    /// <remarks>
    /// **前两条判据钉不住舍入方式**：`0.6` 与 `2.7` 在"银行家舍入"（逢半取偶）下**结果相同**
    /// （都是 1 和 3）。实测确认：把 `MidpointRounding.AwayFromZero` 改成默认的
    /// `ToEven`，前两条**全绿**。
    ///
    /// 只有 `.5` 这个输入能分开：
    ///
    /// | 输入 | 逢半向上（基线 `roundToLong`） | 逢半取偶（C# `Math.Round` 默认） |
    /// | --- | --- | --- |
    /// | `2.5` | **3** | **2** |
    /// | `0.5` | **1** | **0** |
    ///
    /// 选 `0.5` 更狠——逢半取偶会把它取成 0，**方案整条消失**。
    ///
    /// **The first two criteria cannot pin the rounding mode**: `0.6` and `2.7` give the **same** result under banker's rounding (1 and 3). Measured: switching
    /// `MidpointRounding.AwayFromZero` to the default `ToEven` left both **green**. Only a `.5` input separates them (table above). `0.5` is the harsher choice —
    /// banker's rounding makes it 0 and **the plan vanishes entirely**.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldRoundHalfAwayFromZero() {
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model, IVariableItem variable) = MakeModel();
        model.Tokens.Find(variable)!.SetResult(new Flt64(0.5));

        Result<Produce<Flt64>, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsOk.Should().BeTrue();
        result.Value.CuttingPlans.Should().HaveCount(1, "0.5 must round up to 1; banker's rounding would make it 0 and drop the plan");
        result.Value.CuttingPlans[0].Amount.ToULong().Should().Be(
            1UL,
            "Kotlin's roundToLong rounds half away from zero, so 0.5 is 1; .NET's default ToEven would give 0");
    }

    /// <summary>**`SolutionAmount` 走的是同一条取整规则** / `SolutionAmount` follows the same rounding rule.</summary>
    /// <remarks>
    /// 只修了 `ExtractSolution`，漏了 `SolutionAmount`。** 两处基线都是
    /// `roundToLong`，C# 两处都是 `(long)`——**修一处等于没修**。
    ///
    /// `SolutionAmount` 是 **waste 结果（余宽、余料、物料成本）的唯一入口**，
    /// 截断会让这些指标系统性偏低。这条判据通过 `ExtractWasteResult` 间接钉它：
    /// 余宽 40mm 用了 **0.5** 次，正确结果 20mm；截断给 **0**（因为 0.5 取成 0，整条跳过）。
    ///
    /// **fixed only `ExtractSolution` and missed `SolutionAmount`.** Both baseline sites are `roundToLong` and both C# sites were `(long)` — **fixing one changed
    /// nothing**. `SolutionAmount` is **the only entry point for the waste results** (trim width, rest material, material cost), so truncation makes them systematically low.
    /// This criterion pins it through `ExtractWasteResult`: a 40mm rest width used **0.5** times gives 20mm correctly and **0** under truncation (0.5 becomes 0 and the
    /// whole plan is skipped).
    /// </remarks>
    [Fact]
    public void ExtractWasteResult_ShouldUseTheRoundedAmount() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(60.0) });
        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(60.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(60.0)),
            });
        var input = new ProduceInput<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>> { plan },
            demands: new List<ProductDemand<Flt64>> { new(product, Millimetres(60.0)) },
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>>());
        var model = new LinearMetaModel<Flt64>();
        Csp1dProduceContext<Flt64> context = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.MILP)
            .WasteConfig(new WasteMinimizationConfig<Flt64> {
                TrimWidthPenalty = new Flt64(1.0),
            })
            .Build();
        context.Register(model).IsOk.Should().BeTrue();
        model.Tokens.Find(context.Produce[0]!)!.SetResult(new Flt64(0.5));

        WasteMinimizationResult<Flt64>? result = context.ExtractWasteResult(model);

        result.Should().NotBeNull("a trim-width penalty is configured");
        result!.TotalTrimWidth.Should().NotBeNull(
            "0.5 rounds to 1, so the plan is used and its rest width counts");
        // 余宽 = 200 - 60 = 140mm；140 × 1 = 140。
        // Rest width = 200 - 60 = 140mm; 140 x 1 = 140.
        result.TotalTrimWidth!.Value.ToDouble().Should().BeApproximately(
            140.0,
            1e-6,
            "the plan's rest width times the rounded amount");
    }

    /// <summary>**`2.7` 必须提成 3 次，不是 2 次** / `2.7` must extract as 3, not 2.</summary>
    /// <remarks>
    /// 这条与上一条**不同**：`2.7` 截断后仍大于零，方案**不会**被丢弃，
    /// 所以只靠上一条判据**钉不住**"把 2.7 取成 2"的实现。两条一起才完整。
    ///
    /// This differs from the previous one: `2.7` truncated is still positive, so the plan is **not** dropped, and the previous criterion **cannot** catch an implementation
    /// that yields 2. Both are needed.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldRoundTwoPointSevenUpToThree() {
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model, IVariableItem variable) = MakeModel();
        model.Tokens.Find(variable)!.SetResult(new Flt64(2.7));

        Result<Produce<Flt64>, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsOk.Should().BeTrue();
        result.Value.CuttingPlans[0].Amount.ToULong().Should().Be(
            3UL,
            "rounding 2.7 gives 3; truncation would give 2");
    }
}
