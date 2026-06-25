#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.MultiArray;
/// <summary>
/// 二维列表类型辅助方法
/// 2D list type helper methods
/// </summary>
public static class List2Extensions {
    /// <summary>通过 All 索引和 Int 索引获取所有行的指定列 / Get column from all rows</summary>
    public static IEnumerable<T> GetColumn<T>(this IReadOnlyList<IReadOnlyList<T>> list, DummyIndex.All allIndex, int j) => list.Select(row => j >= 0 && j < row.Count ? row[j] : default!);

    /// <summary>获取指定行的所有列 / Get all columns from a row</summary>
    public static IEnumerable<T> GetRow<T>(this IReadOnlyList<IReadOnlyList<T>> list, int i, DummyIndex.All allIndex) => i >= 0 && i < list.Count ? list[i] : Enumerable.Empty<T>();

    /// <summary>获取所有元素 / Get all elements</summary>
    public static IEnumerable<T> GetAll<T>(this IReadOnlyList<IReadOnlyList<T>> list, DummyIndex.All allIndex1, DummyIndex.All allIndex2) => list.SelectMany(row => row);
}

/// <summary>
/// 三维列表类型辅助方法
/// 3D list type helper methods
/// </summary>
public static class List3Extensions {
    /// <summary>获取所有层的指定行指定列 / Get element from all layers at specified row/col</summary>
    public static IEnumerable<T> GetFromAllLayers<T>(this IReadOnlyList<IReadOnlyList<IReadOnlyList<T>>> list, DummyIndex.All allIndex, int j, int k) {
        return list.Select(layer => {
            if (j < 0 || j >= layer.Count) {
                return default!;
            }

            IReadOnlyList<T> row = layer[j];
            return k >= 0 && k < row.Count ? row[k] : default!;
        });
    }

    /// <summary>获取指定层指定列的所有行 / Get all rows at layer/col</summary>
    public static IEnumerable<T> GetColumnFromLayer<T>(this IReadOnlyList<IReadOnlyList<IReadOnlyList<T>>> list, int i, DummyIndex.All allIndex, int k) {
        if (i < 0 || i >= list.Count) {
            return Enumerable.Empty<T>();
        }

        return list[i].Select(row => k >= 0 && k < row.Count ? row[k] : default!);
    }

    /// <summary>获取指定层指定行的所有列 / Get all cols at layer/row</summary>
    public static IEnumerable<T> GetRowFromLayer<T>(this IReadOnlyList<IReadOnlyList<IReadOnlyList<T>>> list, int i, int j, DummyIndex.All allIndex) {
        if (i < 0 || i >= list.Count) {
            return Enumerable.Empty<T>();
        }

        if (j < 0 || j >= list[i].Count) {
            return Enumerable.Empty<T>();
        }

        return list[i][j];
    }
}

/// <summary>
/// 类型别名 / Type aliases
/// </summary>
public static class ListAliases {
    /// <summary>二维列表类型别名使用帮助 / 2D list type alias usage helper</summary>
    public static IReadOnlyList<IReadOnlyList<T>> AsList2<T>(this IReadOnlyList<IReadOnlyList<T>> list) => list;

    /// <summary>三维列表类型别名使用帮助 / 3D list type alias usage helper</summary>
    public static IReadOnlyList<IReadOnlyList<IReadOnlyList<T>>> AsList3<T>(this IReadOnlyList<IReadOnlyList<IReadOnlyList<T>>> list) => list;
}
