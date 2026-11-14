// 关系查询表达式冻结测试 / Tests for relational query expression freezing.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Query;
using Fuookami.Ospf.Math.Symbol.Expression;
using Xunit;
using QueryPlan = Fuookami.Ospf.Framework.Persistence.Query.RelationalQueryPlan;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// 关系查询表达式冻结 / Relational query expression freezing.
/// </summary>
/// <remarks>
/// 冻结的意义只有一个：**构造之后修改调用方的可变容器不得影响计划**。因此每个用例都构造后改原容器再断言，
/// 而不是断言"表达式相等"——后者在没冻结时也会通过。
/// Freezing has exactly one purpose: **mutating the caller's mutable container after construction must not
/// affect the plan**. Every case therefore mutates the original container after construction and then asserts,
/// rather than asserting expression equality — which would pass even without freezing.
/// </remarks>
public sealed class RelationalQueryExpressionFreezeTest {
    /// <summary>
    /// 自定义 payload 里的列表必须被复制：构造后向原列表追加元素不得改变计划。
    /// A list inside a custom payload must be copied: appending to the original after construction must not
    /// change the plan.
    /// </summary>
    [Fact]
    public void Predicate_ShouldCopyCustomPayloadList() {
        var payload = new List<string> { "a" };
        var predicate = new BooleanCustom(payload);
        var plan = new QueryPlan(root: new QuerySource("job"), predicate: predicate);

        payload.Add("b");

        var stored = (BooleanCustom)plan.Predicate!;
        stored.Value.Should().BeAssignableTo<IReadOnlyCollection<object?>>();
        ((IReadOnlyCollection<object?>)stored.Value).Should().HaveCount(1,
            "冻结后原列表的追加不得影响计划 / appending to the original must not affect the plan");
    }

    /// <summary>
    /// 自定义 payload 里的字典同样必须被复制。
    /// A dictionary inside a custom payload must be copied too.
    /// </summary>
    [Fact]
    public void Predicate_ShouldCopyCustomPayloadDictionary() {
        var payload = new Dictionary<string, string> { ["k"] = "v" };
        var predicate = new BooleanCustom(payload);
        var plan = new QueryPlan(root: new QuerySource("job"), predicate: predicate);

        payload["k2"] = "v2";

        var stored = (BooleanCustom)plan.Predicate!;
        // 字符串→字符串字典保留了自己的类型（比退化成无类型字典更好），因此按原类型断言。
        // A string-to-string dictionary keeps its own type (better than degrading to an untyped one), so assert
        // on that type.
        ((IReadOnlyDictionary<string, string>)stored.Value).Should().HaveCount(1);
        ((IReadOnlyDictionary<string, string>)stored.Value).Should().ContainKey("k");
    }

    /// <summary>
    /// Join 条件也要冻结——只冻结根谓词会让 Join 侧留下可变引用，这种半冻结最难排查。
    /// Join conditions must be frozen too: freezing only the root predicate would leave a mutable reference on
    /// the join side, and half-frozen state is the hardest to diagnose.
    /// </summary>
    [Fact]
    public void JoinCondition_ShouldBeFrozen() {
        var payload = new List<string> { "a" };
        var join = new JoinSpec(
            Type: JoinType.Inner,
            Source: new QuerySource("machine"),
            Condition: new BooleanCustom(payload),
            Cardinality: JoinCardinality.ManyToOne);
        var plan = new QueryPlan(root: new QuerySource("job"), joins: new[] { join });

        payload.Add("b");

        ((IReadOnlyCollection<object?>)((BooleanCustom)plan.Joins[0].Condition).Value)
            .Should().HaveCount(1);
    }

    /// <summary>
    /// 复合表达式必须逐层递归，而不是只复制最外层。
    /// A compound expression must be frozen recursively rather than only at the outermost level.
    /// </summary>
    [Fact]
    public void CompoundPredicate_ShouldFreezeNestedPayload() {
        var payload = new List<string> { "a" };
        BooleanExpression predicate = new AndExpression(new BooleanExpression[] {
            BooleanConstant.True(),
            new NotExpression(new BooleanCustom(payload)),
        });
        var plan = new QueryPlan(root: new QuerySource("job"), predicate: predicate);

        payload.Add("b");

        var and = (AndExpression)plan.Predicate!;
        var not = (NotExpression)and.Operands[1];
        ((IReadOnlyCollection<object?>)((BooleanCustom)not.Operand).Value).Should().HaveCount(1);
    }

    /// <summary>
    /// **无法安全复制的 payload 必须被拒绝**，而不是原样保留引用：保留引用等于计划与调用方共享可变状态，
    /// "构造后不可变"就成了假的。
    /// **A payload that cannot be copied safely must be rejected** rather than kept by reference: keeping it means
    /// the plan shares mutable state with the caller and "immutable after construction" becomes false.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectUncopyablePayload() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            predicate: new BooleanCustom(new Uncopyable()));

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            plan.Validate();

