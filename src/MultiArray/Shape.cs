#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using RetInt = Fuookami.Ospf.Utils.Functional.Result<int, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using RetIntArray = Fuookami.Ospf.Utils.Functional.Result<int[], Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using DummyVecResult = System.Collections.Generic.IReadOnlyList<Fuookami.Ospf.MultiArray.DummyIndex>;
using OkRetInt = Fuookami.Ospf.Utils.Functional.Ok<int, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using FailedRetInt = Fuookami.Ospf.Utils.Functional.Failed<int, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using OkIntArray = Fuookami.Ospf.Utils.Functional.Ok<int[], Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using FailedIntArray = Fuookami.Ospf.Utils.Functional.Failed<int[], Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.MultiArray
{
    /// <summary>
    /// 形状接口
    /// Shape interface
    /// </summary>
    public interface IShape
    {
        /// <summary>维度数量 / Number of dimensions</summary>
        int Dimension { get; }

        /// <summary>元素总数 / Total number of elements</summary>
        int Size { get; }

        /// <summary>各维度的步长 / Strides for each dimension</summary>
        int[] Offsets { get; }

        /// <summary>存储顺序 / Storage order</summary>
        StorageOrder StorageOrder { get; }

        /// <summary>获取指定维度的长度 / Get the length of the specified dimension</summary>
        int this[int index] { get; }

        /// <summary>将向量索引转换为线性索引 / Convert vector index to linear index</summary>
        RetInt Index(int[] vector);

        /// <summary>将线性索引转换为向量索引 / Convert linear index to vector index</summary>
        RetIntArray Vector(int index);
    }

    /// <summary>
    /// 形状扩展方法
    /// Shape extension methods
    /// </summary>
    public static class ShapeExtensions
    {
        /// <summary>检查是否为空 / Check if empty</summary>
        public static bool IsEmpty(this IShape shape) => shape.Size == 0;

        /// <summary>创建零向量 / Create zero vector</summary>
        public static int[] Zero(this IShape shape) => new int[shape.Dimension];

        /// <summary>安全转换向量索引 / Safely convert vector index to linear index</summary>
        public static RetInt IndexSafe(this IShape shape, int[] vector) => shape.Index(vector);

        /// <summary>尝试转换向量索引 / Try convert vector index to linear index</summary>
        public static int? IndexOrNull(this IShape shape, int[] vector) => shape.Index(vector).Value;

        /// <summary>安全转换线性索引 / Safely convert linear index to vector index</summary>
        public static RetIntArray VectorSafe(this IShape shape, int index) => shape.Vector(index);

        /// <summary>尝试转换线性索引 / Try convert linear index to vector index</summary>
        public static int[]? VectorOrNull(this IShape shape, int index) => shape.Vector(index).Value;

        /// <summary>不校验地转换向量索引 / Convert vector index without validation</summary>
        public static int IndexUnchecked(this IShape shape, int[] vector) => shape.IndexOrNull(vector) ?? -1;

        /// <summary>不校验地转换线性索引 / Convert linear index without validation</summary>
        public static int[] VectorUnchecked(this IShape shape, int index) => shape.VectorOrNull(index) ?? new int[shape.Dimension];

        /// <summary>安全获取指定维度的步长 / Safely get stride for specified dimension</summary>
        public static RetInt OffsetSafe(this IShape shape, int dimension)
        {
            if (dimension >= shape.Dimension || dimension < 0)
            {
                return new FailedRetInt(
                    ErrorCode.IllegalArgument,
                    $"Dimension mismatch: expected 0..{shape.Dimension - 1}, got {dimension}");
            }
            return new OkRetInt(shape.Offsets[dimension]);
        }

        /// <summary>尝试获取指定维度的步长 / Try get stride for specified dimension</summary>
        public static int? OffsetOrNull(this IShape shape, int dimension) => shape.OffsetSafe(dimension).Value;

        /// <summary>计算实际索引，处理负数索引 / Calculate actual index, handles negative indices</summary>
        public static int? ActualIndex(this IShape shape, int dimension, int index)
        {
            if (dimension < 0 || dimension >= shape.Dimension) return null;
            var len = shape[dimension];
            if (index >= len || index < -len) return null;
            return index >= 0 ? index : len + index;
        }

        /// <summary>获取下一个向量索引 / Get the next vector index</summary>
        public static int[]? Next(this IShape shape, int[] vector)
        {
            var temp = (int[])vector.Clone();
            for (int i = shape.Dimension - 1; i >= 0; i--)
            {
                if (shape[i] == 0 || temp[i] == shape[i] - 1)
                {
                    temp[i] = 0;
                }
                else
                {
                    temp[i] = temp[i] + 1;
                    return temp;
                }
            }
            return null;
        }

        /// <summary>将虚拟向量转换为迭代器向量 / Convert dummy vector to iterator vector</summary>
        public static IReadOnlyList<DummyIndexIterator> DummyToIteratorVector(this IShape shape, IReadOnlyList<DummyIndex> dummyVector)
        {
            return dummyVector.Select((d, i) => d.IteratorOf(shape, i)).ToList();
        }

        /// <summary>将虚拟向量转换为映射向量 / Convert dummy vector to map vector</summary>
        public static IReadOnlyList<MapIndex> DummyToMapVector(this IShape shape, IReadOnlyList<DummyIndex> dummyVector)
        {
            return dummyVector.Select(d => (MapIndex)new MapIndex.Dummy(d)).ToList();
        }

        /// <summary>将映射向量转换为迭代器向量 / Convert map vector to iterator vector</summary>
        public static IReadOnlyList<DummyIndexIterator> MapToIteratorVector(this IShape shape, IReadOnlyList<MapIndex> mapVector)
        {
            return mapVector.Select((m, i) =>
            {
                if (m is MapIndex.Dummy d) return d.DummyValue.IteratorOf(shape, i);
                if (m is MapIndex.Map mp) return (DummyIndexIterator)new DummyIndexIterator.Continuous(0, shape[mp.Index]);
                throw new InvalidOperationException("Unknown MapIndex type");
            }).ToList();
        }

        /// <summary>从任意类型数组创建虚拟向量 / Create dummy vector from any type array</summary>
        public static Result<IReadOnlyList<DummyIndex>, ErrorCode, Error<ErrorCode>> DummyVector(this IShape shape, params object[] v)
        {
            return shape.DummyVectorSafe(v);
        }

        /// <summary>安全创建虚拟向量 / Safely create dummy vector</summary>
        public static Result<IReadOnlyList<DummyIndex>, ErrorCode, Error<ErrorCode>> DummyVectorSafe(this IShape shape, params object[] v)
        {
            if (v.Length != shape.Dimension)
            {
                return new Failed<DummyVecResult, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"Dummy vector dimension mismatch: expected {shape.Dimension}, got {v.Length}");
            }
            var vector = new List<DummyIndex>();
            for (int i = 0; i < shape.Dimension; i++)
            {
                var index = v[i];
                switch (index)
                {
                    case DummyIndex.All:
                        vector.Add(DummyIndex.AllInstance);
                        break;
                    case Range r:
                        vector.Add(DummyIndex.From(r));
                        break;
                    case int intVal:
                        vector.Add(DummyIndex.From(intVal));
                        break;
                    case IIndexed indexed:
                        vector.Add(DummyIndex.From(indexed.Index));
                        break;
                    case DummyIndex di:
                        vector.Add(di);
                        break;
                    default:
                        return new Failed<DummyVecResult, ErrorCode, Error<ErrorCode>>(
                            ErrorCode.IllegalArgument,
                            $"Unknown dummy index type: {index?.GetType().Name}");
                }
            }
            return new Ok<DummyVecResult, ErrorCode, Error<ErrorCode>>(vector);
        }

        /// <summary>不校验地创建虚拟向量 / Create dummy vector without validation</summary>
        public static IReadOnlyList<DummyIndex> DummyVectorUnchecked(this IShape shape, params object[] v)
        {
            return shape.DummyVectorOrNull(v) ?? (IReadOnlyList<DummyIndex>)Array.Empty<DummyIndex>();
        }

        /// <summary>尝试创建虚拟向量 / Try create dummy vector</summary>
        public static IReadOnlyList<DummyIndex>? DummyVectorOrNull(this IShape shape, params object[] v)
        {
            return shape.DummyVectorSafe(v).Value;
        }
    }

    /// <summary>
    /// 一维形状
    /// 1D shape
    /// </summary>
    public sealed record Shape1 : IShape
    {
        private readonly int _d1;

        private Shape1(int d1, StorageOrder storageOrder)
        {
            _d1 = d1;
            StorageOrder = storageOrder;
        }

        /// <summary>第一维度长度 / Length of first dimension</summary>
        public int D1 => _d1;

        /// <inheritdoc/>
        public int Dimension => 1;

        /// <inheritdoc/>
        public int Size => _d1;

        /// <inheritdoc/>
        public StorageOrder StorageOrder { get; }

        /// <inheritdoc/>
        public int[] Offsets => new[] { 1 };

        /// <inheritdoc/>
        public int this[int index] => index == 0 ? _d1 : throw new IndexOutOfRangeException($"Shape1 has only dimension 0, got {index}");

        /// <inheritdoc/>
        public RetInt Index(int[] vector)
        {
            if (vector.Length != 1) return new FailedRetInt(ErrorCode.IllegalArgument, $"Dimension mismatch: expected 1, got {vector.Length}");
            if (vector[0] < 0 || vector[0] >= _d1) return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension 0 length is {_d1}, got {vector[0]}");
            return new OkRetInt(vector[0]);
        }

        /// <inheritdoc/>
        public RetIntArray Vector(int index)
        {
            if (index < 0 || index >= _d1) return new FailedIntArray(ErrorCode.IllegalArgument, $"Linear index out of bounds: index {index} is outside shape size {_d1}");
            return new OkIntArray(new[] { index });
        }

        /// <summary>使用指定存储顺序创建副本 / Create copy with specified storage order</summary>
        public Shape1 WithStorageOrder(StorageOrder order) => new(_d1, order);

        /// <summary>使用默认行主序创建 / Create with default row-major order</summary>
        public static Shape1 Invoke(int d1) => new(d1, StorageOrders.Default);

        /// <summary>使用默认行主序从 ULong 创建 / Create from ULong with default row-major order</summary>
        public static Shape1 Invoke(ulong d1) => new((int)d1, StorageOrders.Default);

        /// <summary>使用默认行主序从集合大小创建 / Create from collection size with default row-major order</summary>
        public static Shape1 Invoke(ICollection<int> d1) => new(d1.Count, StorageOrders.Default);

        /// <summary>使用指定存储顺序创建 / Create with specified storage order</summary>
        public static Shape1 WithOrder(int d1, StorageOrder order) => new(d1, order);

        /// <summary>使用指定存储顺序从 ULong 创建 / Create from ULong with specified storage order</summary>
        public static Shape1 WithOrder(ulong d1, StorageOrder order) => new((int)d1, order);
    }

    /// <summary>
    /// 二维形状
    /// 2D shape
    /// </summary>
    public sealed record Shape2 : IShape
    {
        private readonly int _d1;
        private readonly int _d2;
        private readonly Lazy<int> _totalSize;
        private readonly Lazy<int[]> _offsets;

        private Shape2(int d1, int d2, StorageOrder storageOrder)
        {
            _d1 = d1;
            _d2 = d2;
            StorageOrder = storageOrder;
            _totalSize = new Lazy<int>(() => d1 * d2);
            _offsets = new Lazy<int[]>(() => storageOrder switch
            {
                StorageOrder.RowMajor => new[] { d2, 1 },
                StorageOrder.ColumnMajor => new[] { 1, d1 },
                _ => new[] { d2, 1 }
            });
        }

        /// <summary>第一维度长度 / Length of first dimension</summary>
        public int D1 => _d1;

        /// <summary>第二维度长度 / Length of second dimension</summary>
        public int D2 => _d2;

        /// <inheritdoc/>
        public int Dimension => 2;

        /// <inheritdoc/>
        public int Size => _totalSize.Value;

        /// <inheritdoc/>
        public StorageOrder StorageOrder { get; }

        /// <inheritdoc/>
        public int[] Offsets => _offsets.Value;

        /// <inheritdoc/>
        public int this[int index] => index switch
        {
            0 => _d1,
            1 => _d2,
            _ => throw new IndexOutOfRangeException($"Shape2 has dimensions 0-1, got {index}")
        };

        /// <inheritdoc/>
        public RetInt Index(int[] vector)
        {
            if (vector.Length != 2) return new FailedRetInt(ErrorCode.IllegalArgument, $"Dimension mismatch: expected 2, got {vector.Length}");
            if (vector[0] < 0 || vector[0] >= _d1) return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension 0 length is {_d1}, got {vector[0]}");
            if (vector[1] < 0 || vector[1] >= _d2) return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension 1 length is {_d2}, got {vector[1]}");
            return new OkRetInt(vector[0] * Offsets[0] + vector[1] * Offsets[1]);
        }

        /// <inheritdoc/>
        public RetIntArray Vector(int index)
        {
            var total = _totalSize.Value;
            if (index < 0 || index >= total) return new FailedIntArray(ErrorCode.IllegalArgument, $"Linear index out of bounds: index {index} is outside shape size {total}");
            var off = Offsets;
            return new OkIntArray(StorageOrder switch
            {
                StorageOrder.RowMajor => new[] { index / off[0], index % off[0] / off[1] },
                StorageOrder.ColumnMajor => new[] { index % _d1, index / _d1 },
                _ => new[] { index / off[0], index % off[0] / off[1] }
            });
        }

        /// <summary>使用指定存储顺序创建副本 / Create copy with specified storage order</summary>
        public Shape2 WithStorageOrder(StorageOrder order) => new(_d1, _d2, order);

        /// <summary>使用默认行主序创建 / Create with default row-major order</summary>
        public static Shape2 Invoke(int d1, int d2) => new(d1, d2, StorageOrders.Default);

        /// <summary>使用默认行主序从 ULong 创建 / Create from ULong with default row-major order</summary>
        public static Shape2 Invoke(ulong d1, ulong d2) => new((int)d1, (int)d2, StorageOrders.Default);

        /// <summary>使用默认行主序从集合大小创建 / Create from collection sizes with default row-major order</summary>
        public static Shape2 Invoke(ICollection<int> d1, ICollection<int> d2) => new(d1.Count, d2.Count, StorageOrders.Default);

        /// <summary>使用指定存储顺序创建 / Create with specified storage order</summary>
        public static Shape2 WithOrder(int d1, int d2, StorageOrder order) => new(d1, d2, order);

        /// <summary>使用指定存储顺序从 ULong 创建 / Create from ULong with specified storage order</summary>
        public static Shape2 WithOrder(ulong d1, ulong d2, StorageOrder order) => new((int)d1, (int)d2, order);
    }

    /// <summary>
    /// 三维形状
    /// 3D shape
    /// </summary>
    public sealed record Shape3 : IShape
    {
        private readonly int _d1;
        private readonly int _d2;
        private readonly int _d3;
        private readonly Lazy<int> _totalSize;
        private readonly Lazy<int[]> _offsets;

        private Shape3(int d1, int d2, int d3, StorageOrder storageOrder)
        {
            _d1 = d1;
            _d2 = d2;
            _d3 = d3;
            StorageOrder = storageOrder;
            _totalSize = new Lazy<int>(() => d1 * d2 * d3);
            _offsets = new Lazy<int[]>(() => storageOrder switch
            {
                StorageOrder.RowMajor => new[] { d2 * d3, d3, 1 },
                StorageOrder.ColumnMajor => new[] { 1, d1, d1 * d2 },
                _ => new[] { d2 * d3, d3, 1 }
            });
        }

        /// <summary>第一维度长度 / Length of first dimension</summary>
        public int D1 => _d1;

        /// <summary>第二维度长度 / Length of second dimension</summary>
        public int D2 => _d2;

        /// <summary>第三维度长度 / Length of third dimension</summary>
        public int D3 => _d3;

        /// <inheritdoc/>
        public int Dimension => 3;

        /// <inheritdoc/>
        public int Size => _totalSize.Value;

        /// <inheritdoc/>
        public StorageOrder StorageOrder { get; }

        /// <inheritdoc/>
        public int[] Offsets => _offsets.Value;

        /// <inheritdoc/>
        public int this[int index] => index switch
        {
            0 => _d1,
            1 => _d2,
            2 => _d3,
            _ => throw new IndexOutOfRangeException($"Shape3 has dimensions 0-2, got {index}")
        };

        /// <inheritdoc/>
        public RetInt Index(int[] vector)
        {
            if (vector.Length != 3) return new FailedRetInt(ErrorCode.IllegalArgument, $"Dimension mismatch: expected 3, got {vector.Length}");
            if (vector[0] < 0 || vector[0] >= _d1) return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension 0 length is {_d1}, got {vector[0]}");
            if (vector[1] < 0 || vector[1] >= _d2) return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension 1 length is {_d2}, got {vector[1]}");
            if (vector[2] < 0 || vector[2] >= _d3) return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension 2 length is {_d3}, got {vector[2]}");
            var off = Offsets;
            return new OkRetInt(vector[0] * off[0] + vector[1] * off[1] + vector[2] * off[2]);
        }

        /// <inheritdoc/>
        public RetIntArray Vector(int index)
        {
            var total = _totalSize.Value;
            if (index < 0 || index >= total) return new FailedIntArray(ErrorCode.IllegalArgument, $"Linear index out of bounds: index {index} is outside shape size {total}");
            var off = Offsets;
            return new OkIntArray(StorageOrder switch
            {
                StorageOrder.RowMajor => new[]
                {
                    index / off[0],
                    index % off[0] / off[1],
                    index % off[1]
                },
                StorageOrder.ColumnMajor => new[]
                {
                    index % _d1,
                    index / _d1 % _d2,
                    index / _d1 / _d2
                },
                _ => new[] { index / off[0], index % off[0] / off[1], index % off[1] }
            });
        }

        /// <summary>使用指定存储顺序创建副本 / Create copy with specified storage order</summary>
        public Shape3 WithStorageOrder(StorageOrder order) => new(_d1, _d2, _d3, order);

        /// <summary>使用默认行主序创建 / Create with default row-major order</summary>
        public static Shape3 Invoke(int d1, int d2, int d3) => new(d1, d2, d3, StorageOrders.Default);

        /// <summary>使用默认行主序从 ULong 创建 / Create from ULong with default row-major order</summary>
        public static Shape3 Invoke(ulong d1, ulong d2, ulong d3) => new((int)d1, (int)d2, (int)d3, StorageOrders.Default);

        /// <summary>使用默认行主序从集合大小创建 / Create from collection sizes with default row-major order</summary>
        public static Shape3 Invoke(ICollection<int> d1, ICollection<int> d2, ICollection<int> d3) => new(d1.Count, d2.Count, d3.Count, StorageOrders.Default);

        /// <summary>使用指定存储顺序创建 / Create with specified storage order</summary>
        public static Shape3 WithOrder(int d1, int d2, int d3, StorageOrder order) => new(d1, d2, d3, order);

        /// <summary>使用指定存储顺序从 ULong 创建 / Create from ULong with specified storage order</summary>
        public static Shape3 WithOrder(ulong d1, ulong d2, ulong d3, StorageOrder order) => new((int)d1, (int)d2, (int)d3, order);
    }

    /// <summary>
    /// 四维形状
    /// 4D shape
    /// </summary>
    public sealed record Shape4 : IShape
    {
        private readonly int _d1;
        private readonly int _d2;
        private readonly int _d3;
        private readonly int _d4;
        private readonly Lazy<int> _totalSize;
        private readonly Lazy<int[]> _offsets;

        private Shape4(int d1, int d2, int d3, int d4, StorageOrder storageOrder)
        {
            _d1 = d1;
            _d2 = d2;
            _d3 = d3;
            _d4 = d4;
            StorageOrder = storageOrder;
            _totalSize = new Lazy<int>(() => d1 * d2 * d3 * d4);
            _offsets = new Lazy<int[]>(() => storageOrder switch
            {
                StorageOrder.RowMajor => new[] { d2 * d3 * d4, d3 * d4, d4, 1 },
                StorageOrder.ColumnMajor => new[] { 1, d1, d1 * d2, d1 * d2 * d3 },
                _ => new[] { d2 * d3 * d4, d3 * d4, d4, 1 }
            });
        }

        /// <inheritdoc/>
        public int Dimension => 4;

        /// <inheritdoc/>
        public int Size => _totalSize.Value;

        /// <inheritdoc/>
        public StorageOrder StorageOrder { get; }

        /// <inheritdoc/>
        public int[] Offsets => _offsets.Value;

        /// <inheritdoc/>
        public int this[int index] => index switch
        {
            0 => _d1,
            1 => _d2,
            2 => _d3,
            3 => _d4,
            _ => throw new IndexOutOfRangeException($"Shape4 has dimensions 0-3, got {index}")
        };

        /// <inheritdoc/>
        public RetInt Index(int[] vector)
        {
            if (vector.Length != 4) return new FailedRetInt(ErrorCode.IllegalArgument, $"Dimension mismatch: expected 4, got {vector.Length}");
            if (vector[0] < 0 || vector[0] >= _d1) return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension 0 length is {_d1}, got {vector[0]}");
            if (vector[1] < 0 || vector[1] >= _d2) return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension 1 length is {_d2}, got {vector[1]}");
            if (vector[2] < 0 || vector[2] >= _d3) return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension 2 length is {_d3}, got {vector[2]}");
            if (vector[3] < 0 || vector[3] >= _d4) return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension 3 length is {_d4}, got {vector[3]}");
            var off = Offsets;
            return new OkRetInt(vector[0] * off[0] + vector[1] * off[1] + vector[2] * off[2] + vector[3] * off[3]);
        }

        /// <inheritdoc/>
        public RetIntArray Vector(int index)
        {
            var total = _totalSize.Value;
            if (index < 0 || index >= total) return new FailedIntArray(ErrorCode.IllegalArgument, $"Linear index out of bounds: index {index} is outside shape size {total}");
            var off = Offsets;
            return new OkIntArray(StorageOrder switch
            {
                StorageOrder.RowMajor => new[]
                {
                    index / off[0],
                    index % off[0] / off[1],
                    index % off[1] / off[2],
                    index % off[2]
                },
                StorageOrder.ColumnMajor => new[]
                {
                    index % _d1,
                    index / _d1 % _d2,
                    index / _d1 / _d2 % _d3,
                    index / _d1 / _d2 / _d3
                },
                _ => new[] { index / off[0], index % off[0] / off[1], index % off[1] / off[2], index % off[2] }
            });
        }

        /// <summary>使用指定存储顺序创建副本 / Create copy with specified storage order</summary>
        public Shape4 WithStorageOrder(StorageOrder order) => new(_d1, _d2, _d3, _d4, order);

        /// <summary>使用默认行主序创建 / Create with default row-major order</summary>
        public static Shape4 Invoke(int d1, int d2, int d3, int d4) => new(d1, d2, d3, d4, StorageOrders.Default);

        /// <summary>使用默认行主序从 ULong 创建 / Create from ULong with default row-major order</summary>
        public static Shape4 Invoke(ulong d1, ulong d2, ulong d3, ulong d4) => new((int)d1, (int)d2, (int)d3, (int)d4, StorageOrders.Default);

        /// <summary>使用默认行主序从集合大小创建 / Create from collection sizes with default row-major order</summary>
        public static Shape4 Invoke(ICollection<int> d1, ICollection<int> d2, ICollection<int> d3, ICollection<int> d4) => new(d1.Count, d2.Count, d3.Count, d4.Count, StorageOrders.Default);

        /// <summary>使用指定存储顺序创建 / Create with specified storage order</summary>
        public static Shape4 WithOrder(int d1, int d2, int d3, int d4, StorageOrder order) => new(d1, d2, d3, d4, order);

        /// <summary>使用指定存储顺序从 ULong 创建 / Create from ULong with specified storage order</summary>
        public static Shape4 WithOrder(ulong d1, ulong d2, ulong d3, ulong d4, StorageOrder order) => new((int)d1, (int)d2, (int)d3, (int)d4, order);
    }

    /// <summary>
    /// 动态维度形状
    /// Dynamic dimension shape
    /// </summary>
    public sealed record DynShape : IShape
    {
        private readonly int[] _shape;
        private readonly Lazy<int> _totalSize;
        private readonly Lazy<int[]> _offsets;

        private DynShape(int[] shape, StorageOrder storageOrder)
        {
            _shape = (int[])shape.Clone();
            StorageOrder = storageOrder;
            _totalSize = new Lazy<int>(() =>
            {
                var ret = 1;
                foreach (var l in _shape) ret *= l;
                return ret;
            });
            _offsets = new Lazy<int[]>(() => storageOrder switch
            {
                StorageOrder.RowMajor => CalculateOffsetsRowMajor(_shape),
                StorageOrder.ColumnMajor => CalculateOffsetsColumnMajor(_shape),
                _ => CalculateOffsetsRowMajor(_shape)
            });
        }

        /// <summary>各维度长度的数组 / Array of dimension lengths</summary>
        public int[] ShapeArray => _shape;

        /// <inheritdoc/>
        public int Dimension => _shape.Length;

        /// <inheritdoc/>
        public int Size => _totalSize.Value;

        /// <inheritdoc/>
        public StorageOrder StorageOrder { get; }

        /// <inheritdoc/>
        public int[] Offsets => _offsets.Value;

        /// <inheritdoc/>
        public int this[int index] => _shape[index];

        /// <inheritdoc/>
        public RetInt Index(int[] vector)
        {
            if (Dimension != vector.Length) return new FailedRetInt(ErrorCode.IllegalArgument, $"Dimension mismatch: expected {Dimension}, got {vector.Length}");
            var ret = 0;
            var off = Offsets;
            for (int i = 0; i < _shape.Length; i++)
            {
                if (vector[i] < 0 || vector[i] >= _shape[i])
                    return new FailedRetInt(ErrorCode.IllegalArgument, $"Shape index out of bounds: dimension {i} length is {_shape[i]}, got {vector[i]}");
                ret += vector[i] * off[i];
            }
            return new OkRetInt(ret);
        }

        /// <inheritdoc/>
        public RetIntArray Vector(int index)
        {
            var total = _totalSize.Value;
            if (index < 0 || index >= total) return new FailedIntArray(ErrorCode.IllegalArgument, $"Linear index out of bounds: index {index} is outside shape size {total}");
            var off = Offsets;
            var dim = Dimension;
            return new OkIntArray(StorageOrder switch
            {
                StorageOrder.RowMajor =>
                    Enumerable.Range(0, dim).Select(i =>
                    {
                        var result = index / off[i];
                        index %= off[i];
                        return result;
                    }).ToArray(),
                StorageOrder.ColumnMajor =>
                    Enumerable.Range(0, dim).Select(i =>
                    {
                        if (i == dim - 1) return index;
                        var result = index % _shape[i];
                        index /= _shape[i];
                        return result;
                    }).ToArray(),
                _ =>
                    Enumerable.Range(0, dim).Select(i =>
                    {
                        var result = index / off[i];
                        index %= off[i];
                        return result;
                    }).ToArray()
            });
        }

        /// <summary>使用指定存储顺序创建副本 / Create copy with specified storage order</summary>
        public DynShape WithStorageOrder(StorageOrder order) => new(_shape, order);

        /// <summary>使用默认行主序从 IntArray 创建 / Create from IntArray with default row-major order</summary>
        public static DynShape Invoke(int[] shape) => new(shape, StorageOrders.Default);

        /// <summary>使用默认行主序从 ULong 迭代创建 / Create from ULong enumerable with default row-major order</summary>
        public static DynShape Invoke(IEnumerable<ulong> shape) => new(shape.Select(x => (int)x).ToArray(), StorageOrders.Default);

        /// <summary>使用默认行主序从集合大小迭代创建 / Create from collection sizes with default row-major order</summary>
        public static DynShape Invoke(IEnumerable<ICollection<int>> shape) => new(shape.Select(x => x.Count).ToArray(), StorageOrders.Default);

        /// <summary>使用指定存储顺序从 IntArray 创建 / Create from IntArray with specified storage order</summary>
        public static DynShape WithOrder(int[] shape, StorageOrder order) => new(shape, order);

        /// <summary>使用指定存储顺序从 ULong 迭代创建 / Create from ULong enumerable with specified storage order</summary>
        public static DynShape WithOrder(IEnumerable<ulong> shape, StorageOrder order) => new(shape.Select(x => (int)x).ToArray(), order);

        /// <summary>使用指定存储顺序从集合大小迭代创建 / Create from collection sizes with specified storage order</summary>
        public static DynShape WithOrder(IEnumerable<ICollection<int>> shape, StorageOrder order) => new(shape.Select(x => x.Count).ToArray(), order);

        private static int[] CalculateOffsetsRowMajor(int[] shape)
        {
            if (shape.Length == 0) return Array.Empty<int>();
            var offsets = new int[shape.Length];
            offsets[^1] = 1;
            for (int i = shape.Length - 2; i >= 0; i--)
                offsets[i] = offsets[i + 1] * shape[i + 1];
            return offsets;
        }

        private static int[] CalculateOffsetsColumnMajor(int[] shape)
        {
            if (shape.Length == 0) return Array.Empty<int>();
            var offsets = new int[shape.Length];
            offsets[0] = 1;
            for (int i = 1; i < shape.Length; i++)
                offsets[i] = offsets[i - 1] * shape[i - 1];
            return offsets;
        }

        /// <inheritdoc/>
        public bool Equals(DynShape? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return _shape.AsSpan().SequenceEqual(other._shape) && StorageOrder == other.StorageOrder;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var d in _shape) hash.Add(d);
            hash.Add(StorageOrder);
            return hash.ToHashCode();
        }
    }
}
