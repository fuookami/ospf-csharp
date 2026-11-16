// 布尔翻译器不再恒返回 null/ The boolean translator stops returning null unconditionally.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.EfCore;
using Fuookami.Ospf.Math.Symbol.Expression;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence.EfCore;

/// <summary>
/// **布尔翻译器不再恒返回 null** / The boolean translator stops returning null unconditionally.
/// </summary>
/// <remarks>
/// `DefaultBooleanTranslator.Translate` 第一版是 `=> null`：不看入参、恒不翻译。上层把 `null` 读成"这个形状翻不了"，
/// 于是每个调用方都安静地拿到"不支持"，而它从来没看过谓词——**静默的 `null` 比抛异常更难查**。
///
/// 现在真正翻译等值与 IN 两种形状（`PredicateFieldFilterExtensions.EqOrInFilters` 摊平），
/// 范围比较与 `IS NULL` 还没接，天花板写在注释里。
///
/// The first version of `DefaultBooleanTranslator.Translate` was `=> null`: it never looked at its input. The caller reads `null` as "cannot translate this shape", so every caller
/// quietly got "unsupported" while nothing had been examined — **a silent `null` is harder to diagnose than a throw**. It now really translates equality and in-list
/// (`PredicateFieldFilterExtensions.EqOrInFilters`), with ranges and `IS NULL` still unwired and the ceiling recorded in the comment.
/// </remarks>
public class BooleanTranslatorTests {
    private sealed class Row {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Size { get; set; }
    }

    private static readonly List<Row> Rows = new() {
        new Row { Id = 1, Name = "a", Size = 10 },
        new Row { Id = 2, Name = "b", Size = 20 },
        new Row { Id = 3, Name = "c", Size = 30 },
    };

    /// <summary>单字段等值谓词 / A single-field equality predicate.</summary>
    /// <param name="path">字段路径 / Field path.</param>
    /// <param name="value">目标值 / The target value.</param>
    /// <returns>谓词 / The predicate.</returns>
    private static BooleanExpression Compare(string path, ComparisonOperator @operator, object value) => value switch {
        string text => new Fuookami.Ospf.Math.Symbol.Expression.Comparison<string>(
            Operator: @operator,
            Left: new ScalarReference<string>(new PropertyPath(path)),
            Right: new ScalarConstant<string>(text)),
        int number => new Fuookami.Ospf.Math.Symbol.Expression.Comparison<int>(
            Operator: @operator,
            Left: new ScalarReference<int>(new PropertyPath(path)),
            Right: new ScalarConstant<int>(number)),
        _ => throw new NotSupportedException(),
    };

    /// <summary>单字段 IN 谓词 / A single-field in-list predicate.</summary>
    /// <param name="path">字段路径 / Field path.</param>
    /// <param name="values">候选值 / Candidate values.</param>
    /// <returns>谓词 / The predicate.</returns>
    private static BooleanExpression InList(string path, params int[] values) => new InExpression<int>(
        Value: new ScalarReference<int>(new PropertyPath(path)),
        Candidates: values.Select(value => (ScalarExpression<int>)new ScalarConstant<int>(value)).ToList());

    private static IReadOnlyList<Row> Apply(Expression<Func<Row, bool>>? translated) =>
        translated is null ? Rows : Rows.AsQueryable().Where(translated).ToList();

