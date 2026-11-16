#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **`If` 展开：选分支，以及两条容易漏的约束** / Unrolling an If, and two constraints that slip easily.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `lowerIf`。**这是三类控制流里最浅的一类（不需要迭代变量，只需要"选"），** **但它带着一条容易被忽略的约束：'
/// **两个分支必须产出同样的东西——** **若输出个数不同，选中的是哪一个都会让下游按错的个数去接，而错位之后每个维度都跟着错。**
///
/// Syncs `lowerIf`. **The shallowest of the three (no iteration variable, only a choice), yet it carries an easily-missed constraint: both branches must produce the same
/// thing.** **If their output counts differ, whichever is chosen leaves the downstream wired to the wrong arity, and every dimension shifts after that.**
/// </remarks>
public class OnnxIfExpansionTests
{
    /// <summary>**条件为真时选真分支，为假时选假分支** / A true condition picks the true branch.</summary>
    [Theory]
    [InlineData(1.0, "then_out")]
    [InlineData(0.0, "else_out")]
    public void TheCondition_ShouldPickTheBranch(double condition, string expectedName) {
        OnnxIfExpansion.Expansion expansion = OnnxIfExpansion.Expand(new OnnxIfExpansion.IfNode(
            Condition: condition,
            ConditionIsConstant: true,
            Then: Branch("then_out"),
            Else: Branch("else_out"),
            Outputs: new[] { "y" }));

        expansion.IsOk.Should().BeTrue(expansion.Failure);
        expansion.Chosen!.Outputs[0].Name.Should().Be(expectedName);
    }

    /// <summary>**分支体按位置对齐到节点声明的输出名** / The body aligns to the node's declared output names by position.</summary>
    /// <remarks>
    /// **子图里的输出名与外部声明名通常不同：** **ONNX 按位置对齐，不按名字。** **若实现按名字去匹配，** **两个分支只要有一个名字对不上就会整条失败——**
    /// **而模型本身完全合法。**
    ///
    /// **A subgraph's output names usually differ from the outer declared ones: ONNX aligns by position, not by name. An implementation matching by name would fail the
    /// whole import as soon as one branch's names differ, while the model itself is perfectly legal.**
    /// </remarks>
    [Fact]
    public void TheOutputs_ShouldAlignByPosition() {
        OnnxIfExpansion.Expansion expansion = OnnxIfExpansion.Expand(new OnnxIfExpansion.IfNode(
            Condition: 1.0,
            ConditionIsConstant: true,
            Then: Branch("inner_a"),
            Else: Branch("inner_b"),
            Outputs: new[] { "outer_name" }));

        expansion.IsOk.Should().BeTrue(expansion.Failure);
        expansion.OutputNames.Should().Equal(new[] { "outer_name" }, "position is the contract, not the inner name");
    }

    /// <summary>**条件不是常量就不可展开** / A non-constant condition cannot be unrolled.</summary>
    [Fact]
    public void ANonConstantCondition_ShouldBeRefused() {
        OnnxIfExpansion.Expansion expansion = OnnxIfExpansion.Expand(new OnnxIfExpansion.IfNode(
            Condition: 1.0,
            ConditionIsConstant: false,
            Then: Branch("a"),
            Else: Branch("b"),
            Outputs: new[] { "y" }));

        expansion.IsOk.Should().BeFalse();
        expansion.Failure.Should().Contain("常量");
    }

    /// <summary>**两个分支输出个数不同被拒绝** / Different output counts between the branches are refused.</summary>
    /// <remarks>
    /// **这条是本批最值得写的判据：** **它不会在任何一次求解里暴露——** **两个分支各自都合法，选中的那个也会跑通，** **问题要到"下游按几个结果去接"时才显现。**
    ///
    /// **This is the criterion most worth writing here: it never shows up in a solve — both branches are legal on their own and the chosen one runs — the problem only
    /// appears where the downstream expects a certain number of results.**
    /// </remarks>
    [Fact]
    public void MismatchedBranchArity_ShouldBeRefused() {
        OnnxIfExpansion.Expansion expansion = OnnxIfExpansion.Expand(new OnnxIfExpansion.IfNode(
            Condition: 1.0,
            ConditionIsConstant: true,
            Then: Branch("a"),
            Else: Branch("b", "c"),
            Outputs: new[] { "y" }));

        expansion.IsOk.Should().BeFalse();
        expansion.Failure.Should().Contain("1").And.Contain("2", "both counts have to appear");
    }

    /// <summary>**分支输出数与节点声明不符被拒绝** / A branch whose count differs from the node's declaration is refused.</summary>
    [Fact]
    public void BranchVersusDeclaration_ShouldBeRefused() {
        OnnxIfExpansion.Expansion expansion = OnnxIfExpansion.Expand(new OnnxIfExpansion.IfNode(
            Condition: 1.0,
            ConditionIsConstant: true,
            Then: Branch("a"),
            Else: Branch("b"),
            Outputs: new[] { "y", "z" }));

        expansion.IsOk.Should().BeFalse("the node declares two outputs while each branch produces one");
    }

    /// <summary>**缺分支被拒绝** / A missing branch is refused.</summary>
    [Fact]
    public void AMissingBranch_ShouldBeRefused() {
        OnnxIfExpansion.Expand(new OnnxIfExpansion.IfNode(1.0, true, Branch("a"), null, new[] { "y" })).IsOk.Should().BeFalse();
        OnnxIfExpansion.Expand(new OnnxIfExpansion.IfNode(1.0, true, null, Branch("b"), new[] { "y" })).IsOk.Should().BeFalse();
    }

    /// <summary>**没有声明输出的 `If` 被拒绝** / An If declaring no output is refused.</summary>
    [Fact]
    public void AnEmptyOutputList_ShouldBeRefused() {
        OnnxIfExpansion.Expand(new OnnxIfExpansion.IfNode(1.0, true, Branch("a"), Branch("b"), Array.Empty<string>())).IsOk
            .Should().BeFalse();
    }

    /// <summary>**嵌套深度上限与 `Loop` 的展开上限共用** / The nesting limit shares the Loop unroll limit.</summary>
    [Fact]
    public void TheNestingDepth_ShouldRespectTheLimit() {
        OnnxIfExpansion.CheckDepth(OnnxSubgraphPolicy.MaxUnroll, out _).Should().BeTrue("the inclusive limit is usable");
        OnnxIfExpansion.CheckDepth(OnnxSubgraphPolicy.MaxUnroll + 1, out string? failure).Should().BeFalse();
        failure.Should().NotBeNull();
        OnnxIfExpansion.CheckDepth(-1, out _).Should().BeFalse();
    }

    private static OnnxGraph Branch(params string[] outputs) {
        var infos = new OnnxValueInfo[outputs.Length];
        for (int index = 0; index < outputs.Length; ++index) {
            infos[index] = new OnnxValueInfo(outputs[index], 1, Array.Empty<OnnxDimension>());
        }
        return new OnnxGraph(
            "branch",
            Array.Empty<OnnxNode>(),
            Array.Empty<OnnxTensor>(),
            Array.Empty<OnnxValueInfo>(),
            infos,
            Array.Empty<OnnxValueInfo>());
    }
}