#nullable enable

using System;

namespace Fuookami.Ospf.Utils.Functional;
// ==================== Variant2 ====================
/// <summary>二元变体类型 / 2-ary variant type.</summary>
public abstract record Variant2<T1, T2> {
    /// <summary>第一个值 / First value.</summary>
    public sealed record V1(T1 Value) : Variant2<T1, T2>;
    /// <summary>第二个值 / Second value.</summary>
    public sealed record V2(T2 Value) : Variant2<T1, T2>;

    /// <summary>是否为 V1 / Whether V1.</summary>
    public bool Is1 => this is V1;
    /// <summary>是否为 V2 / Whether V2.</summary>
    public bool Is2 => this is V2;
    /// <summary>V1 值 / V1 value.</summary>
    public T1? Value1 => this is V1 v ? v.Value : default;
    /// <summary>V2 值 / V2 value.</summary>
    public T2? Value2 => this is V2 v ? v.Value : default;

    /// <summary>匹配 / Match.</summary>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2) =>
        this is V1 v1 ? f1(v1.Value) : f2(((V2)this).Value);

    /// <summary>匹配（Action）/ Match (Action).</summary>
    public void Match(Action<T1> f1, Action<T2> f2) {
        if (this is V1 v1) {
            f1(v1.Value);
        }
        else {
            f2(((V2)this).Value);
        }
    }
}

// ==================== Variant3 ====================
/// <summary>三元变体类型 / 3-ary variant type.</summary>
public abstract record Variant3<T1, T2, T3> {
    public sealed record V1(T1 Value) : Variant3<T1, T2, T3>;
    public sealed record V2(T2 Value) : Variant3<T1, T2, T3>;
    public sealed record V3(T3 Value) : Variant3<T1, T2, T3>;

    public bool Is1 => this is V1;
    public bool Is2 => this is V2;
    public bool Is3 => this is V3;
    public T1? Value1 => this is V1 v ? v.Value : default;
    public T2? Value2 => this is V2 v ? v.Value : default;
    public T3? Value3 => this is V3 v ? v.Value : default;

    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3) =>
        this is V1 v1 ? f1(v1.Value) : this is V2 v2 ? f2(v2.Value) : f3(((V3)this).Value);

    public void Match(Action<T1> f1, Action<T2> f2, Action<T3> f3) {
        if (this is V1 v1) {
            f1(v1.Value);
        }
        else if (this is V2 v2) {
            f2(v2.Value);
        }
        else {
            f3(((V3)this).Value);
        }
    }
}

// ==================== Variant4 ====================
/// <summary>四元变体类型 / 4-ary variant type.</summary>
public abstract record Variant4<T1, T2, T3, T4> {
    public sealed record V1(T1 Value) : Variant4<T1, T2, T3, T4>;
    public sealed record V2(T2 Value) : Variant4<T1, T2, T3, T4>;
    public sealed record V3(T3 Value) : Variant4<T1, T2, T3, T4>;
    public sealed record V4(T4 Value) : Variant4<T1, T2, T3, T4>;

    public bool Is1 => this is V1;
    public bool Is2 => this is V2;
    public bool Is3 => this is V3;
    public bool Is4 => this is V4;
    public T1? Value1 => this is V1 v ? v.Value : default;
    public T2? Value2 => this is V2 v ? v.Value : default;
    public T3? Value3 => this is V3 v ? v.Value : default;
    public T4? Value4 => this is V4 v ? v.Value : default;

    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4) =>
        this is V1 v1 ? f1(v1.Value) : this is V2 v2 ? f2(v2.Value) : this is V3 v3 ? f3(v3.Value) : f4(((V4)this).Value);

    public void Match(Action<T1> f1, Action<T2> f2, Action<T3> f3, Action<T4> f4) {
        if (this is V1 v1) {
            f1(v1.Value);
        }
        else if (this is V2 v2) {
            f2(v2.Value);
        }
        else if (this is V3 v3) {
            f3(v3.Value);
        }
        else {
            f4(((V4)this).Value);
        }
    }
}

// ==================== Variant5 ====================
/// <summary>五元变体类型 / 5-ary variant type.</summary>
public abstract record Variant5<T1, T2, T3, T4, T5> {
    public sealed record V1(T1 Value) : Variant5<T1, T2, T3, T4, T5>;
    public sealed record V2(T2 Value) : Variant5<T1, T2, T3, T4, T5>;
    public sealed record V3(T3 Value) : Variant5<T1, T2, T3, T4, T5>;
    public sealed record V4(T4 Value) : Variant5<T1, T2, T3, T4, T5>;
    public sealed record V5(T5 Value) : Variant5<T1, T2, T3, T4, T5>;

    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            _ => f5(((V5)this).Value),
        };
}

