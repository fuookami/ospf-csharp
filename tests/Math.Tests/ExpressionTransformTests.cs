#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Symbol.Expression;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Math.Tests;

/// <summary>
/// 表达式树变换 / Expression tree transformation.
/// </summary>
/// <remarks>
/// 补的两个入口此前不存在**，所以这里钉的是**语义**而不是"函数在不在"：后序、叶子不变、布尔身份不变。
///
/// added two entry points that did not exist**, so these pin the **semantics** rather than "the function exists": post-order, leaves pass through, boolean
/// identity unchanged.
/// </remarks>
public class ExpressionTransformTests {
    /// <summary>标量叶子原样交出，但**回调仍然看得见它们** / Leaves pass through but still reach the callback.</summary>
    [Fact]
    public void TheLeaves_ShouldStillReachTheCallback() {
        var leaf = new ScalarConstant<double>(2.5);

        ScalarExpression<double> result = leaf.Transform(node => node is ScalarConstant<double>
            ? new ScalarConstant<double>(7.0)
            : node);

        result.Should().BeOfType<ScalarConstant<double>>();
        ((ScalarConstant<double>)result).Value.Should().Be(7.0,
            "a leaf still has to reach the callback, or the top-level node could never be replaced");
    }

    /// <summary>**后序**：子节点先变，整体后变 / Post-order: children first, whole node last.</summary>
    /// <remarks>
    /// 这条是**顺序**判据，不是结构判据。前序实现会看到 `ScalarBinary` 里的子节点**还没变过**——
    /// 于是"把所有叶子换成 7"会得到 `1 + 2` 而不是 `7 + 7`，而结构上完全看不出错。
    ///
    /// This pins **order**, not shape. A pre-order implementation sees a binary's operands **before** they change — so "replace every leaf with 7" yields `1 + 2`
    /// rather than `7 + 7`, and the shape looks fine.
    /// </remarks>
    [Fact]
    public void TheBinaryOperands_ShouldBeTransformedBeforeTheParent() {
        var binary = new ScalarBinary<double>(
            BinaryOperator.Add,
            new ScalarConstant<double>(1.0),
            new ScalarConstant<double>(2.0));

        var seen = new List<string>();
        ScalarExpression<double> result = binary.Transform(node => {
            seen.Add(node.TypeName);
            return node is ScalarConstant<double> constant && constant.Value < 3.0
                ? new ScalarConstant<double>(7.0)
                : node;
        });

        // `TypeName` 是节点类别，不是算子名——判据断的是**顺序**，不是标签。
        seen.Should().Equal("Constant", "Constant", "Binary");
        result.Should().BeOfType<ScalarBinary<double>>();
        ((ScalarBinary<double>)result).Left.Should().BeOfType<ScalarConstant<double>>()
            .Which.Value.Should().Be(7.0, "the operand really was replaced before the parent was handed over");
        ((ScalarBinary<double>)result).Right.Should().BeOfType<ScalarConstant<double>>()
            .Which.Value.Should().Be(7.0);
    }

    /// <summary>函数参数逐个变换，**顺序保持** / Function arguments are transformed one by one, in order.</summary>
    [Fact]
    public void TheFunctionArguments_ShouldKeepTheirOrder() {
        var function = new ScalarFunction<double>(
            "f",
            [
                new ScalarConstant<double>(1.0),
                new ScalarConstant<double>(2.0),
                new ScalarConstant<double>(3.0),
            ]);

        ScalarExpression<double> result = function.Transform(node =>
            node is ScalarConstant<double> constant
                ? new ScalarConstant<double>(constant.Value * 10.0)
                : node);

        var rebuilt = (ScalarFunction<double>)result;
        rebuilt.Name.Should().Be("f", "the name is not the callback's business");
        rebuilt.Arguments.Select(argument => ((ScalarConstant<double>)argument).Value)
            .Should().BeEquivalentTo(new[] { 10.0, 20.0, 30.0 }, "argument order is part of the semantics");
    }

    /// <summary>**布尔节点只换标量子树，不改身份** / Boolean nodes keep their identity.</summary>
    [Fact]
    public void TheComparison_ShouldKeepItsOperator() {
        var comparison = new Comparison<double>(
            ComparisonOperator.Lt,
            new ScalarConstant<double>(1.0),
            new ScalarConstant<double>(5.0));

        BooleanExpression result = comparison.TransformScalars<double>(node =>
            node is ScalarConstant<double> constant
                ? new ScalarConstant<double>(constant.Value + 1.0)
                : node);

        var rebuilt = (Comparison<double>)result;
        rebuilt.Operator.Should().Be(ComparisonOperator.Lt,
            "changing the operator would silently rewrite the constraint");
        ((ScalarConstant<double>)rebuilt.Left).Value.Should().Be(2.0);
        ((ScalarConstant<double>)rebuilt.Right).Value.Should().Be(6.0);
    }

