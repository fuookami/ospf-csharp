// 关系查询计划规范化与哈希测试 / Tests for canonical rendering and hashing.

using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Query;
using Fuookami.Ospf.Math.Symbol.Expression;
using Xunit;
using QueryPlan = Fuookami.Ospf.Framework.Persistence.Query.RelationalQueryPlan;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// 关系查询计划规范化与哈希 / Relational query plan canonical rendering and hashing.
/// </summary>
public sealed class RelationalQueryPlanCanonicalTest {
    /// <summary>同一份计划必须得到同一个哈希 / The same plan must yield the same hash.</summary>
    [Fact]
    public void Hash_ShouldBeDeterministic() {
        QueryPlan first = Plan();
        QueryPlan second = Plan();

        first.Hash().Should().Be(second.Hash());
        first.Canonical().Should().Be(second.Canonical());
    }

    /// <summary>不同的计划必须得到不同的哈希 / Different plans must yield different hashes.</summary>
    [Fact]
    public void Hash_ShouldDifferForDifferentPlans() {
        QueryPlan ascending = new(
            root: new QuerySource("job"),
            orderBy: new[] {
                new OrderSpec(new ColumnRef("job", "start"), SortDirection.Ascending),
            });
        QueryPlan descending = new(
            root: new QuerySource("job"),
            orderBy: new[] {
                new OrderSpec(new ColumnRef("job", "start"), SortDirection.Descending),
            });

        ascending.Hash().Should().NotBe(descending.Hash());
    }

    /// <summary>
    /// <c>And</c> 的操作数顺序**不影响**哈希——这是规范化等价的核心，去掉排序会让同一逻辑条件因书写顺序不同
    /// 而得到不同哈希。
    /// <c>And</c> operand order must **not** affect the hash: that sorting is what makes logical equivalence
    /// canonical, and removing it would make the same condition hash differently by writing order.
    /// </summary>
    [Fact]
    public void Hash_ShouldIgnoreAndOperandOrder() {
        BooleanExpression left = Constant();
        BooleanExpression right = new NotExpression(BooleanConstant.False());

        QueryPlan forward = new(
            root: new QuerySource("job"),
            predicate: new AndExpression(new[] { left, right }));
        QueryPlan reversed = new(
            root: new QuerySource("job"),
            predicate: new AndExpression(new[] { right, left }));

        forward.Hash().Should().Be(reversed.Hash());
    }

    /// <summary>
    /// <c>In</c> 的候选顺序同样不影响哈希。
    /// <c>In</c> candidate order must not affect the hash either.
    /// </summary>
    [Fact]
    public void Hash_ShouldIgnoreInCandidateOrder() {
        QueryPlan forward = new(
            root: new QuerySource("job"),
            predicate: new InExpression<int>(
                Value: new ScalarConstant<int>(1),
                Candidates: new ScalarExpression<int>[] {
                    new ScalarConstant<int>(2),
                    new ScalarConstant<int>(3),
                }));
        QueryPlan reversed = new(
            root: new QuerySource("job"),
            predicate: new InExpression<int>(
                Value: new ScalarConstant<int>(1),
                Candidates: new ScalarExpression<int>[] {
                    new ScalarConstant<int>(3),
                    new ScalarConstant<int>(2),
                }));

        forward.Hash().Should().Be(reversed.Hash());
    }

    /// <summary>
    /// 长度前缀必须真的起作用：不长度前缀时 <c>a</c>+<c>bc</c> 与 <c>ab</c>+<c>c</c> 会拼成同一个字符串，
    /// 两份不同的计划就会撞哈希。这条用例正是那个碰撞的复现。
    /// The length prefix must actually work: without it <c>a</c>+<c>bc</c> and <c>ab</c>+<c>c</c> concatenate to
    /// the same string and two different plans collide. This case reproduces exactly that collision.
    /// </summary>
    [Fact]
    public void Hash_ShouldNotCollideOnAmbiguousConcatenation() {
        QueryPlan left = new(
            root: new QuerySource("job"),
            groupBy: new[] { new ColumnRef("a", "bc") });
        QueryPlan right = new(
            root: new QuerySource("job"),
            groupBy: new[] { new ColumnRef("ab", "c") });

        left.Canonical().Should().NotBe(right.Canonical(),
            "长度前缀缺失会让两者拼成同一串 / without length prefixes both concatenate identically");
        left.Hash().Should().NotBe(right.Hash());
    }

