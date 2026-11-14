#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Query;
using Fuookami.Ospf.Math.Symbol.Expression;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Persistence;

/// <summary>
/// **"冻结"必须真的断开引用** / Freezing has to actually cut the reference.
/// </summary>
/// <remarks>
/// **`RelationalQueryExpressionFreezer`（329 行）在全仓测试里一次都没被提到过。**
/// 它做的是把查询计划里的自定义 payload **深拷贝一份**，好让"构造后不可变"这句话成立。
///
/// **失效的症状不是报错，而是"计划在构造之后还会变"**：
/// 调用方改一下自己传进来的那个 `List`，**已经构造好的查询计划就跟着变了**——
/// **而计划是拿去做缓存键、做相等比较、拿去另一个线程执行的东西。**
///
/// **`RelationalQueryExpressionFreezer` (329 lines) appears nowhere in the test tree.**
/// It deep-copies the custom payloads inside a query plan so that "immutable after construction" holds.
///
/// **When it fails the symptom is not an error but "the plan changes after construction"**:
/// the caller mutates the `List` it passed in, **and the already-built plan changes with it** —
/// **while the plan is what gets cached, compared for equality, and executed on another thread.**
/// </remarks>
public class RelationalQueryExpressionFreezerTests
{
    /// <summary>**改原集合不影响冻结后的副本** / Mutating the source must not reach the frozen copy.</summary>
    /// <remarks>
    /// **这是"冻结"二字的全部含义。** 只断言"副本内容相同"是不够的——
    /// **浅拷贝也满足那一条，而浅拷贝正是这里要防的东西。**
    ///
    /// **This is the whole meaning of "freeze".** Asserting "the copy has the same content" is not enough —
    /// **a shallow copy satisfies that too, and a shallow copy is exactly what this guards against.**
    /// </remarks>
    [Fact]
    public void TheFrozenSequence_ShouldNotFollowTheSource()
    {
        var source = new List<string> { "a", "b" };

        object? frozen = RelationalQueryExpressionFreezer.FreezePayload(source);

        source.Add("c");
        source[0] = "changed";

        // **元素类型会被擦成 `object`**：副本是 `ReadOnlyCollection<object>`，不是 `ReadOnlyCollection<string>`。
        // **断言要按实际形状写**——**按"我以为的类型"写，红的是断言而不是实现。**
        //
        // **The element type is erased to `object`**: the copy is a `ReadOnlyCollection<object>`, not `ReadOnlyCollection<string>`.
        // **Assert the actual shape** — **asserting the type I expected turns the criterion red instead of the implementation.**
        frozen.Should().BeAssignableTo<IEnumerable<object>>();
        // **`Equal` 的可变参数就是期望元素，没有 reason 位置**——**这是本仓第四次踩同一脚。**
        // **`Equal`'s variadic parameters are the expected elements, with no reason slot** — **the fourth time in this repo.**
        object[] expected = ["a", "b"];
        ((IEnumerable<object>)frozen!).Should().Equal(expected,
            "the copy was taken before the source changed");
    }

    /// <summary>**改原字典不影响冻结后的副本** / Mutating the source map must not reach the frozen copy.</summary>
    /// <remarks>
    /// **字典走的是另一条分支**（`IDictionary` 与 `IReadOnlyDictionary` 分别处理），
    /// **两条分支都要能断引用，否则只有一条是对的。**
    ///
    /// **Maps take a different branch** (`IDictionary` and `IReadOnlyDictionary` are handled separately),
    /// **and both have to cut the reference, or only one of them is right.**
    /// </remarks>
    [Fact]
    public void TheFrozenMap_ShouldNotFollowTheSource()
    {
        var source = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" };

        object? frozen = RelationalQueryExpressionFreezer.FreezePayload(source);

        source["k"] = "changed";
        source["added"] = "x";

        IReadOnlyDictionary<string, string> map =
            frozen.Should().BeAssignableTo<IReadOnlyDictionary<string, string>>().Subject;
        map.Should().ContainKey("k").WhoseValue.Should().Be("v");
        map.Should().NotContainKey("added");
    }

    /// <summary>**标量原样返回** / Scalars come back as they went in.</summary>
    /// <remarks>
    /// **标量是不可变的，拷贝没有意义**——**而"什么都拷贝一遍"会让值类型的装箱语义悄悄变化。**
    ///
    /// **Scalars are immutable, so copying them means nothing** — **and "copy everything" would quietly change boxing semantics for value types.**
    /// </remarks>
    [Theory]
    [InlineData("text")]
    [InlineData(42)]
    [InlineData(3.5)]
    [InlineData(true)]
    public void TheScalars_ShouldPassThrough(object value) =>
        RelationalQueryExpressionFreezer.FreezePayload(value).Should().Be(value);

    /// <summary>**不可复制的 payload 必须被点名，且给出字段路径** / An uncopyable payload must be named, with its field path.</summary>
    /// <remarks>
    /// **`IsCopyable` 与 `FreezePayload` 必须保持一致**：源码注释里写着
    /// "两者分叉会让'冻结时放过、校验时拒绝'或反之"。**这条判据钉住那个一致性的可观察一半。**
    ///
    /// **`IsCopyable` and `FreezePayload` have to agree**: the source comment says
    /// "if the two diverge, one would freeze what the other rejects". **This pins the observable half of that agreement.**
    /// </remarks>
    [Fact]
    public void TheUncopyablePayload_ShouldBeNamedWithItsPath()
    {
        var custom = new BooleanCustom(new object());

        RelationalQueryExpressionFreezer.FindUnsupportedPayload(custom, "where")
            .Should().Be("where", "a bare object cannot be copied, so the field is named");

        // **可复制的 payload 不该被点名**——**否则每个正常查询都会被拒。**
        // **A copyable payload must not be named** — **otherwise every normal query is rejected.**
        RelationalQueryExpressionFreezer.FindUnsupportedPayload(new BooleanCustom("text"), "where")
            .Should().BeNull("a string payload copies fine");
        RelationalQueryExpressionFreezer.FindUnsupportedPayload(new BooleanCustom(new List<string> { "a" }), "where")
            .Should().BeNull("a list payload is copied, not rejected");
    }

    /// <summary>**嵌套在 `Not` 里的 payload，路径要带上层级** / A payload nested under `Not` carries its path.</summary>
    /// <remarks>
    /// **只报"where"而不报层级，会让人在大表达式里找不到是哪一处**——
    /// **而这个类存在的理由正是"告诉调用方哪一段没法冻结"。**
    ///
    /// **Reporting only "where" without the level leaves the reader hunting inside a large expression** —
    /// **and telling the caller which part could not be frozen is the whole reason this class exists.**
    /// </remarks>
    [Fact]
    public void TheNestedPayload_ShouldCarryItsPath()
    {
        var expression = new NotExpression(new BooleanCustom(new object()));

        RelationalQueryExpressionFreezer.FindUnsupportedPayload(expression, "where")
            .Should().Be("where.operand", "the path has to say where inside the expression it sits");
    }
}
