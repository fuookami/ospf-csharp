// 余料代理绝对值与超产面积的判据 / Criteria for the rest-material proxy's absolute value and the over-production area.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **余料代理的绝对值，以及超产面积这一路** / The rest-material proxy's absolute value, and the over-production area path.
/// </summary>
/// <remarks>
/// **B374 记的两条缺口**：
///
/// 1. "`RestWidthByMaterialLengthProxy` 的绝对值无判据"——只钉了"4 次 = 4 倍"的**比值**；
/// 2. "超产面积那一路仍无判据"——夹具成本高，那一批没做。
///
/// 本批把两条一起补上。**绝对值判据比比值强**：比值判据对"余宽与长度乘反了"、
/// "乘了别的量"都**不敏感**，绝对值判据当场就红。
///
/// **The two gaps recorded in B374**: (1) the proxy's absolute value had no criterion — only the **ratio** "4 uses is 4 times"; (2) the over-production area path had none,
/// because its fixture is expensive. This batch closes both. **An absolute criterion is stronger than a ratio**: a ratio is **insensitive** to "width and length
/// swapped" or "multiplied by something else", while an absolute one fails immediately.
/// </remarks>
public class Csp1dWasteValueTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial(string id, double? length) =>
        new(
            id: id,
            name: id,
            length: length is null ? null : Millimetres(length.Value),
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));

    private static Product<Flt64> MakeProduct(string id, double width) =>
        new(id: id, name: id, width: new List<Quantity<Flt64>> { Millimetres(width) });

    private static CuttingPlan<Flt64> MakePlan(
        string id,
        Material<Flt64> material,
        Product<Flt64> product,
        double sliceWidth) =>
        new(
            id: id,
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(sliceWidth)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(sliceWidth)),
            });

    private static (Csp1dProduceContext<Flt64> Context, LinearMetaModel<Flt64> Model) Build(
        IReadOnlyList<CuttingPlan<Flt64>> plans,
        IReadOnlyList<ProductDemand<Flt64>> demands,
        IReadOnlyList<Material<Flt64>> materials,
        WasteMinimizationConfig<Flt64>? wasteConfig,
        YieldModelingConfig<Flt64>? yieldConfig = null) {
        var input = new ProduceInput<Flt64>(
            cuttingPlans: plans,
            demands: demands,
            materials: materials,
            machines: new List<Machine<Flt64>>());
        Csp1dProduceContextBuilder<Flt64> builder = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.MILP);
        if (wasteConfig is not null) {
            builder = builder.WasteConfig(wasteConfig);
        }
        if (yieldConfig is not null) {
            builder = builder.YieldConfig(yieldConfig);
        }
        Csp1dProduceContext<Flt64> context = builder.Build();
        var model = new LinearMetaModel<Flt64>();
        context.Register(model).IsOk.Should().BeTrue();
        return (context, model);
    }

    /// <summary>**余料代理 = 余宽 × 物料长度**，绝对值 / The proxy is rest width times material length, absolute.</summary>
    /// <remarks>
    /// 物料宽 200mm、切片 60mm ⇒ 余宽 **140mm**；物料长 **6000mm** ⇒ 代理 **840 000**。
    ///
    /// 三个错误实现会被这条钉住：
    ///
    /// | 实现 | 结果 |
    /// | --- | --- |
    /// | 正确 | `840000` |
    /// | **余宽与长度乘反** | 也是 `840000`（**乘法交换律，这条钉不住**） |
    /// | 用切片宽当余宽 | `60 × 6000 = 360000` |
    /// | 只用余宽 | `140` |
    ///
    /// 第一行说明**"乘反"在乘法下不可区分**——如实记录，不假装覆盖。
    ///
    /// Material 200mm wide with a 60mm slice gives a **140mm** rest width; the material is **6000mm** long, so the proxy is **840 000**. The table shows which wrong
    /// implementations this catches; the first row notes that **"swapped operands" is indistinguishable under multiplication** — recorded honestly rather than faked away.
    /// </remarks>
    [Fact]
    public void ExtractWasteResult_ShouldComputeTheRestMaterialProxyAbsolutely() {
        Material<Flt64> material = MakeMaterial("M1", 6000.0);
        Product<Flt64> product = MakeProduct("A", 60.0);
        CuttingPlan<Flt64> plan = MakePlan("p1", material, product, 60.0);
        var wasteConfig = new WasteMinimizationConfig<Flt64> {
            RestMaterialPenalty = new Flt64(1.0),
        };
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model) = Build(
            new List<CuttingPlan<Flt64>> { plan },
            new List<ProductDemand<Flt64>> { new(product, Millimetres(60.0)) },
            new List<Material<Flt64>> { material },
            wasteConfig);
        model.Tokens.Find(context.Produce[0]!)!.SetResult(new Flt64(1.0));

        WasteMinimizationResult<Flt64>? result = context.ExtractWasteResult(model);

        result.Should().NotBeNull();
        result!.TotalRestMaterial.Should().NotBeNull();
        result.TotalRestMaterial!.Value.ToDouble().Should().BeApproximately(
            840000.0,
            1e-6,
            "rest width 140mm times material length 6000mm");
    }

    /// <summary>**物料没有长度时余料为 null，不是零** / Without a material length the rest material is null, not zero.</summary>
    /// <remarks>
    /// 基线是 `?: return null`。若实现改成"返回零"，**"没有数据"和"真的没有余料"就混在一起**——
    /// 下游无法区分，KPI 会显示成"零浪费"。
    ///
    /// The baseline is `?: return null`. If it returned zero instead, **"no data" and "genuinely no waste" would be conflated**, and the KPI would read as
    /// "zero waste".
    /// </remarks>
    [Fact]
    public void ExtractWasteResult_ShouldReturnNullWithoutAMaterialLength() {
        Material<Flt64> material = MakeMaterial("M1", null);
        Product<Flt64> product = MakeProduct("A", 60.0);
        CuttingPlan<Flt64> plan = MakePlan("p1", material, product, 60.0);
        var wasteConfig = new WasteMinimizationConfig<Flt64> {
            RestMaterialPenalty = new Flt64(1.0),
        };
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model) = Build(
            new List<CuttingPlan<Flt64>> { plan },
            new List<ProductDemand<Flt64>> { new(product, Millimetres(60.0)) },
            new List<Material<Flt64>> { material },
            wasteConfig);
        model.Tokens.Find(context.Produce[0]!)!.SetResult(new Flt64(1.0));

        WasteMinimizationResult<Flt64>? result = context.ExtractWasteResult(model);

        result.Should().NotBeNull();
        result!.TotalRestMaterial.Should().BeNull(
            "the material has no length, so the proxy is unavailable — zero would pretend there is no waste");
    }

    /// <summary>**超产面积 = 产品最大宽度 × 超产量** / Over-production area = product max width times the over-produced amount.</summary>
    /// <remarks>
    /// **B374 记的缺口**：超产面积一路无判据。
    ///
    /// 产品宽 60mm、超产量 **2** ⇒ 面积 **120**。
    ///
    /// 这条要 `Yield` 聚合在场（否则整段被 `Yield is not null` 跳过），
    /// 所以夹具要配 `YieldConfig`。**这也是该路一直没判据的原因。**
    ///
    /// **B374's gap**: the over-production area path had no criterion. Product width 60mm with an over-produced amount of **2** gives an area of **120**. The criterion needs
    /// the `Yield` aggregation present (otherwise the whole block is skipped by `Yield is not null`), so the fixture must supply a `YieldConfig` — **the reason this path
    /// went untested**.
    /// </remarks>
    [Fact]
    public void ExtractWasteResult_ShouldComputeTheOverProductionArea() {
        Material<Flt64> material = MakeMaterial("M1", 6000.0);
        Product<Flt64> product = MakeProduct("A", 60.0);
        CuttingPlan<Flt64> plan = MakePlan("p1", material, product, 60.0);
        var wasteConfig = new WasteMinimizationConfig<Flt64> {
            OverProductionAreaPenalty = new Flt64(1.0),
        };
        var yieldConfig = new YieldModelingConfig<Flt64>(
            UnderProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(1.0),
            },
            OverProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(1.0),
            });
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model) = Build(
            new List<CuttingPlan<Flt64>> { plan },
            new List<ProductDemand<Flt64>> { new(product, Millimetres(60.0)) },
            new List<Material<Flt64>> { material },
            wasteConfig,
            yieldConfig);
        model.Tokens.Find(context.Produce[0]!)!.SetResult(new Flt64(1.0));
        context.Yield.Should().NotBeNull("a yield config was supplied, so the aggregation exists");
        URealVar overVar = context.Yield!.OverProduction.First()!;
        model.Tokens.Find(overVar)!.SetResult(new Flt64(2.0));

        WasteMinimizationResult<Flt64>? result = context.ExtractWasteResult(model);

        result.Should().NotBeNull();
        result!.OverProductionArea.Should().NotBeNull(
            "the over-production variable carries 2, so the area path must produce a value");
        result.OverProductionArea!.Value.ToDouble().Should().BeApproximately(
            120.0,
            1e-9,
            "product max width 60mm times 2 over-produced units");
    }
}
