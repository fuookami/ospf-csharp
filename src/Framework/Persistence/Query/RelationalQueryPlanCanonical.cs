// 关系查询计划规范化表示 / Relational query plan canonical representation.
// 与固定 Kotlin 基线 `canonical`／`canonicalQueryKey` 对应。所有片段用"长度前缀"编码，避免拼接歧义
// （`a`+`bc` 与 `ab`+`c` 若不长度前缀会撞成同一个字符串）。
// Mirrors the pinned Kotlin baseline's `canonical`/`canonicalQueryKey`. Every fragment is length-prefixed to
// remove concatenation ambiguity (`a`+`bc` and `ab`+`c` would otherwise collide).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Fuookami.Ospf.Math.Symbol.Expression;

namespace Fuookami.Ospf.Framework.Persistence.Query;

/// <summary>
/// 关系查询计划规范化渲染器 / Relational query plan canonical renderer.
/// </summary>
/// <remarks>
/// **一处与基线一致但容易被误改的语义**：`And`／`Or` 的操作数在渲染时**按字符串排序**，`In` 的候选集同理。
/// 排序是规范化等价的前提——不排序的话，同一逻辑条件换个书写顺序就会得到不同的哈希。不要为了"保留用户书写
/// 顺序"去掉它。
///
/// **C# 专用范围**：本实现与基线的字符串**不保证逐字节相同**（C# 侧表达式节点的类型集合与 Kotlin 并不一致，
/// 例如没有 `ScalarConditional`／`ScalarBoolean`）。因此 `canonical`／`hash` 只用于 C# 内部与 C# 之间
/// 的稳定性比较，**不得**用来声称与 Kotlin 的哈希一致。
///
/// **A baseline behaviour that is easy to break by accident**: `And`/`Or` operands and `In` candidates are
/// **sorted by their rendered string**. That sorting is what makes logical equivalence canonical — without it the
/// same condition in a different writing order hashes differently. Do not remove it to "preserve the user's
/// order".
///
/// **C#-only scope**: the string is **not guaranteed byte-identical** to the baseline (the C# expression node set
/// differs, e.g. no `ScalarConditional`/`ScalarBoolean`). `canonical`/`hash` are therefore stable within C#
/// only and **must not** be used to claim hash equality with Kotlin.
/// </remarks>
internal static class RelationalQueryPlanCanonical {
    /// <summary>
    /// 渲染规范化计划 / Render the canonical plan.
    /// </summary>
    /// <param name="plan">查询计划 / Query plan.</param>
    /// <returns>规范化字符串 / Canonical string.</returns>
    public static string Render(RelationalQueryPlan plan) {
        ArgumentNullException.ThrowIfNull(plan);
        var text = new StringBuilder();
        text.Append("root=");
        AppendSource(text, plan.Root);

        text.Append("|joins=").Append(plan.Joins.Count.ToString(CultureInfo.InvariantCulture));
        foreach (JoinSpec join in plan.Joins) {
            text.Append(';')
                .Append(join.Type)
                .Append(':')
                .Append(join.Cardinality)
                .Append(':');
            AppendSource(text, join.Source);
            text.Append(':');
            AppendToken(text, ExpressionKey(join.Condition));
        }

        text.Append("|predicate=");
        AppendToken(text, plan.Predicate is null ? "" : ExpressionKey(plan.Predicate));

        text.Append("|projections=")
            .Append(plan.Projections.Count.ToString(CultureInfo.InvariantCulture));
        foreach (ProjectionSpec projection in plan.Projections) {
            text.Append(';');
            AppendColumn(text, projection.Column);
            text.Append(':');
            AppendToken(text, projection.Alias ?? "");
        }

        text.Append("|distinct=")
            .Append(plan.Distinct ? "True" : "False");

        text.Append("|groupBy=");
        AppendColumns(text, plan.GroupBy);

        text.Append("|orderBy=")
            .Append(plan.OrderBy.Count.ToString(CultureInfo.InvariantCulture));
        foreach (OrderSpec order in plan.OrderBy) {
            text.Append(';');
            AppendColumn(text, order.Column);
            text.Append(':').Append(order.Direction).Append(':').Append(order.Nulls);
        }

        text.Append("|page=");
        if (plan.Page is { } page) {
            text.Append(page.Limit?.ToString(CultureInfo.InvariantCulture) ?? "unbounded")
                .Append(':')
                .Append(page.Offset.ToString(CultureInfo.InvariantCulture));
        }

        text.Append("|rootKey=");
        AppendColumns(text, plan.RootKey);
        return text.ToString();
    }

