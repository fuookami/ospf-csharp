#nullable enable

using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RetInt = Fuookami.Ospf.Utils.Functional.Result<int, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using RetIntArray = Fuookami.Ospf.Utils.Functional.Result<int[], Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.MultiArray;
/// <summary>
/// 抽象多维数组基类
/// Abstract multi-dimensional array base class
/// </summary>
public abstract class AbstractMultiArray<T, S> : ICollection<T>
    where T : notnull
    where S : IShape {
    private protected List<T> DataList;

    private protected AbstractMultiArray(S shape, Func<int, int[], T>? ctor = null) {
        Shape = shape;
        if (ctor != null) {
            DataList = Enumerable.Range(0, shape.Size)
                .Select(i => ctor(i, shape.VectorUnchecked(i)))
                .ToList();
        }
        else if (shape.Size == 0) {
            DataList = new List<T>();
        }
        else {
            DataList = new List<T>(shape.Size);
        }
    }

    /// <summary>数组形状 / Array shape</summary>
    public S Shape { get; }

    /// <summary>维度数量 / Number of dimensions</summary>
    public int Dimension => Shape.Dimension;

    /// <summary>存储顺序 / Storage order</summary>
    public StorageOrder StorageOrder => Shape.StorageOrder;

    /// <inheritdoc/>
    public int Count => DataList.Count;

    /// <inheritdoc/>
    public bool IsReadOnly => this is not MutableMultiArray<T, S>;

    /// <summary>通过线性索引获取元素 / Get element by linear index</summary>
    public T this[int i] => DataList[i];

    /// <summary>通过向量索引获取元素 / Get element by vector index</summary>
    public T this[int[] v] => DataList[CheckedIndex(v)];

    /// <summary>通过 Indexed 接口获取元素 / Get element by IIndexed</summary>
    public T this[IIndexed e] => DataList[e.Index];

    /// <summary>通过任意类型数组创建视图 / Create view by any type array</summary>
    public MultiArrayView<T, S> this[params object[] v] => new(this, Shape.DummyVectorUnchecked(v));

    /// <summary>创建视图 / Create a view</summary>
    public MultiArrayView<T, S> View(IReadOnlyList<DummyIndex> dummyVector) => new(this, dummyVector);

    /// <summary>枚举迭代器 / Enumerate iterator</summary>
    public IEnumerable<(int LinearIndex, int[] Vector, T Value)> Enumerate() {
        for (int i = 0; i < Shape.Size; i++) {
            yield return (i, Shape.VectorUnchecked(i), DataList[i]);
        }
    }

    /// <inheritdoc/>
    public bool Contains(T item) => DataList.Contains(item);

    /// <inheritdoc/>
    public bool ContainsAll(IEnumerable<T> items) => items.All(Contains);

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex) => DataList.CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => DataList.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    bool ICollection<T>.IsReadOnly => IsReadOnly;

    void ICollection<T>.Add(T item) => throw new NotSupportedException("Immutable multi-array does not support Add.");

    void ICollection<T>.Clear() => throw new NotSupportedException("Immutable multi-array does not support Clear.");

    bool ICollection<T>.Remove(T item) => throw new NotSupportedException("Immutable multi-array does not support Remove.");

    private protected int CheckedIndex(int[] vector) {
        if (vector.Length != Shape.Dimension) {
            throw new ArgumentException($"Dimension mismatch: expected {Shape.Dimension}, got {vector.Length}");
        }

        for (int i = 0; i < Shape.Dimension; i++) {
            if (vector[i] < 0 || vector[i] >= Shape[i]) {
                throw new ArgumentOutOfRangeException(nameof(vector), $"Index out of bounds: dimension {i} length is {Shape[i]}, got {vector[i]}");
            }
        }
        return Shape.IndexUnchecked(vector);
    }
}

