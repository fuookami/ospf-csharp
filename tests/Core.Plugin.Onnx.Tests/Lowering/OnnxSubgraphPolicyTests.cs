#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **子图：能不能展开，是"能不能导入"的分界线** / Whether a subgraph unrolls is the line between importable and not.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxSubgraph` 的可展开性判定。**展开本身依赖降阶上下文（还没移植），**
/// **而"一个依赖数据的 `Loop` 若被放过会发生什么"可以独立说清：** **它会按猜出来的次数展开，解看着正常，只是优化的是另一个网络。**
///
/// Syncs the unrollability decision of `OnnxSubgraph`. **The unrolling itself needs the lowering context, not ported yet, while what happens when a data-dependent Loop
/// slips through can be said on its own: it unrolls some guessed number of times, the answer looks fine, and it optimises a different network.**
/// </remarks>
public class OnnxSubgraphPolicyTests
{
    /// <summary>**非控制流算子被拒绝** / A non-control-flow operator is refused.</summary>
    [Fact]
    public void ANonControlFlowOperator_ShouldBeRefused() {
        OnnxSubgraphPolicy.Decide("Add", 1, constant: true).CanUnroll.Should().BeFalse();
        OnnxSubgraphPolicy.Decide("If", 1, constant: true).CanUnroll.Should().BeTrue();
        OnnxSubgraphPolicy.Decide("Loop", 3, constant: true).CanUnroll.Should().BeTrue();
        OnnxSubgraphPolicy.Decide("Scan", 3, constant: true).CanUnroll.Should().BeTrue();
    }

    /// <summary>**条件不是常量就不可展开** / A non-constant condition cannot be unrolled.</summary>
    /// <remarks>
    /// **这条是整套实现的地基：** **导入期要做完所有分支与循环，** **而依赖数据的条件在导入期根本不存在答案。**
    ///
    /// **This is the foundation of the whole approach: import time resolves every branch and loop, and a data-dependent condition has no answer at import time.**
    /// </remarks>
    [Fact]
    public void ANonConstantCondition_ShouldBeRefused() {
        OnnxSubgraphPolicy.Decision decision = OnnxSubgraphPolicy.Decide("If", 1, constant: false);

        decision.CanUnroll.Should().BeFalse();
        decision.Failure.Should().Contain("常量");
    }

    /// <summary>**`If` 缺一个分支被拒绝** / An If with a missing branch is refused.</summary>
    [Fact]
    public void AnIncompleteIf_ShouldBeRefused() {
        OnnxSubgraphPolicy.Decide("If", 1, constant: true, hasBothBranches: false).CanUnroll.Should().BeFalse();
        OnnxSubgraphPolicy.Decide("Loop", 1, constant: true, hasBothBranches: false).CanUnroll.Should().BeTrue(
            "the branch requirement belongs to If alone");
    }

    /// <summary>**迭代次数越界被拒绝，边界值可用** / A trip count past the limit is refused, the boundary is usable.</summary>
    /// <remarks>
    /// **上限守的是规模不是性能：** **一万次展开会产出没人能转储、没人能诊断的模型，而在导入阶段它"成功"了。**
    ///
    /// **The limit guards size: unrolling ten thousand times yields a model nobody can dump or diagnose, and it "succeeds" at import time.**
    /// </remarks>
    [Fact]
    public void TheTripCount_ShouldRespectTheLimit() {
        OnnxSubgraphPolicy.Decide("Loop", 256, constant: true).CanUnroll.Should().BeTrue("the inclusive limit is usable");
        OnnxSubgraphPolicy.Decide("Loop", 257, constant: true).CanUnroll.Should().BeFalse();
        OnnxSubgraphPolicy.Decide("Loop", 0, constant: true).CanUnroll.Should().BeTrue("a zero-trip loop is degenerate but legal");
        OnnxSubgraphPolicy.Decide("Loop", -1, constant: true).CanUnroll.Should().BeFalse("a negative trip count is not a count");
    }

    /// <summary>**嵌套展开按乘积判定** / Nested unrolling is judged by the product.</summary>
    /// <remarks>
    /// **每层都在上限内、乘起来却超出，是嵌套循环最容易漏的地方：** **256 × 256 已经是六万五千个节点，** **而单看每层都"合规"。**
    ///
    /// **Every level within the limit while the product is not is what nested loops most easily slip past: 256 × 256 is already sixty-five thousand nodes while each level
    /// looks compliant on its own.**
    /// </remarks>
    [Fact]
    public void NestedUnrolling_ShouldBeJudgedByTheProduct() {
        OnnxSubgraphPolicy.DecideNested(new long[] { 4, 4 }).TripCount.Should().Be(16);
        OnnxSubgraphPolicy.DecideNested(new long[] { 4, 4 }).CanUnroll.Should().BeTrue();

        OnnxSubgraphPolicy.DecideNested(new long[] { 256, 256 }).CanUnroll.Should().BeFalse(
            "each level is legal and the product is sixty-five thousand");
        OnnxSubgraphPolicy.DecideNested(new long[] { 16, 16 }).CanUnroll.Should().BeTrue(
            "16 x 16 = 256 is exactly the limit");
        OnnxSubgraphPolicy.DecideNested(new long[] { 17, 16 }).CanUnroll.Should().BeFalse();

        OnnxSubgraphPolicy.DecideNested(System.Array.Empty<long>()).TripCount.Should().Be(1, "no nesting means one pass");
    }

    /// <summary>**嵌套里出现负数被拒绝** / A negative level inside the nesting is refused.</summary>
    [Fact]
    public void ANegativeNestedLevel_ShouldBeRefused() =>
        OnnxSubgraphPolicy.DecideNested(new long[] { 4, -1 }).CanUnroll.Should().BeFalse();
}