#nullable enable

using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Expression.Dsl;
using Fuookami.Ospf.Math.Symbol.Expression.Parser;
using System;
using System.Linq.Expressions;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Expression.Tests;
// Test fixtures
public sealed record User(int Age, string Name, string Status, Address? Address = null);
public sealed record Address(string City);

/// <summary>
/// PathBuilder 测试 / PathBuilder Tests.
/// </summary>
public class PathBuilderTests {
    [Fact]
    public void StringEq_shouldBuildComparison() {
        Comparison<string> expr = ExpressionDsl.Path("name").Eq("John");
        Assert.Equal(ComparisonOperator.Eq, expr.Operator);
        Assert.IsType<ScalarReference<string>>(expr.Left);
        Assert.IsType<ScalarConstant<string>>(expr.Right);
    }

    [Fact]
    public void IntGt_shouldBuildComparison() {
        Comparison<int> expr = ExpressionDsl.Path("age").Gt(18);
        Assert.Equal(ComparisonOperator.Gt, expr.Operator);
    }

    [Fact]
    public void AllComparisonOperators_shouldBuild() {
        PathBuilder path = ExpressionDsl.Path("x");
        Assert.Equal(ComparisonOperator.Eq, path.Eq(1).Operator);
        Assert.Equal(ComparisonOperator.Ne, path.Ne(1).Operator);
        Assert.Equal(ComparisonOperator.Lt, path.Lt(1).Operator);
        Assert.Equal(ComparisonOperator.Le, path.Le(1).Operator);
        Assert.Equal(ComparisonOperator.Gt, path.Gt(1).Operator);
        Assert.Equal(ComparisonOperator.Ge, path.Ge(1).Operator);
    }

    [Fact]
    public void InValues_shouldBuild() {
        InExpression<string> expr = ExpressionDsl.Path("status").InValues("active", "pending");
        Assert.Equal("In", expr.TypeName);
        Assert.Equal(2, expr.Candidates.Count);
    }

    [Fact]
    public void IsNull_shouldBuild() {
        NullCheck expr = ExpressionDsl.Path("email").IsNull();
        Assert.Equal("NullCheck", expr.TypeName);
        Assert.True(expr.IsNull);
    }

    [Fact]
    public void Like_shouldBuildPatternMatch() {
        PatternMatch<string> expr = ExpressionDsl.Path("name").Like("John%");
        Assert.Equal("PatternMatch", expr.TypeName);
    }
}

/// <summary>
/// 逻辑操作符测试 / Logical Operators Tests.
/// </summary>
public class LogicalOperatorsTests {
    [Fact]
    public void And_shouldFlatten() {
        var a = (BooleanExpression)ExpressionDsl.Path("a").Gt(1);
        var b = (BooleanExpression)ExpressionDsl.Path("b").Lt(10);
        AndExpression expr = a.And(b);
        Assert.Equal("And", expr.TypeName);
        Assert.Equal(2, ((AndExpression)expr).Operands.Count);
    }

    [Fact]
    public void Or_shouldFlatten() {
        var a = (BooleanExpression)ExpressionDsl.Path("a").Gt(1);
        var b = (BooleanExpression)ExpressionDsl.Path("b").Lt(10);
        OrExpression expr = a.Or(b);
        Assert.Equal("Or", expr.TypeName);
    }

    [Fact]
    public void Not_shouldWrap() {
        var a = (BooleanExpression)ExpressionDsl.Path("a").Gt(1);
        NotExpression expr = a.Not();
        Assert.Equal("Not", expr.TypeName);
    }

    [Fact]
    public void FlattenedAnd_shouldMergeNested() {
        var a = (BooleanExpression)ExpressionDsl.Path("a").Gt(1);
        var b = (BooleanExpression)ExpressionDsl.Path("b").Lt(10);
        var c = (BooleanExpression)ExpressionDsl.Path("c").Eq("x");
        AndExpression ab = a.And(b);
        AndExpression abc = ab.And(c);
        Assert.Equal(3, ((AndExpression)abc).Operands.Count);
    }
}

