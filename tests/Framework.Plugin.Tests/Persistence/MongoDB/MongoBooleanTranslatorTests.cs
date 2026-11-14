#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Expression;
using Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence.MongoTranslators;

/// <summary>
/// **查询写错不抛异常，它只是答错** / A wrong query does not throw; it answers wrongly.
/// </summary>
/// <remarks>
/// 基线 `MongoBooleanTranslator`（**422 行**），四个 Mongo 翻译器里最后一个、也最容易错的一个。
///
/// **这一层有四处"看起来对、实际反了或漏了"的地方，四条判据分别钉住：**
/// 反转比较、null 与缺失、正则转义顺序、空 `or`。
///
/// The baseline's `MongoBooleanTranslator` (**422 lines**), the last and most error-prone of the four Mongo translators.
///
/// **This layer has four places that look right but are reversed or incomplete, and four criteria pin them:**
/// reversed comparisons, null versus missing, regex escaping order, and empty `or`.
/// </remarks>
public class MongoBooleanTranslatorTests
{
    private static BsonDocument Translate(BooleanExpression expression) =>
        MongoBooleanTranslator.Translate(expression).Value!;

    private static ScalarReference<object?> Ref(string path) => new(new PropertyPath(path));

    private static ScalarConstant<object?> Const(object? value) => new(value);

    /// <summary>**`True` 匹配全部，`False`/`Unknown` 匹配不到** / `True` matches all; `False`/`Unknown` match nothing.</summary>
    /// <remarks>
    /// **`Unknown` 归到"匹配不到"是基线的选择**：三值逻辑里的未知不能当成"通过"，
    /// **当成通过等于让一个没算出来的条件放行数据。**
    ///
    /// **Folding `Unknown` into "matches nothing" is the baseline's choice**: unknown must not pass,
    /// **because passing it lets an undecided condition release data.**
    /// </remarks>
    [Fact]
    public void TheConstants_ShouldFollowThreeValuedLogic()
    {
        Translate(new BooleanConstant(new Trivalent.True())).Should().BeEmpty(
            "true is the empty filter, matching everything");

        // **`Should().Be(document)` 在 `BsonDocument` 上会走集合断言**，所以比较 JSON 文本。
        // **`Should().Be(document)` on `BsonDocument` resolves to a collection assertion**, so compare the JSON text.
        string falsy = Translate(new BooleanConstant(new Trivalent.False())).ToJson();
        falsy.Should().Be("{ \"_id\" : { \"$exists\" : false } }",
            "false has to match nothing, and every document has an _id");

        Translate(new BooleanConstant(new Trivalent.Unknown())).ToJson().Should().Be(falsy,
            "unknown must not pass, or an undecided condition releases data");
    }

    /// <summary>**常量在左时运算符必须反转** / A constant on the left flips the operator.</summary>
    /// <remarks>
    /// **`5 &lt; x` 必须变成 `x &gt; 5`。忘记反转不会报错，只会把"大于 5"变成"小于 5"，结果集完全互补。**
    ///
    /// **`5 &lt; x` must become `x &gt; 5`. Forgetting to reverse does not throw; it turns "greater than 5" into
    /// "less than 5", making the result set its own complement.**
    /// </remarks>
    [Theory]
    [InlineData(ComparisonOperator.Lt, "$gt")]
    [InlineData(ComparisonOperator.Le, "$gte")]
    [InlineData(ComparisonOperator.Gt, "$lt")]
    [InlineData(ComparisonOperator.Ge, "$lte")]
    [InlineData(ComparisonOperator.Eq, "$eq")]
    [InlineData(ComparisonOperator.Ne, "$ne")]
    public void TheConstantOnTheLeft_ShouldFlipTheOperator(ComparisonOperator op, string expected)
    {
        var comparison = new Fuookami.Ospf.Math.Symbol.Expression.Comparison<object?>(
            Operator: op,
            Left: Const(5),
            Right: Ref("Amount"));

        BsonDocument document = Translate(comparison);

        document.Contains("Amount").Should().BeTrue("the field name is the key");
        document["Amount"].AsBsonDocument.Names.Should().Contain(expected,
            $"'{op} 5' written with a field on the right means the operator becomes {expected}");
    }

    /// <summary>**引用在左时运算符不变** / A reference on the left keeps the operator.</summary>
    [Fact]
    public void TheReferenceOnTheLeft_ShouldKeepTheOperator()
    {
        var comparison = new Fuookami.Ospf.Math.Symbol.Expression.Comparison<object?>(
            Operator: ComparisonOperator.Lt,
            Left: Ref("Amount"),
            Right: Const(5));

        Translate(comparison)["Amount"].AsBsonDocument.Names.Should().Contain("$lt",
            "with the field on the left there is nothing to reverse");
    }

    /// <summary>**null 检查必须同时管"是 null"与"字段缺失"** / A null check covers both null and missing.</summary>
    /// <remarks>
    /// **少写 `$exists: true`，缺失的字段会被判成"不为 null"。**
    ///
    /// **Drop `$exists: true` and a missing field counts as "not null".**
    /// </remarks>
    [Fact]
    public void TheNullCheck_ShouldDistinguishNullFromMissing()
    {
        BsonDocument isNull = Translate(new NullCheck(new PropertyPath("Note"), NullCheckType.IsNull));
        string isNullText = isNull.ToJson();
        isNullText.Should().Contain("$or", "null covers both an explicit null and an absent field");
        isNullText.Should().Contain("$exists", "an absent field is what the second branch is for");

        BsonDocument notNull = Translate(new NullCheck(new PropertyPath("Note"), NullCheckType.IsNotNull));
        string notNullText = notNull.ToJson();
        notNullText.Should().Contain("$and",
            "not-null has to require both existence and inequality, or a missing field slips through");
        notNullText.Should().Contain("$ne");
    }

