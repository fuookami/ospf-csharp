#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace Fuookami.Ospf.MultiArray
{
    /// <summary>
    /// 访问顺序枚举
    /// Access order enum
    /// </summary>
    public enum AccessOrder
    {
        /// <summary>行主序（C 风格）/ Row-major (C style)</summary>
        RowMajor,

        /// <summary>列主序（Fortran 风格）/ Column-major (Fortran style)</summary>
        ColumnMajor
    }

    /// <summary>
    /// 访问顺序默认值提供者
    /// Access order default value provider
    /// </summary>
    public static class AccessOrders
    {
        /// <summary>默认访问顺序（行主序）/ Default access order (row-major)</summary>
        public const AccessOrder Default = AccessOrder.RowMajor;
    }

    /// <summary>
    /// 迭代器位置
    /// Iterator position
    /// </summary>
    public sealed record IteratorPosition(int[] Positions, bool Exhausted = false)
    {
        /// <inheritdoc/>
        public bool Equals(IteratorPosition? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Positions.AsSpan().SequenceEqual(other.Positions) && Exhausted == other.Exhausted;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var p in Positions) hash.Add(p);
            hash.Add(Exhausted);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// 多维索引迭代器
    /// Multi-dimensional index iterator
    /// </summary>
    public sealed class MultiIndexIterator : IEnumerator<int[]>
    {
        private readonly IShape _shape;
        private readonly AccessOrder _accessOrder;
        private readonly int _totalSize;
        private int[]? _current;
        private int _count;

        /// <summary>构造函数 / Constructor</summary>
        public MultiIndexIterator(IShape shape, AccessOrder accessOrder = AccessOrders.Default)
        {
            _shape = shape;
            _accessOrder = accessOrder;
            _totalSize = shape.Size;
            _current = null;
            _count = 0;
        }

        /// <inheritdoc/>
        public int[] Current => _current ?? throw new InvalidOperationException("Iterator is not positioned at a valid element.");

        /// <inheritdoc/>
        object? IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (_count >= _totalSize) return false;

            if (_current == null)
            {
                _current = new int[_shape.Dimension];
                _count = 1;
                return true;
            }

            var next = Advance(_current);
            if (next == null)
            {
                _count = _totalSize;
                return false;
            }

            _count++;
            return true;
        }

        /// <summary>已迭代元素数量 / Count of iterated elements</summary>
        public int Count => _count;

        /// <inheritdoc/>
        public void Reset()
        {
            _current = null;
            _count = 0;
        }

        /// <inheritdoc/>
        public void Dispose() { }

        private int[]? Advance(int[] v)
        {
            switch (_accessOrder)
            {
                case AccessOrder.RowMajor:
                    for (int i = v.Length - 1; i >= 0; i--)
                    {
                        if (v[i] + 1 < _shape[i])
                        {
                            v[i]++;
                            return v;
                        }
                        v[i] = 0;
                    }
                    return null;

                case AccessOrder.ColumnMajor:
                    for (int i = 0; i < v.Length; i++)
                    {
                        if (v[i] + 1 < _shape[i])
                        {
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
    public sealed class MultiIndexSequence : IEnumerable<int[]>
    {
        private readonly IShape _shape;
        private readonly AccessOrder _accessOrder;

        /// <summary>构造函数 / Constructor</summary>
        public MultiIndexSequence(IShape shape, AccessOrder accessOrder = AccessOrders.Default)
        {
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
}
