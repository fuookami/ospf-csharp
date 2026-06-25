#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol.Expression;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Expression.Tests;
/// <summary>
/// 标量表达式测试 / Scalar Expression Tests.
/// </summary>
public class ScalarExpressionTests {
    [Fact]
    public void Constant_shouldKeepValue() {
        var expr = new ScalarConstant<int>(42);
        Assert.Equal(42, expr.Value);
        Assert.Equal("Constant", expr.TypeName);
        Assert.True(expr.IsConstant());
        Assert.False(expr.ContainsReference());
        Assert.Empty(expr.Children);
    }

    [Fact]
    public void Reference_shouldHoldPath() {
        var expr = new ScalarReference<int>(PropertyPath.Parse("age"));
        Assert.Equal("age", expr.Path.Value);
        Assert.Equal("Reference", expr.TypeName);
        Assert.False(expr.IsConstant());
        Assert.True(expr.ContainsReference());
        Assert.Single(expr.CollectReferences());
    }

    [Fact]
    public void Unary_shouldNegate() {
        var inner = new ScalarConstant<int>(5);
        var expr = new ScalarUnary<int>(UnaryOperator.Negate, inner);
        Assert.Equal("Unary", expr.TypeName);
        Assert.True(expr.IsConstant());
        Assert.Single(expr.Children);
        Assert.Equal("-(Constant(5))", expr.ToString());
    }

    [Fact]
    public void Binary_shouldAdd() {
        var left = new ScalarConstant<int>(1);
        var right = new ScalarConstant<int>(2);
        var expr = new ScalarBinary<int>(BinaryOperator.Add, left, right);
        Assert.Equal("Binary", expr.TypeName);
        Assert.True(expr.IsConstant());
        Assert.Equal(2, expr.Children.Count);
        Assert.Equal("(Constant(1) + Constant(2))", expr.ToString());
    }

    [Fact]
    public void Function_shouldHoldNameAndArgs() {
        var arg = new ScalarConstant<double>(3.14);
        var expr = new ScalarFunction<double>("abs", new[] { arg });
        Assert.Equal("Function", expr.TypeName);
        Assert.True(expr.IsConstant());
        Assert.Equal("abs(Constant(3.14))", expr.ToString());
    }

    [Fact]
    public void MixedExpression_shouldDetectReferences() {
        var left = (ScalarExpression<int>)new ScalarReference<int>(PropertyPath.Parse("x"));
        var right = (ScalarExpression<int>)new ScalarConstant<int>(10);
        var expr = new ScalarBinary<int>(BinaryOperator.Add, left, right);
        Assert.False(expr.IsConstant());
        Assert.True(expr.ContainsReference());
        Assert.Single(expr.CollectReferences());
    }

    [Fact]
    public void FactoryMethods_shouldBuildAllNodeTypes() {
        ScalarExpression<int> c = ScalarExpressionFactory.Constant(42);
        Assert.IsType<ScalarConstant<int>>(c);

        ScalarExpression<int> r = ScalarExpressionFactory.Reference<int>("age");
        Assert.IsType<ScalarReference<int>>(r);

        ScalarExpression<int> u = ScalarExpressionFactory.Unary(UnaryOperator.Negate, c);
        Assert.IsType<ScalarUnary<int>>(u);

        ScalarExpression<int> b = ScalarExpressionFactory.Binary(BinaryOperator.Add, c, c);
        Assert.IsType<ScalarBinary<int>>(b);

        ScalarExpression<int> f = ScalarExpressionFactory.Function<int>("abs", new[] { c });
        Assert.IsType<ScalarFunction<int>>(f);
    }
}

/// <summary>
/// 布尔表达式测试 / Boolean Expression Tests.
/// </summary>
public class BooleanExpressionTests {
    [Fact]
    public void Comparison_shouldHoldOperatorAndOperands() {
        var left = (ScalarExpression<int>)new ScalarReference<int>(PropertyPath.Parse("age"));
        var right = (ScalarExpression<int>)new ScalarConstant<int>(18);
        var expr = new Comparison<int>(ComparisonOperator.Gt, left, right);
        Assert.Equal("Comparison", expr.TypeName);
        Assert.False(expr.IsConstant());
        Assert.Equal("Reference(age) > Constant(18)", expr.ToString());
    }

    [Fact]
    public void InExpression_shouldHoldCandidates() {
        var value = (ScalarExpression<string>)new ScalarReference<string>(PropertyPath.Parse("status"));
        var candidates = new List<ScalarExpression<string>>
        {
            new ScalarConstant<string>("active"),
            new ScalarConstant<string>("pending"),
        };
        var expr = new InExpression<string>(value, candidates);
        Assert.Equal("In", expr.TypeName);
        Assert.False(expr.IsNegated);
        Assert.Contains("in", expr.ToString());
    }

