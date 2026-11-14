// 谓词字段过滤提取 / Predicate field filter extraction.
// 与固定 Kotlin 基线 `PredicateFieldFilter.kt` 对应：把简单仓储谓词还原成字段级过滤条件，供轻量仓储复用，
// 免去为每种表达式形态写一遍解析。不支持时返回 null 而不是抛异常或静默丢条件。
// Mirrors the pinned Kotlin baseline's `PredicateFieldFilter.kt`: simple repository predicates are reduced to
// field-level filters so lightweight repositories can reuse them. Unsupported shapes yield null rather than throw or
// silently drop a condition.

using System;
using System.Collections;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Symbol.Expression;

namespace Fuookami.Ospf.Framework.Persistence.Expression;

/// <summary>
/// 字段级过滤条件 / Field-level filter.
/// </summary>
/// <remarks>
/// **`Eq = null` 表示"未设置"，不表示"等于 null"**：等值 null 由 <see cref="IsNull"/> 表达。两者含义不同，
/// 混用会让 `WHERE col = NULL`（永不成立）与 `WHERE col IS NULL` 互换，是静默错结果的经典来源。
///
/// **`Eq = null` means "not set", not "equals null"**: an equality against null is carried by
/// <see cref="IsNull"/>. Confusing the two swaps `WHERE col = NULL` (never true) with `WHERE col IS NULL` and is a
/// classic source of silently wrong results.
/// </remarks>
/// <param name="Eq">等值条件 / Equality condition.</param>
/// <param name="InValues">IN 集合条件 / IN-list condition.</param>
/// <param name="Gt">大于条件 / Greater-than condition.</param>
/// <param name="Ge">大于等于条件 / Greater-than-or-equal condition.</param>
/// <param name="Lt">小于条件 / Less-than condition.</param>
/// <param name="Le">小于等于条件 / Less-than-or-equal condition.</param>
/// <param name="IsNull">空值检查；true 表示 IS NULL，false 表示 IS NOT NULL / Null check; true is IS NULL.</param>
public sealed record FieldFilter(
    object? Eq = null,
    IReadOnlyList<object?>? InValues = null,
    object? Gt = null,
    object? Ge = null,
    object? Lt = null,
    object? Le = null,
    bool? IsNull = null);

/// <summary>
/// 谓词字段过滤提取扩展 / Predicate field filter extraction extensions.
/// </summary>
public static class PredicateFieldFilterExtensions {
    /// <summary>
    /// 尝试将谓词解析为字段名到等值条件的映射 / Parse a predicate into field-to-equality filters.
    /// </summary>
    /// <param name="expression">谓词，可为空 / Predicate, may be null.</param>
    /// <returns>字段过滤映射；不支持时返回 null / Field filters, or null when unsupported.</returns>
    public static IReadOnlyDictionary<string, object?>? EqFilters(
        this BooleanExpression? expression) {
        if (expression is null) {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }
        var filters = new Dictionary<string, object?>(StringComparer.Ordinal);
        return CollectEq(expression, filters) ? filters : null;
    }

    /// <summary>
    /// 尝试将谓词解析为字段名到等值或 IN 条件的映射 / Parse a predicate into field-to-equality-or-IN filters.
    /// </summary>
    /// <param name="expression">谓词，可为空 / Predicate, may be null.</param>
    /// <returns>字段过滤映射；不支持时返回 null / Field filters, or null when unsupported.</returns>
    public static IReadOnlyDictionary<string, object?>? EqOrInFilters(
        this BooleanExpression? expression) {
        if (expression is null) {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }
        var filters = new Dictionary<string, object?>(StringComparer.Ordinal);
        return CollectEqOrIn(expression, filters) ? filters : null;
    }

    /// <summary>
    /// 尝试将谓词解析为字段级条件 / Parse a predicate into field-level filters.
    /// </summary>
    /// <param name="expression">谓词，可为空 / Predicate, may be null.</param>
    /// <returns>字段过滤映射；不支持时返回 null / Field filters, or null when unsupported.</returns>
    public static IReadOnlyDictionary<string, FieldFilter>? FieldFilters(
        this BooleanExpression? expression) {
        if (expression is null) {
            return new Dictionary<string, FieldFilter>(StringComparer.Ordinal);
        }
        var filters = new Dictionary<string, MutableFieldFilter>(StringComparer.Ordinal);
        if (!CollectField(expression, filters)) {
            return null;
        }
        var result = new Dictionary<string, FieldFilter>(filters.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, MutableFieldFilter> entry in filters) {
            result[entry.Key] = new FieldFilter(
                Eq: entry.Value.Eq,
                InValues: entry.Value.InValues,
                Gt: entry.Value.Gt,
                Ge: entry.Value.Ge,
                Lt: entry.Value.Lt,
                Le: entry.Value.Le,
                IsNull: entry.Value.IsNull);
        }
        return result;
    }

