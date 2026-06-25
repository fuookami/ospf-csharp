#nullable enable

using System;

namespace Fuookami.Ospf.Utils.Concept;
/// <summary>可复制接口 / Copyable interface (mirrors ospf-kotlin Copyable&lt;Self&gt; : Movable).</summary>
public interface ICopyable<TSelf> : IMovable<TSelf> {
    /// <summary>复制 / Copy.</summary>
    TSelf Copy();

    /// <inheritdoc/>
    TSelf IMovable<TSelf>.Move() => Copy();
}

/// <summary>Copy 扩展方法 / Copy extension methods.</summary>
public static class CopyExtensions {
    /// <summary>复制元素 / Copy element.</summary>
    public static T Copy<T>(this T element) where T : ICopyable<T> => element.Copy();

    /// <summary>安全复制（null 返回 null）/ Safe copy (null returns null).</summary>
    public static T? CopyOrNull<T>(this T? element) where T : ICopyable<T> =>
        element is null ? default : element.Copy();

    /// <summary>复制或使用默认值 / Copy or use default.</summary>
    public static T CopyIfNotNullOr<T>(this T? element, Func<T> defaultValue) where T : ICopyable<T> =>
        element is null ? defaultValue() : element.Copy();
}
