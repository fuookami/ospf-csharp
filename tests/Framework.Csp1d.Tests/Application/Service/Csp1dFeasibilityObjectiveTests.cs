// 可行性阶段目标函数判据 / Criteria for the feasibility-phase objective.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Application.Service;

/// <summary>
/// **可行性阶段的目标只能有人工短缺项** / The feasibility phase's objective must contain only the artificial-deficit terms.
/// </summary>
/// <remarks>
/// 发现**：`setObjective` 第一件事就是 `if (feasibilityPhase) { … return }`，
/// 只放 yield 目标、命名为 `csp1d_phase_one`。
///
/// C# **完全没有这个分支**：`_feasibilityPhase` 只被传给 yield 聚合，`SetObjective` 从未读过它。
/// 于是第一阶段最小化的仍是**批次成本**，人工短缺项的系数虽然被设成 1，**却进不了目标**——
/// **第一阶段解得的是"批次最省"，不是"短缺最小"**。
///
/// **finding**: its `setObjective` opens with `if (feasibilityPhase) { … return }`, emitting only the yield objective under the name `csp1d_phase_one`.
/// C# **had no such branch**: `_feasibilityPhase` was forwarded only to the yield aggregation and `SetObjective` never read it, so phase one still minimised **batch cost** —
/// the deficits' coefficient of 1 **never reached the objective**, and phase one solved "cheapest batches" instead of "smallest deficit".
/// </remarks>
public class Csp1dFeasibilityObjectiveTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));

    private static Product<Flt64> MakeProduct() =>
        new(id: "A", name: "product-a", width: new List<Quantity<Flt64>> { Millimetres(50.0) });

    private static ProduceInput<Flt64> MakeInput() {
        Material<Flt64> material = MakeMaterial();
        Product<Flt64> product = MakeProduct();
        var plan = new CuttingPlan<Flt64>(
            id: "p1",
            material: material,
            slices: new List<CuttingPlanSlice<Flt64>> { new(product, Millimetres(50.0)) },
            demandContributions: new List<CuttingPlanDemandContribution<Flt64>> {
                new(product, Millimetres(50.0)),
            });
        return new ProduceInput<Flt64>(
            cuttingPlans: new List<CuttingPlan<Flt64>> { plan },
            demands: new List<ProductDemand<Flt64>> { new(product, Millimetres(50.0)) },
            materials: new List<Material<Flt64>> { material },
            machines: new List<Machine<Flt64>>());
    }

    private static Csp1dProduceContext<Flt64> Build(bool feasibilityPhase) {
        Csp1dProduceContextBuilder<Flt64> builder = new Csp1dProduceContextBuilder<Flt64>(MakeInput())
            .Mode(Csp1dModelingMode.LP)
            .YieldConfig(new YieldModelingConfig<Flt64>(
                UnderProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                    [new ProductDemandShadowPriceKey("A", "mm")] = new Flt64(1.0),
                },
                OverProductionPenalty: new Dictionary<ProductDemandShadowPriceKey, Flt64> {
                    [new ProductDemandShadowPriceKey("A", "mm")] = Flt64.Zero,
                }));
        if (feasibilityPhase) {
            builder = builder.FeasibilityPhase();
        }
        return builder.Build();
    }

    /// <summary>**可行性阶段的目标名必须是 `csp1d_phase_one`** / The feasibility-phase objective must be named `csp1d_phase_one`.</summary>
    /// <remarks>
    /// 名字是**最省事的区分方式**：正常目标叫 `csp1d_objective`，第一阶段叫 `csp1d_phase_one`。
    /// 只断言"目标存在"抓不住"跑的还是正常目标"。
    ///
    /// The name is **the cheapest discriminator**: the normal objective is `csp1d_objective`, phase one is `csp1d_phase_one`. Asserting merely "an objective exists" cannot
    /// catch "it is still the normal objective".
    /// </remarks>
    [Fact]
    public void Register_ShouldNameTheObjectivePhaseOneDuringTheFeasibilityPhase() {
        Csp1dProduceContext<Flt64> context = Build(feasibilityPhase: true);
        var model = new LinearMetaModel<Flt64>();

        context.Register(model).IsOk.Should().BeTrue();

        context.FeasibilityPhase.Should().BeTrue("the builder flag must reach the context");
        model.MetaSubObjects[0].Name.Should().Be(
            "csp1d_phase_one",
            "the feasibility phase emits its own objective, not the normal batch-cost one");
    }

    /// <summary>**正常模式的目标名必须是 `csp1d_objective`**——反向对照 / In normal mode the objective must be `csp1d_objective` — the reverse control.</summary>
    /// <remarks>
    /// 少了这条，"**无论哪个阶段都叫 `csp1d_phase_one`**"的实现也会绿。
    ///
    /// Without this, an implementation that **always** names the objective `csp1d_phase_one` would also pass.
    /// </remarks>
    [Fact]
    public void Register_ShouldNameTheObjectiveNormallyOutsideTheFeasibilityPhase() {
        Csp1dProduceContext<Flt64> context = Build(feasibilityPhase: false);
        var model = new LinearMetaModel<Flt64>();

        context.Register(model).IsOk.Should().BeTrue();

        context.FeasibilityPhase.Should().BeFalse();
        model.MetaSubObjects[0].Name.Should().Be("csp1d_objective");
    }

    /// <summary>**可行性阶段的目标不得含批次成本项** / The feasibility-phase objective must not contain batch-cost terms.</summary>
    /// <remarks>
    /// 批次成本项的形式是"系数 × `x_i`"。可行性阶段只该有 yield 的短缺/超产项，
    /// 所以断言**没有任何单项式引用方案选择变量**。
    ///
    /// 这条比名字强：名字对了、但把批次项也塞进去的实现会在这条上红。
    ///
    /// Batch-cost terms have the form "coefficient × `x_i`". The feasibility phase should carry only the yield deficit/surplus terms, so this asserts that **no monomial
    /// references a plan selection variable**. Stronger than the name: an implementation that gets the name right but still adds batch terms fails here.
    /// </remarks>
    [Fact]
    public void Register_ShouldOmitTheBatchCostTermsDuringTheFeasibilityPhase() {
        Csp1dProduceContext<Flt64> context = Build(feasibilityPhase: true);
        var model = new LinearMetaModel<Flt64>();

        context.Register(model).IsOk.Should().BeTrue();

        IReadOnlyList<LinearMonomial<Flt64>> monomials = model.MetaSubObjects[0].Polynomial.Monomials;
        var planVariables = new HashSet<object>();
        for (int index = 0; index < context.Produce.PlanCount; index++) {
            planVariables.Add(context.Produce[index]!);
        }
        monomials.Any(m => planVariables.Contains(m.Symbol)).Should().BeFalse(
            "phase one minimises the artificial deficit only; a batch-cost term would make it minimise batches again");
        monomials.Should().NotBeEmpty("the artificial deficit terms must be there");
    }
}
