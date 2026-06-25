#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Expression;
/// <summary>
/// 属性路径 / Property Path.
/// Kotlin @JvmInline value class PropertyPath(val value: String); C# readonly record struct.
/// 点分隔路径，如 user.address.city / Dot-separated path, e.g. user.address.city.
/// </summary>
public readonly record struct PropertyPath(string Value) {
    /// <summary>获取路径分段 / Get path segments.</summary>
    public IReadOnlyList<string> Segments => Value.Length == 0
        ? Array.Empty<string>()
        : Value.Split('.');

    /// <summary>路径是否为空 / Whether path is empty.</summary>
    public bool IsEmpty => Value.Length == 0;

    /// <summary>路径是否非空 / Whether path is non-empty.</summary>
    public bool IsNotEmpty => Value.Length > 0;

    /// <summary>获取分段数量 / Get number of segments.</summary>
    public int Depth => Segments.Count;

    /// <summary>获取根分段 / Get root segment, null if empty.</summary>
    public string? Root => Segments is { Count: > 0 } segs ? segs[0] : null;

    /// <summary>获取叶分段 / Get leaf segment, null if empty.</summary>
    public string? Leaf => Segments is { Count: > 0 } segs ? segs[^1] : null;

    /// <summary>获取父路径 / Get parent path, null if only one segment.</summary>
    public PropertyPath? Parent {
        get {
            IReadOnlyList<string> segs = Segments;
            return segs.Count <= 1 ? null : new PropertyPath(string.Join(".", segs.Take(segs.Count - 1)));
        }
    }

    /// <summary>获取子路径 / Get child path, null if only one segment.</summary>
    public PropertyPath? Child {
        get {
            IReadOnlyList<string> segs = Segments;
            return segs.Count <= 1 ? null : new PropertyPath(string.Join(".", segs.Skip(1)));
        }
    }

    /// <summary>判断是否是另一个路径的子路径 / Check if this is a sub-path of another.</summary>
    public bool IsSubPathOf(PropertyPath other) {
        if (IsEmpty || other.IsEmpty) {
            return false;
        }

        IReadOnlyList<string> otherSegs = other.Segments;
        IReadOnlyList<string> thisSegs = Segments;
        if (thisSegs.Count <= otherSegs.Count) {
            return false;
        }

        return thisSegs.Take(otherSegs.Count).SequenceEqual(otherSegs);
    }

    /// <summary>判断是否是另一个路径的父路径 / Check if this is a parent path of another.</summary>
    public bool IsParentPathOf(PropertyPath other) => other.IsSubPathOf(this);

    /// <summary>拼接路径 / Concatenate paths.</summary>
    public PropertyPath Concat(PropertyPath other) {
        if (IsEmpty) {
            return other;
        }

        if (other.IsEmpty) {
            return this;
        }

        return new PropertyPath($"{Value}.{other.Value}");
    }

    /// <summary>拼接分段 / Concatenate segment.</summary>
    public PropertyPath Concat(string segment) =>
        IsEmpty ? new PropertyPath(segment) : new PropertyPath($"{Value}.{segment}");

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>空路径 / Empty path.</summary>
    public static readonly PropertyPath Empty = new("");

    /// <summary>从分段创建路径 / Create path from segments.</summary>
    public static PropertyPath Of(IReadOnlyList<string> segments) =>
        new(string.Join(".", segments));

    /// <summary>从可变参数创建路径 / Create path from vararg segments.</summary>
    public static PropertyPath Of(params string[] segments) => Of((IReadOnlyList<string>)segments);

    /// <summary>从字符串解析路径 / Parse path from string.</summary>
    public static PropertyPath Parse(string text) => new(text.Trim());

    /// <summary>尝试从字符串解析路径 / Try to parse path from string, null if invalid.</summary>
    public static PropertyPath? ParseOrNull(string text) {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) {
            return null;
        }

        string[] segments = trimmed.Split('.');
        return segments.All(IsValidIdentifier) ? new PropertyPath(trimmed) : null;
    }

    private static bool IsValidIdentifier(string id) {
        if (id.Length == 0) {
            return false;
        }

        char first = id[0];
        if (!char.IsLetter(first) && first != '_') {
            return false;
        }

        return id.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}

/// <summary>字符串扩展：转属性路径 / String to PropertyPath extensions.</summary>
public static class PropertyPathExtensions {
    /// <summary>字符串转属性路径 / String to PropertyPath.</summary>
    public static PropertyPath ToPropertyPath(this string text) => PropertyPath.Parse(text);

    /// <summary>字符串尝试转属性路径 / String to PropertyPathOrNull.</summary>
    public static PropertyPath? ToPropertyPathOrNull(this string text) => PropertyPath.ParseOrNull(text);
}
