// 物理量核心算子 / Quantity core operators.
// 对应。C# 侧 `QuantityOps` 已提供 `PlusSafe`／`MinusSafe`／
// `QuantityProduct`／`OrdSafe`／`MaxSafe`／`MinSafe`／`QuantityZeroOf`，`QuantityOperators` 已提供加减乘除与标量运算，
// 因此本文件**只补确实缺失的部分**，不再重复定义已有能力。
// . C#'s `QuantityOps` already provides
// `PlusSafe`/`MinusSafe`/`QuantityProduct`/`OrdSafe`/`MaxSafe`/`MinSafe`/`QuantityZeroOf` and `QuantityOperators`
// already provides arithmetic and scalar operations, so this file **only adds what is genuinely missing**.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 物理量核心算子 / Quantity core operators.
/// </summary>
public static class QuantityCore {
    /// <summary>
    /// 比较两个量值，不可比较时返回 null / Compare two quantities, or null when incomparable.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <returns>比较结果或 null / The order, or null.</returns>
    public static Order? OrdOrNull<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> {
        Result<Order, ErrorCode, Error<ErrorCode>> result =
            QuantityOps.OrdSafe(lhs, rhs, "quantity");
        return result.IsOk ? result.Value : null;
    }

    /// <summary>
    /// 判断两个量值量纲是否相同 / Check whether two quantities share a dimension.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <returns>量纲是否相同 / Whether the dimensions match.</returns>
    public static bool IsSameDimension<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> => lhs.Unit.Quantity.Equals(rhs.Unit.Quantity);

    /// <summary>
    /// 由样本构造指定倍数的量值 / Build a quantity of the sample's unit with the given magnitude.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="sample">样本量值 / Sample quantity.</param>
    /// <param name="scaler">量值 / Magnitude.</param>
    /// <returns>同单位的量值 / A quantity in the same unit.</returns>
    public static Quantity<V> Of<V>(Quantity<V> sample, V scaler)
        where V : struct, IFloatingNumber<V> => new(scaler, sample.Unit);

    /// <summary>
    /// 按因子缩放量值 / Scale a quantity by a factor.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="value">量值 / Quantity.</param>
    /// <param name="factor">因子 / Factor.</param>
    /// <returns>缩放后的量值 / Scaled quantity.</returns>
    public static Quantity<V> ScaledBy<V>(Quantity<V> value, V factor)
        where V : struct, IFloatingNumber<V> => new(value.Value.Times(factor), value.Unit);

    /// <summary>
    /// 判断量值是否严格为正 / Check whether a quantity is strictly positive.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="value">量值 / Quantity.</param>
    /// <returns>是否严格为正 / Whether strictly positive.</returns>
    public static bool IsPositive<V>(Quantity<V> value)
        where V : struct, IFloatingNumber<V> =>
        OrdOrNull(value, QuantityOps.QuantityZeroOf(value)) is Order.Greater;

    /// <summary>
    /// 判断量值是否非正 / Check whether a quantity is non-positive.
    /// </summary>
    /// <remarks>
    /// 定义为 <see cref="IsPositive"/> 的取反，因此二者对每个输入**恰好一个为真**；调用方按二分支写不会漏掉分支。
    /// 注意与自身零值比较恒为同量纲，所以这里不存在"不可比较"的情形。
    ///
    /// Defined as the negation of <see cref="IsPositive"/>, so exactly one holds for every input and a caller
    /// branching on the two cannot drop a case. Comparing against the value's own zero is always same-dimension, so
    /// incomparability does not arise here.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="value">量值 / Quantity.</param>
    /// <returns>是否非正 / Whether non-positive.</returns>
    public static bool IsNonPositive<V>(Quantity<V> value)
        where V : struct, IFloatingNumber<V> => !IsPositive(value);

    /// <summary>量值相等 / Quantities are equal.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <returns>是否相等 / Whether equal.</returns>
    public static bool Equal<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> => OrdOrNull(lhs, rhs) is Order.Equal;

    /// <summary>严格大于 / Strictly greater.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <returns>是否成立 / Whether it holds.</returns>
    public static bool Greater<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> => OrdOrNull(lhs, rhs) is Order.Greater;