// ==================== Variant6 ====================
/// <summary>六元变体类型 / 6-ary variant type.</summary>
public abstract record Variant6<T1, T2, T3, T4, T5, T6> {
    public sealed record V1(T1 Value) : Variant6<T1, T2, T3, T4, T5, T6>;
    public sealed record V2(T2 Value) : Variant6<T1, T2, T3, T4, T5, T6>;
    public sealed record V3(T3 Value) : Variant6<T1, T2, T3, T4, T5, T6>;
    public sealed record V4(T4 Value) : Variant6<T1, T2, T3, T4, T5, T6>;
    public sealed record V5(T5 Value) : Variant6<T1, T2, T3, T4, T5, T6>;
    public sealed record V6(T6 Value) : Variant6<T1, T2, T3, T4, T5, T6>;

    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            _ => f6(((V6)this).Value),
        };
}

// ==================== Variant7 ====================
/// <summary>七元变体类型 / 7-ary variant type.</summary>
public abstract record Variant7<T1, T2, T3, T4, T5, T6, T7> {
    public sealed record V1(T1 Value) : Variant7<T1, T2, T3, T4, T5, T6, T7>;
    public sealed record V2(T2 Value) : Variant7<T1, T2, T3, T4, T5, T6, T7>;
    public sealed record V3(T3 Value) : Variant7<T1, T2, T3, T4, T5, T6, T7>;
    public sealed record V4(T4 Value) : Variant7<T1, T2, T3, T4, T5, T6, T7>;
    public sealed record V5(T5 Value) : Variant7<T1, T2, T3, T4, T5, T6, T7>;
    public sealed record V6(T6 Value) : Variant7<T1, T2, T3, T4, T5, T6, T7>;
    public sealed record V7(T7 Value) : Variant7<T1, T2, T3, T4, T5, T6, T7>;

    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            _ => f7(((V7)this).Value),
        };
}

// ==================== Variant8 ====================
/// <summary>八元变体类型 / 8-ary variant type.</summary>
public abstract record Variant8<T1, T2, T3, T4, T5, T6, T7, T8> {
    public sealed record V1(T1 Value) : Variant8<T1, T2, T3, T4, T5, T6, T7, T8>;
    public sealed record V2(T2 Value) : Variant8<T1, T2, T3, T4, T5, T6, T7, T8>;
    public sealed record V3(T3 Value) : Variant8<T1, T2, T3, T4, T5, T6, T7, T8>;
    public sealed record V4(T4 Value) : Variant8<T1, T2, T3, T4, T5, T6, T7, T8>;
    public sealed record V5(T5 Value) : Variant8<T1, T2, T3, T4, T5, T6, T7, T8>;
    public sealed record V6(T6 Value) : Variant8<T1, T2, T3, T4, T5, T6, T7, T8>;
    public sealed record V7(T7 Value) : Variant8<T1, T2, T3, T4, T5, T6, T7, T8>;
    public sealed record V8(T8 Value) : Variant8<T1, T2, T3, T4, T5, T6, T7, T8>;

    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            _ => f8(((V8)this).Value),
        };
}

/// <summary>**9 选一** / One of 9.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant9<T1, T2, T3, T4, T5, T6, T7, T8, T9> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant9<T1, T2, T3, T4, T5, T6, T7, T8, T9>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant9<T1, T2, T3, T4, T5, T6, T7, T8, T9>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant9<T1, T2, T3, T4, T5, T6, T7, T8, T9>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant9<T1, T2, T3, T4, T5, T6, T7, T8, T9>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant9<T1, T2, T3, T4, T5, T6, T7, T8, T9>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant9<T1, T2, T3, T4, T5, T6, T7, T8, T9>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant9<T1, T2, T3, T4, T5, T6, T7, T8, T9>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant9<T1, T2, T3, T4, T5, T6, T7, T8, T9>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant9<T1, T2, T3, T4, T5, T6, T7, T8, T9>;

    /// <summary>**处理全部 9 个分支** / Handle all 9 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            _ => f9(((V9)this).Value),
        };
}

/// <summary>**10 选一** / One of 10.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant10<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;

    /// <summary>**处理全部 10 个分支** / Handle all 10 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            _ => f10(((V10)this).Value),
        };
}

