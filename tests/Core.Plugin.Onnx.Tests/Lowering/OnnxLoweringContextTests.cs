#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **作用域：读得出去，写不进来** / Scopes read outward and write inward.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxLoweringContext` 最小形态，也是子图展开的前提。**判据钉的是"泄漏"这一类错误：**
/// **一个大字典会让子图的中间结果跑到外层，而症状是"外层某处引用到了不该看见的名字"，在几百个节点的图里几乎无法定位。**
///
/// Syncs the minimal `OnnxLoweringContext`, the prerequisite for unrolling subgraphs. **The criteria target leakage: a single dictionary lets a subgraph's intermediates
/// escape outward, and the symptom is "somewhere outside references a name it should not see" — nearly impossible to locate in a graph of a few hundred nodes.**
/// </remarks>
public class OnnxLoweringContextTests
{
    /// <summary>**子图看得见外层** / A subgraph sees the enclosing scope.</summary>
    [Fact]
    public void ANestedScope_ShouldSeeTheOuterOne() {
        var context = new OnnxLoweringContext();
        context.TryBind("outer", Expression("a"), new[] { 3 }, out string? bindFailure).Should().BeTrue(bindFailure);

        context.PushScope();
        context.TryGet("outer", out OnnxLoweringContext.Binding? binding).Should().BeTrue(
            "a subgraph body references the enclosing tensors — that is how If and Loop bodies work");
        binding!.Shape.Should().Equal(new[] { 3 });
    }

    /// <summary>**子图的中间结果不会泄漏到外层** / A subgraph's intermediate does not escape.</summary>
    /// <remarks>
    /// **这条是这一批存在的理由：** **若子图写到外层，** **外层之后每次按名字查找都可能命中一个本不存在的张量，** **而那种错在数值上表现为"某一项莫名多出来"。**
    ///
    /// **This is why the batch exists: if a subgraph wrote outward, every later lookup outside could hit a tensor that does not exist there, and that shows up numerically as
    /// "one term appeared out of nowhere".**
    /// </remarks>
    [Fact]
    public void ASubgraphIntermediate_ShouldNotEscape() {
        var context = new OnnxLoweringContext();

        context.PushScope();
        context.TryBind("inner", Expression("b"), new[] { 2 }, out _).Should().BeTrue();
        context.TryPopScope(out string? popFailure).Should().BeTrue(popFailure);

        context.TryGet("inner", out _).Should().BeFalse("the intermediate belonged to the subgraph and left with it");
        context.Depth.Should().Be(1);
    }

    /// <summary>**内层遮蔽外层，退出后外层恢复** / An inner binding shadows the outer one and gives way on exit.</summary>
    /// <remarks>
    /// **遮蔽是子图的正常语义：** **`Loop` 体里常有一个与外部同名的迭代变量。** **若反过来"外层优先"，** **子图里用的会是外层那个张量——**
    /// **而模型算的是另一个东西，却仍然能求解。**
    ///
    /// **Shadowing is normal subgraph semantics: a Loop body usually has an iteration variable sharing a name with something outside. With "outer wins" instead, the
    /// subgraph would use the outer tensor and compute something else while still solving.**
    /// </remarks>
    [Fact]
    public void AnInnerBinding_ShouldShadowAndThenGiveWay() {
        var context = new OnnxLoweringContext();
        context.TryBind("v", Expression("outer_v"), new[] { 5 }, out _);

        context.PushScope();
        context.TryBind("v", Expression("inner_v"), new[] { 1 }, out string? bindFailure).Should().BeTrue(
            bindFailure is null ? "a shadowing name is legal" : bindFailure);
        context.TryGet("v", out OnnxLoweringContext.Binding? shadowed);
        shadowed!.Shape.Should().Equal(new[] { 1 }, "the innermost definition is the one in force");

        context.TryPopScope(out _);
        context.TryGet("v", out OnnxLoweringContext.Binding? restored);
        restored!.Shape.Should().Equal(new[] { 5 }, "and the outer definition comes back untouched");
    }

    /// <summary>**同一作用域内重名被拒绝** / A duplicate name in one scope is refused.</summary>
    /// <remarks>
    /// **ONNX 的输出名在同一层唯一：** **重名意味着模型非法或我们读错了字段，** **而静默覆盖会把先写的那个节点的结果悄悄换掉。**
    ///
    /// **ONNX output names are unique per level: a duplicate means an illegal model or a misread field, and silently overwriting swaps out the earlier node's result.**
    /// </remarks>
    [Fact]
    public void ADuplicateInOneScope_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        context.TryBind("v", Expression("first"), new[] { 1 }, out _).Should().BeTrue();

        context.TryBind("v", Expression("second"), new[] { 1 }, out string? failure).Should().BeFalse();
        failure.Should().Contain("v");