/// <summary>
/// 不可变多维数组
/// Immutable multi-dimensional array
/// </summary>
public class MultiArray<T, S> : AbstractMultiArray<T, S>
    where T : notnull
    where S : IShape {
    internal MultiArray(S shape, Func<int, int[], T>? ctor = null) : base(shape, ctor) { }

    /// <summary>工厂方法 / Factory methods</summary>
    public static class Factory {
        /// <summary>使用默认值创建（可能失败）/ Create with default values (may fail)</summary>
        public static Result<MultiArray<T, S>, ErrorCode, Error<ErrorCode>> New(S shape) => NewSafe(shape);

        /// <summary>使用默认值安全创建 / Safely create with default values</summary>
        public static Result<MultiArray<T, S>, ErrorCode, Error<ErrorCode>> NewSafe(S shape) {
            object? defaultValue = DefaultValueRegistry.GetDefault(typeof(T));
            if (defaultValue == null) {
                return new Failed<MultiArray<T, S>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"Type {typeof(T).Name} does not have a registered default value");
            }
            return new Ok<MultiArray<T, S>, ErrorCode, Error<ErrorCode>>(
                new MultiArray<T, S>(shape, (_, _) => (T)defaultValue));
        }

        /// <summary>使用指定值创建 / Create with specified value</summary>
        public static MultiArray<T, S> NewWith(S shape, T value) => new MultiArray<T, S>(shape, (_, _) => value);

        /// <summary>使用生成器创建 / Create with generator</summary>
        public static MultiArray<T, S> NewBy(S shape, Func<int, int[], T> generator) => new MultiArray<T, S>(shape, generator);

        /// <summary>从列表创建（可能失败）/ Create from list (may fail)</summary>
        public static Result<MultiArray<T, S>, ErrorCode, Error<ErrorCode>> FromList(
            S shape, IReadOnlyList<T> list, AccessOrder accessOrder = AccessOrders.Default) {
            if (list.Count != shape.Size) {
                return new Failed<MultiArray<T, S>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"List size ({list.Count}) must match shape size ({shape.Size}).");
            }

            if (accessOrder == (AccessOrder)(int)shape.StorageOrder) {
                return new Ok<MultiArray<T, S>, ErrorCode, Error<ErrorCode>>(
                    new MultiArray<T, S>(shape, (i, _) => list[i]));
            }

            T[] reordered = ReorderToStorageOrder(shape, list, accessOrder);
            return new Ok<MultiArray<T, S>, ErrorCode, Error<ErrorCode>>(
                new MultiArray<T, S>(shape, (i, _) => reordered[i]));
        }
    }

    /// <summary>转换存储顺序 / Convert storage order</summary>
    public MultiArray<T, DynShape> ToStorageOrder(StorageOrder order) {
        if (Shape.StorageOrder == order) {
            int[] dims = Enumerable.Range(0, Shape.Dimension).Select(i => Shape[i]).ToArray();
            var newShape = DynShape.WithOrder(dims, order);
            return new MultiArray<T, DynShape>(newShape, (i, _) => DataList[i]);
        }

        int[] dimensions = Enumerable.Range(0, Shape.Dimension).Select(i => Shape[i]).ToArray();
        var targetShape = DynShape.WithOrder(dimensions, order);

        return new MultiArray<T, DynShape>(targetShape, (newLinearIndex, _) => {
            int[] vector = targetShape.VectorUnchecked(newLinearIndex);
            int oldLinearIndex = Shape.IndexUnchecked(vector);
            return DataList[oldLinearIndex];
        });
    }

    /// <summary>重塑数组 / Reshape array</summary>
    public MultiArray<T, NS> Reshape<NS>(NS newShape, T fillValue) where NS : IShape => new MultiArray<T, NS>(newShape, (i, _) => i < Count ? DataList[i] : fillValue);

    /// <summary>转换为可变数组 / Convert to mutable array</summary>
    public MutableMultiArray<T, S> ToMutable() => new MutableMultiArray<T, S>(Shape, (i, _) => DataList[i]);

    /// <summary>转换为列表 / Convert to list</summary>
    public List<T> ToList() => new(DataList);

    private static T[] ReorderToStorageOrder(IShape shape, IReadOnlyList<T> list, AccessOrder accessOrder) {
        var reordered = new T[shape.Size];
        int inputIndex = 0;
        foreach (int[] vector in new MultiIndexSequence(shape, accessOrder)) {
            reordered[shape.IndexUnchecked(vector)] = list[inputIndex++];
        }
        return reordered;
    }
}