/// <summary>**11 选一** / One of 11.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;
    /// <summary>第 11 个分支 / Branch 11.</summary>
    public sealed record V11(T11 Value) : Variant11<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;

    /// <summary>**处理全部 11 个分支** / Handle all 11 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10, Func<T11, R> f11) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            V10 v10 => f10(v10.Value),
            _ => f11(((V11)this).Value),
        };
}

/// <summary>**12 选一** / One of 12.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 11 个分支 / Branch 11.</summary>
    public sealed record V11(T11 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;
    /// <summary>第 12 个分支 / Branch 12.</summary>
    public sealed record V12(T12 Value) : Variant12<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;

    /// <summary>**处理全部 12 个分支** / Handle all 12 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10, Func<T11, R> f11, Func<T12, R> f12) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            V10 v10 => f10(v10.Value),
            V11 v11 => f11(v11.Value),
            _ => f12(((V12)this).Value),
        };
}

/// <summary>**13 选一** / One of 13.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 11 个分支 / Branch 11.</summary>
    public sealed record V11(T11 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 12 个分支 / Branch 12.</summary>
    public sealed record V12(T12 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;
    /// <summary>第 13 个分支 / Branch 13.</summary>
    public sealed record V13(T13 Value) : Variant13<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>;

    /// <summary>**处理全部 13 个分支** / Handle all 13 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10, Func<T11, R> f11, Func<T12, R> f12, Func<T13, R> f13) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            V10 v10 => f10(v10.Value),
            V11 v11 => f11(v11.Value),
            V12 v12 => f12(v12.Value),
            _ => f13(((V13)this).Value),
        };
}

