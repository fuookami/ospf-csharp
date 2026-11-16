#nullable enable

using Fuookami.Ospf.Framework.Persistence.Expression;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Parse;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;

/// <summary>**布尔表达式 → Mongo 过滤器** / Boolean expression → a Mongo filter.</summary>
/// <remarks>
/// 四个 Mongo 翻译器里，这一个最大、也最容易出错。
/// 标量、排序、更新三个翻译器已就位，这里是最后一个。
///
/// **查询写错不抛异常，它只是答错**——而这一层里有四处"看起来对、实际反了或漏了"的地方：
/// **反转比较**、**null 与"字段缺失"**、**正则转义顺序**、**不支持的谓词按策略处理**。
///
/// This translator is the largest of the four Mongo translators and the easiest to get wrong.
/// The scalar, sort and update translators are in place; this is the last one.
///
/// **A wrong query does not throw; it answers wrongly** — and this layer has four places that look right but are reversed or incomplete:
/// **reversed comparisons**, **null versus "field missing"**, **regex escaping order**, and **unsupported predicates handled by policy**.
/// </remarks>
public static class MongoBooleanTranslator
{
    /// <summary>把布尔表达式翻成 Mongo 过滤器 / Translate a boolean expression into a Mongo filter.</summary>
    /// <param name="expression">布尔表达式 / Boolean expression.</param>
    /// <param name="resolveFieldName">字段名解析器 / Field-name resolver.</param>
    /// <param name="policy">不支持的谓词怎么处理 / What to do with an unsupported predicate.</param>
    public static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> Translate(
        BooleanExpression expression,
        Func<string, string?>? resolveFieldName = null,
        UnsupportedPredicatePolicy policy = UnsupportedPredicatePolicy.FailFast)
    {
        ArgumentNullException.ThrowIfNull(expression);
        resolveFieldName ??= static name => name;
        return Translate(expression, resolveFieldName, policy, depth: 0);
    }

    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> Translate(
        BooleanExpression expression,
        Func<string, string?> resolveFieldName,
        UnsupportedPredicatePolicy policy,
        int depth)
    {
        // **深度护栏失败而不是返回原值**：返回原值会让一个没翻译的子树混进过滤器，
        // **而过滤器会"成功"执行，只是结果不对。**
        //
        // **The depth guard fails rather than returning the input**: returning it slips an untranslated subtree into the filter,
        // **and the filter "succeeds" with the wrong answer.**
        if (depth > MaxDepth)
        {
            return Unsupported(expression, policy, $"嵌套超过 {MaxDepth} 层 / nested deeper than {MaxDepth}");
        }

        switch (expression)
        {
            case BooleanConstant constant:
                // **`True` 是空过滤器（匹配全部），`False` 与 `Unknown` 都匹配不到任何东西。**
                // **`Unknown` 归到"匹配不到"是选择**：三值逻辑里的未知不能当成"通过"，
                // **当成通过等于让一个没算出来的条件放行数据。**
                //
                // **`True` is the empty filter (match everything); `False` and `Unknown` match nothing.**
                // **Folding `Unknown` into "matches nothing" is its choice**: unknown must not pass,
                // **because passing it lets an undecided condition release data.**
                return Results.Ok<BsonDocument?>(
                    constant.Value is Trivalent.True ? new BsonDocument() : AlwaysFalse());

            // **`Comparison<T>` 与 `System.Comparison<T>` 同名**，必须全限定。
            // **`Comparison<T>` collides with `System.Comparison<T>`**, so it is fully qualified.
            case Fuookami.Ospf.Math.Symbol.Expression.Comparison<object?> comparison:
                return TranslateComparison(comparison, resolveFieldName, policy, depth);

            case InExpression<object?> inExpression:
                return TranslateIn(inExpression, resolveFieldName, policy);

            case PatternMatch<object?> patternMatch:
                return TranslatePatternMatch(patternMatch, resolveFieldName, policy);

            case NullCheck nullCheck:
                return TranslateNullCheck(nullCheck, resolveFieldName, policy);

            case AndExpression and:
                return TranslateLogical(and.Operands, "$and", resolveFieldName, policy, depth);

            case OrExpression or:
                return TranslateLogical(or.Operands, "$or", resolveFieldName, policy, depth);

            case NotExpression not:
                return TranslateNot(not, resolveFieldName, policy, depth);

            case BooleanCustom:
                return Unsupported(expression, policy,
                    "自定义布尔表达式不支持 / Custom boolean expressions are not supported");

            default:
                return Unsupported(expression, policy,
                    $"不支持的布尔表达式 / Unsupported boolean expression: {expression.TypeName}");
        }
    }

