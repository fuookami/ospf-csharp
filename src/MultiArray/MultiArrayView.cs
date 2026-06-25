#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.MultiArray
{
    /// <summary>
    /// 多维数组视图
    /// Multi-dimensional array view
    /// </summary>
    public class MultiArrayView<T, S> : ICollection<T>
        where T : notnull
        where S : IShape
    {
        private readonly AbstractMultiArray<T, S> _origin;
        private readonly IReadOnlyList<DummyIndex> _dummyVector;
        private readonly IReadOnlyList<DummyIndexIterator> _iteratorVector;
        private readonly HashSet<int> _dummyDimensions;

        /// <summary>构造函数 / Constructor</summary>
        public MultiArrayView(AbstractMultiArray<T, S> origin, IReadOnlyList<DummyIndex> dummyVector)
        {
            _origin = origin;
            _dummyVector = dummyVector;
            _iteratorVector = origin.Shape.DummyToIteratorVector(dummyVector);

            var shapeList = new List<int>();
            var dummyDims = new HashSet<int>();

            for (int dim = 0; dim < dummyVector.Count; dim++)
            {
                var len = dummyVector[dim].LenOf(origin.Shape, dim);
                if (len > 1)
                {
                    shapeList.Add(len);
                    dummyDims.Add(dim);
                }
            }

            Shape = DynShape.Invoke(shapeList.ToArray());
            _dummyDimensions = dummyDims;
        }

        /// <summary>全范围构造 / All-range constructor</summary>
        public MultiArrayView(AbstractMultiArray<T, S> origin)
            : this(origin, Enumerable.Range(0, origin.Dimension).Select(_ => (DummyIndex)DummyIndex.AllInstance).ToList())
        {
        }

        /// <summary>视图的形状 / Shape of the view</summary>
        public DynShape Shape { get; }

        /// <inheritdoc/>
        public int Count => Shape.Size;

        /// <inheritdoc/>
        public bool IsReadOnly => true;

        /// <summary>通过线性索引获取元素 / Get element by linear index</summary>
        public T this[int i] => _origin[ActualVectorUnchecked(Shape.VectorUnchecked(i))];

        /// <summary>通过向量索引获取元素 / Get element by vector index (safe)</summary>
        public Result<T, ErrorCode, Error<ErrorCode>> this[int[] v] =>
            ActualVector(v).Map(vec => _origin[vec]);

        /// <summary>通过任意类型数组创建子视图 / Create sub-view</summary>
        public MultiArrayView<T, S> this[params object[] v]
        {
            get
            {
                var subDummyVector = Shape.DummyVectorUnchecked(v);
                var newDummyVector = new List<DummyIndex>();
                int j = 0;

                for (int i = 0; i < _origin.Shape.Dimension; i++)
                {
                    if (_dummyDimensions.Contains(i))
                    {
                        if (j < subDummyVector.Count)
                            newDummyVector.Add(subDummyVector[j]);
                        j++;
                    }
                    else
                    {
                        newDummyVector.Add(_dummyVector[i]);
                    }
                }

                return new MultiArrayView<T, S>(_origin, newDummyVector);
            }
        }

        /// <inheritdoc/>
        public bool Contains(T item)
        {
            foreach (var elem in this)
                if (Equals(elem, item)) return true;
            return false;
        }

        /// <inheritdoc/>
        public bool ContainsAll(IEnumerable<T> items) => items.All(Contains);

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex)
        {
            int idx = arrayIndex;
            foreach (var item in this)
                array[idx++] = item;
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Shape.Size; i++)
                yield return this[i];
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        void ICollection<T>.Add(T item) => throw new NotSupportedException("MultiArrayView is read-only.");

        /// <inheritdoc/>
        void ICollection<T>.Clear() => throw new NotSupportedException("MultiArrayView is read-only.");

        /// <inheritdoc/>
        bool ICollection<T>.Remove(T item) => throw new NotSupportedException("MultiArrayView is read-only.");

        private Result<int[], ErrorCode, Error<ErrorCode>> ActualVector(int[] v)
        {
            if (v.Length != Shape.Dimension)
                return new Failed<int[], ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"View index dimension mismatch: expected {Shape.Dimension}, got {v.Length}.");
            return new Ok<int[], ErrorCode, Error<ErrorCode>>(ActualVectorUnchecked(v));
        }

        private int[] ActualVectorUnchecked(int[] v)
        {
            var result = new int[_origin.Shape.Dimension];
            int viewIndex = 0;

            for (int i = 0; i < _origin.Shape.Dimension; i++)
            {
                if (_dummyDimensions.Contains(i))
                {
                    result[i] = _iteratorVector[i].Get(v[viewIndex++]) ?? 0;
                }
                else
                {
                    var iter = _iteratorVector[i];
                    result[i] = iter switch
                    {
                        DummyIndexIterator.Single s => s.Index,
                        DummyIndexIterator.Continuous c => c.Start,
                        DummyIndexIterator.Discrete d => d.Indices.Count > 0 ? d.Indices[0] : 0,
                        _ => 0
                    };
                }
            }

            return result;
        }
    }

    /// <summary>
    /// 多维数组映射视图
    /// Multi-dimensional array mapped view
    /// </summary>
    public class MappedMultiArrayView<T, S> : ICollection<T>
        where T : notnull
        where S : IShape
    {
        private readonly AbstractMultiArray<T, S> _origin;
        private readonly IReadOnlyList<MapIndex> _mapVector;

        /// <summary>构造函数 / Constructor</summary>
        public MappedMultiArrayView(AbstractMultiArray<T, S> origin, IReadOnlyList<MapIndex> mapVector)
        {
            _origin = origin;
            _mapVector = mapVector;

            // Validate
            if (mapVector.Count != origin.Dimension)
                throw new ArgumentException($"Map vector size ({mapVector.Count}) must match origin dimension ({origin.Dimension})");

            var mapIndices = mapVector.OfType<MapIndex.Map>().Select(m => m.Index).ToList();
            var uniqueIndices = mapIndices.ToHashSet();
            if (mapIndices.Count != uniqueIndices.Count)
                throw new ArgumentException("Duplicate map indices found");

            if (!mapIndices.All(idx => idx >= 0 && idx < origin.Shape.Dimension))
                throw new ArgumentException("Out of bounds map index");

            var sortedIndices = mapIndices.OrderBy(x => x).ToList();
            if (!sortedIndices.SequenceEqual(Enumerable.Range(0, mapIndices.Count)))
                throw new ArgumentException("Non-contiguous map indices");

            // Calculate shape
            var shapeList = new List<int>();
            foreach (var mapIndex in mapVector)
            {
                switch (mapIndex)
                {
                    case MapIndex.Dummy d:
                        shapeList.Add(d.DummyValue.LenOf(origin.Shape, shapeList.Count));
                        break;
                    case MapIndex.Map m:
                        shapeList.Add(origin.Shape[m.Index]);
                        break;
                }
            }
            Shape = DynShape.Invoke(shapeList.ToArray());
        }

        /// <summary>视图的形状 / Shape of the view</summary>
        public DynShape Shape { get; }

        /// <inheritdoc/>
        public int Count => Shape.Size;

        /// <inheritdoc/>
        public bool IsReadOnly => true;

        /// <summary>通过线性索引获取元素 / Get element by linear index</summary>
        public T this[int i] => _origin[MapVectorUnchecked(Shape.VectorUnchecked(i))];

        /// <summary>通过向量索引获取元素 / Get element by vector index (safe)</summary>
        public Result<T, ErrorCode, Error<ErrorCode>> this[int[] v] =>
            MapVectorCheck(v).Map(vec => _origin[vec]);

        /// <summary>通过任意类型数组创建子映射视图 / Create sub-mapped view</summary>
        public MappedMultiArrayView<T, S> this[params object[] v]
        {
            get
            {
                var subDummyVector = Shape.DummyVectorUnchecked(v);
                var newMapVector = new List<MapIndex>();
                int j = 0;

                foreach (var mapIndex in _mapVector)
                {
                    switch (mapIndex)
                    {
                        case MapIndex.Dummy:
                            newMapVector.Add(new MapIndex.Dummy(subDummyVector[j]));
                            j++;
                            break;
                        case MapIndex.Map:
                            newMapVector.Add(mapIndex);
                            break;
                    }
                }

                return new MappedMultiArrayView<T, S>(_origin, newMapVector);
            }
        }

        /// <inheritdoc/>
        public bool Contains(T item)
        {
            foreach (var elem in this)
                if (Equals(elem, item)) return true;
            return false;
        }

        /// <inheritdoc/>
        public bool ContainsAll(IEnumerable<T> items) => items.All(Contains);

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex)
        {
            int idx = arrayIndex;
            foreach (var item in this)
                array[idx++] = item;
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Shape.Size; i++)
                yield return _origin[MapVectorUnchecked(Shape.VectorUnchecked(i))];
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        void ICollection<T>.Add(T item) => throw new NotSupportedException("MappedMultiArrayView is read-only.");

        /// <inheritdoc/>
        void ICollection<T>.Clear() => throw new NotSupportedException("MappedMultiArrayView is read-only.");

        /// <inheritdoc/>
        bool ICollection<T>.Remove(T item) => throw new NotSupportedException("MappedMultiArrayView is read-only.");

        private Result<int[], ErrorCode, Error<ErrorCode>> MapVectorCheck(int[] v)
        {
            if (v.Length != Shape.Dimension)
                return new Failed<int[], ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"Mapped view index dimension mismatch: expected {Shape.Dimension}, got {v.Length}.");
            return new Ok<int[], ErrorCode, Error<ErrorCode>>(MapVectorUnchecked(v));
        }

        private int[] MapVectorUnchecked(int[] v)
        {
            var result = new int[_origin.Shape.Dimension];
            int viewIndex = 0;

            for (int i = 0; i < _mapVector.Count; i++)
            {
                switch (_mapVector[i])
                {
                    case MapIndex.Dummy d:
                        var iter = d.DummyValue.IteratorOf(_origin.Shape, i);
                        result[i] = iter.Get(v[viewIndex++]) ?? 0;
                        break;
                    case MapIndex.Map m:
                        result[m.Index] = v[viewIndex++];
                        break;
                }
            }

            return result;
        }
    }
}
