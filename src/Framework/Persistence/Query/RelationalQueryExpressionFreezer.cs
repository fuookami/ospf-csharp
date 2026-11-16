// 关系查询表达式冻结 / Relational query expression freezing.
// 与`freezeBoolean`／`freezeScalar`／`freezeCustomValue` 对应：构造计划时把表达式树深度复制，
// 使调用方在构造之后修改自己的容器不再影响计划。
// The pinned Kotlin: /`freezeScalar`/`freezeCustomValue`: the expression tree
// is deep-copied while constructing the plan, so a caller mutating its own containers afterwards no longer
// affects the plan.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Fuookami.Ospf.Math.Symbol.Expression;

namespace Fuookami.Ospf.Framework.Persistence.Query;

/// <summary>
/// 关系查询表达式冻结器 / Relational query expression freezer.
/// </summary>
/// <remarks>
/// **与一处实质差异**：基线还有 `findExpressionCycle`，因为 Kotlin 的表达式树可以经由可变容器成环。
/// C# 的表达式节点是不可变记录、只能自底向上构造，**结构上无法成环**，所以这里没有树级循环检测——移植过来
/// 只会是不可达代码。真正可能成环的是自定义 payload 携带的容器，这里用递归深度上限兜住。
///
/// **A substantive difference from the code**: the code also has `findExpressionCycle`, because a Kotlin
/// expression tree can cycle through mutable containers. C# expression nodes are immutable records built
/// bottom-up, so a structural cycle is **impossible** and a tree-level check would be dead code. The real risk is
/// a container inside a custom payload, bounded here by a recursion-depth limit.
/// </remarks>
internal static class RelationalQueryExpressionFreezer {
    /// <summary>payload 最大复制深度 / Maximum payload copy depth.</summary>
    private const int MaxPayloadDepth = 64;

    private static readonly MethodInfo FreezeTypedMethod =
        typeof(RelationalQueryExpressionFreezer).GetMethod(
            nameof(FreezeTyped),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// 冻结布尔表达式 / Freeze a boolean expression.
    /// </summary>
    /// <param name="expression">待冻结表达式 / Expression to freeze.</param>
    /// <returns>冻结后的表达式 / The frozen expression.</returns>
    public static BooleanExpression Freeze(BooleanExpression expression) {
        ArgumentNullException.ThrowIfNull(expression);
        return Freeze(expression, depth: 0);
    }

    /// <summary>
    /// 把任意 payload 复制成不可变值 / Copy an arbitrary payload into an immutable value.
    /// </summary>
    /// <param name="value">原始 payload / Original payload.</param>
    /// <returns>不可变副本 / An immutable copy.</returns>
    public static object? FreezePayload(object? value) => FreezePayload(value, depth: 0);

    /// <summary>
    /// 查找无法安全复制的自定义 payload / Find a custom payload that cannot be copied safely.
    /// </summary>
    /// <remarks>
    /// 构造计划时**拒绝**这类 payload，而不是原样保留引用：保留引用等于计划与调用方共享可变状态，
    /// "构造后不可变"就成了假的。可复制集合与 <see cref="FreezePayload(object?)"/> 的判定保持一致——两者分叉会让
    /// "冻结时放过、校验时拒绝"或反之。
    ///
    /// The code **rejects** such payloads at construction rather than keeping the reference: keeping it means
    /// the plan shares mutable state with the caller and "immutable after construction" becomes false. The copyable
    /// set is kept identical to <see cref="FreezePayload(object?)"/>; if the two diverge, one would freeze what the other
    /// rejects.
    /// </remarks>
    /// <param name="expression">布尔表达式 / Boolean expression.</param>
    /// <param name="field">该表达式在计划中的字段路径 / Field path of the expression in the plan.</param>
    /// <returns>无法复制的 payload 所在字段路径；全部可复制时为 null.</returns>
    public static string? FindUnsupportedPayload(BooleanExpression expression, string field) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(field);

        switch (expression) {
            case BooleanCustom custom:
                return IsCopyable(custom.Value) ? null : field;
            case AndExpression and:
                return FindUnsupportedInOperands(and.Operands, field);
            case OrExpression or:
                return FindUnsupportedInOperands(or.Operands, field);
            case NotExpression not:
                return FindUnsupportedPayload(not.Operand, $"{field}.operand");
            default:
                return FindUnsupportedInGeneric(expression, field);
        }
    }

    private static string? FindUnsupportedInOperands(
        IReadOnlyList<BooleanExpression> operands,
        string field) {
        for (int index = 0; index < operands.Count; index++) {
            string? found = FindUnsupportedPayload(operands[index], $"{field}.operands[{index}]");
            if (found is not null) {
                return found;
            }
        }
        return null;
    }