    /// <summary>严格小于 / Strictly less.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <returns>是否成立 / Whether it holds.</returns>
    public static bool Less<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> => OrdOrNull(lhs, rhs) is Order.Less;

    /// <summary>大于等于 / Greater than or equal.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <returns>是否成立 / Whether it holds.</returns>
    public static bool GreaterOrEqual<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> =>
        OrdOrNull(lhs, rhs) is Order.Greater or Order.Equal;

    /// <summary>小于等于 / Less than or equal.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <returns>是否成立 / Whether it holds.</returns>
    public static bool LessOrEqual<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> =>
        OrdOrNull(lhs, rhs) is Order.Less or Order.Equal;

    /// <summary>
    /// 求和，空序列返回 null / Sum a sequence, returning null when it is empty.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="values">量值序列 / Quantity sequence.</param>
    /// <returns>和或 null / The sum, or null.</returns>
    public static Quantity<V>? Sum<V>(IEnumerable<Quantity<V>> values)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(values);
        Quantity<V>? total = null;
        foreach (Quantity<V> value in values) {
            total = total is null
                ? value
                : QuantityOps.PlusSafe(total, value).Value;
        }
        return total;
    }

    /// <summary>
    /// 按选择器求和，空序列返回 null / Sum with a selector, returning null when empty.
    /// </summary>
    /// <typeparam name="T">元素类型 / Element type.</typeparam>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="values">元素序列 / Element sequence.</param>
    /// <param name="selector">量值选择器 / Quantity selector.</param>
    /// <returns>和或 null / The sum, or null.</returns>
    public static Quantity<V>? SumOfQuantity<T, V>(
        this IEnumerable<T> values,
        Func<T, Quantity<V>> selector)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(selector);
        return Sum(values.Select(selector));
    }

    /// <summary>
    /// 取量值最大的元素，空序列返回默认值 / The element with the largest quantity, or default when empty.
    /// </summary>
    /// <remarks>
    /// 量纲不可比的元素按"不更新当前最优"处理：一个无法比较的候选既不该替换已知最优，也不该让整趟取最大失败。
    /// An incomparable candidate never replaces the current best, and never fails the whole scan.
    /// </remarks>
    /// <typeparam name="T">元素类型 / Element type.</typeparam>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="values">元素序列 / Element sequence.</param>
    /// <param name="selector">量值选择器 / Quantity selector.</param>
    /// <remarks>
    /// **`T` 必须是引用类型**。无约束泛型里的 `T?` 对值类型**不会**变成 `Nullable&lt;T&gt;`，空序列会返回 `default(T)`
    /// 而不是 null——用它取"最大元组"会拿到一个成员全为默认值的元组，随后解引用即 NRE。这个坑曾经真实
    /// 踩到过，因此把约束写死成 `class`，让误用变成编译错误而不是运行期崩溃。值类型场景请用
    /// <see cref="TryMaxByQuantity{T, V}"/>。
    ///
    /// **`T` must be a reference type.** In an unconstrained generic, `T?` does **not** become `Nullable&lt;T&gt;` for
    /// value types, so an empty sequence returns `default(T)` rather than null — using it to pick a "largest tuple" hands
 /// back a tuple of default members and the next dereference NREs. This was hit for real in earlier of this session,
    /// hence the `class` constraint so misuse becomes a compile error instead of a runtime crash. For value types use
    /// <see cref="TryMaxByQuantity{T, V}"/>.
    /// </remarks>
    /// <returns>最大元素或 null / The largest element, or null.</returns>
    public static T? MaxByQuantityOrNull<T, V>(
        this IEnumerable<T> values,
        Func<T, Quantity<V>> selector)
        where T : class
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(selector);
        T? best = default;
        Quantity<V>? bestValue = null;
        bool has = false;
        foreach (T item in values) {
            Quantity<V> value = selector(item);
            if (!has) {
                best = item;
                bestValue = value;
                has = true;
                continue;
            }
            if (OrdOrNull(value, bestValue!) is Order.Greater) {
                best = item;
                bestValue = value;
            }
        }
        return best;
    }

    /// <summary>
    /// 取量值最大的元素，值类型安全版本 / The element with the largest quantity, safe for value types.
    /// </summary>
    /// <remarks>
    /// 用 `bool` 返回值表达"是否找到"，而不是靠 `T?`：值类型下 `T?` 不可靠（见
    /// <see cref="MaxByQuantityOrNull{T,V}"/> 的说明）。
    /// Expresses "found" through the `bool` return rather than `T?`, which is unreliable for value types (see
    /// <see cref="MaxByQuantityOrNull{T,V}"/>).
    /// </remarks>
    /// <typeparam name="T">元素类型 / Element type.</typeparam>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="values">元素序列 / Element sequence.</param>
    /// <param name="selector">量值选择器 / Quantity selector.</param>
    /// <param name="best">最大元素 / The largest element.</param>
    /// <returns>是否找到元素 / Whether an element was found.</returns>
    public static bool TryMaxByQuantity<T, V>(
        this IEnumerable<T> values,
        Func<T, Quantity<V>> selector,
        out T best)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(selector);
        best = default!;
        Quantity<V>? bestValue = null;
        bool has = false;
        foreach (T item in values) {
            Quantity<V> value = selector(item);
            if (!has) {
                best = item;
                bestValue = value;
                has = true;
                continue;
            }
            if (OrdOrNull(value, bestValue!) is Order.Greater) {
                best = item;
                bestValue = value;
            }
        }
        return has;
    }

    /// <summary>
    /// 取量值最小的元素，空序列返回默认值 / The element with the smallest quantity, or default when empty.
    /// </summary>
    /// <typeparam name="T">元素类型 / Element type.</typeparam>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="values">元素序列 / Element sequence.</param>
    /// <param name="selector">量值选择器 / Quantity selector.</param>
    /// <returns>最小元素或默认值 / The smallest element, or default.</returns>
    public static T? MinByQuantityOrNull<T, V>(
        this IEnumerable<T> values,
        Func<T, Quantity<V>> selector)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(selector);
        T? best = default;
        Quantity<V>? bestValue = null;
        bool has = false;
        foreach (T item in values) {
            Quantity<V> value = selector(item);
            if (!has) {
                best = item;
                bestValue = value;
                has = true;
                continue;
            }
            if (OrdOrNull(value, bestValue!) is Order.Less) {
                best = item;
                bestValue = value;
            }
        }
        return best;
    }

    /// <summary>
    /// 按量值升序排序 / Sort ascending by quantity.
    /// </summary>
    /// <typeparam name="T">元素类型 / Element type.</typeparam>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="values">元素序列 / Element sequence.</param>
    /// <param name="selector">量值选择器 / Quantity selector.</param>
    /// <returns>排序后的列表 / The sorted list.</returns>
    public static IReadOnlyList<T> SortedByQuantity<T, V>(
        this IEnumerable<T> values,
        Func<T, Quantity<V>> selector)
        where V : struct, IFloatingNumber<V> =>
        SortByQuantity(values, selector, descending: false);

    /// <summary>
    /// 按量值降序排序 / Sort descending by quantity.
    /// </summary>
    /// <typeparam name="T">元素类型 / Element type.</typeparam>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="values">元素序列 / Element sequence.</param>
    /// <param name="selector">量值选择器 / Quantity selector.</param>
    /// <returns>排序后的列表 / The sorted list.</returns>
    public static IReadOnlyList<T> SortedByDescendingQuantity<T, V>(
        this IEnumerable<T> values,
        Func<T, Quantity<V>> selector)
        where V : struct, IFloatingNumber<V> =>
        SortByQuantity(values, selector, descending: true);

    /// <summary>
    /// 按量值排序（实现）/ Sort by quantity (implementation).
    /// </summary>
    /// <remarks>
    /// 用稳定排序，量纲不可比的两项视为相等从而**保持原有相对顺序**：抛异常会让一趟排序整体失败，
    /// 而随便定一个顺序又会让结果不稳定、难以复现。
    ///
    /// Uses a stable sort and treats incomparable pairs as equal so their **original relative order is kept**:
    /// throwing would fail the whole sort, while picking an arbitrary order would make results unstable and hard to
    /// reproduce.
    /// </remarks>
    /// <typeparam name="T">元素类型 / Element type.</typeparam>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="values">元素序列 / Element sequence.</param>
    /// <param name="selector">量值选择器 / Quantity selector.</param>
    /// <param name="descending">是否降序 / Whether descending.</param>
    /// <returns>排序后的列表 / The sorted list.</returns>
    private static IReadOnlyList<T> SortByQuantity<T, V>(
        IEnumerable<T> values,
        Func<T, Quantity<V>> selector,
        bool descending)
        where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(selector);
        var indexed = values.Select((item, index) => (Item: item, Index: index)).ToList();
        indexed.Sort((left, right) => {
            Order? ord = OrdOrNull(selector(left.Item), selector(right.Item));
            int comparison = ord switch {
                Order.Less => -1,
                Order.Greater => 1,
                _ => 0,
            };
            if (descending) {
                comparison = -comparison;
            }
            return comparison != 0 ? comparison : left.Index.CompareTo(right.Index);
        });
        return indexed.Select(pair => pair.Item).ToList();
    }

    /// <summary>
    /// 计算可容纳的单元数量 / Count how many cells fit.
    /// </summary>
    /// <remarks>
    /// 可用量或单元非正、或量纲不同时返回 0——这些情况下"能放几个"没有意义，返回 0 比返回一个编造的数字安全。
    /// <c>limit</c> 是防御性上限：可用量相对单元极大时逐次相减会跑很久，封顶保证调用方拿到确定性的耗时。
    ///
    /// A non-positive available/cell size or a dimension mismatch yields 0: "how many fit" is meaningless there,
    /// and 0 is safer than an invented number. <c>limit</c> is a defensive cap: with a huge available-to-cell ratio
    /// the repeated subtraction would run for a long time, and capping keeps the cost deterministic.
    ///
    /// ponytail: 逐次相减是 O(available/cell)；要更快应在调用方给出可除的量纲（长度/长度），此处保留循环以维持
    /// "无除零、无量纲推导"行为。
    /// ponytail: repeated subtraction is O(available/cell); a faster version needs a dimensionless division supplied
    /// by the caller. The loop stays to keep its "no division, no dimension inference" behaviour.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="available">可用量 / Available quantity.</param>
    /// <param name="cell">单元尺寸 / Cell size.</param>
    /// <param name="limit">上限 / Upper bound.</param>
    /// <returns>可容纳数量 / Fitting count.</returns>
    public static int CountFitting<V>(
        Quantity<V> available,
        Quantity<V> cell,
        int limit = 100000)
        where V : struct, IFloatingNumber<V> {
        if (!IsPositive(available) || !IsPositive(cell) ||
            !IsSameDimension(available, cell)) {
            return 0;
        }
        Quantity<V> remaining = available;
        int count = 0;
        while (count < limit && GreaterOrEqual(remaining, cell)) {
            remaining = QuantityOps.MinusSafe(remaining, cell).Value;
            count++;
        }
        return count;
    }

    /// <summary>
    /// 把单元尺寸的整数倍换算为量值 / Convert an integer multiple of a cell size into a quantity.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="cell">单元尺寸 / Cell size.</param>
    /// <param name="times">倍数 / Multiple.</param>
    /// <returns>量值 / Quantity.</returns>
    public static Quantity<V> ScaledQuantity<V>(Quantity<V> cell, int times)
        where V : struct, IFloatingNumber<V> {
        Quantity<V> acc = QuantityOps.QuantityZeroOf(cell);
        for (int index = 0; index < times; index++) {
            acc = QuantityOps.PlusSafe(acc, cell).Value;
        }
        return acc;
    }

    /// <summary>
    /// 取两个量值中较大者，不可比较时取左值 / Take the larger quantity, preferring the left when incomparable.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左值 / Left value.</param>
    /// <param name="rhs">右值 / Right value.</param>
    /// <returns>较大值 / The larger value.</returns>
    public static Quantity<V> MaxOf<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> =>
        OrdOrNull(lhs, rhs) is Order.Less ? rhs : lhs;
}
