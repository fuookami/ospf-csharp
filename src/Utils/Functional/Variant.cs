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
