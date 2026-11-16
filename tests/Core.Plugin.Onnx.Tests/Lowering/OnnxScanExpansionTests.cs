#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **`Scan` 契约：长度藏在一个输入的形状里** / The Scan contract: the length hides in an input's shape.
/// </summary>
/// <remarks>
/// `lowerScan`。**它与 `Loop` 的差别不是"重复"，而是"带过去什么"：** **`Scan` 带状态量，'
/// **且扫描长度来自某个输入的首维，不是节点属性——** **读成"元素总数"会让长度大一个量级，而展开后的模型只是"更慢"，不报错。**
///
/// Syncs `lowerScan`. **The difference from Loop is not repeating but what is carried: Scan carries states, and the scan length comes from an input's leading dimension
/// rather than a node attribute — reading it as the element count makes the length an order of magnitude too large, and the unrolled model is merely "slower".**
/// </remarks>
public class OnnxScanExpansionTests
{
    /// <summary>**合法扫描通过并报出长度与规模** / A legal scan passes with its length and size.</summary>
    [Fact]
    public void ALegalScan_ShouldPass() {
        OnnxScanExpansion.Decision decision = OnnxScanExpansion.Decide(Anode(length: 10, bodyNodes: 3, states: 2));

        decision.CanUnroll.Should().BeTrue(decision.Failure);
        decision.ScanLength.Should().Be(10);
        decision.UnrolledNodes.Should().Be(30, "ten rounds of three nodes each");
    }

    /// <summary>**长度取首维而不是元素总数** / The length is the leading dimension, not the element count.</summary>
    /// <remarks>
    /// **这条判据钉的是一个具体的误读：** **把 `[10, 4]` 读成"40 个元素"会让扫描次数变成 40，** **而模型照常展开、照常求解——**
    /// **只是每一轮的切片都错位，最后的结果与原本要算的东西无关。**
    ///
    /// **This pins one specific misreading: taking `[10, 4]` as "40 elements" makes the scan run 40 times, and the model still unrolls and still solves — every round's slice
    /// is simply misaligned and the answer has nothing to do with what was meant.**
    /// </remarks>
    [Fact]
    public void TheLength_ShouldComeFromTheLeadingDimension() {
        OnnxScanExpansion.Decision asRead = OnnxScanExpansion.Decide(Anode(length: 10, bodyNodes: 1, states: 1));
        asRead.ScanLength.Should().Be(10, "ten is the leading dimension; forty would be the element count of a [10, 4] tensor");

        OnnxScanExpansion.Decide(Anode(length: 10, bodyNodes: 1, states: 1) with { ScannedInputShape = 40 }).CanUnroll
            .Should().BeFalse("a per-round input claiming forty rounds cannot be sliced forty times from ten");
    }

    /// <summary>**被逐轮切片的输入首维必须与长度一致** / A sliced input's leading dimension must match the length.</summary>
    [Fact]
    public void ASlicedInput_ShouldAgreeWithTheLength() {
        OnnxScanExpansion.Decide(Anode(length: 6, bodyNodes: 1, states: 1) with { ScannedInputShape = 6 }).CanUnroll
            .Should().BeTrue("six rounds of six slices line up");
        OnnxScanExpansion.Decide(Anode(length: 6, bodyNodes: 1, states: 1) with { ScannedInputShape = 5 }).CanUnroll
            .Should().BeFalse("five slices cannot cover six rounds");
    }

    /// <summary>**状态量四个数目必须相等** / All four state counts must agree.</summary>
    /// <remarks>
    /// **与 `Loop` 同一条理由：** **少一个就会在某一轮之后丢失上一轮的状态，而展开后的模型照常可解，只是少了一条递推。**
    ///
    /// **Same reason as Loop: one missing drops a round's state, and the unrolled model still solves — it just lost a recurrence.**
    /// </remarks>
    [Fact]
    public void TheStates_ShouldMatchOnAllFourCounts() {
        OnnxScanExpansion.Decide(Anode(length: 2, bodyNodes: 1, states: 1) with { BodyStatesIn = 2 }).CanUnroll.Should().BeFalse();
        OnnxScanExpansion.Decide(Anode(length: 2, bodyNodes: 1, states: 1) with { BodyStatesOut = 2 }).CanUnroll.Should().BeFalse();
        OnnxScanExpansion.Decide(Anode(length: 2, bodyNodes: 1, states: 2) with { FinalStates = 1 }).CanUnroll.Should().BeFalse();
        OnnxScanExpansion.Decide(Anode(length: 2, bodyNodes: 1, states: 2)).CanUnroll.Should().BeTrue();
    }

    /// <summary>**长度为零或越界被拒绝** / A zero or oversized length is refused.</summary>
    /// <remarks>
    /// **零长度不是"合法但无用"：** **它意味着扫描输入的首维是空的，** **而那通常是上游某个形状算错了——** **放过它会让模型悄悄少掉整段扫描。**
    ///
    /// **A zero length is not "legal but useless": it means the scan input's leading dimension is empty, which usually means an upstream shape went wrong, and letting it
    /// through silently drops the whole scan.**
    /// </remarks>
    [Fact]
    public void TheLength_ShouldBeBounded() {
        OnnxScanExpansion.Decide(Anode(length: 0, bodyNodes: 1, states: 1)).CanUnroll.Should().BeFalse();
        OnnxScanExpansion.Decide(Anode(length: -1, bodyNodes: 1, states: 1)).CanUnroll.Should().BeFalse();
        OnnxScanExpansion.Decide(Anode(length: OnnxSubgraphPolicy.MaxUnroll, bodyNodes: 1, states: 1)).CanUnroll.Should().BeTrue();
        OnnxScanExpansion.Decide(Anode(length: OnnxSubgraphPolicy.MaxUnroll + 1, bodyNodes: 1, states: 1)).CanUnroll.Should().BeFalse();
    }

    /// <summary>**规模预算与 `Loop` 共用** / The node budget is shared with Loop.</summary>
    [Fact]
    public void TheNodeBudget_ShouldBeSharedWithLoop() {
        OnnxScanExpansion.Decide(Anode(length: 200, bodyNodes: 50, states: 1)).CanUnroll.Should().BeTrue("exactly at the budget");
        OnnxScanExpansion.Decision overdrawn = OnnxScanExpansion.Decide(Anode(length: 200, bodyNodes: 51, states: 1));
        overdrawn.CanUnroll.Should().BeFalse();
        overdrawn.Failure.Should().Contain("10000");
    }

    /// <summary>**缺循环体被拒绝** / A scan without a body is refused.</summary>
    [Fact]
    public void AMissingBody_ShouldBeRefused() =>
        OnnxScanExpansion.Decide(Anode(length: 2, bodyNodes: 1, states: 1) with { Body = null }).CanUnroll.Should().BeFalse();

    private static OnnxScanExpansion.ScanNode Anode(long length, int bodyNodes, int states) {
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
        return new OnnxScanExpansion.ScanNode(body, length, length, states, states, states, states);
    }
}