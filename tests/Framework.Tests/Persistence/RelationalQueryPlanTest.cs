// 关系查询计划容器测试 / Tests for the relational query plan container.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Query;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using QueryPlan = Fuookami.Ospf.Framework.Persistence.Query.RelationalQueryPlan;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// 关系查询计划容器 / Relational query plan container.
/// </summary>
public sealed class RelationalQueryPlanTest {
    /// <summary>
    /// 合法的最小计划必须**通过**校验——只测拒绝的套件会把"全部拒绝"也当成正确实现。
    /// A legal minimal plan must **pass** validation: a rejection-only suite would bless an implementation
    /// that rejects everything.
    /// </summary>
    [Fact]
    public void Validate_ShouldAcceptMinimalPlan() {
        var plan = new QueryPlan(root: new QuerySource("job"));

        plan.Validate().IsOk.Should().BeTrue();
    }

    /// <summary>
    /// 构造时裁剪空白，否则同一份计划会因不可见字符得到不同的名称匹配结果。
    /// Construction trims whitespace; otherwise identical plans would match source names differently because of
    /// invisible characters.
    /// </summary>
    [Fact]
    public void Constructor_ShouldTrimWhitespace() {
        var plan = new QueryPlan(
            root: new QuerySource("  job  ", "  j  "),
            projections: new[] { new ProjectionSpec(new ColumnRef(" job ", " start "), "  s  ") },
            groupBy: new[] { new ColumnRef(" job ", " start ") },
            orderBy: new[] { new OrderSpec(new ColumnRef(" job ", " start ")) },
            rootKey: new[] { new ColumnRef(" job ", " id ") });

        plan.Root.Name.Should().Be("job");
        plan.Root.Alias.Should().Be("j");
        plan.Projections[0].Column.Source.Should().Be("job");
        plan.Projections[0].Column.Path.Should().Be("start");
        plan.Projections[0].Alias.Should().Be("s");
        plan.GroupBy[0].Path.Should().Be("start");
        plan.OrderBy[0].Column.Source.Should().Be("job");
        plan.RootKey[0].Path.Should().Be("id");
    }

    /// <summary>
    /// 空白名称裁剪后为空，必须在校验时被拒绝——裁剪在前、校验在后，这条顺序不能反。
    /// A whitespace-only name trims to empty and must be rejected by validation: trimming happens first and
    /// validation second, and that order must not be reversed.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectWhitespaceOnlyRootName() {
        var plan = new QueryPlan(root: new QuerySource("   "));

        Result<Success, ErrorCode, Error<ErrorCode>> result = plan.Validate();

