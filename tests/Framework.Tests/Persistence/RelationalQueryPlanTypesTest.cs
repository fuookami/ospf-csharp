// 关系查询计划支撑类型测试 / Tests for the relational query plan supporting types.

using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Query;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// 关系查询计划支撑类型 / Relational query plan supporting types.
/// </summary>
public sealed class RelationalQueryPlanTypesTest {
    /// <summary>
    /// <c>ManyToOne</c> **不是**多行关系——它是"多对一"，目标侧只有一行。把它算进多行会让编译器误加去重，
    /// 从而悄悄改变返回行数。
    /// <c>ManyToOne</c> is **not** a to-many relation: it is many-to-one, so the target side holds a single row.
    /// Counting it as to-many would make the compiler add distinct and silently change the row count.
    /// </summary>
    /// <param name="cardinality">基数 / Cardinality.</param>
    /// <param name="expected">期望是否为多行 / Expected to-many verdict.</param>
    [Theory]
    [InlineData(JoinCardinality.OneToOne, false)]
    [InlineData(JoinCardinality.ManyToOne, false)]
    [InlineData(JoinCardinality.OneToMany, true)]
    [InlineData(JoinCardinality.ManyToMany, true)]
    public void IsToMany_ShouldOnlyCoverTargetSideMultiplicity(
        JoinCardinality cardinality,
        bool expected) =>
        cardinality.IsToMany().Should().Be(expected);

    /// <summary>
    /// 默认值：16 个 Join、16 层深度、128 个投影；分页不设上限且不跳过。
 /// Defaults match the implementation: 16 joins, depth 16, 128 projections; pagination is unbounded and skips
    /// nothing.
    /// </summary>
    [Fact]
    public void Defaults_ShouldMatchBaseline() {
        var limits = new RelationalQueryLimits();
        limits.MaxJoins.Should().Be(16);
        limits.MaxDepth.Should().Be(16);
        limits.MaxProjections.Should().Be(128);

        var page = new PageSpec();
        page.Limit.Should().BeNull("不设上限表示不限制 / unbounded means no limit");
        page.Offset.Should().Be(0);

        var order = new OrderSpec(new ColumnRef("job", "start"));
        order.Direction.Should().Be(SortDirection.Ascending);
        order.Nulls.Should().Be(NullsOrder.Unspecified,
            "未指定不得被当成 First 或 Last / unspecified must not become First or Last");
    }

    /// <summary>
    /// Join 条件必须保留传入的表达式实例，不能在构造时被替换成占位符。
    /// A join condition must keep the expression instance supplied, not be replaced by a placeholder.
    /// </summary>
    [Fact]
    public void JoinSpec_ShouldKeepConditionInstance() {
        Fuookami.Ospf.Math.Symbol.Expression.BooleanExpression condition =
            Fuookami.Ospf.Math.Symbol.Expression.BooleanConstant.True();

        var spec = new JoinSpec(
            Type: JoinType.Left,
            Source: new QuerySource("machine", "m"),
            Condition: condition,
            Cardinality: JoinCardinality.ManyToOne);

        spec.Condition.Should().BeSameAs(condition);
        spec.Source.Alias.Should().Be("m");
    }
}
