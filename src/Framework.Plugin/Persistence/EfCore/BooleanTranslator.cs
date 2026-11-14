#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.EfCore;

public interface IBooleanTranslator<T> where T : class {
    global::System.Linq.Expressions.Expression<Func<T, bool>>? Translate(object predicate);
}

/// <summary>
/// 把仓储谓词翻译成 EF Core 能用的布尔表达式 / Translates a repository predicate into a boolean expression EF Core can use.
/// </summary>
/// <remarks>
/// 第一版是 `=> null`——不看入参、恒不翻译。`null` 在上层的意思是"这个形状翻不了"，于是每个调用方都会安静地拿到
/// "不支持"，而实际上它从来没看过谓词。**静默的 `null` 比抛异常更难查**：上层会当成"这类查询本来就不支持"。
///
/// 现在真正翻译 `EquivalentExpression` 的两个形状：**等值**与**介于（`IN`）**，由
/// `PredicateFieldFilter.EqOrInFilters` 摊平（该助手早就存在，且已经处理了"`Eq = null` 表示未设置、
/// 等于 null 用 `IsNull`"这个坑）。翻不了的形状仍然返回 `null`——但那是**看过之后**的判断。
///
/// The first version was `=> null`: it never looked at its input and never translated. To the caller `null` means "cannot translate this shape", so every caller quietly got
/// "unsupported" while nothing had been examined — **a silent `null` is harder to diagnose than a throw**, because the caller concludes the query class is simply not
/// supported. It now really translates the two shapes `PredicateFieldFilter.EqOrInFilters` flattens — **equality** and **in-list** — reusing the helper that already handles the
/// "`Eq = null` means unset, equality against null uses `IsNull`" trap. Shapes it cannot express still yield `null`, but only after looking.
///
/// <para>
/// **天花板**：范围比较（`Gt`/`Ge`/`Lt`/`Le`）、`IS NULL`、以及 `OR` 之外的嵌套还没接。升级路径：`FieldFilters`
/// 已经能给出这几种，接进来即可——但仓内现在没有调用方需要它们，先到"等值与 IN"为止。
///
/// **Ceiling**: range comparisons (`Gt`/`Ge`/`Lt`/`Le`), `IS NULL` and nesting beyond a flat AND are not wired yet. Upgrade path: `FieldFilters` already yields these, so wiring them is
/// mechanical — but nothing in this repository needs them yet, so it stops at equality and in-list.
/// </para>
/// </remarks>
public class DefaultBooleanTranslator<T> : IBooleanTranslator<T> where T : class {
    /// <inheritdoc/>
    public global::System.Linq.Expressions.Expression<Func<T, bool>>? Translate(object predicate) {
        ArgumentNullException.ThrowIfNull(predicate);
        if (predicate is not Fuookami.Ospf.Math.Symbol.Expression.BooleanExpression expression) {
            return null;
        }
        IReadOnlyDictionary<string, object?>? filters =
            Fuookami.Ospf.Framework.Persistence.Expression.PredicateFieldFilterExtensions.EqOrInFilters(expression);
        if (filters is null) {
            return null;
        }

        global::System.Linq.Expressions.ParameterExpression parameter = global::System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
        global::System.Linq.Expressions.Expression? body = null;
        foreach (KeyValuePair<string, object?> entry in filters) {
            global::System.Linq.Expressions.Expression member = BuildMember(parameter, entry.Key);
            global::System.Linq.Expressions.Expression term;
            if (entry.Value is IEnumerable<object?> values) {
                // **`IN` 用 `Enumerable.Contains` 而不是手写一串 `||`**：手写要处理空集合（应恒假）与单元素（退化成等值）。
                // **`IN` goes through `Enumerable.Contains` rather than a hand-written chain of `||`**, which would have to handle the empty set (always false) and the single
                // element (degenerating to equality) itself.
                // **把候选值装成与字段同类型的数组**：直接传 `IEnumerable<object>` 会让 `Enumerable.Contains` 的泛型参数对不上，
                // 挂到 EF Core 上还会变成装箱比较，SQL 里根本用不上索引。
                // **Pack the candidates into an array of the member's own type**: passing `IEnumerable<object>` leaves `Enumerable.Contains`'s type argument unsatisfiable and, on
                // EF Core, degenerates into boxing comparisons that no SQL index can serve.
                object?[] candidates = values.ToArray();
                var typed = Array.CreateInstance(member.Type, candidates.Length);
                for (int index = 0; index < candidates.Length; ++index) {
                    typed.SetValue(candidates[index], index);
                }
                global::System.Reflection.MethodInfo contains = typeof(Enumerable)
                    .GetMethods()
                    .First(method => method.Name == nameof(Enumerable.Contains)
                        && method.GetParameters().Length == 2)
                    .MakeGenericMethod(member.Type);
                global::System.Linq.Expressions.MethodCallExpression call = global::System.Linq.Expressions.Expression.Call(
                    contains, global::System.Linq.Expressions.Expression.Constant(typed), member);
                term = call;
            }
            else {
                term = global::System.Linq.Expressions.Expression.Equal(
                    member,
                    global::System.Linq.Expressions.Expression.Constant(entry.Value, member.Type));
            }
            body = body is null
                ? term
                : global::System.Linq.Expressions.Expression.AndAlso(body, term);
        }

        return body is null
            ? global::System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(
                global::System.Linq.Expressions.Expression.Constant(true), parameter)
            : global::System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static global::System.Linq.Expressions.Expression BuildMember(
        global::System.Linq.Expressions.ParameterExpression parameter, string propertyName) {
        global::System.Linq.Expressions.Expression current = parameter;
        foreach (string part in propertyName.Split('.')) {
            current = global::System.Linq.Expressions.Expression.Property(current, part);
        }
        return current;
    }
}