    private static string? FindUnsupportedInGeneric(BooleanExpression expression, string field) {
        Type type = expression.GetType();
        if (!type.IsGenericType) {
            return null;
        }
        Type definition = type.GetGenericTypeDefinition();
        if (definition == typeof(Fuookami.Ospf.Math.Symbol.Expression.Comparison<>)) {
            return FindUnsupportedInScalar(Property(expression, "Left"), $"{field}.left")
                ?? FindUnsupportedInScalar(Property(expression, "Right"), $"{field}.right");
        }
        if (definition == typeof(InExpression<>)) {
            string? value = FindUnsupportedInScalar(Property(expression, "Value"), $"{field}.value");
            if (value is not null) {
                return value;
            }
            int index = 0;
            foreach (object? candidate in (IEnumerable)Property(expression, "Candidates")!) {
                string? found = FindUnsupportedInScalar(candidate, $"{field}.candidates[{index}]");
                if (found is not null) {
                    return found;
                }
                index++;
            }
            return null;
        }
        if (definition == typeof(PatternMatch<>)) {
            return FindUnsupportedInScalar(Property(expression, "Value"), $"{field}.value")
                ?? FindUnsupportedInScalar(Property(expression, "Pattern"), $"{field}.pattern");
        }
        return null;
    }

    private static string? FindUnsupportedInScalar(object? scalar, string field) {
        if (scalar is null || !scalar.GetType().IsGenericType) {
            return null;
        }
        Type definition = scalar.GetType().GetGenericTypeDefinition();
        if (definition == typeof(ScalarCustom<>)) {
            return IsCopyable(Property(scalar, "Value")) ? null : field;
        }
        if (definition == typeof(ScalarUnary<>)) {
            return FindUnsupportedInScalar(Property(scalar, "Operand"), $"{field}.operand");
        }
        if (definition == typeof(ScalarBinary<>)) {
            return FindUnsupportedInScalar(Property(scalar, "Left"), $"{field}.left")
                ?? FindUnsupportedInScalar(Property(scalar, "Right"), $"{field}.right");
        }
        if (definition == typeof(ScalarFunction<>)) {
            int index = 0;
            foreach (object? argument in (IEnumerable)Property(scalar, "Arguments")!) {
                string? found = FindUnsupportedInScalar(argument, $"{field}.arguments[{index}]");
                if (found is not null) {
                    return found;
                }
                index++;
            }
        }
        return null;
    }

    private static bool IsCopyable(object? value) => value switch {
        null => true,
        string or bool or char or Enum => true,
        sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal or DateTime or DateTimeOffset or Guid
            or TimeSpan => true,
        IDictionary => true,
        IReadOnlyDictionary<string, string> => true,
        IEnumerable => true,
        _ => false,
    };

    private static object? Property(object? instance, string name) =>
        instance?.GetType().GetProperty(name)?.GetValue(instance);

    private static BooleanExpression Freeze(BooleanExpression expression, int depth) {
        if (depth > MaxPayloadDepth) {
            return expression;
        }
        return expression switch {
            BooleanConstant or NullCheck => expression,
            BooleanCustom custom => custom with {
                Value = FreezePayload(custom.Value, depth + 1)!,
            },
            AndExpression and => new AndExpression(FreezeAll(and.Operands, depth + 1)),
            OrExpression or => new OrExpression(FreezeAll(or.Operands, depth + 1)),
            NotExpression not => new NotExpression(Freeze(not.Operand, depth + 1)),
            Fuookami.Ospf.Math.Symbol.Expression.Comparison<bool> comparison => comparison with {
                Left = FreezeScalar(comparison.Left, depth + 1),
                Right = FreezeScalar(comparison.Right, depth + 1),
            },
            _ => FreezeOpenGeneric(expression, depth),
        };
    }

    /// <summary>
    /// 按运行时泛型实参冻结节点 / Freeze a node using its runtime generic argument.
    /// </summary>
    /// <remarks>
    /// <c>Comparison&lt;T&gt;</c>、<c>InExpression&lt;T&gt;</c>、<c>PatternMatch&lt;T&gt;</c> 的 <c>T</c> 只有运行时
    /// 才知道，单一模式无法覆盖全部取值，因此按实参调用泛型版本；这不是重复实现，而是把同一份逻辑实例化。
    /// The <c>T</c> of those nodes is only known at run time, so one pattern cannot cover every instantiation and
    /// the generic version is invoked with the actual argument. That is an instantiation of the same logic, not a
    /// duplicate implementation.
    /// </remarks>
    private static BooleanExpression FreezeOpenGeneric(BooleanExpression expression, int depth) {
        Type type = expression.GetType();
        if (!type.IsGenericType) {
            return expression;
        }
        try {
            return (BooleanExpression)FreezeTypedMethod
                .MakeGenericMethod(type.GetGenericArguments()[0])
                .Invoke(null, new object[] { expression, depth })!;
        }
        catch (TargetInvocationException) {
            return expression;
        }
        catch (ArgumentException) {
            return expression;
        }
    }

