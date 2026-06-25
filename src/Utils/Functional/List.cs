#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Utils.Functional;
/// <summary>列表查找结果 / List find result (mirrors ospf-kotlin ListFindResult).</summary>
public sealed record ListFindResult<T>(int Index, T Value, bool Found);

/// <summary>List 扩展方法 / List extension methods.</summary>
public static class ListExtensions {
    /// <summary>按条件查找，返回索引和值 / Find by predicate, return index and value.</summary>
    public static ListFindResult<T> FindOr<T>(this IList<T> list, Predicate<T> predicate) {
        for (int i = 0; i < list.Count; i++) {
            if (predicate(list[i])) {
                return new ListFindResult<T>(i, list[i], true);
            }
        }
        return new ListFindResult<T>(-1, default!, false);
    }

    /// <summary>安全获取（越界返回 null）/ Safe get (returns null if out of bounds).</summary>
    public static T? GetOrNull<T>(this IReadOnlyList<T> list, int index) =>
        index >= 0 && index < list.Count ? list[index] : default;

    /// <summary>转换为只读列表 / Convert to read-only list.</summary>
    public static IReadOnlyList<T> AsReadOnly<T>(this IList<T> list) =>
        list is IReadOnlyList<T> rol ? rol : list.ToList();
}
