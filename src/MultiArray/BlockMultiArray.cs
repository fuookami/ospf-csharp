#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.MultiArray
{
    /// <summary>
    /// 分块多维数组（稀疏存储）
    /// Block multi-dimensional array (sparse storage)
    /// </summary>
    public class BlockMultiArray<T, S> : ICollection<T>
        where T : notnull
        where S : IShape
    {
        private sealed class IndexKey : IEquatable<IndexKey>
        {
            public int[] Indices { get; }
            private readonly int _hash;

            private IndexKey(int[] indices, bool copy)
            {
                Indices = copy ? (int[])indices.Clone() : indices;
                _hash = ComputeHashCode(Indices);
            }

            public static IndexKey Persistent(int[] indices) => new(indices, true);
            public static IndexKey Transient(int[] indices) => new(indices, false);

            public int[] AsIntArray() => Indices;
            public IReadOnlyList<int> ToListKey() => Indices;

            public bool Equals(IndexKey? other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Indices.AsSpan().SequenceEqual(other.Indices);
            }

            public override bool Equals(object? obj) => Equals(obj as IndexKey);
            public override int GetHashCode() => _hash;

            private static int ComputeHashCode(int[] indices)
            {
                var hash = new HashCode();
                foreach (var i in indices) hash.Add(i);
                return hash.ToHashCode();
            }
        }

        private readonly Dictionary<IndexKey, T> _blocks;

        private BlockMultiArray(S shape, Dictionary<IndexKey, T> blocks)
        {
            Shape = shape;
            _blocks = blocks;
        }

        /// <summary>构造函数 / Constructor</summary>
        public BlockMultiArray(S shape) : this(shape, new Dictionary<IndexKey, T>()) { }

        /// <summary>从字典构造 / Constructor from dictionary</summary>
        public BlockMultiArray(S shape, IReadOnlyDictionary<IReadOnlyList<int>, T> blocks)
            : this(shape, blocks.ToDictionary(
                kvp => IndexKey.Persistent(kvp.Key.ToArray()),
                kvp => kvp.Value))
        {
        }

        /// <summary>数组形状 / Array shape</summary>
        public S Shape { get; }

        /// <summary>通过 vararg 索引获取元素 / Get element by vararg indices</summary>
        public T? Get(params int[] indices)
        {
            return _blocks.TryGetValue(IndexKey.Transient(indices), out var value) ? value : default;
        }

        /// <summary>通过向量索引设置元素 / Set element by vector index</summary>
        public void Set(int[] indices, T value)
        {
            _blocks[IndexKey.Persistent(indices)] = value;
        }

        /// <summary>获取或设置默认值 / Get or set default value</summary>
        public T GetOrSet(int[] indices, Func<T> defaultValue)
        {
            var key = IndexKey.Transient(indices);
            if (_blocks.TryGetValue(key, out var existed))
                return existed;
            var value = defaultValue();
            _blocks[IndexKey.Persistent(indices)] = value;
            return value;
        }

        /// <summary>检查是否包含指定索引 / Check if index exists</summary>
        public bool ContainsIndex(int[] indices) => _blocks.ContainsKey(IndexKey.Transient(indices));

        /// <summary>移除指定索引的元素 / Remove element at index</summary>
        public T? Remove(int[] indices)
        {
            var key = IndexKey.Transient(indices);
            if (_blocks.TryGetValue(key, out var value))
            {
                _blocks.Remove(key);
                return value;
            }
            return default;
        }

        /// <summary>清除所有元素 / Clear all elements</summary>
        public void Clear() => _blocks.Clear();

        /// <summary>获取所有已存储的索引 / Get all stored indices</summary>
        public IReadOnlySet<IReadOnlyList<int>> Indices()
        {
            return new HashSet<IReadOnlyList<int>>(
                _blocks.Keys.Select(k => (IReadOnlyList<int>)k.Indices.ToList()),
                new ListIntEqualityComparer());
        }

        /// <summary>转换为 MultiArray / Convert to MultiArray</summary>
        public MultiArray<T, S> ToMultiArray(T defaultValue)
        {
            var array = MutableMultiArray<T, S>.Factory.NewWith(Shape, defaultValue);
            foreach (var (key, value) in _blocks)
            {
                array[key.AsIntArray()] = value;
            }
            return array.ToImmutable();
        }

        /// <inheritdoc/>
        public int Count => _blocks.Count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public bool Contains(T item) => _blocks.Values.Contains(item);

        /// <inheritdoc/>
        public bool ContainsAll(IEnumerable<T> items) => items.All(Contains);

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex) => _blocks.Values.ToList().CopyTo(array, arrayIndex);

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => _blocks.Values.GetEnumerator();

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<T>.Add(T item) => throw new NotSupportedException("Use Set(int[] indices, T value) instead.");
        bool ICollection<T>.Remove(T item) => throw new NotSupportedException("Use Remove(int[] indices) instead.");

        /// <summary>从 MultiArray 创建 / Create from MultiArray</summary>
        public static BlockMultiArray<T, S> FromMultiArray(MultiArray<T, S> array, Func<T, bool>? filter = null)
        {
            var blocks = new Dictionary<IndexKey, T>();
            for (int i = 0; i < array.Shape.Size; i++)
            {
                var vector = array.Shape.VectorUnchecked(i);
                var value = array[vector];
                if (filter == null || filter(value))
                    blocks[IndexKey.Persistent(vector)] = value;
            }
            return new BlockMultiArray<T, S>(array.Shape, blocks);
        }

        /// <summary>创建空的 BlockMultiArray / Create empty BlockMultiArray</summary>
        public static BlockMultiArray<T, S> Empty(S shape) => new(shape);

        private sealed class ListIntEqualityComparer : IEqualityComparer<IReadOnlyList<int>>
        {
            public bool Equals(IReadOnlyList<int>? x, IReadOnlyList<int>? y)
            {
                if (x is null && y is null) return true;
                if (x is null || y is null) return false;
                return x.SequenceEqual(y);
            }

            public int GetHashCode(IReadOnlyList<int> obj)
            {
                var hash = new HashCode();
                foreach (var i in obj) hash.Add(i);
                return hash.ToHashCode();
            }
        }
    }
}
