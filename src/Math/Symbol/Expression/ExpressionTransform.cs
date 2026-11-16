#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Expression;

/// <summary>
/// 表达式树变换 / Expression tree transformation.
/// </summary>
/// <remarks>
/// 与基线 `ExpressionTransform.kt`（113 行）对应：**只重建节点，不改变求值语义，也不越过共享模型边界。**
///
/// **后序**：子节点先变换，当前节点最后交给回调。顺序不是细节——前序版本会让回调看到一个**子节点还没变过**的树，
/// 于是"把所有 `x` 换成 `y`"会漏掉嵌套在深处的那些。
///
/// 补**：这两个入口此前在本仓完全不存在（`grep -rn "Rebuild|WithChildren"` 在 `Math/Symbol/Expression/` 下零命中）。
/// 缺口是怎么找到的值得一提：**按文件名比出来的 91 个"缺失"里，绝大多数其实在别的文件里**
/// （`Einsum`、`CombinatoricsAsync`、`EscapeTime` 都能找到，只是不在同名文件）。**名字对不上不等于能力缺失**——
/// 这条要单独验，结果只剩这一个是真的。
///
/// Mirrors the baseline's `ExpressionTransform.kt` (113 lines): **rebuild the nodes only, without changing evaluation semantics or crossing the shared model boundary.**
/// **Post-order**: children first, the current node last to the callback. The order is not a detail — a pre-order version hands the callback a tree whose children are
/// **still untransformed**, so "replace every `x` with `y`" misses the deeply nested ones.
/// ****: neither entry point existed here. How the gap was found matters: **of the 91 name-based "missing" files, almost all live inside differently-named files**
/// (`Einsum`, `CombinatoricsAsync`, `EscapeTime` are all present) — **a name mismatch is not a missing capability**, so each had to be checked, and only this one was real.
/// </remarks>
public static class ExpressionTransform
{
    /// <summary>
    /// 递归变换标量表达式 / Recursively transform a scalar expression.
    /// </summary>
    /// <typeparam name="T">数值类型 / The numeric type.</typeparam>
    /// <param name="expression">原表达式 / The source expression.</param>
    /// <param name="transformer">节点变换回调 / Node transformation callback.</param>
    /// <returns>变换后的表达式 / The transformed expression.</returns>
    /// <remarks>
    /// 叶子节点原样交出——**回调仍然会看到它们**，因为它接的是"重建后的节点"，不只是"重建后的内部节点"。
    /// Leaves are passed through unchanged — **the callback still sees them**, because it receives the rebuilt node, not only the rebuilt interior.
    /// **本仓没有 `ScalarConditional`**：基线那条条件分支在这里没有对应物，**刻意不凭空造一个**。
    /// **There is no `ScalarConditional` here**: the baseline's conditional branch has no counterpart, and none is invented.
    /// </remarks>
    public static ScalarExpression<T> Transform<T>(
        this ScalarExpression<T> expression,
        Func<ScalarExpression<T>, ScalarExpression<T>> transformer) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(transformer);

