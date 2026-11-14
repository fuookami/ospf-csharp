// 关系查询 Join 图校验测试 / Tests for relational query join-graph validation.

using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Query;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using QueryPlan = Fuookami.Ospf.Framework.Persistence.Query.RelationalQueryPlan;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// 关系查询 Join 图校验 / Relational query join-graph validation.
/// </summary>
public sealed class RelationalQueryJoinValidationTest {
    /// <summary>
    /// 真正把两侧关联起来的 Join 必须通过——只测拒绝会让"全部拒绝"也算实现。
    /// A join that genuinely correlates both sides must pass: a rejection-only suite would let "reject
    /// everything" count as an implementation.
    /// </summary>
    [Fact]
    public void Validate_ShouldAcceptCorrelatedJoin() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] { CorrelatedJoin("machine", "machine.jobId", "job.id") });

        plan.Validate().IsOk.Should().BeTrue();
    }

    /// <summary>
    /// 关联条件只引用已绑定列、没有目标侧列时就是笛卡尔积，必须拒绝。
    /// A condition that only references already-bound columns has no target-side side and is a cross product, so it
    /// must be rejected.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectJoinWithoutTargetSide() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] { CorrelatedJoin("machine", "job.id", "job.parentId") });

        Field(plan.Validate()).Should().Be("joins[0].condition");
    }

    /// <summary>
    /// 比较一端不是列引用（这里是常量）时不算关联。
    /// A comparison with a non-reference operand (a constant here) is not a correlation.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectComparisonWithConstant() {
        var condition = new Comparison<bool>(
            Operator: ComparisonOperator.Eq,
            Left: new ScalarReference<bool>(new PropertyPath("machine.jobId")),
            Right: new ScalarConstant<bool>(true));
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] { Join("machine", condition) });

        Field(plan.Validate()).Should().Be("joins[0].condition");
    }

    /// <summary>
    /// 关联条件里出现布尔常量必须直接拒绝：`WHERE true` 式的 Join 就是笛卡尔积。
    /// A boolean constant in the condition must be rejected outright: a `WHERE true` join is a cross product.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectBooleanConstantInCondition() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] {
                Join("machine", new AndExpression(new BooleanExpression[] {
                    BooleanConstant.True(),
                    Correlation("machine.jobId", "job.id"),
                })),
            });

        ValidationError(plan.Validate()).Message.Should().Contain("布尔常量");
    }

    /// <summary>
    /// <c>AND</c> 允许附带过滤条件：只要有一支保留关联即可。
    /// <c>AND</c> tolerates extra filters: one branch keeping the correlation is enough.
    /// </summary>
    [Fact]
    public void Validate_ShouldAcceptAndWithExtraFilter() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] {
                Join("machine", new AndExpression(new BooleanExpression[] {
                    Correlation("machine.jobId", "job.id"),
                    new Comparison<int>(
                        Operator: ComparisonOperator.Gt,
                        Left: new ScalarReference<int>(new PropertyPath("machine.capacity")),
                        Right: new ScalarConstant<int>(0)),
                })),
            });

        plan.Validate().IsOk.Should().BeTrue();
    }

    /// <summary>
    /// <c>OR</c> 要求**每个**分支都保留关联——否则某个分支会退化成笛卡尔积，而行数照样会膨胀。
    /// <c>OR</c> requires **every** branch to keep a correlation; otherwise a branch degrades into a cross product
    /// and the row count inflates anyway.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectOrWithUncorrelatedBranch() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] {
                Join("machine", new OrExpression(new BooleanExpression[] {
                    Correlation("machine.jobId", "job.id"),
                    BooleanConstant.True(),
                })),
            });

        Field(plan.Validate()).Should().Be("joins[0].condition");
    }

    /// <summary>
    /// 引用未绑定数据源必须拒绝——那说明 Join 顺序写错了。
    /// A reference to a not-yet-bound source must be rejected: the join order is wrong.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectUnknownQualifier() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] { Join("machine", Correlation("machine.jobId", "ghost.id")) });

        ValidationError(plan.Validate()).Message.Should().Contain("ghost");
    }

    /// <summary>
    /// 后一个 Join 可以引用前一个已绑定的数据源（Join 顺序决定可见性）。
    /// A later join may reference a previously bound source, since join order determines visibility.
    /// </summary>
    [Fact]
    public void Validate_ShouldBindEarlierJoinsForLaterOnes() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] {
                CorrelatedJoin("machine", "machine.jobId", "job.id"),
                CorrelatedJoin("tool", "tool.machineId", "machine.id"),
            });

        plan.Validate().IsOk.Should().BeTrue();
    }

    /// <summary>
    /// 数据源名称与别名冲突必须拒绝，否则限定符会有歧义。
    /// A source name colliding with an alias must be rejected, or qualifiers become ambiguous.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectNameCollidingWithAlias() {
        var plan = new QueryPlan(
            root: new QuerySource("job", "j"),
            joins: new[] {
                Join("j", Correlation("j.jobId", "job.id")),
            });

        ValidationError(plan.Validate()).Message.Should().Contain("冲突");
    }

    /// <summary>重复的数据源名称必须拒绝 / A duplicated source name must be rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectDuplicatedSourceName() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] {
                CorrelatedJoin("machine", "machine.jobId", "job.id"),
                CorrelatedJoin("machine", "machine.jobId", "job.id"),
            });

        ValidationError(plan.Validate()).Message.Should().Contain("重复");
    }

    /// <summary>
    /// <c>Exists</c> 是半连接，只有声明多行基数才有意义；一对一的 Exists 等价于普通内连接而会误导读者。
    /// <c>Exists</c> is a semi-join that only makes sense with a to-many cardinality; a one-to-one `Exists`
    /// reads as an inner join and misleads.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectExistsWithSingleRowCardinality() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] {
                Join(
                    "machine",
                    Correlation("machine.jobId", "job.id"),
                    type: JoinType.Exists,
                    cardinality: JoinCardinality.ManyToOne),
            });

        Field(plan.Validate()).Should().Be("joins[0].cardinality");
    }

    /// <summary><c>Exists</c> 声明多行基数时必须通过 / An `Exists` with a to-many cardinality must pass.</summary>
    [Fact]
    public void Validate_ShouldAcceptExistsWithToManyCardinality() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] {
                Join(
                    "machine",
                    Correlation("machine.jobId", "job.id"),
                    type: JoinType.Exists,
                    cardinality: JoinCardinality.ManyToMany),
            });

        plan.Validate().IsOk.Should().BeTrue();
    }

    /// <summary>
    /// 限定符解析的边界：<c>.x</c> 与 <c>x.</c> 都不是限定引用，只有两侧都有内容才算。
    /// Qualifier parsing edge cases: `.x` and `x.` are not qualified references; both sides must be non-empty.
    /// </summary>
    [Fact]
    public void SourceQualifier_ShouldRequireBothSides() {
        RelationalQueryJoinValidator.SourceQualifier("a.b").Should().Be("a");
        RelationalQueryJoinValidator.SourceQualifier("a.b.c").Should().Be("a");
        RelationalQueryJoinValidator.SourceQualifier(".b").Should().BeNull();
        RelationalQueryJoinValidator.SourceQualifier("a.").Should().BeNull();
        RelationalQueryJoinValidator.SourceQualifier("ab").Should().BeNull();
    }

    private static JoinSpec Join(
        string source,
        BooleanExpression condition,
        JoinType type = JoinType.Inner,
        JoinCardinality cardinality = JoinCardinality.ManyToOne) =>
        new(Type: type, Source: new QuerySource(source), Condition: condition, Cardinality: cardinality);

    private static JoinSpec CorrelatedJoin(string source, string leftPath, string rightPath) =>
        Join(source, Correlation(leftPath, rightPath));

    private static BooleanExpression Correlation(string leftPath, string rightPath) =>
        new Comparison<bool>(
            Operator: ComparisonOperator.Eq,
            Left: new ScalarReference<bool>(new PropertyPath(leftPath)),
            Right: new ScalarReference<bool>(new PropertyPath(rightPath)));

    private static string Field(Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        ValidationError(result).Field;

    private static RelationalQueryValidationError ValidationError(
        Result<Success, ErrorCode, Error<ErrorCode>> result) {
        result.IsOk.Should().BeFalse();
        var error = (ExErr<ErrorCode, RelationalQueryValidationError>)
            ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error;
        return error.TypedValue;
    }
}
