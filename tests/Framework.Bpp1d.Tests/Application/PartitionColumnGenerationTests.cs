// 分区列生成预算判据 / Criteria for the partition column generation budget.

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Application;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Application;

/// <summary>
/// **分区列生成预算的四条规则** / The four budget rules of partition column generation.
/// </summary>
/// <remarks>
/// 对应基线 `solve` 开头的那一条复合判断。**判据不需要求解器**：它是一个纯函数。
///
/// the single compound guard at the top of the: . **No criterion needs a solver**: it is a pure function.
/// </remarks>
public class PartitionColumnGenerationTests {
    /// <summary>**列数上限必须能容纳每个分区一列** / The column budget must fit one column per partition.</summary>
    /// <remarks>
    /// 上限小于分区数时，种子阶段算出的可用列数是负数，**每个分区连一列都放不进去**；
    /// 而失败会发生在更靠后的主问题构建里，报"主问题不可行"——**与真正的原因无关**。
    ///
    /// With a budget below the partition count the seed phase computes a negative number of available columns and **not one column per partition fits**; the failure
    /// then surfaces later in master construction as "infeasible master" — **unrelated to the real cause**.
    /// </remarks>
    [Fact]
    public void IsValidFor_ShouldRequireAColumnBudgetAtLeastThePartitionCount() {
        var config = new PartitionColumnGenerationConfig(ColumnLimit: 2);

        config.IsValidFor(2).Should().BeTrue("two partitions and a budget of two");
        config.IsValidFor(3).Should().BeFalse("three partitions cannot share a budget of two");
    }

    /// <summary>**迭代上限、容差有限、容差为正** / Iteration limit, finite tolerance, positive tolerance.</summary>
    [Fact]
    public void IsValidFor_ShouldRequireAIterationAndAPositiveFiniteTolerance() {
        new PartitionColumnGenerationConfig(IterationLimit: 0).IsValidFor(2).Should().BeFalse();
        new PartitionColumnGenerationConfig(IterationLimit: -1).IsValidFor(2).Should().BeFalse();
        new PartitionColumnGenerationConfig(Tolerance: 0.0).IsValidFor(2).Should().BeFalse();
        new PartitionColumnGenerationConfig(Tolerance: -1e-6).IsValidFor(2).Should().BeFalse();
        new PartitionColumnGenerationConfig(Tolerance: double.NaN).IsValidFor(2).Should().BeFalse(
            "a NaN tolerance makes every comparison false, so pricing would never accept or reject anything");
        new PartitionColumnGenerationConfig(Tolerance: double.PositiveInfinity).IsValidFor(2).Should().BeFalse();
        new PartitionColumnGenerationConfig().IsValidFor(2).Should().BeTrue("the defaults are valid");
    }

    /// <summary>**轨迹记录是值语义** / The trace record has value semantics.</summary>
    /// <remarks>
    /// 同一轮的轨迹条目按值相等，**不同目标值则不相等**——否则"收敛轨迹"无法从外部核对。
    ///
    /// Two trace entries of the same round compare equal by value, **while different objectives do not** — otherwise a "convergence trace" cannot be checked from
    /// the outside.
    /// </remarks>
    [Fact]
    public void Iteration_ShouldCompareByValue() {
        var first = new PartitionColumnGenerationIteration(
            PhaseOne: true,
            Objective: new Fuookami.Ospf.Math.Algebra.Number.Flt64(1.0),
            AddedColumns: 2,
            PricingProven: false);
        var same = new PartitionColumnGenerationIteration(
            PhaseOne: true,
            Objective: new Fuookami.Ospf.Math.Algebra.Number.Flt64(1.0),
            AddedColumns: 2,
            PricingProven: false);
        var different = new PartitionColumnGenerationIteration(
            PhaseOne: true,
            Objective: new Fuookami.Ospf.Math.Algebra.Number.Flt64(2.0),
            AddedColumns: 2,
            PricingProven: false);

        first.Should().Be(same);
        first.Should().NotBe(different);
    }
}