        ScalarExpression<T> rebuilt = expression switch {
            ScalarUnary<T> unary => unary with { Operand = unary.Operand.Transform(transformer) },
            ScalarBinary<T> binary => binary with {
                Left = binary.Left.Transform(transformer),
                Right = binary.Right.Transform(transformer)
            },
            ScalarFunction<T> function => function with {
                Arguments = function.Arguments.Select(argument => argument.Transform(transformer)).ToArray()
            },
            // **其余都是叶子**（`ScalarConstant`/`ScalarReference`/`ScalarSymbolReference`/`ScalarCustom`）：原样交出。
            // **Everything else is a leaf**: passed through unchanged.
            _ => expression,
        };
        return transformer(rebuilt);
    }

    /// <summary>
    /// 递归变换布尔表达式 / Transform a boolean expression recursively.
    /// </summary>
    /// <typeparam name="T">数值类型 / The numeric type.</typeparam>
    /// <param name="expression">布尔表达式 / The boolean expression.</param>
    /// <param name="transformer">节点变换器 / The node transformer.</param>
    /// <returns>变换后的表达式 / The transformed expression.</returns>
    /// <remarks>
    /// **这是基线 `BooleanExpression.transform` 那个重载，此前缺着。**
    /// 它做的是**两步**：先把标量节点按原样重走一遍（`it`），再把布尔结构交给变换器。
    /// **第一步看起来什么都没改，但它保证标量子树先被重建**——
    /// **省掉它，变换器拿到的仍然是原来那些标量实例，而"变换"就成了只改外壳。**
    /// **基线那一步用星投影（`ScalarExpression&lt;*&gt;`）；C# 没有星投影，所以要一个类型参数，**
    /// **而它不能瞎填：填 `decimal` 会让 `Comparison&lt;double&gt;` 落进叶子分支，"重建"就成了空转。**
    ///
    /// **This is the baseline `BooleanExpression.transform` overload, missing formerly.**
    /// It does **two** things: first it walks the scalar nodes through unchanged (`it`), then hands the boolean structure to the transformer.
    /// **The first step looks like it changes nothing, but it guarantees the scalar subtrees are rebuilt first** —
    /// **skip it and the transformer still receives the original scalar instances, so "transform" only changes the shell.**
    /// </remarks>
    public static BooleanExpression Transform<T>(
        this BooleanExpression expression,
        Func<BooleanExpression, BooleanExpression> transformer) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(transformer);

        return expression
            .TransformScalars<T>(scalar => scalar)
            .TransformBooleans(transformer);
    }

    /// <summary>
    /// 递归变换布尔表达式中的标量节点 / Transform the scalar nodes inside a boolean expression.
    /// </summary>
    /// <typeparam name="T">数值类型 / The numeric type.</typeparam>
    /// <param name="expression">原表达式 / The source expression.</param>
    /// <param name="transformer">标量节点变换回调 / Scalar node transformation callback.</param>
    /// <returns>变换后的表达式 / The transformed expression.</returns>
    /// <remarks>
    /// **这一层不改布尔节点的身份**：`Comparison`/`InExpression`/`PatternMatch` 原类重建，只换掉里面的标量子树。
    /// **This layer does not change boolean identity**: comparisons, memberships and pattern matches are rebuilt as the same type with new scalar subtrees.
    /// </remarks>

    public static BooleanExpression TransformScalars<T>(
        this BooleanExpression expression,
        Func<ScalarExpression<T>, ScalarExpression<T>> transformer) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(transformer);

        return expression switch {
            Comparison<T> comparison => comparison with {
                Left = comparison.Left.Transform(transformer),
                Right = comparison.Right.Transform(transformer)
            },
            InExpression<T> membership => membership with {
                Value = membership.Value.Transform(transformer),
                Candidates = membership.Candidates.Select(candidate => candidate.Transform(transformer)).ToArray()
            },
            PatternMatch<T> match => match with {
                Value = match.Value.Transform(transformer),
                Pattern = match.Pattern.Transform(transformer)
            },
            // **`Operands` 只读，只能走构造器**：`with` 会在运行时被编译器拒绝（CS0200 已先红过一次）。
            // **`Operands` is read-only, so it takes the constructor**: `with` cannot assign it.
            AndExpression and => new AndExpression(
                and.Operands.Select(operand => operand.TransformScalars(transformer)).ToArray()),
            OrExpression or => new OrExpression(
                or.Operands.Select(operand => operand.TransformScalars(transformer)).ToArray()),
            NotExpression not => not with { Operand = not.Operand.TransformScalars(transformer) },
            // **`BooleanConstant`/`NullCheck`/`BooleanCustom` 是叶子**，以及任何带别的 `T` 的节点都不动。
            // **Constants, null checks and custom booleans are leaves**, and any node carrying a different `T` is left alone.
            _ => expression,
        };
    }

    /// <summary>
    /// 递归变换布尔节点 / Transform the boolean nodes recursively.
    /// </summary>
    /// <param name="expression">原表达式 / The source expression.</param>
    /// <param name="transformer">布尔节点变换回调 / Boolean node transformation callback.</param>
    /// <returns>变换后的表达式 / The transformed expression.</returns>
    /// <remarks>
    /// **只重建 `And`/`Or`/`Not`**：其余节点原样交出，与基线一致——这一层不改变 `Comparison`、`InExpression`、`PatternMatch` 的**布尔身份**。
    /// **Only `And`/`Or`/`Not` are rebuilt**: everything else passes through as the baseline does — this layer does not change the **boolean identity** of a comparison,
    /// membership or pattern match.
    /// </remarks>
    public static BooleanExpression TransformBooleans(
        this BooleanExpression expression,
        Func<BooleanExpression, BooleanExpression> transformer) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(transformer);

        BooleanExpression rebuilt = expression switch {
            AndExpression and => new AndExpression(
                and.Operands.Select(operand => operand.TransformBooleans(transformer)).ToArray()),
            OrExpression or => new OrExpression(
                or.Operands.Select(operand => operand.TransformBooleans(transformer)).ToArray()),
            NotExpression not => not with { Operand = not.Operand.TransformBooleans(transformer) },
            _ => expression,
        };
        return transformer(rebuilt);
    }
}
