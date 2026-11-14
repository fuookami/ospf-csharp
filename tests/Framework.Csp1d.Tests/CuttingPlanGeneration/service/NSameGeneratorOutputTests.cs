// NSame 生成器的真实产出测试 / Tests for NSame's actual plan output.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.Service;

/// <summary>
/// `NSameGenerator` 必须真的产出方案，而不只是构造一个收集器。
/// `NSameGenerator` must really emit plans rather than merely construct a collector.
/// </summary>
/// <remarks>
/// B304 之前它的方法体只有一行注释：`// Stub: NSame generation implementation deferred`。
/// 既有两项测试**全部通过**——一项断言空输入返回空（空壳自然满足），一项只断言 `NotBeNull()`。
///
/// **这正是本项目反复出现的形态**：接口、统计、收集器都齐，唯独串起来的那个是空壳。
/// 所以这里的判据必须落在**产出的方案本身**：条数、切片数、贡献、去重。
///
/// Before B304 its method body was a single comment: `// Stub: NSame generation implementation deferred`. Both existing tests **passed** — one asserted
/// empty input yields empty output (trivially true for a shell), the other only asserted `NotBeNull()`.
///
/// **This is the recurring shape in this project**: interface, statistics and collector all present, and the one thing tying them together is a shell.
/// So the criteria here must land on **the emitted plans themselves**: count, slice count, contributions, de-duplication.
/// </remarks>
public class NSameGeneratorOutputTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)
                ),
                Step: Millimetres(1.0)
            )
        );

    private static Product<Flt64> MakeProduct(string id, double width) =>
        new(
            id: id,
            name: "product-" + id,
            width: new List<Quantity<Flt64>> { Millimetres(width) }
        );

    private static GenerationInput<Flt64> MakeInput() =>
        new() {
            Materials = new List<Material<Flt64>> { MakeMaterial() },
            Demands = new List<ProductDemand<Flt64>> {
                new(MakeProduct("A", 20.0), Millimetres(20.0)),
                new(MakeProduct("B", 30.0), Millimetres(30.0)),
            },
        };

    /// <summary>**每个需求必须产出一条方案** / Each demand must yield one plan.</summary>
    [Fact]
    public void TwoDemands_ShouldEmitTwoPlans() {
        var generator = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput());

        plans.Should().HaveCount(2);
    }

    /// <summary>**每条方案必须是单切片** / Every plan must carry exactly one slice.</summary>
    /// <remarks>
    /// "N-Same"的语义就是**同宽单产品切片**。若实现误写成多切片，方案条数仍然对，但语义错了。
    ///
    /// The semantics of "N-Same" is **same-width single-product slices**. If the implementation wrongly emitted multi-slice plans the count would still match
    /// while the semantics would be wrong.
    /// </remarks>
    [Fact]
    public void EveryPlan_ShouldHaveExactlyOneSlice() {
        var generator = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput());

        plans.Should().OnlyContain(plan => plan.Slices.Count == 1);
    }

    /// <summary>**每条方案必须带需求贡献** / Every plan must carry a demand contribution.</summary>
    [Fact]
    public void EveryPlan_ShouldCarryADemandContribution() {
        var generator = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput());

        plans.Should().OnlyContain(plan => plan.DemandContributions.Count == 1);
        plans.SelectMany(plan => plan.DemandContributions.Select(c => c.Product.Id))
            .Should().BeEquivalentTo(new[] { "A", "B" });
    }

    /// <summary>**方案 ID 必须互不相同** / Plan ids must be distinct.</summary>
    [Fact]
    public void PlanIds_ShouldBeDistinct() {
        var generator = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput());

        plans.Select(plan => plan.Id).Distinct().Should().HaveCount(plans.Count);
    }

    /// <summary>**统计必须反映真实产出** / The statistics must reflect the real output.</summary>
    /// <remarks>
    /// 空壳的统计是 `0`，而"产出 2 条"的统计必须是 `2`。**这条判据同时钉住产出与统计**。
    ///
    /// A shell reports `0`; emitting two plans must report `2`. **This criterion pins output and statistics together.**
    /// </remarks>
    [Fact]
    public void Report_ShouldCountTheEmittedPlans() {
        var generator = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        CuttingPlanGenerationReport<CuttingPlan<Flt64>> report = generator.GenerateWithReport(MakeInput());

        report.Statistics.GeneratedCandidates.Should().Be(2);
        report.Statistics.AcceptedPlans.Should().Be(2);
        report.Statistics.VisitedNodes.Should().Be(2);
    }

    /// <summary>**宽度不可分切的产物必须被跳过** / Products whose width cannot be cut must be skipped.</summary>
    /// <remarks>
    /// 物料幅宽上界是 200mm。300mm 的产品**无法分切**，必须被过滤掉——否则生成器会产出物理上不可能的方案。
    ///
    /// The material's upper bound is 200mm. A 300mm product **cannot be cut** and must be filtered — otherwise the generator emits physically impossible plans.
    /// </remarks>
    [Fact]
    public void ProductWiderThanMaterial_ShouldBeSkipped() {
        var input = new GenerationInput<Flt64> {
            Materials = new List<Material<Flt64>> { MakeMaterial() },
            Demands = new List<ProductDemand<Flt64>> {
                new(MakeProduct("Wide", 300.0), Millimetres(300.0)),
            },
        };
        var generator = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(input);

        plans.Should().BeEmpty();
    }

    /// <summary>**违反约束的产物必须被过滤** / Plans violating a constraint must be filtered.</summary>
    /// <remarks>
    /// 用 `MaxKnifeCountConstraint(0)`：每条单切片方案的切数都是 1，**必然超过阈值 0**，
    /// 所以约束路径一旦被跳过，方案就会漏出来。
    ///
    /// **这是"约束检查被删掉"那个变异体唯一能被抓到的判据**：其余测试都用无约束配置。
    ///
    /// Using `MaxKnifeCountConstraint(0)`: every single-slice plan has one cut, **necessarily exceeding the threshold 0**, so once the constraint path is skipped
    /// the plans leak out.
    ///
    /// **This is the only criterion that catches the "constraint check deleted" mutant**: every other test uses the unconstrained configuration.
    /// </remarks>
    [Fact]
    public void PlansViolatingAConstraint_ShouldBeFiltered() {
        var constraints = new List<ICuttingPlanConstraint<Flt64>> {
            new MaxKnifeCountConstraint<Flt64>(UInt64.Zero),
        };
        var generator = new NSameGenerator<Flt64>(constraints);

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput());

        plans.Should().BeEmpty();
    }

    /// <summary>**同一需求重复出现必须被去重** / A repeated identical demand must be de-duplicated.</summary>
    /// <remarks>
    /// 收集器按 canonical key 去重。**若去重失效，两条完全相同的方案会同时留下**，而"产出条数"仍然看似正常。
    ///
    /// The collector de-duplicates by canonical key. **If de-duplication broke, two identical plans would both survive** while the output count still looks
    /// plausible.
    /// </remarks>
    [Fact]
    public void DuplicateDemand_ShouldBeDeduplicated() {
        Product<Flt64> product = MakeProduct("A", 20.0);
        var input = new GenerationInput<Flt64> {
            Materials = new List<Material<Flt64>> { MakeMaterial() },
            Demands = new List<ProductDemand<Flt64>> {
                new(product, Millimetres(20.0)),
                new(product, Millimetres(20.0)),
            },
        };
        var generator = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(input);

        plans.Should().HaveCount(1);
    }

    /// <summary>**多个物料必须各自产出** / Several materials must each produce plans.</summary>
    [Fact]
    public void TwoMaterials_ShouldProducePlansForEach() {
        var input = new GenerationInput<Flt64> {
            Materials = new List<Material<Flt64>> {
                MakeMaterial(),
                new(
                    id: "M2",
                    name: "material-2",
                    widthRange: new WidthRange<Flt64>(
                        Width: new QuantityRange<Flt64>(
                            LowerBound: Millimetres(100.0),
                            UpperBound: Millimetres(200.0)
                        ),
                        Step: Millimetres(1.0)
                    )
                ),
            },
            Demands = new List<ProductDemand<Flt64>> {
                new(MakeProduct("A", 20.0), Millimetres(20.0)),
            },
        };
        var generator = new NSameGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(input);

        plans.Select(plan => plan.Material.Id).Distinct().Should().HaveCount(2);
    }
}
