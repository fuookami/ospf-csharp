#nullable enable

using System;

using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;

/// <summary>
/// 生成器共用的量值算术 / Quantity arithmetic shared by the generators.
/// </summary>
/// <remarks>
/// `NSum` 与 `FullSum` 都需要"加法、减法、判负、重复、最大可重复次数"这五件事。
/// 抽出来是因为**两个生成器各写一份，第三份迟早也会出现**，而其中"减法成功不等于非负"这一条尤其容易被漏掉。
///
/// `NSum` and `FullSum` both need the same five things: add, subtract, sign check, repeat, and maximum repeat count. They are extracted here because
/// **writing them twice guarantees a third copy eventually**, and the subtle point that **a successful subtraction does not mean a non-negative result** is
/// exactly what gets missed.
///
/// **本类不检查单位是否相同以外的东西**：它只回答"这次算术成不成立"，不回答"这个方案合不合法"。
///
/// **This class checks nothing beyond unit compatibility**: it answers "did the arithmetic work", never "is this plan legal".
/// </remarks>
internal static class GenerationQuantityMath {
    /// <summary>相加；单位不同或算术失败时返回空 / Add, returning null on unit mismatch or arithmetic failure.</summary>
    /// <param name="left">加数 / Left operand.</param>
    /// <param name="right">被加数 / Right operand.</param>
    /// <param name="arithmetic">算术策略 / Arithmetic strategy.</param>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <returns>和，或空 / The sum, or null.</returns>
    public static Quantity<V>? Add<V>(Quantity<V> left, Quantity<V> right, IQuantityArithmetic<V> arithmetic)
        where V : struct {
        if (left.Unit != right.Unit) {
            return null;
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> sum = arithmetic.Add(left, right);
        return sum.IsOk ? sum.Value : null;
    }

    /// <summary>相减；单位不同或算术失败时返回空 / Subtract, returning null on unit mismatch or arithmetic failure.</summary>
    /// <param name="left">被减数 / Minuend.</param>
    /// <param name="right">减数 / Subtrahend.</param>
    /// <param name="arithmetic">算术策略 / Arithmetic strategy.</param>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <returns>差，或空 / The difference, or null.</returns>
    public static Quantity<V>? Subtract<V>(Quantity<V> left, Quantity<V> right, IQuantityArithmetic<V> arithmetic)
        where V : struct {
        if (left.Unit != right.Unit) {
            return null;
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> difference = arithmetic.Subtract(left, right);
        return difference.IsOk ? difference.Value : null;
    }

    /// <summary>是否为负 / Whether the quantity is negative.</summary>
    /// <param name="quantity">量值 / Quantity.</param>
    /// <param name="arithmetic">算术策略 / Arithmetic strategy.</param>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <returns>是否小于零 / Whether it is below zero.</returns>
    public static bool IsNegative<V>(Quantity<V> quantity, IQuantityArithmetic<V> arithmetic)
        where V : struct, IComparable<V> {
        Quantity<V> zero = arithmetic.Zero(quantity.Unit);
        return quantity.Value.CompareTo(zero.Value) < 0;
    }

    /// <summary>剩余宽度，且必须非负 / Remaining width, which must be non-negative.</summary>
    /// <param name="upperBound">上界 / Upper bound.</param>
    /// <param name="used">已用宽度 / Used width.</param>
    /// <param name="arithmetic">算术策略 / Arithmetic strategy.</param>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <returns>非负的剩余宽度，越界时为负 / The non-negative remainder, negative when over the bound.</returns>
    /// <remarks>
    /// **名字里带 NonNegative 是故意的**：调用方必须自己判 `IsNegative`，
    /// 因为把"减法成功"当成"没越界"正是本项目已经犯过一次的错。
    ///
    /// **The `NonNegative` in the name is deliberate**: callers must check `IsNegative` themselves, because treating "the subtraction succeeded" as "still within
    /// bounds" is a mistake this project has already made once.
    /// </remarks>
    public static Quantity<V>? RemainingNonNegative<V>(Quantity<V> upperBound, Quantity<V> used, IQuantityArithmetic<V> arithmetic)
        where V : struct => Subtract(upperBound, used, arithmetic);

    /// <summary>把宽度重复若干次 / Repeat a width a number of times.</summary>
    /// <param name="width">宽度 / Width.</param>
    /// <param name="amount">次数 / Amount.</param>
    /// <param name="arithmetic">算术策略 / Arithmetic strategy.</param>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <returns>重复后的宽度，或空 / The repeated width, or null.</returns>
    public static Quantity<V>? Repeat<V>(Quantity<V> width, ulong amount, IQuantityArithmetic<V> arithmetic)
        where V : struct {
        if (amount == 0UL) {
            return arithmetic.Zero(width.Unit);
        }

        Quantity<V> result = arithmetic.Zero(width.Unit);
        for (ulong index = 0; index < amount; ++index) {
            Quantity<V>? next = Add(result, width, arithmetic);
            if (next is null) {
                return null;
            }
            result = next;
        }
        return result;
    }

    /// <summary>
    /// 在给定剩余宽度内最多能重复几次 / How many times the width fits in the remaining space.
    /// </summary>
    /// <param name="width">宽度 / Width.</param>
    /// <param name="remaining">剩余宽度 / Remaining width.</param>
    /// <param name="arithmetic">算术策略 / Arithmetic strategy.</param>
    /// <param name="limit">上限，仅用于防御性溢出保护；默认不截断 / Upper limit for defensive overflow protection only; no truncation by default.</param>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <returns>最大次数 / The maximum amount.</returns>
    /// <remarks>
    /// 用**逐次相加 + 判负**实现，而不是除法：数值类型是泛型，除法需要额外的算术接口成员。
    ///
    /// **默认上限曾是 `4096`，那会把合法结果静默截断**：1mm 宽、10000mm 可用时正确答案是 10000，
    /// 而旧实现在 4096 处停下、**不报任何错**。正确做法是**不设上限**，所以这是一个真实偏离。
    ///
    /// **为什么不能简单删掉上限**：宽度为零时，逐次相加永远不推进，不设上限也会死循环。
    /// 所以改成**显式的零/负宽守卫**（直接返回 0）——既去掉截断，又不继承死循环。
    ///
    /// Implemented as **repeated addition plus a sign check** rather than division, because the value type is generic and division would need an extra arithmetic member.
    ///
    /// **The default cap used to be `4096`, which silently truncated legitimate results**: with a 1mm width and 10000mm available the correct answer is 10000, but the
 /// old implementation stopped at 4096 **without reporting anything**. Its `computeMaxRepeatCount` has **no cap**, so this was a real divergence.
    ///
 /// **Why the cap cannot simply be deleted**: with a zero width, repeated addition never advances and the implementation itself loops forever. So instead there is an
    /// **explicit zero/negative-width guard** returning 0 — removing the truncation without inheriting its infinite loop.
    /// </remarks>
    public static ulong MaxRepeatCount<V>(Quantity<V> width, Quantity<V> remaining, IQuantityArithmetic<V> arithmetic, ulong limit = ulong.MaxValue)
        where V : struct, IComparable<V> {
        // 零宽或负宽永远放不下任何东西；不先挡住，逐次相加会永远不推进。
        // A zero or negative width never fits anything; without this guard, repeated addition would never advance.
        if (width.Value.CompareTo(arithmetic.Zero(width.Unit).Value) <= 0) {
            return 0UL;
        }
        ulong count = 0UL;
        Quantity<V> accumulated = arithmetic.Zero(width.Unit);
        while (count < limit) {
            Quantity<V>? next = Add(accumulated, width, arithmetic);
            if (next is null) {
                break;
            }
            Quantity<V>? leftover = Subtract(remaining, next, arithmetic);
            if (leftover is null || IsNegative(leftover, arithmetic)) {
                break;
            }
            accumulated = next;
            ++count;
        }
        return count;
    }

    /// <summary>按样本解析默认算术策略 / Resolve the default arithmetic strategy from a sample.</summary>
    /// <param name="sample">数值样本 / Value sample.</param>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <returns>算术策略，或空 / The strategy, or null.</returns>
    public static IQuantityArithmetic<V>? Resolve<V>(V sample) where V : struct {
        Result<IQuantityArithmetic<V>, ErrorCode, Error<ErrorCode>> result =
            DefaultQuantityArithmetic.ResolveFor(sample);
        return result.IsOk ? result.Value : null;
    }

    /// <summary>单位键，用于比较不同物理单位 / Unit key used to compare distinct physical units.</summary>
    /// <param name="unit">物理单位 / Physical unit.</param>
    /// <returns>单位键 / The unit key.</returns>
    /// <remarks>
    /// **取值**：`PhysicalUnit.canonicalUnitKey` 是 `symbol ?: name ?: toString`，
    /// 而 `toString` 本身就是 `symbol ?: name ?: "${dimensionSymbol}(${scale})"`。
    ///
    /// **曾在这里加过 `GetType.FullName`，那是错的，撤回。** 当时的理由是两个不同类型、
    /// 但换算规则相同的单位会撞成同一个键——**而那正是行为**：看符号/名称/量纲+比例，
    /// **不看类型**。加类型名会让"同一单位"的两个对象变成"不同单位"，
    /// 也就是**同一份输入在两种实现下得到不同的键**，这属于跨语言语义分叉。
    ///
    /// **这是单位键的唯一实现**：`GenerationWidthIndex.CanonicalUnitKey` 与
    /// `CrossContributionProjector.Measure` 都委托到这里，因此只有一个实现。
    ///
    /// **The value**: `PhysicalUnit.canonicalUnitKey` is `symbol ?: name ?: toString`, and `toString` is
    /// itself `symbol ?: name ?: "${dimensionSymbol}(${scale})"`.
    ///
    /// **once added `GetType.FullName` here; that was wrong and reverts it.** The reasoning then was that two differently typed units with the same
    /// conversion rule collide on one key — **and that is exactly its behaviour**: it looks only at symbol/name/dimension+scale, **never at the type**.
 /// Adding the type name on the C# side makes two units the implementation considers "the same" become "different", i.e. **the same input yields different keys under
    /// the two implementations**, which is a cross-language semantic divergence.
    ///
    /// **This is the single implementation of the unit key**: both `GenerationWidthIndex.CanonicalUnitKey` and `CrossContributionProjector.Measure` delegate here,
    /// matching "one private fun".
    /// </remarks>
    public static string UnitKey(PhysicalUnit unit) =>
        unit.Symbol
            ?? unit.Name
            ?? unit.ToString();
}