/// <summary>
/// 可变多维数组
/// Mutable multi-dimensional array
/// </summary>
public class MutableMultiArray<T, S> : AbstractMultiArray<T, S>
    where T : notnull
    where S : IShape {
    internal MutableMultiArray(S shape, Func<int, int[], T>? ctor = null) : base(shape, ctor) { }

    /// <summary>工厂方法 / Factory methods</summary>
    public static class Factory {
        /// <summary>使用默认值安全创建 / Safely create with default values</summary>
        public static Result<MutableMultiArray<T, S>, ErrorCode, Error<ErrorCode>> New(S shape) => NewSafe(shape);

        /// <summary>使用默认值安全创建 / Safely create with default values</summary>
        public static Result<MutableMultiArray<T, S>, ErrorCode, Error<ErrorCode>> NewSafe(S shape) {
            object? defaultValue = DefaultValueRegistry.GetDefault(typeof(T));
            if (defaultValue == null) {
                return new Failed<MutableMultiArray<T, S>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"Type {typeof(T).Name} does not have a registered default value");
            }
            return new Ok<MutableMultiArray<T, S>, ErrorCode, Error<ErrorCode>>(
                new MutableMultiArray<T, S>(shape, (_, _) => (T)defaultValue));
        }

        /// <summary>使用指定值创建 / Create with specified value</summary>
        public static MutableMultiArray<T, S> NewWith(S shape, T value) => new MutableMultiArray<T, S>(shape, (_, _) => value);

        /// <summary>使用生成器创建 / Create with generator</summary>
        public static MutableMultiArray<T, S> NewBy(S shape, Func<int, int[], T> generator) => new MutableMultiArray<T, S>(shape, generator);

        /// <summary>从列表创建 / Create from list</summary>
        public static Result<MutableMultiArray<T, S>, ErrorCode, Error<ErrorCode>> FromList(
            S shape, IReadOnlyList<T> list, AccessOrder accessOrder = AccessOrders.Default) {
            if (list.Count != shape.Size) {
                return new Failed<MutableMultiArray<T, S>, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"List size ({list.Count}) must match shape size ({shape.Size}).");
            }

            if (accessOrder == (AccessOrder)(int)shape.StorageOrder) {
                return new Ok<MutableMultiArray<T, S>, ErrorCode, Error<ErrorCode>>(
                    new MutableMultiArray<T, S>(shape, (i, _) => list[i]));
            }

            T[] reordered = ReorderToStorageOrder(shape, list, accessOrder);
            return new Ok<MutableMultiArray<T, S>, ErrorCode, Error<ErrorCode>>(
                new MutableMultiArray<T, S>(shape, (i, _) => reordered[i]));
        }
    }

    /// <summary>通过线性索引设置元素 / Set element by linear index</summary>
    public new T this[int i] {
        get => DataList[i];
        set => DataList[i] = value;
    }

    /// <summary>通过向量索引设置元素 / Set element by vector index</summary>
    public new T this[int[] v] {
        get => DataList[CheckedIndex(v)];
        set => DataList[CheckedIndex(v)] = value;
    }

    /// <summary>通过 Indexed 接口设置元素 / Set element by IIndexed</summary>
    public new T this[IIndexed e] {
        get => DataList[e.Index];
        set => DataList[e.Index] = value;
    }

    /// <summary>填充所有元素 / Fill all elements</summary>
    public void Fill(T value) {
        for (int i = 0; i < Count; i++) {
            DataList[i] = value;
        }
    }

    /// <summary>使用生成器填充 / Fill with generator</summary>
    public void FillBy(Func<int, int[], T> generator) {
        for (int i = 0; i < Count; i++) {
            DataList[i] = generator(i, Shape.VectorUnchecked(i));
        }
    }

    /// <summary>转换为不可变数组 / Convert to immutable array</summary>
    public MultiArray<T, S> ToImmutable() => new MultiArray<T, S>(Shape, (i, _) => DataList[i]);

    private static T[] ReorderToStorageOrder(IShape shape, IReadOnlyList<T> list, AccessOrder accessOrder) {
        var reordered = new T[shape.Size];
        int inputIndex = 0;
        foreach (int[] vector in new MultiIndexSequence(shape, accessOrder)) {
            reordered[shape.IndexUnchecked(vector)] = list[inputIndex++];
        }
        return reordered;
    }
}

/// <summary>
/// 不可变多维数组类型别名
/// Immutable multi-dimensional array type aliases
/// </summary>
public static class MultiArrayAliases {
    /// <summary>便捷创建一维数组 / Create 1D array</summary>
    public static MultiArray<T, Shape1> Of<T>(int d1, T value) where T : notnull =>
        new(Shape1.Invoke(d1), (_, _) => value);

    /// <summary>便捷创建二维数组 / Create 2D array</summary>
    public static MultiArray<T, Shape2> Of<T>(int d1, int d2, T value) where T : notnull =>
        new(Shape2.Invoke(d1, d2), (_, _) => value);

    /// <summary>便捷创建三维数组 / Create 3D array</summary>
    public static MultiArray<T, Shape3> Of<T>(int d1, int d2, int d3, T value) where T : notnull =>
        new(Shape3.Invoke(d1, d2, d3), (_, _) => value);

    /// <summary>便捷创建可变一维数组 / Create mutable 1D array</summary>
    public static MutableMultiArray<T, Shape1> MutableOf<T>(int d1, T value) where T : notnull =>
        new(Shape1.Invoke(d1), (_, _) => value);

    /// <summary>便捷创建可变二维数组 / Create mutable 2D array</summary>
    public static MutableMultiArray<T, Shape2> MutableOf<T>(int d1, int d2, T value) where T : notnull =>
        new(Shape2.Invoke(d1, d2), (_, _) => value);

    /// <summary>便捷创建可变三维数组 / Create mutable 3D array</summary>
    public static MutableMultiArray<T, Shape3> MutableOf<T>(int d1, int d2, int d3, T value) where T : notnull =>
        new(Shape3.Invoke(d1, d2, d3), (_, _) => value);
}