    [Fact]
    public void NotInExpression_shouldBeNegated() {
        var value = (ScalarExpression<int>)new ScalarReference<int>(PropertyPath.Parse("id"));
        var candidates = new List<ScalarExpression<int>> { new ScalarConstant<int>(1), new ScalarConstant<int>(2) };
        var expr = new InExpression<int>(value, candidates, true);
        Assert.Equal("NotIn", expr.TypeName);
        Assert.True(expr.IsNegated);
    }

    [Fact]
    public void PatternMatch_shouldHoldMode() {
        var value = (ScalarExpression<string>)new ScalarReference<string>(PropertyPath.Parse("name"));
        var pattern = (ScalarExpression<string>)new ScalarConstant<string>("John%");
        var expr = new PatternMatch<string>(value, pattern, PatternMatchMode.Like);
        Assert.Equal("PatternMatch", expr.TypeName);
        Assert.Contains("like", expr.ToString().ToLowerInvariant());
    }

    [Fact]
    public void NullCheck_shouldDetectNull() {
        var expr = new NullCheck(PropertyPath.Parse("email"), NullCheckType.IsNull);
        Assert.Equal("NullCheck", expr.TypeName);
        Assert.True(expr.IsNull);
        Assert.False(expr.IsNotNull);
        Assert.Contains("is null", expr.ToString());
    }

    [Fact]
    public void AndExpression_shouldRequireAtLeastOneOperand() => Assert.Throws<ArgumentException>(() => new AndExpression(Array.Empty<BooleanExpression>()));

    [Fact]
    public void OrExpression_shouldRequireAtLeastOneOperand() => Assert.Throws<ArgumentException>(() => new OrExpression(Array.Empty<BooleanExpression>()));

    [Fact]
    public void NotExpression_shouldWrapOperand() {
        var inner = BooleanConstant.True();
        var expr = new NotExpression(inner);
        Assert.Equal("Not", expr.TypeName);
        Assert.Single(expr.Children);
        Assert.Equal("not (true)", expr.ToString());
    }

    [Fact]
    public void BooleanConstant_shouldSupportTrivalent() {
        var t = BooleanConstant.True();
        Assert.True(t.IsTrue);
        Assert.False(t.IsFalse);
        Assert.False(t.IsUnknown);
        Assert.True(t.IsConstant());

        var f = BooleanConstant.False();
        Assert.True(f.IsFalse);

        var u = BooleanConstant.Unknown();
        Assert.True(u.IsUnknown);
    }

    [Fact]
    public void ComparisonFactoryMethods_shouldBuildCorrectOperators() {
        var l = (ScalarExpression<int>)new ScalarConstant<int>(1);
        var r = (ScalarExpression<int>)new ScalarConstant<int>(2);
        Assert.Equal(ComparisonOperator.Eq, BooleanExpressionFactory.Eq(l, r).Operator);
        Assert.Equal(ComparisonOperator.Ne, BooleanExpressionFactory.Ne(l, r).Operator);
        Assert.Equal(ComparisonOperator.Lt, BooleanExpressionFactory.Lt(l, r).Operator);
        Assert.Equal(ComparisonOperator.Le, BooleanExpressionFactory.Le(l, r).Operator);
        Assert.Equal(ComparisonOperator.Gt, BooleanExpressionFactory.Gt(l, r).Operator);
        Assert.Equal(ComparisonOperator.Ge, BooleanExpressionFactory.Ge(l, r).Operator);
    }
}

/// <summary>
/// 表达式遍历测试 / Expression Traversal Tests.
/// </summary>
public class ExpressionTraversalTests {
    [Fact]
    public void Depth_shouldCalculateCorrectly() {
        var a = (BooleanExpression)BooleanConstant.True();
        var b = (BooleanExpression)BooleanConstant.False();
        var andExpr = new AndExpression(new[] { a, b });
        Assert.Equal(2, andExpr.Depth()); // 1 (And) + 1 (max child depth)

        var notExpr = new NotExpression(andExpr);
        Assert.Equal(3, notExpr.Depth()); // 1 (Not) + 2 (And depth)
    }

    [Fact]
    public void LogicalOperatorCount_shouldCount() {
        var a = (BooleanExpression)BooleanConstant.True();
        var b = (BooleanExpression)BooleanConstant.False();
        var c = (BooleanExpression)BooleanConstant.True();

        var andExpr = new AndExpression(new[] { a, b });
        Assert.Equal(2, andExpr.LogicalOperatorCount()); // 2 operands

        var orExpr = new OrExpression(new[] { andExpr, c });
        // Or has 2 operands + (And's 2 + 0) = 4
        Assert.Equal(4, orExpr.LogicalOperatorCount());
    }
}
