// 用真实 SCIP 的对偶解喂框架影子价格 / Feed framework shadow prices from real SCIP duals.

#if SCIP_INTEGRATION

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using ColumnsSolver = Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.Application.Service;

/// <summary>
/// **真实 SCIP 的对偶解驱动框架影子价格** / Real SCIP duals drive the framework's shadow prices.
/// </summary>
/// <remarks>
/// **这是 记了 40 多批的缺口，补上。**
///
/// 既有判据 `Csp1dShadowPriceNonZeroTests` 喂的是**手工塞进字典的常数**
/// （`duals[constraint] = new Flt64(3.5)`）。它证明的是"**给定**非零对偶时映射不丢值"，
/// **完全没有证明"真实求解器会产出非零对偶"**——后者才是 的缺口。
///
/// | 判据 | 对偶来源 | 证明什么 |
/// | --- | --- | --- |
/// | `Csp1dShadowPriceNonZeroTests` | **手工常数** | 映射不丢值 |
/// | **本文件** | **真实 SCIP `DualSolution`** | 真实对偶**存在且非零**，且能穿过映射 |
///
/// 模型取"材料用量约束起作用"的最小情形：需求超过单张板材的容量，
/// 于是材料约束的右端项收紧会抬高目标值 ⇒ 对偶**必然非零**。
///
/// **This closes the gap recorded for 40+ batches.** The existing criterion feeds **hand-written constants**; it proves "a *given* non-zero dual is not lost by the
/// mapping", **never that a real solver produces non-zero duals** — which is the actual gap. The model is the minimal case where the material-usage constraint binds, so
/// tightening its right-hand side must raise the objective, making the dual **necessarily non-zero**.
/// </remarks>
public class ScipShadowPriceIntegrationTests {
    /// <summary>**真实求解器产出的对偶必须非零，并且穿过影子价格映射** / Duals from the real solver must be non-zero and survive the shadow-price mapping.</summary>
    [Fact]
    public async Task RealScipDuals_ShouldBeNonZeroAndSurviveTheMapping() {
        (Csp1dProduceContext<Flt64> context, LinearMetaModel<Flt64> model) = BuildModel();

        var solver = new ScipColumnGenerationSolver();
        Result<ColumnsSolver.LpResult, ErrorCode, Error<ErrorCode>> solved =
            await solver.SolveLPAsync("shadow-price", model);

        solved.IsOk.Should().BeTrue(solved.ToString());
        ColumnsSolver.LpResult lp = solved.Value;

        // **先证明求解器真的给了非零对偶。** 少了这一步，后面"映射能取到值"可能只是取到了 0。
        // **First prove the solver actually produced non-zero duals**; without this, "the mapping found a value" could just mean it found zero.
        var nonZero = new List<(MathConstraint Constraint, Flt64 Dual)>();
        foreach (KeyValuePair<MathConstraint, Flt64> pair in lp.DualSolution) {
            if (global::System.Math.Abs(pair.Value.ToDouble()) > 1e-9) {
                nonZero.Add((pair.Key, pair.Value));
            }
        }
        nonZero.Should().NotBeEmpty(
            "the material constraint binds in this model, so at least one dual must be non-zero");

        var lifecycle = new Csp1dShadowPriceLifecycle<Flt64>(Millimetres(1.0).Value, context.CgPipelines);
        Result<ShadowPriceMap<Flt64>, ErrorCode, Error<ErrorCode>> extracted =
            lifecycle.ExtractFromDualSolution(model, lp.DualSolution);

        extracted.IsOk.Should().BeTrue(extracted.ToString());
        ShadowPriceMap<Flt64> prices = extracted.Value;
        prices.Should().NotBeNull();

        // **非零对偶必须留下非零痕迹。** 只断言 "IsOk" 会让"把所有对偶都乘 0"的变异存活。
        // **Non-zero duals must leave a non-zero trace.** Asserting only `IsOk` would let "multiply every dual by 0" survive.
        // **按已知键查，不按"约束自己带的键"查**。
        // **Look up by the known key**, not by "whatever key the constraint carries" (the earlier lesson: a lookup key that mutates along with the mutant proves nothing).
        // **实测该模型的对偶键族是需求与设备**，不是材料——因为方案已经覆盖了物料用量。
        // 需求约束起作用（需求 3 而方案只切 1 件），所以它的对偶**非零**（实测 0.020）。
        //
        // **Measured, this model's dual keys are demand and machine capacity, not material** — the plan already covers the material usage. The demand constraint binds
        // (demand 3, one piece per plan), so its dual is **non-zero** (measured 0.020).
        var demandKey = new ProductDemandShadowPriceKey("A", "mm");
        Flt64? demandPrice = prices[demandKey];
        demandPrice.Should().NotBeNull(
            "the product demand constraint exists in the model, so its shadow price must be present");
        global::System.Math.Abs(demandPrice!.Value.ToDouble()).Should().BeGreaterThan(
            1e-9,
            "demand 3 exceeds what one plan yields, so the demand shadow price must be non-zero");
    }

    /// <summary>构造模型 / Build the model.</summary>
    /// <returns>上下文与线性元模型 / The context and the linear meta model.</returns>
    private static (Csp1dProduceContext<Flt64> Context, LinearMetaModel<Flt64> Model) BuildModel() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(100.0)),
                Step: Millimetres(1.0)));
        var machine = new Machine<Flt64>(
            id: "K1",
            name: "machine",
            capacity: Millimetres(3600.0));
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            machineId: "K1",
            capacityConsumption: Millimetres(50.0),
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(50.0)),
            });
        // **需求量 3 而单张板材只放 1 件**（100 / 50 = 2，但方案只切 1 件），
        // 所以材料约束必然起作用 ⇒ 对偶非零。
        // **Demand 3 while each plan yields 1 piece**, so the material constraint must bind and its dual is non-zero.
        var input = new ProduceInput<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>> { plan },
            demands: new List<ProductDemand<Flt64>> { new(product, Millimetres(3.0)) },
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>> { machine });
        var model = new LinearMetaModel<Flt64>(name: "scip-shadow-price");
        Csp1dProduceContext<Flt64> context = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.LP)
            .Extensions(new List<Csp1dModelingExtension<Flt64>>())
            .Build();
        context.Register(model).IsOk.Should().BeTrue("the probe model must register");
        return (context, model);
    }

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);
}

#endif