    /// <summary>**比较：引用/常量、常量/引用（反转）、其余走 `$expr`** / Comparison: ref/const, const/ref (reversed), otherwise `$expr`.</summary>
    /// <remarks>
    /// **反转那一段是整层最容易错的地方**：`5 &lt; x` 必须变成 `x &gt; 5`。
    /// **忘记反转不会报错，只会把"大于 5"变成"小于 5"**——**结果集完全互补，而调用方看不出异常。**
    ///
    /// **The reversed branch is the easiest place in the whole layer to get wrong**: `5 &lt; x` must become `x &gt; 5`.
    /// **Forgetting to reverse does not throw; it turns "greater than 5" into "less than 5"** —
    /// **the result set becomes its own complement and the caller sees nothing unusual.**
    /// </remarks>
    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> TranslateComparison(
        Fuookami.Ospf.Math.Symbol.Expression.Comparison<object?> comparison,
        Func<string, string?> resolveFieldName,
        UnsupportedPredicatePolicy policy,
        int depth)
    {
        var leftRef = comparison.Left as ScalarReference<object?>;
        var leftConst = comparison.Left as ScalarConstant<object?>;
        var rightRef = comparison.Right as ScalarReference<object?>;
        var rightConst = comparison.Right as ScalarConstant<object?>;

        // **引用与 常量**：直接写成 `{field: {$op: value}}`。
        // **Reference versus constant**: written directly as `{field: {$op: value}}`.
        if (leftRef is not null && rightConst is not null)
        {
            return FieldComparison(leftRef, rightConst, comparison.Operator, reversed: false,
                resolveFieldName, comparison, policy);
        }

        // **常量与 引用**：**运算符必须反转**——`5 < x` 就是 `x > 5`。
        // **Constant versus reference**: **the operator has to be reversed** — `5 < x` is `x > 5`.
        if (leftConst is not null && rightRef is not null)
        {
            return FieldComparison(rightRef, leftConst, comparison.Operator, reversed: true,
                resolveFieldName, comparison, policy);
        }

        // **其余情况（引用与引用、常量与常量、含运算的）走 `$expr`**：
        // **`$expr` 才能比较两个字段**，而普通过滤器只能比较字段与常量。
        //
        // **Everything else (ref/ref, const/const, arithmetic) goes through `$expr`**.
        // **only `$expr` can compare two fields**, while ordinary filters compare a field with a constant.
        Result<BsonValue, ErrorCode, Error<ErrorCode>> left =
            MongoScalarTranslator.Translate(comparison.Left, name => resolveFieldName(name) ?? name);
        if (!left.IsOk)
        {
            return Unsupported(comparison, policy, "左侧标量无法翻译 / left scalar could not be translated");
        }
        Result<BsonValue, ErrorCode, Error<ErrorCode>> right =
            MongoScalarTranslator.Translate(comparison.Right, name => resolveFieldName(name) ?? name);
        if (!right.IsOk)
        {
            return Unsupported(comparison, policy, "右侧标量无法翻译 / right scalar could not be translated");
        }

        string exprOperator = ExprOperator(comparison.Operator);
        return Results.Ok<BsonDocument?>(new BsonDocument("$expr",
            new BsonDocument(exprOperator, new BsonArray { left.Value, right.Value })));
    }

    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> FieldComparison(
        ScalarReference<object?> reference,
        ScalarConstant<object?> constant,
        ComparisonOperator comparisonOperator,
        bool reversed,
        Func<string, string?> resolveFieldName,
        BooleanExpression expression,
        UnsupportedPredicatePolicy policy)
    {
        string? field = resolveFieldName(reference.Path.Value);
        if (field is null)
        {
            return Unsupported(expression, policy,
                $"比较路径未解析 / Unresolved comparison path: {reference.Path.Value}");
        }

        // **`null` 常量的比较要按触发器的语义处理，不能当成普通值**：
        // **这里直接拒绝**（`Null comparison constant is not supported`），
        // **因为 Mongo 里 `{f: null}` 会同时匹配"字段是 null"与"字段不存在"**，
        // **而写这个条件的人通常只想表达前者。**
        //
 // **A `null` constant cannot be treated as an ordinary value**: **this implementation rejects it outright**
        // (`Null comparison constant is not supported`), **because `{f: null}` in Mongo matches both
        // "the field is null" and "the field is missing"**, **while whoever wrote the condition usually meant only the former.**
        if (constant.Value is null)
        {
            return Unsupported(expression, policy,
                "null 比较常量不支持 / Null comparison constant is not supported");
        }

        string? mongoOperator = reversed
            ? ReversedFilterOperator(comparisonOperator)
            : FilterOperator(comparisonOperator);

        return Results.Ok<BsonDocument?>(new BsonDocument(field,
            new BsonDocument(mongoOperator, ToBson(constant.Value))));
    }

    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> TranslateIn(
        InExpression<object?> inExpression,
        Func<string, string?> resolveFieldName,
        UnsupportedPredicatePolicy policy)
    {
        if (inExpression.Value is not ScalarReference<object?> reference)
        {
            return Unsupported(inExpression, policy,
                "IN 的值必须是字段引用 / IN value must be a field reference");
        }

        string? field = resolveFieldName(reference.Path.Value);
        if (field is null)
        {
            return Unsupported(inExpression, policy,
                $"IN 路径未解析 / Unresolved IN path: {reference.Path.Value}");
        }

        // **候选值必须全是常量且非空**：**空 `$in: []` 在 Mongo 里匹配不到任何东西**，
        // **而"一个候选都没有"通常意味着上游构造错了**，静默变成一个永远为假的条件会把错误藏起来。
        //
        // **Candidates must all be constants and non-empty**: **`$in: []` matches nothing in Mongo**,
        // **and "no candidates at all" usually means the caller built it wrong**; silently turning that into
        // an always-false condition hides the mistake.
        var values = new BsonArray();
        foreach (ScalarExpression<object?> candidate in inExpression.Candidates)
        {
            if (candidate is not ScalarConstant<object?> constant)
            {
                return Unsupported(inExpression, policy,
                    "IN 候选值必须是标量常量 / IN candidates must be scalar constants");
            }
            values.Add(ToBson(constant.Value));
        }
        if (values.Count == 0)
        {
            return Unsupported(inExpression, policy,
                "IN 候选值不能为空 / IN candidates must be non-empty");
        }

        string mongoOperator = inExpression.Negated ? "$nin" : "$in";
        return Results.Ok<BsonDocument?>(new BsonDocument(field,
            new BsonDocument(mongoOperator, values)));
    }

    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> TranslatePatternMatch(
        PatternMatch<object?> patternMatch,
        Func<string, string?> resolveFieldName,
        UnsupportedPredicatePolicy policy)
    {
        if (patternMatch.Value is not ScalarReference<object?> reference)
        {
            return Unsupported(patternMatch, policy,
                "模式匹配的值必须是字段引用 / Pattern value must be a field reference");
        }

        string? field = resolveFieldName(reference.Path.Value);
        if (field is null)
        {
            return Unsupported(patternMatch, policy,
                $"模式路径未解析 / Unresolved pattern path: {reference.Path.Value}");
        }

        if (patternMatch.Pattern is not ScalarConstant<object?> constant || constant.Value is null)
        {
            return Unsupported(patternMatch, policy,
                "模式必须是标量常量 / Pattern must be a scalar constant");
        }

        string pattern = Convert.ToString(constant.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        string regex = patternMatch.Mode switch
        {
            // **`Exact`/`Prefix`/`Suffix`/`Contains` 都要先转义再拼锚点**：
            // **用户给的 `a.b` 里的点必须匹配字面点**，否则 `a.b` 会匹配 `axb`。
            //
            // **`Exact`/`Prefix`/`Suffix`/`Contains` escape before anchoring**.
            // **a dot in the user's `a.b` has to match a literal dot**, or `a.b` also matches `axb`.
            PatternMatchMode.Exact => "^" + EscapeRegex(pattern) + "$",
            PatternMatchMode.Prefix => "^" + EscapeRegex(pattern),
            PatternMatchMode.Suffix => EscapeRegex(pattern) + "$",
            PatternMatchMode.Contains => EscapeRegex(pattern),
            PatternMatchMode.Like => TranslateLikeToRegex(pattern),
            // **`Regex` 原样传递**：调用方明确要求正则，转义会破坏它的意图。
            // **`Regex` passes through verbatim**: the caller asked for a regex, and escaping would defeat that.
            PatternMatchMode.Regex => pattern,
            _ => EscapeRegex(pattern),
        };

        var document = new BsonDocument(field, new BsonDocument("$regex", new BsonString(regex)));
        return Results.Ok<BsonDocument?>(
            patternMatch.Negated ? new BsonDocument("$nor", new BsonArray { document }) : document);
    }

    /// <summary>**null 检查必须同时管"是 null"与"字段缺失"** / A null check has to cover both "is null" and "is missing".</summary>
    /// <remarks>
    /// **Mongo 里"字段是 null"与"字段不存在"是两种状态**，而业务上的"空"通常两者都算。
    ///
    /// **`isNull` 用 `$or: [{f: null}, {f: {$exists: false}}]`**；
    /// **`is not null` 用 `$and: [{f: {$exists: true}}, {f: {$ne: null}}]`**——
    /// **少写 `$exists: true`，缺失的字段会被判成"不为 null"。**
    ///
    /// **In Mongo "the field is null" and "the field does not exist" are two states**, while a business notion of
    /// "empty" usually covers both.
    ///
    /// **`isNull` uses `$or: [{f: null}, {f: {$exists: false}}]`**;
    /// **`is not null` uses `$and: [{f: {$exists: true}}, {f: {$ne: null}}]`** —
    /// **drop `$exists: true` and a missing field counts as "not null".**
    /// </remarks>
    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> TranslateNullCheck(
        NullCheck nullCheck,
        Func<string, string?> resolveFieldName,
        UnsupportedPredicatePolicy policy)
    {
        string? field = resolveFieldName(nullCheck.Path.Value);
        if (field is null)
        {
            return Unsupported(nullCheck, policy,
                $"null 检查路径未解析 / Unresolved null-check path: {nullCheck.Path.Value}");
        }

        if (nullCheck.IsNull)
        {
            return Results.Ok<BsonDocument?>(new BsonDocument("$or", new BsonArray
            {
                new BsonDocument(field, BsonNull.Value),
                new BsonDocument(field, new BsonDocument("$exists", false)),
            }));
        }

        return Results.Ok<BsonDocument?>(new BsonDocument("$and", new BsonArray
        {
            new BsonDocument(field, new BsonDocument("$exists", true)),
            new BsonDocument(field, new BsonDocument("$ne", BsonNull.Value)),
        }));
    }

    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> TranslateLogical(
        IReadOnlyList<BooleanExpression> operands,
        string mongoOperator,
        Func<string, string?> resolveFieldName,
        UnsupportedPredicatePolicy policy,
        int depth)
    {
        // **这里有一段"操作数为空"的分支（空 `and` 恒真、空 `or` 匹配不到），
        // 而 C# 的 `AndExpression`/`OrExpression` 构造函数直接拒绝空操作数**——
        // **所以那一段在这里不可达，已删掉，而不是留一段取不到的代码。**
        // `MongoBooleanTranslatorTests.TheEmptyLogical_ShouldBeImpossibleToConstruct` 钉住这个约束。
        //
 // **This implementation has an "operands are empty" branch here (empty `and` is always true, empty `or` matches nothing),
        // and the C# `AndExpression`/`OrExpression` constructors reject empty operands outright** —
        // **so that branch is unreachable here and was deleted rather than left as dead code.**
        // `MongoBooleanTranslatorTests.TheEmptyLogical_ShouldBeImpossibleToConstruct` pins the constraint.
        var conditions = new BsonArray();
        foreach (BooleanExpression operand in operands)
        {
            Result<BsonDocument?, ErrorCode, Error<ErrorCode>> translated =
                Translate(operand, resolveFieldName, policy, depth + 1);
            if (!translated.IsOk)
            {
                return translated;
            }
            conditions.Add(translated.Value ?? new BsonDocument());
        }

        return Results.Ok<BsonDocument?>(new BsonDocument(mongoOperator, conditions));
    }

    /// <summary>**否定必须对整个子树取反** / A negation has to apply to the whole subtree.</summary>
    /// <remarks>
    /// **`not (a and b)` 是 `$nor: [{$and: [...]}]`，不是"分别否定 a 和 b"**——
    /// **后者是德摩根律的结果，而写成 `$nor` 更不容易错。**
    ///
    /// **`not (a and b)` is `$nor: [{$and: [...]}]`, not "negate a and b separately"** —
    /// **the latter is what De Morgan gives, and `$nor` is harder to get wrong.**
    /// </remarks>
    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> TranslateNot(
        NotExpression not,
        Func<string, string?> resolveFieldName,
        UnsupportedPredicatePolicy policy,
        int depth)
    {
        Result<BsonDocument?, ErrorCode, Error<ErrorCode>> operand =
            Translate(not.Operand, resolveFieldName, policy, depth + 1);
        if (!operand.IsOk)
        {
            return operand;
        }

        return Results.Ok<BsonDocument?>(new BsonDocument("$nor",
            new BsonArray { operand.Value ?? new BsonDocument() }));
    }

    /// <summary>**"匹配不到任何东西"的过滤器** / A filter that matches nothing.</summary>
    /// <remarks>
    /// **`{_id: {$exists: false}}`**：每个文档都有 `_id`，所以这条过滤不了任何东西。
    /// **换成 `{_id: null}` 就错了**——它还会匹配 `_id` 缺失的文档（正常集合里没有，但语义不同）。
    ///
 /// **This implementation uses `{_id: {$exists: false}}`**: every document has an `_id`, so nothing passes.
    /// **`{_id: null}` would be wrong** — it also matches documents without an `_id` (absent in a normal collection, but a different meaning).
    /// </remarks>
    private static BsonDocument AlwaysFalse() =>
        new("_id", new BsonDocument("$exists", false));

    private static string FilterOperator(ComparisonOperator comparisonOperator) => comparisonOperator switch
    {
        ComparisonOperator.Eq => "$eq",
        ComparisonOperator.Ne => "$ne",
        ComparisonOperator.Lt => "$lt",
        ComparisonOperator.Le => "$lte",
        ComparisonOperator.Gt => "$gt",
        ComparisonOperator.Ge => "$gte",
        _ => "$eq",
    };

    /// <summary>**常量在左时，运算符必须反过来** / With the constant on the left, the operator flips.</summary>
    /// <remarks>
    /// **`Lt` 变 `$gt`、`Le` 变 `$gte`、`Gt` 变 `$lt`、`Ge` 变 `$lte`，而 `Eq`/`Ne` 不变。**
    /// **漏掉任何一条都会让结果集变成它的补集。**
    ///
    /// **`Lt` becomes `$gt`, `Le` becomes `$gte`, `Gt` becomes `$lt`, `Ge` becomes `$lte`, and `Eq`/`Ne` stay.**
    /// **Missing any one turns the result set into its complement.**
    /// </remarks>
    private static string ReversedFilterOperator(ComparisonOperator comparisonOperator) =>
        comparisonOperator switch
        {
            ComparisonOperator.Lt => "$gt",
            ComparisonOperator.Le => "$gte",
            ComparisonOperator.Gt => "$lt",
            ComparisonOperator.Ge => "$lte",
            ComparisonOperator.Eq => "$eq",
            ComparisonOperator.Ne => "$ne",
            _ => "$eq",
        };

    private static string ExprOperator(ComparisonOperator comparisonOperator) => comparisonOperator switch
    {
        ComparisonOperator.Eq => "$eq",
        ComparisonOperator.Ne => "$ne",
        ComparisonOperator.Lt => "$lt",
        ComparisonOperator.Le => "$lte",
        ComparisonOperator.Gt => "$gt",
        ComparisonOperator.Ge => "$gte",
        _ => "$eq",
    };

    /// <summary>**先转义，再替换通配符** / Escape first, then substitute the wildcards.</summary>
    /// <remarks>
    /// **顺序反了就是错的**：先替换 `%` → `.*`，再转义，`.*` 会变成 `\.\*`——
    /// **于是 `LIKE 'a%'` 变成匹配字面 `a.*`，一条也匹配不到。**
    ///
    /// **`_` 同理**：转义后再替换成 `.`，否则 `.` 会先被转义成 `\.`。
    ///
    /// **The order matters**: substituting `%` → `.*` before escaping turns `.*` into `\.\*` —
    /// **so `LIKE 'a%'` matches the literal `a.*` and finds nothing.**
    ///
    /// **Same for `_`**: substitute it after escaping, or the `.` gets escaped first.
    /// </remarks>
    private static string TranslateLikeToRegex(string pattern) =>
        EscapeRegex(pattern).Replace("%", ".*", StringComparison.Ordinal)
            .Replace("_", ".", StringComparison.Ordinal);

    private static string EscapeRegex(string pattern)
    {
        var builder = new StringBuilder(pattern.Length * 2);
        foreach (char character in pattern)
        {
            if (character is '[' or ']' or '{' or '}' or '(' or ')' or '*' or '+'
                or '?' or '.' or '\\' or '^' or '$' or '|')
            {
                builder.Append('\\');
            }
            builder.Append(character);
        }
        return builder.ToString();
    }

    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> Unsupported(
        BooleanExpression expression,
        UnsupportedPredicatePolicy policy,
        string reason)
    {
        // **三种策略都以失败返回，区别在错误的"种类"**：
        // **调用方靠种类判断"这条谓词能不能降级成永远为假"，而不是靠解析错误信息。**
        //
        // **All three policies return a failure; they differ in the error's kind**.
        // **the caller recognises whether a predicate may be degraded to always-false by that kind, not by parsing a message.**
        UnsupportedPredicateDetail detail = policy switch
        {
            UnsupportedPredicatePolicy.AlwaysFalse =>
                UnsupportedPredicateDetail.AlwaysFalse(expression.TypeName, reason, "MongoDB"),
            UnsupportedPredicatePolicy.ClientFilter =>
                UnsupportedPredicateDetail.ClientFilter(expression.TypeName, reason, "MongoDB"),
            _ => UnsupportedPredicateDetail.FailFast(expression.TypeName, reason, "MongoDB"),
        };
        _ = detail;
        return new Failed<BsonDocument?, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument,
            $"谓词翻译失败 / Predicate translation failed: {expression.TypeName}: {reason}",
            new ParseIssue(ParseIssueType.Semantic, reason));
    }

    private static BsonValue ToBson(object? value) => value switch
    {
        null => BsonNull.Value,
        BsonValue bson => bson,
        string text => new BsonString(text),
        bool flag => BsonBoolean.Create(flag),
        int number => new BsonInt32(number),
        long number => new BsonInt64(number),
        double number => new BsonDouble(number),
        decimal number => new BsonDecimal128(number),
        DateTime moment => new BsonDateTime(moment.ToUniversalTime()),
        _ => BsonValue.Create(value),
    };

    /// <summary>嵌套深度上限 / Maximum nesting depth.</summary>
    public const int MaxDepth = 64;
}