        result.IsOk.Should().BeFalse();
        ValidationError(result).Field.Should().Be("root");
    }

    [Fact]
    public void Validate_ShouldRejectAliasEqualToName() {
        var plan = new QueryPlan(root: new QuerySource("job", "job"));

        ValidationError(plan.Validate()).Field.Should().Be("root.alias");
    }

    /// <summary>
    /// Join 深度是"数据源个数"，即 Join 数 + 1；差一（off-by-one）会让恰好到达上限的计划被误拒。
    /// Join depth counts sources, i.e. joins + 1; an off-by-one would wrongly reject a plan exactly at the
    /// limit.
    /// </summary>
    [Fact]
    public void Validate_ShouldCountDepthAsJoinsPlusOne() {
        var atLimit = new QueryPlan(
            root: new QuerySource("job"),
            joins: Joins(1));
        atLimit.Validate(new RelationalQueryLimits(MaxJoins: 1, MaxDepth: 2, MaxProjections: 16))
            .IsOk.Should().BeTrue("Join 数 1 + 根 1 = 深度 2，恰好在上限 / 1 join + 1 root = depth 2, exactly "
                + "at the limit");

        var overDepth = new QueryPlan(
            root: new QuerySource("job"),
            joins: Joins(2));
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            overDepth.Validate(new RelationalQueryLimits(MaxJoins: 2, MaxDepth: 2, MaxProjections: 16));
        result.IsOk.Should().BeFalse();
        ValidationError(result).Field.Should().Be("joins");
    }

    [Fact]
    public void Validate_ShouldRejectTooManyProjections() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            projections: Enumerable.Range(0, 3)
                .Select(index => new ProjectionSpec(new ColumnRef("job", $"c{index}")))
                .ToArray());

        ValidationError(plan.Validate(new RelationalQueryLimits(MaxProjections: 2))).Field
            .Should().Be("projections");
    }

    /// <summary>
    /// 分页上限必须为正；<c>Limit = 0</c> 表示"取零行"是很多后端不支持的语义，基线明确拒绝。
    /// A page limit must be positive; <c>Limit = 0</c> (fetch zero rows) is a semantic many backends do not
    /// support and the code rejects it explicitly.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void Validate_ShouldRejectInvalidPage(int limit, int offset) {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            page: new PageSpec(Limit: limit, Offset: offset));

        ValidationError(plan.Validate()).Field.Should().Be("page");
    }

    /// <summary>分页为 null 表示不限制，必须通过 / A null page means unbounded and must pass.</summary>
    [Fact]
    public void Validate_ShouldAcceptUnboundedPage() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            page: new PageSpec(Limit: null, Offset: 7));

        plan.Validate().IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData("rootKey")]
    [InlineData("projections")]
    [InlineData("groupBy")]
    [InlineData("orderBy")]
    public void Validate_ShouldRejectEmptyColumnReference(string field) {
        var empty = new ColumnRef(" ", "");
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            projections: field == "projections"
                ? new[] { new ProjectionSpec(empty) }
                : null,
            groupBy: field == "groupBy" ? new[] { empty } : null,
            orderBy: field == "orderBy" ? new[] { new OrderSpec(empty) } : null,
            rootKey: field == "rootKey" ? new[] { empty } : null);

        ValidationError(plan.Validate()).Field.Should().Be(field);
    }

    /// <summary>
    /// 列表必须与调用方的数组解耦：构造后改原数组不得影响计划，否则"不可变快照"是假的。
    /// Lists must be decoupled from the caller's array: mutating the source array after construction must not
    /// affect the plan, otherwise the "immutable snapshot" is fiction.
    /// </summary>
    [Fact]
    public void Constructor_ShouldDetachFromCallerArray() {
        var source = new List<ProjectionSpec> {
            new(new ColumnRef("job", "start")),
        };
        var plan = new QueryPlan(root: new QuerySource("job"), projections: source);

        source.Add(new ProjectionSpec(new ColumnRef("job", "end")));

        plan.Projections.Should().HaveCount(1);
    }

    /// <summary>
    /// 构造可用的 Join：关联条件必须是"被 Join 侧 ↔ 已绑定侧"的列对列关联，否则会被 Join 图校验拒绝（那正是
    /// 笛卡尔积）。用 <c>BooleanConstant.True</c> 当条件的计划在这条规则落地后已不再合法。
    /// Build usable joins: the condition must be a column-to-column correlation between the joined side and a
    /// bound side, otherwise the join-graph check rejects it as a cross product. A plan whose conditions are
    /// <c>BooleanConstant.True</c> is no longer legal once that rule exists.
    /// </summary>
    private static JoinSpec[] Joins(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new JoinSpec(
                Type: JoinType.Inner,
                Source: new QuerySource($"s{index}"),
                Condition: new Fuookami.Ospf.Math.Symbol.Expression.Comparison<bool>(
                    Operator: Fuookami.Ospf.Math.Symbol.Expression.ComparisonOperator.Eq,
                    Left: new Fuookami.Ospf.Math.Symbol.Expression.ScalarReference<bool>(
                        new Fuookami.Ospf.Math.Symbol.Expression.PropertyPath($"s{index}.jobId")),
                    Right: new Fuookami.Ospf.Math.Symbol.Expression.ScalarReference<bool>(
                        new Fuookami.Ospf.Math.Symbol.Expression.PropertyPath("job.id"))),
                Cardinality: JoinCardinality.ManyToOne))
            .ToArray();

    private static RelationalQueryValidationError ValidationError(
        Result<Success, ErrorCode, Error<ErrorCode>> result) {
        Error<ErrorCode> error = (result as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error;
        error.Should().BeOfType<ExErr<ErrorCode, RelationalQueryValidationError>>();
        return ((ExErr<ErrorCode, RelationalQueryValidationError>)error).TypedValue;
    }
}