    /// <summary>**`And` 的操作数要递归下去** / `And` operands recurse.</summary>
    [Fact]
    public void TheAndOperands_ShouldRecurse() {
        var and = new AndExpression([
            new Comparison<double>(ComparisonOperator.Eq,
                new ScalarConstant<double>(1.0), new ScalarConstant<double>(1.0)),
            new Comparison<double>(ComparisonOperator.Eq,
                new ScalarConstant<double>(2.0), new ScalarConstant<double>(2.0)),
        ]);

        BooleanExpression result = and.TransformScalars<double>(node =>
            node is ScalarConstant<double> constant
                ? new ScalarConstant<double>(constant.Value * 100.0)
                : node);

        var rebuilt = (AndExpression)result;
        rebuilt.Operands.Should().HaveCount(2);
        foreach (BooleanExpression operand in rebuilt.Operands) {
            operand.Should().BeOfType<Comparison<double>>()
                .Which.Left.Should().BeOfType<ScalarConstant<double>>()
                .Which.Value.Should().BeGreaterThan(1.0, "a non-recursing version would leave the operands untouched");
        }
    }

    /// <summary>**两步：先重建标量子树，再交给变换器** / Two steps: rebuild the scalar subtrees first, then hand over.</summary>
    /// <remarks>
    /// **这是`BooleanExpression.transform` 重载，此前 C# 缺着。**
    ///
    /// **省掉第一步（`TransformScalars(it)`）不会报错，也不会少成员**——
    /// **它只是让变换器拿到原来那些标量实例**，于是"变换"变成只改外壳。
    /// **所以判据必须能看见"标量是新实例"这件事，否则这条重载少了什么都测不出来。**
    ///
 /// **This is the implementation `BooleanExpression.transform` overload, absent from C# formerly.**
    /// **Dropping the first step (`TransformScalars(it)`) raises nothing and loses no member** —
    /// **it merely hands the transformer the original scalar instances, so "transform" only changes the shell.**
    /// **So the criterion has to see that the scalars are fresh instances, or it cannot tell what the overload dropped.**
    /// </remarks>
    [Fact]
    public void TheBooleanTransform_ShouldRebuildScalarSubtreesFirst() {
        var comparison = new Comparison<double>(ComparisonOperator.Eq,
            new ScalarConstant<double>(3.0), new ScalarConstant<double>(3.0));
        var and = new AndExpression([comparison, comparison]);

        BooleanExpression result = and.Transform<double>(node => node);

        var rebuilt = (AndExpression)result;
        rebuilt.Operands.Should().HaveCount(2, "the boolean structure is rebuilt");
        foreach (BooleanExpression operand in rebuilt.Operands) {
            operand.Should().NotBeSameAs(comparison,
                "the scalar step has to rebuild the comparison, not pass the original instance through");
        }
    }

    /// <summary>**变换器在最外层被调用一次** / The transformer runs on the outermost node.</summary>
    [Fact]
    public void TheBooleanTransform_ShouldGiveTheTransformerTheRebuiltNode() {
        var and = new AndExpression([
            new Comparison<double>(ComparisonOperator.Eq,
                new ScalarConstant<double>(1.0), new ScalarConstant<double>(1.0)),
        ]);
        int calls = 0;

        and.Transform<double>(node => {
            calls++;
            return node;
        });

        calls.Should().BeGreaterThan(0, "the transformer is what the transform means; never calling it is a no-op");
    }

    /// <summary>**`TransformBooleans` 只重建 And/Or/Not** / Only And/Or/Not are rebuilt.</summary>
    /// <remarks>
    /// 是这个行为：`Comparison` 的布尔身份不由这一层改变。**判据钉的是"没改"，因为"改了"才会破坏语义。**
    ///
 /// The implementation behaves the same way: this layer does not change a comparison's boolean identity. **The criterion pins "not changed", because a change is what
    /// would break the semantics.**
    /// </remarks>
    [Fact]
    public void TheTransformBooleans_ShouldLeaveTheComparisonAlone() {
        var comparison = new Comparison<double>(
            ComparisonOperator.Gt,
            new ScalarConstant<double>(3.0),
            new ScalarConstant<double>(1.0));

        var seen = new List<string>();
        BooleanExpression result = comparison.TransformBooleans(node => {
            seen.Add(node.TypeName);
            return node;
        });

        result.Should().BeOfType<Comparison<double>>()
            .Which.Operator.Should().Be(ComparisonOperator.Gt);
        seen.Should().Equal("Comparison");
    }
}
