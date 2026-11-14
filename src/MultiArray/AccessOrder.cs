#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.MultiArray;
/// <summary>
/// 访问顺序枚举
/// Access order enum
/// </summary>
public enum AccessOrder {
    /// <summary>行主序（C 风格）/ Row-major (C style)</summary>
    RowMajor,

    /// <summary>列主序（Fortran 风格）/ Column-major (Fortran style)</summary>
    ColumnMajor
}

/// <summary>
/// 访问顺序默认值提供者
/// Access order default value provider
/// </summary>
public static class AccessOrders {
    /// <summary>默认访问顺序（行主序）/ Default access order (row-major)</summary>
    public const AccessOrder Default = AccessOrder.RowMajor;
}

/// <summary>
/// 迭代器位置
/// Iterator position
/// </summary>
public sealed record IteratorPosition(int[] Positions, bool Exhausted = false) {
    /// <inheritdoc/>
    public bool Equals(IteratorPosition? other) {
        if (other is null) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Positions.AsSpan().SequenceEqual(other.Positions) && Exhausted == other.Exhausted;
    }

    /// <inheritdoc/>
    public override int GetHashCode() {
        var hash = new HashCode();
        foreach (int p in Positions) {
            hash.Add(p);
        }

        hash.Add(Exhausted);
        return hash.ToHashCode();
    }
}

/// <summary>
/// 多维索引迭代器
/// Multi-dimensional index iterator
/// </summary>
public sealed class MultiIndexIterator : IEnumerator<int[]> {
    private readonly IShape _shape;
    private readonly AccessOrder _accessOrder;
    private readonly int _totalSize;
    private int[]? _current;
    private int _count;

    /// <summary>构造函数 / Constructor</summary>
    public MultiIndexIterator(IShape shape, AccessOrder accessOrder = AccessOrders.Default) {
        _shape = shape;
        _accessOrder = accessOrder;
        _totalSize = shape.Size;
        _current = null;
        _count = 0;
    }

    /// <inheritdoc/>
    public int[] Current => _current is null
        ? throw new InvalidOperationException("Iterator is not positioned at a valid element.")
        : (int[])_current.Clone();

    /// <inheritdoc/>
    object? IEnumerator.Current => Current;

    /// <inheritdoc/>
    public bool MoveNext() {
        if (_count >= _totalSize) {
            return false;
        }

        if (_current == null) {
            _current = new int[_shape.Dimension];
            _count = 1;
            return true;
        }

        int[]? next = Advance(_current);
        if (next == null) {
            _count = _totalSize;
            return false;
        }

        _count++;
        return true;
    }

    /// <summary>已迭代元素数量 / Count of iterated elements</summary>
    public int Count => _count;

    /// <inheritdoc/>
    public void Reset() {
        _current = null;
        _count = 0;
    }

    /// <inheritdoc/>
    public void Dispose() { }

    private int[]? Advance(int[] v) {
        switch (_accessOrder) {
            case AccessOrder.RowMajor:
                for (int i = v.Length - 1; i >= 0; i--) {
                    if (v[i] + 1 < _shape[i]) {
                        v[i]++;
                        return v;
                    }
                    v[i] = 0;
                }
                return null;

            case AccessOrder.ColumnMajor:
                for (int i = 0; i < v.Length; i++) {
                    if (v[i] + 1 < _shape[i]) {
                        v[i]++;
                        return v;
                    }
                    v[i] = 0;
                }
                return null;

            default:
                return null;
        }
    }
}

/// <summary>
/// 多维索引序列
/// Multi-dimensional index sequence
/// </summary>
public sealed class MultiIndexSequence : IEnumerable<int[]> {
    private readonly IShape _shape;
    private readonly AccessOrder _accessOrder;

    /// <summary>构造函数 / Constructor</summary>
    public MultiIndexSequence(IShape shape, AccessOrder accessOrder = AccessOrders.Default) {
        _shape = shape;
        _accessOrder = accessOrder;
    }

    /// <inheritdoc/>
    public MultiIndexIterator GetEnumerator() => new(_shape, _accessOrder);