    /// <summary>
    /// 空白在构造时已被裁剪，因此带空白的输入与规范输入必须同哈希——否则同一份计划只因输入空格就换了审计标识。
    /// Whitespace is trimmed at construction, so padded and canonical inputs must hash identically; otherwise the
    /// same plan would change its audit identity just because of spaces.
    /// </summary>
    [Fact]
    public void Hash_ShouldIgnoreSurroundingWhitespace() {
        QueryPlan padded = new(
            root: new QuerySource(" job ", " j "),
            groupBy: new[] { new ColumnRef(" job ", " id ") });
        QueryPlan trimmed = new(
            root: new QuerySource("job", "j"),
            groupBy: new[] { new ColumnRef("job", "id") });

        padded.Hash().Should().Be(trimmed.Hash());
    }

    /// <summary>
    /// 哈希必须是 64 位十六进制小写——长度或大小写不同会让下游按字符串比较时静默失配。
    /// The hash must be 64 lowercase hex characters; a different length or casing would silently mismatch when
    /// downstream compares strings.
    /// </summary>
    [Fact]
    public void Hash_ShouldBeLowercaseSha256Hex() {
        string hash = Plan().Hash();

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    /// <summary>规范化字符串必须包含计划的每个组成部分 / The canonical string must cover every plan component.</summary>
    [Fact]
    public void Canonical_ShouldCoverEveryComponent() {
        string canonical = Plan().Canonical();

        canonical.Should().Contain("root=");
        canonical.Should().Contain("|joins=");
        canonical.Should().Contain("|predicate=");
        canonical.Should().Contain("|projections=");
        canonical.Should().Contain("|distinct=");
        canonical.Should().Contain("|groupBy=");
        canonical.Should().Contain("|orderBy=");
        canonical.Should().Contain("|page=");
        canonical.Should().Contain("|rootKey=");
    }

    /// <summary>不设上限的分页必须渲染成 <c>unbounded</c> 而不是空——否则与"没有分页"无法区分。</summary>
    [Fact]
    public void Canonical_ShouldRenderUnboundedPage() {
        QueryPlan plan = new(
            root: new QuerySource("job"),
            page: new PageSpec(Limit: null, Offset: 5));

        plan.Canonical().Should().Contain("|page=unbounded:5");
    }

    private static BooleanExpression Constant() => BooleanConstant.True();

    private static QueryPlan Plan() => new(
        root: new QuerySource("job", "j"),
        joins: new[] {
            new JoinSpec(
                Type: JoinType.Inner,
                Source: new QuerySource("machine", "m"),
                Condition: new Comparison<int>(
                    Operator: ComparisonOperator.Eq,
                    Left: new ScalarConstant<int>(1),
                    Right: new ScalarConstant<int>(2)),
                Cardinality: JoinCardinality.ManyToOne),
        },
        predicate: new AndExpression(new[] { Constant(), BooleanConstant.False() }),
        projections: new[] { new ProjectionSpec(new ColumnRef("job", "start"), "s") },
        distinct: true,
        groupBy: new[] { new ColumnRef("job", "id") },
        orderBy: new[] {
            new OrderSpec(new ColumnRef("job", "start"), SortDirection.Descending, NullsOrder.Last),
        },
        page: new PageSpec(Limit: 10, Offset: 0),
        rootKey: new[] { new ColumnRef("job", "id") });
}