    private static bool CollectEq(
        BooleanExpression expression,
        IDictionary<string, object?> filters) {
        if (expression is AndExpression and) {
            foreach (BooleanExpression operand in and.Operands) {
                if (!CollectEq(operand, filters)) {
                    return false;
                }
            }
            return true;
        }
        if (expression is not AndExpression &&
            ParseComparison(expression) is { } comparison) {
            if (comparison.Operator != ComparisonOperator.Eq) {
                return false;
            }
            filters[comparison.Path] = comparison.Value;
            return true;
        }
        return false;
    }

    private static bool CollectEqOrIn(
        BooleanExpression expression,
        IDictionary<string, object?> filters) {
        if (expression is AndExpression and) {
            foreach (BooleanExpression operand in and.Operands) {
                if (!CollectEqOrIn(operand, filters)) {
                    return false;
                }
            }
            return true;
        }
        if (expression is AndExpression) {
            return false;
        }
        if (ParseComparison(expression) is { } comparison) {
            if (comparison.Operator != ComparisonOperator.Eq) {
                return false;
            }
            filters[comparison.Path] = comparison.Value;
            return true;
        }
        if (ParseInValues(expression) is { } membership) {
            filters[membership.Path] = membership.Values;
            return true;
        }
        return false;
    }

    private static bool CollectField(
        BooleanExpression expression,
        IDictionary<string, MutableFieldFilter> filters) {
        if (expression is AndExpression and) {
            foreach (BooleanExpression operand in and.Operands) {
                if (!CollectField(operand, filters)) {
                    return false;
                }
            }
            return true;
        }
        if (expression is AndExpression) {
            return false;
        }
        if (expression is NullCheck check) {
            MutableFilter(filters, check.Path.Value).IsNull =
                check.Type == NullCheckType.IsNull;
            return true;
        }
        if (ParseComparison(expression) is { } comparison) {
            MutableFieldFilter filter = MutableFilter(filters, comparison.Path);
            switch (comparison.Operator) {
                case ComparisonOperator.Eq:
                    // 等值 null 必须落到 IsNull，否则后续拼出的 `= NULL` 永不成立。
                    // An equality against null must land in IsNull, or the emitted `= NULL` never matches.
                    if (comparison.Value is null) {
                        filter.IsNull = true;
                    }
                    else {
                        filter.Eq = comparison.Value;
                    }
                    return true;
                case ComparisonOperator.Gt:
                    filter.Gt = comparison.Value;
                    return true;
                case ComparisonOperator.Ge:
                    filter.Ge = comparison.Value;
                    return true;
                case ComparisonOperator.Lt:
                    filter.Lt = comparison.Value;
                    return true;
                case ComparisonOperator.Le:
                    filter.Le = comparison.Value;
                    return true;
                default:
                    // Ne 无法用上面任何一个字段表达，必须整体放弃而不是忽略这个条件。
                    // Ne maps onto none of the fields above, so the whole parse is abandoned rather than dropping
                    // the condition.
                    return false;
            }
        }
        if (ParseInValues(expression) is { } membership) {
            MutableFilter(filters, membership.Path).InValues = membership.Values;
            return true;
        }
        return false;
    }

    private static MutableFieldFilter MutableFilter(
        IDictionary<string, MutableFieldFilter> filters,
        string path) {
        if (!filters.TryGetValue(path, out MutableFieldFilter? filter)) {
            filter = new MutableFieldFilter();
            filters[path] = filter;
        }
        return filter;
    }

    /// <summary>
    /// 尝试解析字段与常量之间的比较 / Parse a field-to-constant comparison.
    /// </summary>
    /// <remarks>
    /// 只接受"字段 ↔ 常量"。字段在右侧时**操作符方向必须翻转**（`5 &lt; field` 等价于 `field &gt; 5`），
    /// 不翻转会把大小关系反过来。列对列、函数对常量都不在此列，返回 null。
    ///
    /// Only "field ↔ constant" is accepted. When the field is on the right the **operator must flip** (`5 &lt; field`
    /// equals `field &gt; 5`); without flipping the ordering is inverted. Column-to-column and function comparisons
    /// return null.
    /// </remarks>
    /// <param name="expression">布尔表达式 / Boolean expression.</param>
    /// <returns>解析结果或 null / Parsed result or null.</returns>
    private static ParsedComparison? ParseComparison(BooleanExpression expression) {
        Type type = expression.GetType();
        if (!type.IsGenericType ||
            type.GetGenericTypeDefinition() !=
                typeof(Fuookami.Ospf.Math.Symbol.Expression.Comparison<>)) {
            return null;
        }
        object? left = Property(expression, "Left");
        object? right = Property(expression, "Right");
        if (left is null || right is null) {
            return null;
        }
        var @operator = (ComparisonOperator)Property(expression, "Operator")!;
        if (IsReference(left) && IsConstant(right)) {
            return new ParsedComparison(ReferencePath(left)!, @operator, ConstantValue(right));
        }
        if (IsReference(right) && IsConstant(left)) {
            return new ParsedComparison(
                ReferencePath(right)!,
                Flip(@operator),
                ConstantValue(left));
        }
        return null;
    }