/// <summary>**14 选一** / One of 14.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 11 个分支 / Branch 11.</summary>
    public sealed record V11(T11 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 12 个分支 / Branch 12.</summary>
    public sealed record V12(T12 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 13 个分支 / Branch 13.</summary>
    public sealed record V13(T13 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;
    /// <summary>第 14 个分支 / Branch 14.</summary>
    public sealed record V14(T14 Value) : Variant14<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>;

    /// <summary>**处理全部 14 个分支** / Handle all 14 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10, Func<T11, R> f11, Func<T12, R> f12, Func<T13, R> f13, Func<T14, R> f14) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            V10 v10 => f10(v10.Value),
            V11 v11 => f11(v11.Value),
            V12 v12 => f12(v12.Value),
            V13 v13 => f13(v13.Value),
            _ => f14(((V14)this).Value),
        };
}

/// <summary>**15 选一** / One of 15.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 11 个分支 / Branch 11.</summary>
    public sealed record V11(T11 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 12 个分支 / Branch 12.</summary>
    public sealed record V12(T12 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 13 个分支 / Branch 13.</summary>
    public sealed record V13(T13 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 14 个分支 / Branch 14.</summary>
    public sealed record V14(T14 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;
    /// <summary>第 15 个分支 / Branch 15.</summary>
    public sealed record V15(T15 Value) : Variant15<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>;

    /// <summary>**处理全部 15 个分支** / Handle all 15 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10, Func<T11, R> f11, Func<T12, R> f12, Func<T13, R> f13, Func<T14, R> f14, Func<T15, R> f15) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            V10 v10 => f10(v10.Value),
            V11 v11 => f11(v11.Value),
            V12 v12 => f12(v12.Value),
            V13 v13 => f13(v13.Value),
            V14 v14 => f14(v14.Value),
            _ => f15(((V15)this).Value),
        };
}

/// <summary>**16 选一** / One of 16.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 11 个分支 / Branch 11.</summary>
    public sealed record V11(T11 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 12 个分支 / Branch 12.</summary>
    public sealed record V12(T12 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 13 个分支 / Branch 13.</summary>
    public sealed record V13(T13 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 14 个分支 / Branch 14.</summary>
    public sealed record V14(T14 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 15 个分支 / Branch 15.</summary>
    public sealed record V15(T15 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;
    /// <summary>第 16 个分支 / Branch 16.</summary>
    public sealed record V16(T16 Value) : Variant16<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>;

    /// <summary>**处理全部 16 个分支** / Handle all 16 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10, Func<T11, R> f11, Func<T12, R> f12, Func<T13, R> f13, Func<T14, R> f14, Func<T15, R> f15, Func<T16, R> f16) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            V10 v10 => f10(v10.Value),
            V11 v11 => f11(v11.Value),
            V12 v12 => f12(v12.Value),
            V13 v13 => f13(v13.Value),
            V14 v14 => f14(v14.Value),
            V15 v15 => f15(v15.Value),
            _ => f16(((V16)this).Value),
        };
}

/// <summary>**17 选一** / One of 17.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 11 个分支 / Branch 11.</summary>
    public sealed record V11(T11 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 12 个分支 / Branch 12.</summary>
    public sealed record V12(T12 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 13 个分支 / Branch 13.</summary>
    public sealed record V13(T13 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 14 个分支 / Branch 14.</summary>
    public sealed record V14(T14 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 15 个分支 / Branch 15.</summary>
    public sealed record V15(T15 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 16 个分支 / Branch 16.</summary>
    public sealed record V16(T16 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;
    /// <summary>第 17 个分支 / Branch 17.</summary>
    public sealed record V17(T17 Value) : Variant17<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>;

    /// <summary>**处理全部 17 个分支** / Handle all 17 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10, Func<T11, R> f11, Func<T12, R> f12, Func<T13, R> f13, Func<T14, R> f14, Func<T15, R> f15, Func<T16, R> f16, Func<T17, R> f17) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            V10 v10 => f10(v10.Value),
            V11 v11 => f11(v11.Value),
            V12 v12 => f12(v12.Value),
            V13 v13 => f13(v13.Value),
            V14 v14 => f14(v14.Value),
            V15 v15 => f15(v15.Value),
            V16 v16 => f16(v16.Value),
            _ => f17(((V17)this).Value),
        };
}

/// <summary>**18 选一** / One of 18.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 11 个分支 / Branch 11.</summary>
    public sealed record V11(T11 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 12 个分支 / Branch 12.</summary>
    public sealed record V12(T12 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 13 个分支 / Branch 13.</summary>
    public sealed record V13(T13 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 14 个分支 / Branch 14.</summary>
    public sealed record V14(T14 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 15 个分支 / Branch 15.</summary>
    public sealed record V15(T15 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 16 个分支 / Branch 16.</summary>
    public sealed record V16(T16 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 17 个分支 / Branch 17.</summary>
    public sealed record V17(T17 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;
    /// <summary>第 18 个分支 / Branch 18.</summary>
    public sealed record V18(T18 Value) : Variant18<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>;

    /// <summary>**处理全部 18 个分支** / Handle all 18 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10, Func<T11, R> f11, Func<T12, R> f12, Func<T13, R> f13, Func<T14, R> f14, Func<T15, R> f15, Func<T16, R> f16, Func<T17, R> f17, Func<T18, R> f18) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            V10 v10 => f10(v10.Value),
            V11 v11 => f11(v11.Value),
            V12 v12 => f12(v12.Value),
            V13 v13 => f13(v13.Value),
            V14 v14 => f14(v14.Value),
            V15 v15 => f15(v15.Value),
            V16 v16 => f16(v16.Value),
            V17 v17 => f17(v17.Value),
            _ => f18(((V18)this).Value),
        };
}

/// <summary>**19 选一** / One of 19.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 11 个分支 / Branch 11.</summary>
    public sealed record V11(T11 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 12 个分支 / Branch 12.</summary>
    public sealed record V12(T12 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 13 个分支 / Branch 13.</summary>
    public sealed record V13(T13 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 14 个分支 / Branch 14.</summary>
    public sealed record V14(T14 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 15 个分支 / Branch 15.</summary>
    public sealed record V15(T15 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 16 个分支 / Branch 16.</summary>
    public sealed record V16(T16 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 17 个分支 / Branch 17.</summary>
    public sealed record V17(T17 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 18 个分支 / Branch 18.</summary>
    public sealed record V18(T18 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;
    /// <summary>第 19 个分支 / Branch 19.</summary>
    public sealed record V19(T19 Value) : Variant19<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>;

    /// <summary>**处理全部 19 个分支** / Handle all 19 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10, Func<T11, R> f11, Func<T12, R> f12, Func<T13, R> f13, Func<T14, R> f14, Func<T15, R> f15, Func<T16, R> f16, Func<T17, R> f17, Func<T18, R> f18, Func<T19, R> f19) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            V10 v10 => f10(v10.Value),
            V11 v11 => f11(v11.Value),
            V12 v12 => f12(v12.Value),
            V13 v13 => f13(v13.Value),
            V14 v14 => f14(v14.Value),
            V15 v15 => f15(v15.Value),
            V16 v16 => f16(v16.Value),
            V17 v17 => f17(v17.Value),
            V18 v18 => f18(v18.Value),
            _ => f19(((V19)this).Value),
        };
}

/// <summary>**20 选一** / One of 20.</summary>
/// <remarks>
/// **与基线 `Variant{0}` 对应。** 构造后恰好一个分支有值，
/// **`Match` 要求调用方处理全部 {0} 个分支**——**漏一个编译不过，而不是运行时抛异常。**
///
/// **Corresponds to the baseline `Variant{0}`.** Exactly one branch carries a value after construction,
/// **and `Match` requires the caller to handle all {0}** — **missing one fails to compile rather than throwing at run time.**
/// </remarks>
public abstract record Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20> {
    /// <summary>第 1 个分支 / Branch 1.</summary>
    public sealed record V1(T1 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 2 个分支 / Branch 2.</summary>
    public sealed record V2(T2 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 3 个分支 / Branch 3.</summary>
    public sealed record V3(T3 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 4 个分支 / Branch 4.</summary>
    public sealed record V4(T4 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 5 个分支 / Branch 5.</summary>
    public sealed record V5(T5 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 6 个分支 / Branch 6.</summary>
    public sealed record V6(T6 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 7 个分支 / Branch 7.</summary>
    public sealed record V7(T7 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 8 个分支 / Branch 8.</summary>
    public sealed record V8(T8 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 9 个分支 / Branch 9.</summary>
    public sealed record V9(T9 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 10 个分支 / Branch 10.</summary>
    public sealed record V10(T10 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 11 个分支 / Branch 11.</summary>
    public sealed record V11(T11 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 12 个分支 / Branch 12.</summary>
    public sealed record V12(T12 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 13 个分支 / Branch 13.</summary>
    public sealed record V13(T13 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 14 个分支 / Branch 14.</summary>
    public sealed record V14(T14 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 15 个分支 / Branch 15.</summary>
    public sealed record V15(T15 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 16 个分支 / Branch 16.</summary>
    public sealed record V16(T16 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 17 个分支 / Branch 17.</summary>
    public sealed record V17(T17 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 18 个分支 / Branch 18.</summary>
    public sealed record V18(T18 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 19 个分支 / Branch 19.</summary>
    public sealed record V19(T19 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;
    /// <summary>第 20 个分支 / Branch 20.</summary>
    public sealed record V20(T20 Value) : Variant20<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>;

    /// <summary>**处理全部 20 个分支** / Handle all 20 branches.</summary>
    /// <remarks>
    /// **参数个数就是分支个数**：漏一个编译不过。**这是这个类型存在的全部理由**——
    /// **换成 `object` 加 `if`，漏掉的分支只会在运行时变成"没有反应"。**
    ///
    /// **The parameter count is the branch count**: missing one fails to compile. **That is the whole reason this type exists** —
    /// **with `object` plus `if`, a missed branch only becomes "nothing happens" at run time.**
    /// </remarks>
    public R Match<R>(Func<T1, R> f1, Func<T2, R> f2, Func<T3, R> f3, Func<T4, R> f4, Func<T5, R> f5, Func<T6, R> f6, Func<T7, R> f7, Func<T8, R> f8, Func<T9, R> f9, Func<T10, R> f10, Func<T11, R> f11, Func<T12, R> f12, Func<T13, R> f13, Func<T14, R> f14, Func<T15, R> f15, Func<T16, R> f16, Func<T17, R> f17, Func<T18, R> f18, Func<T19, R> f19, Func<T20, R> f20) =>
        this switch {
            V1 v1 => f1(v1.Value),
            V2 v2 => f2(v2.Value),
            V3 v3 => f3(v3.Value),
            V4 v4 => f4(v4.Value),
            V5 v5 => f5(v5.Value),
            V6 v6 => f6(v6.Value),
            V7 v7 => f7(v7.Value),
            V8 v8 => f8(v8.Value),
            V9 v9 => f9(v9.Value),
            V10 v10 => f10(v10.Value),
            V11 v11 => f11(v11.Value),
            V12 v12 => f12(v12.Value),
            V13 v13 => f13(v13.Value),
            V14 v14 => f14(v14.Value),
            V15 v15 => f15(v15.Value),
            V16 v16 => f16(v16.Value),
            V17 v17 => f17(v17.Value),
            V18 v18 => f18(v18.Value),
            V19 v19 => f19(v19.Value),
            _ => f20(((V20)this).Value),
        };
}