    /// <inheritdoc/>
    IEnumerator<int[]> IEnumerable<int[]>.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// 多维数组访问顺序扩展 / Multi-array access-order extensions.
/// </summary>
public static class MultiArrayAccessExtensions {
    /// <summary>按指定访问顺序迭代元素 / Iterate elements in the requested access order.</summary>
    public static IEnumerable<T> IterWithOrder<T, S>(
        this AbstractMultiArray<T, S> source,
        AccessOrder accessOrder = AccessOrders.Default)
        where T : notnull
        where S : IShape {
        bool matchingOrder = (accessOrder, source.StorageOrder) switch {
            (AccessOrder.RowMajor, StorageOrder.RowMajor) => true,
            (AccessOrder.ColumnMajor, StorageOrder.ColumnMajor) => true,
            _ => false
        };

        if (matchingOrder && source.Shape.HasCanonicalTraversalIndexing()) {
            for (int i = 0; i < source.Count; i++) {
                yield return source[i];
            }
            yield break;
        }

        foreach (int[] vector in new MultiIndexSequence(source.Shape, accessOrder)) {
            yield return source[vector];
        }
    }

    /// <summary>按指定访问顺序枚举元素 / Enumerate elements in the requested access order.</summary>
    public static IEnumerable<(int LinearIndex, int[] Vector, T Value)> EnumerateWithOrder<T, S>(
        this AbstractMultiArray<T, S> source,
        AccessOrder accessOrder = AccessOrders.Default)
        where T : notnull
        where S : IShape {
        int linearIndex = 0;
        foreach (int[] vector in new MultiIndexSequence(source.Shape, accessOrder)) {
            yield return (linearIndex++, vector, source[vector]);
        }
    }

    /// <summary>按指定访问顺序展平 / Flatten in the requested access order.</summary>
    public static List<T> Flatten<T, S>(
        this AbstractMultiArray<T, S> source,
        AccessOrder accessOrder = AccessOrders.Default)
        where T : notnull
        where S : IShape =>
        source.IterWithOrder(accessOrder).ToList();

    /// <summary>按请求顺序迭代多维数组视图 / Iterate a view in the requested order.</summary>
    public static IEnumerable<T> IterWithOrder<T, S>(
        this MultiArrayView<T, S> source,
        AccessOrder accessOrder = AccessOrders.Default)
        where T : notnull
        where S : IShape {
        foreach (int[] vector in new MultiIndexSequence(source.Shape, accessOrder)) {
            Result<T, ErrorCode, Error<ErrorCode>> result = source[vector];
            if (result.IsOk) {
                yield return result.Value;
            }
        }
    }

    /// <summary>按指定访问顺序迭代虚拟向量 / Iterate a dummy vector in the requested access order.</summary>
    public static IEnumerable<int[]> IterateWithOrder(
        this IReadOnlyList<DummyIndex> dummyVector,
        IShape shape,
        AccessOrder accessOrder = AccessOrders.Default) {
        IReadOnlyList<DummyIndexIterator> iteratorVector = shape.DummyToIteratorVector(dummyVector);
        int[] lengths = iteratorVector.Select(iterator => iterator.Len()).ToArray();
        if (lengths.Length == 0 || lengths.Any(length => length == 0)) {
            yield break;
        }

        int[] current = new int[lengths.Length];
        bool exhausted = false;
        while (!exhausted) {
            int[] vector = new int[shape.Dimension];
            for (int i = 0; i < vector.Length; i++) {
                vector[i] = iteratorVector[i].Get(current[i]) ?? 0;
            }
            yield return vector;

            int axis;
            switch (accessOrder) {
                case AccessOrder.RowMajor:
                    for (axis = current.Length - 1; axis >= 0; axis--) {
                        if (current[axis] + 1 < lengths[axis]) {
                            current[axis]++;
                            break;
                        }
                        current[axis] = 0;
                    }
                    exhausted = axis < 0;
                    break;

                case AccessOrder.ColumnMajor:
                    for (axis = 0; axis < current.Length; axis++) {
                        if (current[axis] + 1 < lengths[axis]) {
                            current[axis]++;
                            break;
                        }
                        current[axis] = 0;
                    }
                    exhausted = axis >= current.Length;
                    break;

                default:
                    yield break;
            }
        }
    }

    private static bool HasCanonicalTraversalIndexing(this IShape shape) {
        if (shape is not Shape1 && shape is not Shape2 && shape is not Shape3 && shape is not Shape4 && shape is not DynShape) {
            return false;
        }

        int stride = 1;
        IEnumerable<int> axes = shape.StorageOrder == StorageOrder.RowMajor
            ? Enumerable.Range(0, shape.Dimension).Reverse()
            : Enumerable.Range(0, shape.Dimension);
        foreach (int axis in axes) {
            if (shape.Offsets[axis] != stride) {
                return false;
            }
            stride *= shape[axis];
        }
        return true;
    }
}
