#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **`Loop` 展开契约：次数、累加器、规模三件** / The Loop contract: count, accumulators, size.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `lowerLoop`。**`If` 只做“选”，`Loop` 要做“重复并把上一轮的结果带进下一轮”，** **所以它多出三件 `If` 没有的东西。**
/// **这里只做契约判定，不做展开本身。**
///
/// Syncs `lowerLoop`. **If only chooses; Loop repeats and carries the previous round into the next, so it has three things If does not.** ** decides the contract,
/// not the unrolling.**
/// </remarks>
public class OnnxLoopExpansionTests
{
    /// <summary>**一个合法的循环通过并报出展开规模** / A legal loop passes and reports its unrolled size.</summary>
    [Fact]
    public void ALegalLoop_ShouldPassWithItsSize() {
        OnnxLoopExpansion.Decision decision = OnnxLoopExpansion.Decide(Anode(trip: 10, bodyNodes: 4, carried: 2));

        decision.CanUnroll.Should().BeTrue(decision.Failure);
        decision.UnrolledNodes.Should().Be(40, "ten rounds of four nodes each");
    }

    /// <summary>**次数或终止条件不是常量就不可展开** / A non-constant bound cannot be unrolled.</summary>
    [Fact]
    public void ANonConstantBound_ShouldBeRefused() {
        OnnxLoopExpansion.Decision decision = OnnxLoopExpansion.Decide(Anode(trip: 4, bodyNodes: 2, carried: 1) with { LimitsAreConstant = false });

        decision.CanUnroll.Should().BeFalse();
        decision.Failure.Should().Contain("常量");
    }

    /// <summary>**次数越界被拒绝、边界可用** / A trip count past the limit is refused, the boundary is usable.</summary>
    [Fact]
    public void TheTripCount_ShouldRespectTheLimit() {
        OnnxLoopExpansion.Decide(Anode(trip: 256, bodyNodes: 1, carried: 1)).CanUnroll.Should().BeTrue();
        OnnxLoopExpansion.Decide(Anode(trip: 257, bodyNodes: 1, carried: 1)).CanUnroll.Should().BeFalse();
        OnnxLoopExpansion.Decide(Anode(trip: -1, bodyNodes: 1, carried: 1)).CanUnroll.Should().BeFalse();
        OnnxLoopExpansion.Decide(Anode(trip: 0, bodyNodes: 5, carried: 1)).CanUnroll.Should().BeTrue("a zero-trip loop is degenerate but legal");
    }

    /// <summary>**携带值四个数目必须相等** / All four carried-value counts must agree.</summary>
    /// <remarks>
    /// **少一个就会在某一轮之后丢失上一轮的结果，而展开后的模型照常可解——只是少了一条递推。** '
    /// **这正是"累加器"这类错误的形状：不报错、能求解、答案是另一个东西。**
    ///
    /// **One missing drops a round's result, and the unrolled model still solves — it just lost a recurrence.** **That is the shape of an accumulator error: no complaint, it
    /// solves, and the answer is something else.**
    /// </remarks>
    [Fact]
    public void TheCarriedValues_ShouldMatchOnAllFourCounts() {
        OnnxLoopExpansion.Decide(Anode(trip: 2, bodyNodes: 1, carried: 1) with { BodyCarriedInputs = 2 }).CanUnroll
            .Should().BeFalse("the body receives more than the initial values provide");

        OnnxLoopExpansion.Decide(Anode(trip: 2, bodyNodes: 1, carried: 1) with { BodyCarriedOutputs = 2 }).CanUnroll
            .Should().BeFalse("the body produces more than it received");

        OnnxLoopExpansion.Decide(Anode(trip: 2, bodyNodes: 1, carried: 2) with { CarriedOutputs = 1 }).CanUnroll
            .Should().BeFalse("the final count disagrees with what the body produces");

        OnnxLoopExpansion.Decide(Anode(trip: 2, bodyNodes: 1, carried: 2)).CanUnroll
            .Should().BeTrue("matching counts are the legal case");
    }

    /// <summary>**规模守卫独立于次数上限** / The size guard is independent of the trip limit.</summary>
    /// <remarks>
    /// **一个 200 次、每轮 50 个节点的循环展开后是一万个节点，它通过了次数检查，而模型已经大到没人能看。**
    ///
    /// **A 200-iteration loop with 50 nodes per round unrolls to ten thousand nodes, passes the trip check, and is already too large to inspect.**
    /// </remarks>
    [Fact]
    public void TheSizeGuard_ShouldBeIndependentOfTheTripLimit() {
        OnnxLoopExpansion.Decide(Anode(trip: 200, bodyNodes: 50, carried: 1)).CanUnroll.Should().BeTrue(
            "exactly at the node budget is still allowed");

        OnnxLoopExpansion.Decision overdrawn = OnnxLoopExpansion.Decide(Anode(trip: 200, bodyNodes: 51, carried: 1));
        overdrawn.CanUnroll.Should().BeFalse("200 x 51 is past the budget even though 200 passes the trip check");
        overdrawn.Failure.Should().Contain("10000", "the message has to name the budget, or the reader cannot tell what to trim");
    }

    /// <summary>**缺循环体被拒绝** / A loop without a body is refused.</summary>
    [Fact]
    public void AMissingBody_ShouldBeRefused() =>
        OnnxLoopExpansion.Decide(Anode(trip: 2, bodyNodes: 1, carried: 1) with { Body = null }).CanUnroll.Should().BeFalse();

    /// <summary>**空循环体按一轮一个节点计** / An empty body counts as one node per round.</summary>
    /// <remarks>
    /// **空体按 0 个节点算会让"展开后规模"永远是 0，** **而 0 永远通过预算检查——** **于是一个空体的大循环会把规模守卫整条绕过去。**
    ///
    /// **Counting an empty body as zero nodes makes the unrolled size always zero, and zero always passes the budget — a large loop with an empty body would walk
    /// straight past the guard.**
    /// </remarks>
    [Fact]
    public void AnEmptyBody_ShouldCountAsOneNodePerRound() {
        OnnxLoopExpansion.Decision decision = OnnxLoopExpansion.Decide(Anode(trip: 256, bodyNodes: 0, carried: 1));

        decision.CanUnroll.Should().BeTrue("256 x 1 is still inside the budget");
        decision.UnrolledNodes.Should().Be(256, "an empty body is not free: counting it as zero would bypass the budget entirely");
    }

    private static OnnxLoopExpansion.LoopNode Anode(long trip, int bodyNodes, int carried) {
        var nodes = new OnnxNode[bodyNodes];
        for (int index = 0; index < bodyNodes; ++index) {
            nodes[index] = new OnnxNode("Identity", "n" + index, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<OnnxAttribute>());
        }
        var body = new OnnxGraph(
            "body",
            nodes,
            Array.Empty<OnnxTensor>(),
            Array.Empty<OnnxValueInfo>(),
            Array.Empty<OnnxValueInfo>(),
            Array.Empty<OnnxValueInfo>());
        return new OnnxLoopExpansion.LoopNode(
            trip,
            true,
            body,
            carried,
            carried,
            carried,
            carried);
    }
}