/// <summary>
/// TypedPathBuilder 测试 / TypedPathBuilder Tests.
/// </summary>
public class TypedPathBuilderTests {
    [Fact]
    public void PropertyEq_shouldBuildComparison() {
        Comparison<int> expr = ExpressionDsl.Prop<User, int>(u => u.Age).Eq(18);
        Assert.Equal(ComparisonOperator.Eq, expr.Operator);
        Assert.Equal("age", ((ScalarReference<int>)expr.Left).Path.Value);
    }

    [Fact]
    public void PropertyGt_shouldBuildComparison() {
        Comparison<int> expr = ExpressionDsl.Prop<User, int>(u => u.Age).Gt(18);
        Assert.Equal(ComparisonOperator.Gt, expr.Operator);
    }

    [Fact]
    public void NestedProperty_shouldExtractPath() {
        Comparison<string> expr = ExpressionDsl.Prop<User, string>(u => u.Address!.City).Eq("NYC");
        Assert.Equal("address.city", ((ScalarReference<string>)expr.Left).Path.Value);
    }

    [Fact]
    public void InValues_shouldBuild() {
        InExpression<string> expr = ExpressionDsl.Prop<User, string>(u => u.Status).InValues("active", "pending");
        Assert.Equal("In", expr.TypeName);
    }

    [Fact]
    public void SchemaPredicateBlock_shouldWork() {
        var schema = new TestUserSchema();
        BooleanExpression expr = schema.Predicate<User, TestUserSchema>(s => s.AgeField.Gt(18).And(s.StatusField.Eq("active")));
        Assert.Equal("And", expr.TypeName);
    }

    private sealed class TestUserSchema : PredicateSchema<User> {
        public TypedPathBuilder<User, int> AgeField => Field(u => u.Age);
        public TypedPathBuilder<User, string> StatusField => Field(u => u.Status);
    }
}

/// <summary>
/// 便捷函数测试 / Convenience Functions Tests.
/// </summary>
public class ConvenienceFunctionsTests {
    [Fact]
    public void Compare_shouldBuild() {
        Comparison<int> expr = ExpressionDsl.Compare("age", ComparisonOperator.Gt, 18);
        Assert.Equal(ComparisonOperator.Gt, expr.Operator);
    }

    [Fact]
    public void QuickEq_shouldBuild() {
        Comparison<string> expr = ExpressionDsl.Eq("name", "John");
        Assert.Equal(ComparisonOperator.Eq, expr.Operator);
    }

    [Fact]
    public void InExpr_shouldBuild() {
        InExpression<string> expr = ExpressionDsl.InExpr("status", new[] { "active", "pending" });
        Assert.Equal("In", expr.TypeName);
    }

    [Fact]
    public void IsNull_shouldBuild() {
        NullCheck expr = ExpressionDsl.IsNull("email");
        Assert.Equal("NullCheck", expr.TypeName);
    }

    [Fact]
    public void And_shouldBuild() {
        var a = (BooleanExpression)ExpressionDsl.Eq("a", 1);
        var b = (BooleanExpression)ExpressionDsl.Eq("b", 2);
        AndExpression expr = ExpressionDsl.And(a, b);
        Assert.Equal("And", expr.TypeName);
    }

    [Fact]
    public void Abs_shouldBuild() {
        ScalarReference<object?> inner = ExpressionDsl.ScalarPath<object?>("x");
        ScalarFunction<object?> expr = ExpressionDsl.Abs(inner);
        Assert.Equal("Function", expr.TypeName);
        Assert.Equal("abs", expr.Name);
    }
}

/// <summary>
/// DSL 与解析器等价测试 / DSL vs Parser Equivalence Tests.
/// </summary>
public class DslParserEquivalenceTests {
    [Fact]
    public void SimpleComparison_shouldMatch() {
        Comparison<int> dsl = ExpressionDsl.Eq("age", 18);
        BooleanExpression? parsed = BooleanExpressionParser.ParseOrNull("age = 18");
        Assert.NotNull(parsed);
        Assert.Equal("Comparison", parsed!.TypeName);
        Assert.Equal(dsl.Operator, ((Comparison<object>)parsed).Operator);
    }

    [Fact]
    public void NullCheck_shouldMatch() {
        NullCheck dsl = ExpressionDsl.IsNull("email");
        BooleanExpression? parsed = BooleanExpressionParser.ParseOrNull("email is null");
        Assert.NotNull(parsed);
        Assert.Equal("NullCheck", parsed!.TypeName);
    }
}
