// 关系查询 Join 图校验 / Relational query join-graph validation.
// 与`validate` 的 Join 部分及 `containsExactColumnCorrelation`／`containsBooleanConstant` 对应：
// 校验数据源命名不冲突、关联条件引用的都是已绑定的数据源，并且关联条件里**确实存在**一条目标侧与已绑定侧的
// 列对列关联。
// Plus `containsExactColumnCorrelation` and
// `containsBooleanConstant`: source naming must not conflict, references must resolve to already bound sources,
// and the condition must **actually contain** a column-to-column correlation between the joined side and a bound
// side.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Symbol.Expression;

namespace Fuookami.Ospf.Framework.Persistence.Query;

/// <summary>
/// 关系查询 Join 图校验器 / Relational query join-graph validator.
/// </summary>
internal static class RelationalQueryJoinValidator {
    /// <summary>
    /// 从列路径中取出数据源限定符 / Extract the source qualifier from a column path.
    /// </summary>
    /// <param name="path">列路径 / Column path.</param>
    /// <returns>限定符；路径不含限定符时为 null / The qualifier, or null when the path has none.</returns>
    public static string? SourceQualifier(string path) {
        ArgumentNullException.ThrowIfNull(path);
        int separator = path.IndexOf('.', StringComparison.Ordinal);
        // 必须同时满足"前面有内容"和"后面有内容"：`.x` 与 `x.` 都不是限定引用。
        // Both sides must be non-empty: `.x` and `x.` are not qualified references.
        return separator > 0 && separator < path.Length - 1
            ? path[..separator]
            : null;
    }

    /// <summary>
    /// 判断表达式是否包含精确的列对列关联 / Check whether an expression contains an exact column-to-column
    /// correlation.
    /// </summary>
    /// <remarks>
    /// 比较两端**必须直接是**限定列引用，且一端属于目标数据源、另一端属于已绑定数据源。`AND` 允许附带过滤条件
    /// （任一支路满足即可），`OR` 要求每个分支都保留关联（否则某些分支会退化成笛卡尔积），`NOT` 递归检查。
    ///
    /// Both comparison operands **must directly be** qualified column references, one on the target side and one
    /// on a bound side. `AND` tolerates extra filters (any branch suffices), `OR` requires every branch to retain a
    /// correlation (otherwise some branch degrades into a cross product), and `NOT` recurses.
    /// </remarks>
    /// <param name="expression">待检查的布尔表达式 / Boolean expression to inspect.</param>
    /// <param name="targetSources">目标数据源的名称与别名 / Names and aliases of the target source.</param>
    /// <param name="boundSources">已绑定数据源的名称与别名 / Names and aliases of already bound sources.</param>
    /// <returns>是否包含满足要求的列对列关联 / Whether a permitted correlation is present.</returns>
    public static bool ContainsExactColumnCorrelation(
        BooleanExpression expression,
        IReadOnlySet<string> targetSources,
        IReadOnlySet<string> boundSources) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(targetSources);
        ArgumentNullException.ThrowIfNull(boundSources);

