// DFS 生成器的真实产出测试 / Tests for DFS's actual plan output.

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
/// `DFSGenerator` 必须真的搜索，且必须**在扩展时**检查剪枝约束。
/// `DFSGenerator` must really search and must check pruning constraints **while expanding**.
/// </summary>
/// <remarks>
/// B308 之前 C# 侧**根本没有这个类**——基线的 `DFSGenerator`（484 行）从未移植。
///
/// **本批的核心判据是差分**：DFS 与 FullSum 的搜索逻辑完全相同，唯一差别是
/// "扩展时是否检查剪枝约束"。所以判据不能只看"产出了一些方案"，
/// 必须看**同一条剪枝约束下两者的产出不同**。
///
/// Before B308 the class **did not exist at all** on the C# side — the baseline `DFSGenerator` (484 lines) had never been ported.
///
/// **The core criterion is differential**: DFS and FullSum share the identical search logic, differing only in whether pruning constraints are checked while
/// expanding. So the criterion cannot be "it produced some plans"; it must be that **the same pruning constraint makes the two produce different results**.
/// </remarks>
public class DFSGeneratorOutputTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial(double upperBound = 200.0) =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(upperBound)
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

    private static GenerationInput<Flt64> MakeInput(params (string Id, double Width)[] products) =>
        new() {
            Materials = new List<Material<Flt64>> { MakeMaterial() },
            Demands = products
                .Select(product => new ProductDemand<Flt64>(
                    MakeProduct(product.Id, product.Width),
                    Millimetres(product.Width)))
                .ToList(),
        };

    /// <summary>**空输入必须返回空** / Empty input must yield nothing.</summary>
    [Fact]
    public void EmptyInput_ShouldReturnEmpty() {
        var generator = new DFSGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        generator.Generate(new GenerationInput<Flt64>()).Should().BeEmpty();
    }

    /// <summary>**无约束时必须真的产出方案** / With no constraints it must really emit plans.</summary>
    [Fact]
    public void Unconstrained_ShouldProducePlans() {
        var generator = new DFSGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(
            MakeInput(("A", 50.0), ("B", 60.0)));

        plans.Should().NotBeEmpty();
    }

    /// <summary>**同一宽度必须能出多刀** / One width must be cut several times.</summary>
    [Fact]
    public void OneProduct_ShouldBeCutSeveralTimes() {
        var generator = new DFSGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput(("A", 50.0)));

        plans.SelectMany(plan => plan.Slices)
            .Should().Contain(slice => slice.Amount == new UInt64(4UL));
    }

    /// <summary>**总宽不得超过幅宽** / The total width must not exceed the material.</summary>
    [Fact]
    public void EveryPlanTotalWidth_ShouldFitTheMaterial() {
        var generator = new DFSGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(
            MakeInput(("A", 50.0), ("B", 60.0), ("C", 70.0)));

        plans.Should().NotBeEmpty();
        foreach (CuttingPlan<Flt64> plan in plans) {
            double total = plan.Slices.Sum(slice => slice.Width.Value.ToDouble() * slice.Amount.ToULong());
            total.Should().BeLessThanOrEqualTo(200.0, "plan {0} exceeds the material width", plan.Id);
        }
    }

    /// <summary>**同一组合只能被枚举一次** / Each combination must be enumerated exactly once.</summary>
    [Fact]
    public void Candidates_ShouldNotExceedAcceptedPlans() {
        var generator = new DFSGenerator<Flt64>(GenerationConstraints<Flt64>.Unconstrained());

        CuttingPlanGenerationReport<CuttingPlan<Flt64>> report = generator.GenerateWithReport(
            MakeInput(("A", 50.0), ("B", 60.0), ("C", 70.0)));

        report.Statistics.AcceptedPlans.Should().BePositive();
        report.Statistics.GeneratedCandidates.Should().Be(
            report.Statistics.AcceptedPlans,
            "a repeated enumeration would be hidden by de-duplication");
    }

    /// <summary>**剪枝约束必须在扩展时生效** / A pruning constraint must take effect while expanding.</summary>
    /// <remarks>
    /// `MaxKnifeCountConstraint` 的 `IsPruning` 是 `true`。阈值设为 1 时，**DFS 在扩展阶段就拒绝第二刀**，
    /// 因此产出里不可能出现 2 刀及以上的方案。
    ///
    /// **这条判据只有在"扩展时检查"时才成立**：若实现改成只在叶节点检查（或完全不检查），
    /// 多刀方案就会漏出来。这正好锁住 DFS 与 FullSum 的语义差别。
    ///
    /// `MaxKnifeCountConstraint.IsPruning` is `true`. With a threshold of 1, **DFS rejects the second cut during expansion**, so plans with two or more cuts
    /// cannot appear.
    ///
    /// **This holds only if the check happens while expanding**: if the implementation checked only at leaves (or not at all) multi-cut plans would leak out.
    /// That is exactly what pins down the semantic difference from FullSum.
    /// </remarks>
    [Fact]
    public void PruningConstraint_ShouldLimitExpansion() {
        var constraints = new List<ICuttingPlanConstraint<Flt64>> {
            new MaxKnifeCountConstraint<Flt64>(new UInt64(1UL)),
        };
        var generator = new DFSGenerator<Flt64>(constraints);

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput(("A", 50.0)));

        plans.Should().NotBeEmpty();
        plans.SelectMany(plan => plan.Slices)
            .Should().OnlyContain(slice => slice.Amount.ToULong() <= 1UL);
    }

    /// <summary>**非剪枝约束必须在叶节点生效** / A non-pruning constraint must take effect at leaves.</summary>
    /// <remarks>
    /// `MinKnifeCountConstraint` 的 `IsPruning` 是 `false`——**它只在方案成型时才判定**，中间节点尚未满足是正常的。
    ///
    /// 阈值设为 3 时，只有至少 3 刀的方案能通过。**若叶节点跳过非剪枝约束，1 刀和 2 刀的方案就会漏出来**。
    ///
    /// **这条判据是整批唯一覆盖"叶节点约束"的**：其余测试用的都是剪枝约束，
    /// 所以删掉叶节点检查后它们**全部照样通过**。
    ///
    /// `MinKnifeCountConstraint.IsPruning` is `false` — **it is judged only once a plan is formed**, and being unsatisfied at an intermediate node is normal.
    ///
    /// With a threshold of 3 only plans with at least three cuts pass. **If leaves skipped non-pruning constraints, one- and two-cut plans would leak out.**
    ///
    /// **This is the only criterion covering "leaf constraints"**: the other tests all use pruning constraints, so deleting the leaf check leaves them **all
    /// passing**.
    /// </remarks>
    [Fact]
    public void LeafConstraint_ShouldFilterAtEmission() {
        var constraints = new List<ICuttingPlanConstraint<Flt64>> {
            new MinKnifeCountConstraint<Flt64>(new UInt64(3UL)),
        };
        var generator = new DFSGenerator<Flt64>(constraints);

        IReadOnlyList<CuttingPlan<Flt64>> plans = generator.Generate(MakeInput(("A", 50.0), ("B", 60.0)));

        plans.Should().NotBeEmpty();
        foreach (CuttingPlan<Flt64> plan in plans) {
            ulong cuts = 0UL;
            foreach (CuttingPlanSlice<Flt64> slice in plan.Slices) {
                cuts += slice.Amount.ToULong();
            }
            cuts.Should().BeGreaterThanOrEqualTo(3UL, "plan {0} has too few cuts for the leaf constraint", plan.Id);
        }
    }

    /// <summary>**同一条剪枝约束下 DFS 与 FullSum 的产出必须不同** / The same pruning constraint must make DFS and FullSum differ.</summary>
    /// <remarks>
    /// 这是本批**唯一能证明"两者真的是不同生成器"的判据**。
    ///
    /// 若两者的差别被抹平（例如都把 `applyPruningConstraints` 传成同一个值），
    /// 各自的行为测试**仍然全部通过**——只有差分测试会失败。
    ///
    /// This is **the only criterion proving the two really are different generators**. If the distinction were flattened (e.g. both passing the same
    /// `applyPruningConstraints`), each side's own behaviour tests would **still all pass** — only a differential test fails.
    /// </remarks>
    [Fact]
    public void SamePruningConstraint_ShouldMakeDfsAndFullSumDiffer() {
        var constraints = new List<ICuttingPlanConstraint<Flt64>> {
            new MaxKnifeCountConstraint<Flt64>(new UInt64(1UL)),
        };
        var dfs = new DFSGenerator<Flt64>(constraints);
        var fullSum = new FullSumGenerator<Flt64>(constraints);
        GenerationInput<Flt64> input = MakeInput(("A", 50.0));

        IReadOnlyList<CuttingPlan<Flt64>> dfsPlans = dfs.Generate(input);
        IReadOnlyList<CuttingPlan<Flt64>> fullSumPlans = fullSum.Generate(input);

        ulong dfsMaxCuts = dfsPlans.SelectMany(plan => plan.Slices).Select(slice => slice.Amount.ToULong()).DefaultIfEmpty(0UL).Max();
        ulong fullSumMaxCuts = fullSumPlans.SelectMany(plan => plan.Slices).Select(slice => slice.Amount.ToULong()).DefaultIfEmpty(0UL).Max();

        dfsMaxCuts.Should().Be(1UL, "DFS prunes expansion beyond one cut");
        fullSumMaxCuts.Should().BeGreaterThan(1UL, "FullSum does not check pruning constraints while expanding");
    }
}