        result.IsOk.Should().BeFalse("不可复制的 payload 必须拒绝 / an uncopyable payload must be rejected");
        ((Fuookami.Ospf.Utils.Error.ExErr<Fuookami.Ospf.Utils.Error.ErrorCode,
            RelationalQueryValidationError>)
            ((Fuookami.Ospf.Utils.Functional.Failed<Fuookami.Ospf.Utils.Functional.Success,
                Fuookami.Ospf.Utils.Error.ErrorCode,
                Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)result).Error)
            .TypedValue.Field.Should().Be("predicate");
    }

    /// <summary>Join 条件里的不可复制 payload 同样要拒绝 / An uncopyable payload in a join condition is rejected too.</summary>
    [Fact]
    public void Validate_ShouldRejectUncopyablePayloadInJoin() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            joins: new[] {
                new JoinSpec(
                    Type: JoinType.Inner,
                    Source: new QuerySource("machine"),
                    Condition: new BooleanCustom(new Uncopyable()),
                    Cardinality: JoinCardinality.ManyToOne),
            });

        plan.Validate().IsOk.Should().BeFalse();
    }

    /// <summary>
    /// 可复制的容器（这里是列表）必须**通过**校验——只测拒绝会让"拒绝一切"也算实现。
    /// A copyable container (a list here) must **pass**: a rejection-only suite would let "reject everything" count.
    /// </summary>
    [Fact]
    public void Validate_ShouldAcceptCopyablePayload() {
        var plan = new QueryPlan(
            root: new QuerySource("job"),
            predicate: new BooleanCustom(new List<string> { "a" }));

        plan.Validate().IsOk.Should().BeTrue();
    }

    /// <summary>不可复制 payload 的替身 / Stand-in for an uncopyable payload.</summary>
    private sealed class Uncopyable {
        /// <summary>随意状态 / Arbitrary state.</summary>
        public int Value { get; set; }
    }

    /// <summary>
    /// 结构本身不可变，因此冻结必须**保留原有语义**：比较表达式的操作符与两侧引用不得被改变。
    /// The structure is immutable, so freezing must **preserve semantics**: a comparison's operator and both
    /// sides must survive unchanged.
    /// </summary>
    [Fact]
    public void Freeze_ShouldPreserveComparisonSemantics() {
        var comparison = new Comparison<int>(
            Operator: ComparisonOperator.Lt,
            Left: new ScalarConstant<int>(1),
            Right: new ScalarConstant<int>(2));
        var plan = new QueryPlan(root: new QuerySource("job"), predicate: comparison);

        var stored = (Comparison<int>)plan.Predicate!;
        stored.Operator.Should().Be(ComparisonOperator.Lt);
        ((ScalarConstant<int>)stored.Left).Value.Should().Be(1);
        ((ScalarConstant<int>)stored.Right).Value.Should().Be(2);
    }

    /// <summary>
    /// 泛型实参不是 <c>bool</c> 的比较表达式同样要被正确处理——只处理 <c>Comparison&lt;bool&gt;</c> 会让最常见的
    /// 数值比较漏掉。
    /// A comparison whose generic argument is not <c>bool</c> must be handled too: covering only
    /// <c>Comparison&lt;bool&gt;</c> would miss the most common numeric comparisons.
    /// </summary>
    [Fact]
    public void Freeze_ShouldHandleNonBooleanGenericArgument() {
        var comparison = new Comparison<decimal>(
            Operator: ComparisonOperator.Eq,
            Left: new ScalarCustom<decimal>(new List<string> { "a" }),
            Right: new ScalarConstant<decimal>(0m));
        var plan = new QueryPlan(root: new QuerySource("job"), predicate: comparison);

        var stored = (Comparison<decimal>)plan.Predicate!;
        stored.Left.Should().BeOfType<ScalarCustom<decimal>>();
        ((IReadOnlyCollection<object?>)((ScalarCustom<decimal>)stored.Left).Value).Should().HaveCount(1);
    }

    /// <summary>
    /// 不修改原容器时，冻结不得改变表达式的可读形态——否则日志与错误信息会莫名其妙地变。
    /// Without mutating the original container, freezing must not change the expression's readable form, or
    /// logs and error messages would drift for no reason.
    /// </summary>
    [Fact]
    public void Freeze_ShouldKeepReadableForm() {
        BooleanExpression predicate = new AndExpression(new BooleanExpression[] {
            BooleanConstant.True(),
            BooleanConstant.False(),
        });
        var plan = new QueryPlan(root: new QuerySource("job"), predicate: predicate);

        plan.Predicate!.ToString().Should().Be(predicate.ToString());
    }
}