        switch (expression) {
            case Fuookami.Ospf.Math.Symbol.Expression.Comparison<bool> comparison:
                return Compare(comparison.Left, comparison.Right, targetSources, boundSources);
            case AndExpression and:
                foreach (BooleanExpression operand in and.Operands) {
                    if (ContainsExactColumnCorrelation(operand, targetSources, boundSources)) {
                        return true;
                    }
                }
                return false;
            case OrExpression or: {
                if (or.Operands.Count == 0) {
                    return false;
                }
                foreach (BooleanExpression operand in or.Operands) {
                    if (!ContainsExactColumnCorrelation(operand, targetSources, boundSources)) {
                        return false;
                    }
                }
                return true;
            }
            case NotExpression not:
                return ContainsExactColumnCorrelation(not.Operand, targetSources, boundSources);
            default:
                return GenericCorrelation(expression, targetSources, boundSources);
        }
    }

    /// <summary>
    /// 判断表达式是否包含布尔常量 / Check whether an expression contains a boolean constant.
    /// </summary>
    /// <param name="expression">布尔表达式 / Boolean expression.</param>
    /// <returns>是否包含布尔常量 / Whether a boolean constant is present.</returns>
    public static bool ContainsBooleanConstant(BooleanExpression expression) {
        ArgumentNullException.ThrowIfNull(expression);
        switch (expression) {
            case BooleanConstant:
                return true;
            case AndExpression and:
                return AnyOperand(and.Operands);
            case OrExpression or:
                return AnyOperand(or.Operands);
            case NotExpression not:
                return ContainsBooleanConstant(not.Operand);
            case Fuookami.Ospf.Math.Symbol.Expression.Comparison<bool> comparison:
                return ContainsBooleanConstant(comparison.Left)
                    || ContainsBooleanConstant(comparison.Right);
            default:
                return GenericBooleanConstant(expression);
        }
    }

    private static bool Compare(
        ScalarExpression<bool> left,
        ScalarExpression<bool> right,
        IReadOnlySet<string> targetSources,
        IReadOnlySet<string> boundSources) {
        if (left is not ScalarReference<bool> leftReference ||
            right is not ScalarReference<bool> rightReference) {
            return false;
        }
        string? leftQualifier = SourceQualifier(leftReference.Path.Value);
        string? rightQualifier = SourceQualifier(rightReference.Path.Value);
        return leftQualifier is not null && rightQualifier is not null &&
            (targetSources.Contains(leftQualifier) && boundSources.Contains(rightQualifier) ||
                targetSources.Contains(rightQualifier) && boundSources.Contains(leftQualifier));
    }

    private static bool AnyOperand(IReadOnlyList<BooleanExpression> operands) {
        foreach (BooleanExpression operand in operands) {
            if (ContainsBooleanConstant(operand)) {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsBooleanConstant(object scalar) {
        ArgumentNullException.ThrowIfNull(scalar);
        Type type = scalar.GetType();
        if (!type.IsGenericType) {
            return false;
        }
        Type definition = type.GetGenericTypeDefinition();
        if (definition == typeof(ScalarUnary<>)) {
            return ContainsBooleanConstant(Property(scalar, "Operand")!);
        }
        if (definition == typeof(ScalarBinary<>)) {
            return ContainsBooleanConstant(Property(scalar, "Left")!)
                || ContainsBooleanConstant(Property(scalar, "Right")!);
        }
        if (definition == typeof(ScalarFunction<>)) {
            var arguments = (IEnumerable<object>)Property(scalar, "Arguments")!;
            foreach (object argument in arguments) {
                if (ContainsBooleanConstant(argument)) {
                    return true;
                }
            }
            return false;
        }
        return false;
    }

    private static object? Property(object instance, string name) =>
        instance.GetType().GetProperty(name)?.GetValue(instance);

    /// <summary>
    /// 按运行时泛型实参检查关联 / Check a correlation using the runtime generic argument.
    /// </summary>
    /// <remarks>
    /// <c>Comparison&lt;T&gt;</c> 之类的节点只有在 <c>T</c> 已知时才能取出泛型标量；这里按实参分派到泛型版本。
    /// Nodes like <c>Comparison&lt;T&gt;</c> only yield typed scalars once <c>T</c> is known, so dispatch to the
    /// generic version with the actual argument.
    /// </remarks>
    private static bool GenericCorrelation(
        BooleanExpression expression,
        IReadOnlySet<string> targetSources,
        IReadOnlySet<string> boundSources) {
        Type type = expression.GetType();
        if (!type.IsGenericType ||
            type.GetGenericTypeDefinition() !=
                typeof(Fuookami.Ospf.Math.Symbol.Expression.Comparison<>)) {
            return false;
        }
        object? left = Property(expression, "Left");
        object? right = Property(expression, "Right");
        if (left is null || right is null) {
            return false;
        }
        string? leftQualifier = ReferenceQualifier(left);
        string? rightQualifier = ReferenceQualifier(right);
        return leftQualifier is not null && rightQualifier is not null &&
            (targetSources.Contains(leftQualifier) && boundSources.Contains(rightQualifier) ||
                targetSources.Contains(rightQualifier) && boundSources.Contains(leftQualifier));
    }

    private static bool GenericBooleanConstant(BooleanExpression expression) {
        Type type = expression.GetType();
        if (!type.IsGenericType) {
            return false;
        }
        Type definition = type.GetGenericTypeDefinition();
        if (definition == typeof(Fuookami.Ospf.Math.Symbol.Expression.Comparison<>)) {
            object? left = Property(expression, "Left");
            object? right = Property(expression, "Right");
            return (left is not null && ContainsBooleanConstant(left))
                || (right is not null && ContainsBooleanConstant(right));
        }
        if (definition == typeof(InExpression<>)) {
            object? value = Property(expression, "Value");
            if (value is not null && ContainsBooleanConstant(value)) {
                return true;
            }
            var candidates = (IEnumerable<object>)Property(expression, "Candidates")!;
            foreach (object candidate in candidates) {
                if (ContainsBooleanConstant(candidate)) {
                    return true;
                }
            }
            return false;
        }
        if (definition == typeof(PatternMatch<>)) {
            object? value = Property(expression, "Value");
            object? pattern = Property(expression, "Pattern");
            return (value is not null && ContainsBooleanConstant(value))
                || (pattern is not null && ContainsBooleanConstant(pattern));
        }
        return false;
    }

    private static string? ReferenceQualifier(object scalar) {
        Type type = scalar.GetType();
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ScalarReference<>)) {
            return null;
        }
        object? path = Property(scalar, "Path");
        return path is PropertyPath propertyPath ? SourceQualifier(propertyPath.Value) : null;
    }
}