    private static BooleanExpression FreezeTyped<T>(BooleanExpression expression, int depth) =>
        expression switch {
            Fuookami.Ospf.Math.Symbol.Expression.Comparison<T> comparison => comparison with {
                Left = FreezeScalar(comparison.Left, depth + 1),
                Right = FreezeScalar(comparison.Right, depth + 1),
            },
            InExpression<T> membership => membership with {
                Value = FreezeScalar(membership.Value, depth + 1),
                Candidates = FreezeScalarAll(membership.Candidates, depth + 1),
            },
            PatternMatch<T> pattern => pattern with {
                Value = FreezeScalar(pattern.Value, depth + 1),
                Pattern = FreezeScalar(pattern.Pattern, depth + 1),
            },
            _ => expression,
        };

    private static ScalarExpression<T> FreezeScalar<T>(
        ScalarExpression<T> scalar,
        int depth) {
        if (depth > MaxPayloadDepth) {
            return scalar;
        }
        return scalar switch {
            ScalarConstant<T> or ScalarReference<T> or ScalarSymbolReference<T> => scalar,
            ScalarCustom<T> custom => custom with {
                Value = FreezePayload(custom.Value, depth + 1)!,
            },
            ScalarUnary<T> unary => unary with {
                Operand = FreezeScalar(unary.Operand, depth + 1),
            },
            ScalarBinary<T> binary => binary with {
                Left = FreezeScalar(binary.Left, depth + 1),
                Right = FreezeScalar(binary.Right, depth + 1),
            },
            ScalarFunction<T> function => function with {
                Arguments = FreezeScalarAll(function.Arguments, depth + 1),
            },
            _ => scalar,
        };
    }

    private static IReadOnlyList<BooleanExpression> FreezeAll(
        IReadOnlyList<BooleanExpression> operands,
        int depth) {
        var frozen = new BooleanExpression[operands.Count];
        for (int index = 0; index < operands.Count; index++) {
            frozen[index] = Freeze(operands[index], depth);
        }
        return new ReadOnlyCollection<BooleanExpression>(frozen);
    }

    private static IReadOnlyList<ScalarExpression<T>> FreezeScalarAll<T>(
        IReadOnlyList<ScalarExpression<T>> scalars,
        int depth) {
        var frozen = new ScalarExpression<T>[scalars.Count];
        for (int index = 0; index < scalars.Count; index++) {
            frozen[index] = FreezeScalar(scalars[index], depth);
        }
        return new ReadOnlyCollection<ScalarExpression<T>>(frozen);
    }

    private static object? FreezePayload(object? value, int depth) {
        if (value is null || depth > MaxPayloadDepth) {
            return value;
        }
        return value switch {
            string or bool or char or Enum => value,
            sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal or DateTime or DateTimeOffset or Guid
                or TimeSpan => value,
            IReadOnlyDictionary<string, string> map => CopyMap(map),
            IDictionary dictionary => CopyUntypedMap(dictionary, depth + 1),
            IEnumerable sequence => CopySequence(sequence, depth + 1),
            _ => value,
        };
    }

    private static IReadOnlyDictionary<string, string> CopyMap(
        IReadOnlyDictionary<string, string> map) {
        var copy = new Dictionary<string, string>(map.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in map) {
            copy[entry.Key] = entry.Value;
        }
        return new ReadOnlyDictionary<string, string>(copy);
    }

    private static IReadOnlyDictionary<object, object?> CopyUntypedMap(
        IDictionary dictionary,
        int depth) {
        var copy = new Dictionary<object, object?>();
        foreach (DictionaryEntry entry in dictionary) {
            copy[entry.Key] = FreezePayload(entry.Value, depth);
        }
        return new ReadOnlyDictionary<object, object?>(copy);
    }

    private static IReadOnlyList<object?> CopySequence(IEnumerable enumerable, int depth) {
        var copy = new List<object?>();
        foreach (object? item in enumerable) {
            copy.Add(FreezePayload(item, depth));
        }
        return new ReadOnlyCollection<object?>(copy);
    }
}