        context.TryGet("v", out OnnxLoweringContext.Binding? kept);
        kept!.Shape.Should().Equal(new[] { 1 }, "the first binding survives the refused overwrite");
    }

    /// <summary>**最外层不可弹出** / The root scope cannot be popped.</summary>
    [Fact]
    public void TheRootScope_ShouldNotBePoppable() {
        var context = new OnnxLoweringContext();

        context.TryPopScope(out string? failure).Should().BeFalse();
        failure.Should().NotBeNull("popping the root would empty the table and every later lookup would fail far from the cause");
        context.Depth.Should().Be(1);
    }

    /// <summary>**空名字被拒绝** / An empty name is refused.</summary>
    [Fact]
    public void AnEmptyName_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        context.TryBind("", Expression("x"), new[] { 1 }, out string? failure).Should().BeFalse();
        failure.Should().NotBeNull();
    }

    /// <summary>**找不到的名字返回 false，不返回空绑定** / A missing name returns false rather than an empty binding.</summary>
    [Fact]
    public void AMissingName_ShouldReturnFalse() {
        var context = new OnnxLoweringContext();
        context.TryGet("ghost", out _).Should().BeFalse();
    }

    /// <summary>**就地更新：同名覆盖后取到的是新值** / An in-place update overwrites and the new value wins.</summary>
    /// <remarks>
    /// **ONNX 允许节点输出与输入同名（就地更新），那个名字在此之后的含义就是新值。** **`TryBind` 的严格性是为"声明"服务的，'
    /// **而"这个名字现在指新值"是另一件事。**
    ///
    /// **ONNX allows a node output to share an input's name, and the name means the new value afterwards. TryBind's strictness serves "declaring a name"; "this name now
    /// means the new value" is a different act.**
    /// </remarks>
    [Fact]
    public void AnInPlaceUpdate_ShouldOverwrite() {
        var context = new OnnxLoweringContext();
        context.TryBind("acc", Expression("old"), new[] { 2 }, out _).Should().BeTrue();

        context.TryRebind("acc", Expression("new"), new[] { 3 }, out string? failure).Should().BeTrue(failure);
        context.TryGet("acc", out OnnxLoweringContext.Binding? binding);
        binding!.Expression.Monomials[0].Symbol.Name.Should().Be("new", "the name now means the new value");
        binding.Shape.Should().Equal(new[] { 3 }, "and the new shape comes with it");
    }

    /// <summary>**覆盖只在当前作用域：外层同名不受影响** / The overwrite is local to the current scope.</summary>
    /// <remarks>
    /// **这一条是就地更新与"泄漏"的分界线：** **体里覆盖同名若写到外层，** **外层之后每次按名字查找都会命中体里的值——'
    /// **而那种错在数值上表现为"某一项突然变成了循环最后那次的值"。**
    ///
    /// **This is the line between an in-place update and a leak: were the overwrite written outward, every later lookup outside would hit the body's value, and that shows up
    /// numerically as "one term suddenly became the loop's last value".**
    /// </remarks>
    [Fact]
    public void AnInPlaceUpdate_ShouldNotReachTheOuterScope() {
        var context = new OnnxLoweringContext();
        context.TryBind("v", Expression("outer"), new[] { 5 }, out _);

        context.PushScope();
        context.TryRebind("v", Expression("inner"), new[] { 1 }, out _).Should().BeTrue();
        context.TryGet("v", out OnnxLoweringContext.Binding? inside);
        inside!.Expression.Monomials[0].Symbol.Name.Should().Be("inner");

        context.TryPopScope(out _);
        context.TryGet("v", out OnnxLoweringContext.Binding? outside);
        outside!.Expression.Monomials[0].Symbol.Name.Should().Be("outer", "the enclosing binding is untouched");
        outside.Shape.Should().Equal(new[] { 5 });
    }

    /// <summary>**覆盖也拒绝空名字** / An empty name is refused here as well.</summary>
    [Fact]
    public void AnInPlaceUpdate_WithNoName_ShouldBeRefused() {
        new OnnxLoweringContext().TryRebind("", Expression("x"), new[] { 1 }, out string? failure).Should().BeFalse();
        failure.Should().NotBeNull();
    }

    /// <summary>**覆盖一个从未存在的名字等于声明它** / Rebinding an unseen name is just declaring it.</summary>
    [Fact]
    public void RebindingAnUnseenName_ShouldBindIt() {
        var context = new OnnxLoweringContext();
        context.TryRebind("fresh", Expression("x"), new[] { 1 }, out string? failure).Should().BeTrue(failure);
        context.TryGet("fresh", out _).Should().BeTrue();
    }

    private static LinearPolynomial<Flt64> Expression(string name) {
        var variable = new RealVar(name);
        return new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
            Flt64.Zero);
    }
}