    /// <summary>**`Exact`/`Contains` 必须转义正则元字符** / `Exact`/`Contains` must escape regex metacharacters.</summary>
    /// <remarks>
    /// **用户给的 `a.b` 里的点必须匹配字面点**，否则 `a.b` 会匹配 `axb`。
    /// **这是"看起来更宽松"的错**——它不多报错，只是多返回了不该返回的行。
    ///
    /// **The dot in the user's `a.b` has to match a literal dot**, or `a.b` also matches `axb`.
    /// **This is the "looks more permissive" error** — it does not complain, it just returns rows it should not.
    /// </remarks>
    [Fact]
    public void TheExactPattern_ShouldEscapeMetacharacters()
    {
        var patternMatch = new PatternMatch<object?>(
            Value: Ref("Code"),
            Pattern: Const("a.b"),
            Mode: PatternMatchMode.Contains);

        string regex = Translate(patternMatch)["Code"].AsBsonDocument["$regex"].AsString;

        regex.Should().Be("a\\.b", "the dot has to be escaped so it matches only a literal dot");
    }

    /// <summary>**`Like` 先转义再替换通配符** / `Like` escapes before substituting wildcards.</summary>
    /// <remarks>
    /// **顺序反了就是错的**：先替换 `%` → `.*`，再转义，`.*` 会变成 `\.\*`——
    /// **于是 `LIKE 'a%'` 变成匹配字面 `a.*`，一条也匹配不到。**
    ///
    /// **The order matters**: substituting `%` before escaping turns `.*` into `\.\*` —
    /// **so `LIKE 'a%'` matches the literal `a.*` and finds nothing.**
    /// </remarks>
    [Fact]
    public void TheLikePattern_ShouldEscapeBeforeSubstitutingWildcards()
    {
        var prefixLike = new PatternMatch<object?>(
            Value: Ref("Name"),
            Pattern: Const("a%"),
            Mode: PatternMatchMode.Like);

        string regex = Translate(prefixLike)["Name"].AsBsonDocument["$regex"].AsString;

        regex.Should().Be("a.*",
            "the % becomes a wildcard, and it must not survive as an escaped literal");
    }

    /// <summary>**空逻辑在 C# 里根本构造不出来** / An empty logical cannot even be constructed in C#.</summary>
    /// <remarks>
    /// 基线 `translateAnd` 与 `translateOr` 都有一段"操作数为空"的分支（空 `and` 恒真、空 `or` 匹配不到），
    /// **因为 Kotlin 的 `AndExpression` 允许空列表。**
    ///
    /// **C# 的 `AndExpression` / `OrExpression` 构造函数直接拒绝空操作数**（实测
    /// `ArgumentException: And expression requires at least one operand`），
    /// **所以我按基线写的那两段分支在这里不可达——已删掉，而不是留一段取不到的代码。**
    ///
    /// **这条判据钉的是那个约束本身**：谁把构造函数的检查去掉，"空逻辑"就会从"不可能"变成"未处理"。
    ///
    /// The baseline's `translateAnd` and `translateOr` both have an "operands are empty" branch (empty `and` is always true,
    /// empty `or` matches nothing), **because Kotlin's `AndExpression` allows an empty list.**
    ///
    /// **The C# `AndExpression` / `OrExpression` constructors reject empty operands outright** (measured:
    /// `ArgumentException: And expression requires at least one operand`),
    /// **so the two branches written after the baseline are unreachable here — they were deleted rather than left as dead code.**
    ///
    /// **This pins the constraint itself**: remove the constructor check and "empty logical" goes from impossible to unhandled.
    /// </remarks>
    [Fact]
    public void TheEmptyLogical_ShouldBeImpossibleToConstruct()
    {
        FluentActions.Invoking(() => new AndExpression([]))
            .Should().Throw<System.ArgumentException>(
                "an AND with no operands is rejected, which is why the translator needs no empty branch");
        FluentActions.Invoking(() => new OrExpression([]))
            .Should().Throw<System.ArgumentException>(
                "an OR with no operands is rejected for the same reason");
    }

    /// <summary>**不支持的谓词必须失败** / An unsupported predicate has to fail.</summary>
    [Fact]
    public void TheCustomPredicate_ShouldFail()
    {
        MongoBooleanTranslator.Translate(new BooleanCustom(new object())).IsFailed.Should().BeTrue(
            "a custom predicate cannot be pushed into a Mongo filter");
    }

    /// <summary>**`NOT` 对整个子树取反** / `NOT` negates the whole subtree.</summary>
    [Fact]
    public void TheNot_ShouldWrapTheWholeSubtree()
    {
        var not = new NotExpression(new AndExpression([RefComparison("A"), RefComparison("B")]));

        BsonDocument document = Translate(not);

        document.Contains("$nor").Should().BeTrue("NOT wraps the whole subtree rather than negating each part");
        document["$nor"].AsBsonArray.Count.Should().Be(1, "one negated condition, not two");
        document["$nor"].AsBsonArray[0].AsBsonDocument.Contains("$and").Should().BeTrue(
            "the AND itself is preserved inside the negation");
        document["$nor"].AsBsonArray[0].AsBsonDocument["$and"].AsBsonArray.Count.Should().Be(2,
            "both operands of the AND survive inside the negation");
    }

    private static Fuookami.Ospf.Math.Symbol.Expression.Comparison<object?> RefComparison(string field) =>
        new(Operator: ComparisonOperator.Eq, Left: Ref(field), Right: Const(1));
}