    /// <summary>
    /// 渲染表达式键 / Render an expression key.
    /// </summary>
    /// <param name="expression">布尔表达式 / Boolean expression.</param>
    /// <returns>表达式键 / The expression key.</returns>
    public static string ExpressionKey(BooleanExpression expression) {
        ArgumentNullException.ThrowIfNull(expression);
        return expression switch {
            BooleanConstant constant => Node("BooleanConstant", constant.Value.ToString()),
            NullCheck check => Node("NullCheck", check.Type.ToString(), check.Path.Value),
            AndExpression and => Node(
                "And",
                and.Operands.Select(ExpressionKey).OrderBy(key => key, StringComparer.Ordinal)),
            OrExpression or => Node(
                "Or",
                or.Operands.Select(ExpressionKey).OrderBy(key => key, StringComparer.Ordinal)),
            NotExpression not => Node("Not", ExpressionKey(not.Operand)),
            BooleanCustom custom => Node(
                "BooleanCustom",
                PayloadShape(custom.Value),
                DescriptionKey(custom.Description)),
            _ => expression.GetType().IsGenericType
                ? GenericExpressionKey(expression)
                : Node("Unhandled", expression.TypeName),
        };
    }

    private static string ScalarKey(object scalar) {
        ArgumentNullException.ThrowIfNull(scalar);
        Type type = scalar.GetType();
        if (!type.IsGenericType) {
            return Node("ScalarUnhandled", type.Name);
        }
        Type definition = type.GetGenericTypeDefinition();
        if (definition == typeof(ScalarConstant<>)) {
            return Node("ScalarConstant", PayloadShape(Get(scalar, "Value")));
        }
        if (definition == typeof(ScalarReference<>)) {
            object? path = Get(scalar, "Path");
            return Node("ScalarReference", path?.ToString() ?? "");
        }
        if (definition == typeof(ScalarSymbolReference<>)) {
            object? symbol = Get(scalar, "Symbol");
            return Node("ScalarSymbolReference", symbol?.ToString() ?? "");
        }
        if (definition == typeof(ScalarUnary<>)) {
            return Node(
                "ScalarUnary",
                Get(scalar, "Operator")?.ToString() ?? "",
                ScalarKey(Get(scalar, "Operand")!));
        }
        if (definition == typeof(ScalarBinary<>)) {
            return Node(
                "ScalarBinary",
                Get(scalar, "Operator")?.ToString() ?? "",
                ScalarKey(Get(scalar, "Left")!),
                ScalarKey(Get(scalar, "Right")!));
        }
        if (definition == typeof(ScalarFunction<>)) {
            var arguments = (IEnumerable)Get(scalar, "Arguments")!;
            return Node(
                "ScalarFunction",
                Get(scalar, "Name")?.ToString() ?? "",
                Node("Arguments", arguments.Cast<object>().Select(ScalarKey)));
        }
        if (definition == typeof(ScalarCustom<>)) {
            return Node(
                "ScalarCustom",
                PayloadShape(Get(scalar, "Value")),
                DescriptionKey(Get(scalar, "Description") as string));
        }
        return Node("ScalarUnhandled", type.Name);
    }

    private static object? Get(object instance, string property) =>
        instance.GetType().GetProperty(property)?.GetValue(instance);