    /// <summary>
    /// 尝试解析字段 <c>IN</c> 常量集合 / Parse a field IN constant list.
    /// </summary>
    /// <remarks>
    /// <c>NOT IN</c>、空集合、以及任一候选不是常量时都返回 null——把 `NOT IN` 当 `IN` 处理会**返回本该排除的行**。
    ///
    /// <c>NOT IN</c>, an empty list, and any non-constant candidate all yield null; treating `NOT IN` as `IN` would
    /// **return the rows that should have been excluded**.
    /// </remarks>
    /// <param name="expression">布尔表达式 / Boolean expression.</param>
    /// <returns>解析结果或 null / Parsed result or null.</returns>
    private static ParsedInValues? ParseInValues(BooleanExpression expression) {
        Type type = expression.GetType();
        if (!type.IsGenericType ||
            type.GetGenericTypeDefinition() != typeof(InExpression<>)) {
            return null;
        }
        if ((bool)Property(expression, "Negated")!) {
            return null;
        }
        object? value = Property(expression, "Value");
        if (value is null || !IsReference(value)) {
            return null;
        }
        var values = new List<object?>();
        foreach (object candidate in (IEnumerable)Property(expression, "Candidates")!) {
            if (!IsConstant(candidate)) {
                return null;
            }
            values.Add(ConstantValue(candidate));
        }
        return values.Count == 0
            ? null
            : new ParsedInValues(ReferencePath(value)!, values);
    }

    private static ComparisonOperator Flip(ComparisonOperator @operator) => @operator switch {
        ComparisonOperator.Lt => ComparisonOperator.Gt,
        ComparisonOperator.Le => ComparisonOperator.Ge,
        ComparisonOperator.Gt => ComparisonOperator.Lt,
        ComparisonOperator.Ge => ComparisonOperator.Le,
        _ => @operator,
    };

    private static bool IsReference(object scalar) =>
        scalar.GetType().IsGenericType &&
        scalar.GetType().GetGenericTypeDefinition() == typeof(ScalarReference<>);

    private static bool IsConstant(object scalar) =>
        scalar.GetType().IsGenericType &&
        scalar.GetType().GetGenericTypeDefinition() == typeof(ScalarConstant<>);

    private static string? ReferencePath(object reference) =>
        Property(reference, "Path") is PropertyPath path ? path.Value : null;

    private static object? ConstantValue(object constant) =>
        Property(constant, "Value");

    private static object? Property(object instance, string name) =>
        instance.GetType().GetProperty(name)?.GetValue(instance);

    /// <summary>可变的字段级过滤器 / Mutable field-level filter.</summary>
    private sealed class MutableFieldFilter {
        /// <summary>等值条件 / Equality condition.</summary>
        public object? Eq { get; set; }

        /// <summary>IN 集合条件 / IN-list condition.</summary>
        public IReadOnlyList<object?>? InValues { get; set; }

        /// <summary>大于条件 / Greater-than condition.</summary>
        public object? Gt { get; set; }

        /// <summary>大于等于条件 / Greater-than-or-equal condition.</summary>
        public object? Ge { get; set; }

        /// <summary>小于条件 / Less-than condition.</summary>
        public object? Lt { get; set; }

        /// <summary>小于等于条件 / Less-than-or-equal condition.</summary>
        public object? Le { get; set; }

        /// <summary>空值检查 / Null check.</summary>
        public bool? IsNull { get; set; }
    }

    /// <summary>字段与常量比较的解析结果 / Parsed field-to-constant comparison.</summary>
    /// <param name="Path">字段路径 / Field path.</param>
    /// <param name="Operator">比较操作符 / Comparison operator.</param>
    /// <param name="Value">常量值 / Constant value.</param>
    private sealed record ParsedComparison(
        string Path,
        ComparisonOperator Operator,
        object? Value);

    /// <summary>字段 IN 条件的解析结果 / Parsed field IN condition.</summary>
    /// <param name="Path">字段路径 / Field path.</param>
    /// <param name="Values">候选常量值 / Candidate constant values.</param>
    private sealed record ParsedInValues(string Path, IReadOnlyList<object?> Values);
}