    /// <summary>
    /// **等值谓词真的翻成过滤条件** / An equality predicate really becomes a filter.
    /// </summary>
    /// <remarks>
    /// 三点成对：翻译结果**不是 `null`**（第一版恒 `null`）/ 套到数据上只留一行 / 换一个等值得到另一行——
    /// 若翻译器仍返回 `null`，第 2 点会拿到全部三行，第 3 点会拿到同样的三行。
    ///
    /// Three paired points: the translation is **not `null`** (the first version always was); applied to the data it leaves exactly one row; and a different equality yields a different
    /// row — with a `null` translation point 2 would see all three rows and point 3 the same three.
    /// </remarks>
    [Fact]
    public void Translate_ShouldTurnAnEqualityPredicateIntoAFilter() {
        var translator = new DefaultBooleanTranslator<Row>();
        BooleanExpression whereIdIs2 = Compare("Id", ComparisonOperator.Eq, 2);

        Expression<Func<Row, bool>>? translated = translator.Translate(whereIdIs2);

        translated.Should().NotBeNull("an equality predicate is translatable");
        IReadOnlyList<Row> matched = Apply(translated);
        matched.Should().ContainSingle();
        matched[0].Id.Should().Be(2);

        Expression<Func<Row, bool>>? byName = translator.Translate(Compare("Name", ComparisonOperator.Eq, "c"));
        byName.Should().NotBeNull();
        IReadOnlyList<Row> byNameRows = Apply(byName);
        byNameRows.Should().ContainSingle();
        byNameRows[0].Id.Should().Be(3, "a different equality must select a different row");
    }

    /// <summary>
    /// **IN 谓词翻成集合包含** / An in-list predicate becomes a containment test.
    /// </summary>
    /// <remarks>
    /// 四点成对：不是 `null` / 两元素集合命中两行 / 单元素集合命中一行 / 空集合命中零行——
    /// 最后一条是 `Enumerable.Contains` 给的（手写 `||` 链在空集合上容易退化成"恒真"）。
    ///
    /// Four paired points: not `null`; a two-element list matches two rows; a single element matches one; and an empty list matches none — the last one comes from
    /// `Enumerable.Contains` (a hand-written `||` chain tends to degenerate to "always true" on an empty set).
    /// </remarks>
    [Fact]
    public void Translate_ShouldTurnAnInListPredicateIntoAContainment() {
        var translator = new DefaultBooleanTranslator<Row>();

        Expression<Func<Row, bool>>? two = translator.Translate(
            InList("Id", 1, 3));
        two.Should().NotBeNull();
        Apply(two).Select(row => row.Id).Should().BeEquivalentTo(new[] { 1, 3 });

        Expression<Func<Row, bool>>? one = translator.Translate(
            InList("Size", 20));
        one.Should().NotBeNull();
        Apply(one).Should().ContainSingle();

        // **空集合是"翻不了"，不是"全命中"**：`EqOrInFilters` 对空 IN 返回 `null`（它拒绝这个形状），
        // 翻译器跟着返回 `null`。这里钉的是"返回 null 而不是恒真的谓词"——后者会让上层把该查不到的行全查出来。
        // **An empty set is "cannot translate", not "matches everything"**: `EqOrInFilters` returns `null` for an empty in-list (it refuses the shape) and the translator follows.
        // What is pinned is "null rather than an always-true predicate" — the latter would return every row the caller meant to exclude.
        translator.Translate(InList("Id")).Should().BeNull(
            "an empty in-list is refused rather than silently turned into 'always true'");
    }

    /// <summary>
    /// **翻不了的形状仍然返回 `null`，但是看过之后** / Untranslatable shapes still yield `null`, but only after looking.
    /// </summary>
    /// <remarks>
    /// 三点成对：不是 `BooleanExpression` 的入参 → `null` / `null` 入参 → 抛 `ArgumentNullException`（不是悄悄返回 `null`）/
    /// 等值谓词 → 不是 `null`。第三点是与前两点的对照：`null` 现在是"这个形状不行"，而不是"我不看"。
    ///
    /// Three paired points: a non-`BooleanExpression` argument yields `null`; a `null` argument throws `ArgumentNullException` instead of quietly returning `null`; and an equality
    /// predicate does not. The third contrasts with the first two: `null` now means "this shape is not expressible" rather than "I did not look".
    /// </remarks>
    [Fact]
    public void Translate_ShouldReturnNullOnlyAfterRejectingTheShape() {
        var translator = new DefaultBooleanTranslator<Row>();

        translator.Translate("not a predicate").Should().BeNull("a foreign object is a shape it cannot express");
        Action nullArgument = () => translator.Translate(null!);
        nullArgument.Should().Throw<ArgumentNullException>("silently returning null for a null argument hides a caller bug");
        translator.Translate(Compare("Id", ComparisonOperator.Eq, 1)).Should().NotBeNull();
    }
}
