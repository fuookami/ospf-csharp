#nullable enable

namespace Fuookami.Ospf.Utils.Functional;
/// <summary>部分相等接口 / Partial equality interface (mirrors ospf-kotlin PartialEq&lt;in Self&gt;).</summary>
public interface IPartialEq<in TSelf> {
    /// <summary>部分相等比较 / Partial equality comparison.</summary>
    bool? PartialEq(TSelf rhs);
}

/// <summary>相等接口 / Equality interface (mirrors ospf-kotlin Eq&lt;in Self&gt;).</summary>
public interface IEq<in TSelf> : IPartialEq<TSelf> {
    /// <summary>相等比较 / Equality comparison.</summary>
    bool Eq(TSelf rhs) => PartialEq(rhs) ?? false;

    /// <summary>不等比较 / Inequality comparison.</summary>
    bool Neq(TSelf rhs) => !Eq(rhs);
}

/// <summary>Eq 扩展方法 / Eq extension methods.</summary>
public static class EqExtensions {
    /// <summary>安全相等比较（处理 null）/ Safe equality comparison (handles null).</summary>
    public static bool? PartialEqNullable<T>(this T lhs, T? rhs)
        where T : IPartialEq<T> => rhs is null ? null : lhs.PartialEq(rhs);

    /// <summary>安全相等（处理 null）/ Safe equality (handles null).</summary>
    public static bool EqNullable<T>(this T lhs, T? rhs)
        where T : IEq<T> => rhs is not null && lhs.Eq(rhs);
}