    /// <summary>
    /// 渲染泛型未知的表达式节点 / Render an expression node whose generic argument is unknown.
    /// </summary>
    /// <remarks>
    /// <c>Comparison&lt;T&gt;</c>／<c>InExpression&lt;T&gt;</c>／<c>PatternMatch&lt;T&gt;</c> 的 <c>T</c> 运行时才
    /// 确定；按属性反射取值即可，与标量键的取值方式一致。
    /// </remarks>
    /// <param name="expression">表达式 / The expression.</param>
    /// <returns>表达式键 / The expression key.</returns>
    internal static string GenericExpressionKey(BooleanExpression expression) {
        ArgumentNullException.ThrowIfNull(expression);
        Type definition = expression.GetType().GetGenericTypeDefinition();
        if (definition == typeof(Fuookami.Ospf.Math.Symbol.Expression.Comparison<>)) {
            return Node(
                "Comparison",
                Get(expression, "Operator")?.ToString() ?? "",
                ScalarKey(Get(expression, "Left")!),
                ScalarKey(Get(expression, "Right")!));
        }
        if (definition == typeof(InExpression<>)) {
            var candidates = (IEnumerable)Get(expression, "Candidates")!;
            return Node(
                "In",
                Get(expression, "Negated")?.ToString() ?? "",
                ScalarKey(Get(expression, "Value")!),
                Node(
                    "Candidates",
                    candidates.Cast<object>()
                        .Select(ScalarKey)
                        .OrderBy(key => key, StringComparer.Ordinal)));
        }
        if (definition == typeof(PatternMatch<>)) {
            return Node(
                "PatternMatch",
                Get(expression, "Mode")?.ToString() ?? "",
                Get(expression, "Negated")?.ToString() ?? "",
                ScalarKey(Get(expression, "Value")!),
                ScalarKey(Get(expression, "Pattern")!));
        }
        return Node("Unhandled", expression.TypeName);
    }

    private static string PayloadShape(object? value) {
        switch (value) {
            case null:
                return Node("null");
            case string text:
                return Node("string", text);
            case char or bool or sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal:
                return Node("literal", Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
            case Enum enumeration:
                return Node("enum", enumeration.GetType().Name, enumeration.ToString());
            case IDictionary dictionary:
                return Node(
                    "map",
                    dictionary.Count.ToString(CultureInfo.InvariantCulture));
            case IEnumerable sequence:
                return Node(
                    "sequence",
                    sequence.Cast<object?>().Count().ToString(CultureInfo.InvariantCulture));
            default:
                // 未知类型只留类型名，不把可能不稳定的 ToString 写进哈希。
                // An unknown type contributes only its type name; an unstable ToString never enters the hash.
                return Node("unsupported", value.GetType().Name);
        }
    }

    private static string DescriptionKey(string? description) =>
        Node("description", description ?? "");

    private static string Node(string tag, params string[] parts) => Node(tag, (IEnumerable<string>)parts);

    private static string Node(string tag, IEnumerable<string> parts) {
        var text = new StringBuilder();
        AppendToken(text, tag);
        IList<string> list = parts as IList<string> ?? parts.ToList();
        text.Append(list.Count.ToString(CultureInfo.InvariantCulture));
        foreach (string part in list) {
            AppendToken(text, part);
        }
        return text.ToString();
    }

    private static void AppendSource(StringBuilder text, QuerySource source) {
        AppendToken(text, source.Name);
        text.Append(':');
        AppendToken(text, source.Alias ?? "");
    }

    private static void AppendColumn(StringBuilder text, ColumnRef column) {
        AppendToken(text, column.Source);
        text.Append(':');
        AppendToken(text, column.Path);
    }

    private static void AppendColumns(StringBuilder text, IReadOnlyList<ColumnRef> columns) {
        text.Append(columns.Count.ToString(CultureInfo.InvariantCulture));
        foreach (ColumnRef column in columns) {
            text.Append(';');
            AppendColumn(text, column);
        }
    }

    private static void AppendToken(StringBuilder text, string value) {
        text.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
    }
}
