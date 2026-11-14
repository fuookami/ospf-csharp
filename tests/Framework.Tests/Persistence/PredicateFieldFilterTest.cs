// 谓词字段过滤提取测试 / Tests for predicate field filter extraction.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Expression;
using Fuookami.Ospf.Math.Symbol.Expression;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// 谓词字段过滤提取 / Predicate field filter extraction.
/// </summary>
public sealed class PredicateFieldFilterTest {
    /// <summary>null 谓词表示"无过滤"，必须返回空映射而不是 null / A null predicate means no filter and yields an empty map.</summary>
    [Fact]
    public void NullPredicate_ShouldYieldEmptyFilters() {
        ((BooleanExpression?)null).EqFilters().Should().NotBeNull().And.BeEmpty();
        ((BooleanExpression?)null).EqOrInFilters().Should().NotBeNull().And.BeEmpty();
        ((BooleanExpression?)null).FieldFilters().Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>单字段等值条件必须解析出来 / A single equality condition is extracted.</summary>
    [Fact]
    public void EqFilters_ShouldExtractEquality() {
        BooleanExpression predicate = Compare("job.state", ComparisonOperator.Eq, "RUNNING");

        IReadOnlyDictionary<string, object?>? filters = predicate.EqFilters();

        filters.Should().NotBeNull();
        filters!["job.state"].Should().Be("RUNNING");
    }

    /// <summary>`AND` 下多个等值条件全部收集 / Multiple equalities under `AND` are all collected.</summary>
    [Fact]
    public void EqFilters_ShouldCollectAllAndBranches() {
        BooleanExpression predicate = new AndExpression(new[] {
            Compare("job.state", ComparisonOperator.Eq, "RUNNING"),
            Compare("job.owner", ComparisonOperator.Eq, "alice"),
        });

        IReadOnlyDictionary<string, object?>? filters = predicate.EqFilters();

        filters.Should().HaveCount(2);
        filters!["job.owner"].Should().Be("alice");
    }

    /// <summary>
    /// 只要有一个分支不受支持就整体返回 null：**部分收集**会让过滤条件悄悄变宽，返回本不该返回的行。
    /// A single unsupported branch makes the whole parse null: **partial collection** would silently widen the
    /// filter and return rows that should have been excluded.
    /// </summary>
    [Fact]
    public void EqFilters_ShouldFailWholeParseOnUnsupportedBranch() {
        BooleanExpression predicate = new AndExpression(new[] {
            Compare("job.state", ComparisonOperator.Eq, "RUNNING"),
            Compare("job.priority", ComparisonOperator.Gt, 3),
        });

        predicate.EqFilters().Should().BeNull();
    }

    /// <summary>
    /// **操作符方向必须翻转**：`5 &lt; field` 等价于 `field &gt; 5`，不翻转会把大小关系整个反过来。
    /// **The operator must flip**: `5 &lt; field` equals `field &gt; 5`, and without flipping the ordering is
    /// inverted.
    /// </summary>
    [Fact]
    public void FieldFilters_ShouldFlipOperatorWhenFieldIsOnRight() {
        var predicate = new Comparison<int>(
            Operator: ComparisonOperator.Lt,
            Left: new ScalarConstant<int>(5),
            Right: new ScalarReference<int>(new PropertyPath("job.priority")));

        IReadOnlyDictionary<string, FieldFilter>? filters = predicate.FieldFilters();

        filters.Should().NotBeNull();
        filters!["job.priority"].Gt.Should().Be(5);
        filters["job.priority"].Lt.Should().BeNull("方向翻转后不再是小于 / after flipping it is no longer less-than");
    }

    /// <summary>
    /// **等值 null 必须落到 `IsNull`**，而不是 `Eq = null`：后者拼出的 `= NULL` 永不成立。
    /// **An equality against null must land in `IsNull`**, not `Eq = null`: the latter emits `= NULL`, which never
    /// matches.
    /// </summary>
    [Fact]
    public void FieldFilters_ShouldMapNullEqualityToIsNull() {
        var predicate = new Comparison<string>(
            Operator: ComparisonOperator.Eq,
            Left: new ScalarReference<string>(new PropertyPath("job.owner")),
            Right: new ScalarConstant<string>(null!));

        IReadOnlyDictionary<string, FieldFilter>? filters = predicate.FieldFilters();

        filters!["job.owner"].IsNull.Should().BeTrue();
        filters["job.owner"].Eq.Should().BeNull();
    }

    /// <summary>`IS NOT NULL` 必须映射成 <c>IsNull = false</c>，而不是"未设置"。</summary>
    [Fact]
    public void FieldFilters_ShouldMapIsNotNull() {
        var predicate = new NullCheck(
            Type: NullCheckType.IsNotNull,
            Path: new PropertyPath("job.owner"));

        predicate.FieldFilters()!["job.owner"].IsNull.Should().BeFalse();
    }

    /// <summary>`NOT IN` 不能被当成 `IN` —— 那会**返回本该排除的行**。</summary>
    [Fact]
    public void FieldFilters_ShouldRejectNotIn() {
        var predicate = new InExpression<int>(
            Value: new ScalarReference<int>(new PropertyPath("job.priority")),
            Candidates: new ScalarExpression<int>[] { new ScalarConstant<int>(1) },
            Negated: true);

        predicate.FieldFilters().Should().BeNull();
        predicate.EqOrInFilters().Should().BeNull();
    }

    /// <summary>`IN` 常量集合必须收集成列表 / An `IN` constant list is collected as a list.</summary>
    [Fact]
    public void EqOrInFilters_ShouldCollectInValues() {
        var predicate = new InExpression<int>(
            Value: new ScalarReference<int>(new PropertyPath("job.priority")),
            Candidates: new ScalarExpression<int>[] {
                new ScalarConstant<int>(1),
                new ScalarConstant<int>(2),
            });

        IReadOnlyDictionary<string, object?>? filters = predicate.EqOrInFilters();

        filters.Should().NotBeNull();
        filters!["job.priority"].Should().BeAssignableTo<IReadOnlyList<object?>>()
            .Which.Should().Equal(1, 2);
    }

    /// <summary>候选里混入非常量时必须整体放弃，而不是丢掉那一项 / A non-constant candidate aborts the parse.</summary>
    [Fact]
    public void EqOrInFilters_ShouldRejectNonConstantCandidate() {
        var predicate = new InExpression<int>(
            Value: new ScalarReference<int>(new PropertyPath("job.priority")),
            Candidates: new ScalarExpression<int>[] {
                new ScalarConstant<int>(1),
                new ScalarReference<int>(new PropertyPath("job.other")),
            });

        predicate.EqOrInFilters().Should().BeNull();
    }

    /// <summary>空 `IN` 集合不予解析 / An empty `IN` set is not parsed.</summary>
    [Fact]
    public void EqOrInFilters_ShouldRejectEmptyInSet() {
        var predicate = new InExpression<int>(
            Value: new ScalarReference<int>(new PropertyPath("job.priority")),
            Candidates: System.Array.Empty<ScalarExpression<int>>());

        predicate.EqOrInFilters().Should().BeNull();
    }

    /// <summary>列对列比较不在此列 / A column-to-column comparison is not reducible.</summary>
    [Fact]
    public void EqFilters_ShouldRejectColumnToColumn() {
        var predicate = new Comparison<int>(
            Operator: ComparisonOperator.Eq,
            Left: new ScalarReference<int>(new PropertyPath("job.a")),
            Right: new ScalarReference<int>(new PropertyPath("job.b")));

        predicate.EqFilters().Should().BeNull();
    }

    /// <summary>
    /// 同一字段的多个条件必须合并到同一个过滤器，而不是后一个覆盖前一个。
    /// Several conditions on one field must merge into a single filter rather than overwriting each other.
    /// </summary>
    [Fact]
    public void FieldFilters_ShouldMergeConditionsOnSameField() {
        BooleanExpression predicate = new AndExpression(new[] {
            Compare("job.priority", ComparisonOperator.Ge, 1),
            Compare("job.priority", ComparisonOperator.Le, 9),
        });

        FieldFilter filter = predicate.FieldFilters()!["job.priority"];
        filter.Ge.Should().Be(1);
        filter.Le.Should().Be(9);
    }

    /// <summary>`OR` 不是 `AND`，不得被展开成字段过滤 / `OR` is not `AND` and must not be flattened.</summary>
    [Fact]
    public void EqFilters_ShouldRejectOr() {
        BooleanExpression predicate = new OrExpression(new[] {
            Compare("job.state", ComparisonOperator.Eq, "RUNNING"),
            Compare("job.state", ComparisonOperator.Eq, "QUEUED"),
        });

        predicate.EqFilters().Should().BeNull();
        predicate.FieldFilters().Should().BeNull();
    }

    private static BooleanExpression Compare(
        string path,
        ComparisonOperator @operator,
        object value) => value switch {
            string text => new Comparison<string>(
                Operator: @operator,
                Left: new ScalarReference<string>(new PropertyPath(path)),
                Right: new ScalarConstant<string>(text)),
            int number => new Comparison<int>(
                Operator: @operator,
                Left: new ScalarReference<int>(new PropertyPath(path)),
                Right: new ScalarConstant<int>(number)),
            _ => throw new System.NotSupportedException(),
        };
}
