#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Geometry
{
    /// <summary>
    /// N 维点 / N-dimensional point.
    /// </summary>
    /// <typeparam name="D">维度标签 / Dimension tag (struct implementing IDimension).</typeparam>
    /// <typeparam name="V">坐标值类型 / Coordinate value type.</typeparam>
    public sealed class Point<D, V>
        where D : struct, IDimension
        where V : struct, IFloatingNumber<V>
    {
        private readonly V[] _coords;

        /// <summary>维度标签实例 / Dimension tag instance.</summary>
        public D Dimension { get; }

        /// <summary>维度标签实例（别名）/ Dimension tag instance (alias).</summary>
        public D Dim => Dimension;

        /// <summary>坐标数 / Coordinate count.</summary>
        public int Count => _coords.Length;

        /// <summary>坐标数（别名）/ Coordinate count (alias).</summary>
        public int Size => _coords.Length;

        /// <summary>维度索引范围 / Dimension index range.</summary>
        public IEnumerable<int> Indices => Dimension.Indices;

        /// <summary>
        /// 构造函数 / Constructor.
        /// </summary>
        public Point(V[] coords, D dimension)
        {
            _coords = coords ?? throw new ArgumentNullException(nameof(coords));
            Dimension = dimension;
        }

        /// <summary>索引器 / Indexer.</summary>
        public V this[int index] => _coords[index];

        /// <summary>创建新坐标点 / Create point with new coordinates.</summary>
        public Point<D, V> WithCoords(V[] newCoords) => new(newCoords, Dimension);

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj is not Point<D, V> other) return false;
            if (_coords.Length != other._coords.Length) return false;
            for (var i = 0; i < _coords.Length; i++)
            {
                if (!_coords[i].Eq(other._coords[i])) return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var c in _coords) hash.Add(c);
            return hash.ToHashCode();
        }

        /// <summary>近似相等（使用默认精度）/ Approximately equal (default epsilon).</summary>
        public bool ApproxEq(Point<D, V> other)
        {
            if (_coords.Length != other._coords.Length) return false;
            for (var i = 0; i < _coords.Length; i++)
            {
                if (!_coords[i].Equiv(other._coords[i])) return false;
            }
            return true;
        }

        /// <summary>近似相等（指定精度）/ Approximately equal with custom epsilon.</summary>
        public bool ApproxEq(Point<D, V> other, V epsilon)
        {
            if (_coords.Length != other._coords.Length) return false;
            for (var i = 0; i < _coords.Length; i++)
            {
                if (!_coords[i].Minus(other._coords[i]).Abs().Leq(epsilon)) return false;
            }
            return true;
        }

        /// <summary>欧几里得距离 / Euclidean distance to another point.</summary>
        public V DistanceTo(Point<D, V> other)
        {
            var c = _coords[0].Constants;
            var sum = Dimension.Indices.Aggregate(c.Zero, (acc, i) => acc.Plus(_coords[i].Minus(other._coords[i]).Sqr()));
            return sum.Sqrt();
        }

        // ===== Operators =====

        /// <summary>点加法（逐坐标）/ Point addition (coordinate-wise).</summary>
        public static Point<D, V> operator +(Point<D, V> lhs, Point<D, V> rhs)
            => new(lhs._coords.Zip(rhs._coords, (a, b) => a.Plus(b)).ToArray(), lhs.Dimension);

        /// <summary>点减法（逐坐标）/ Point subtraction (coordinate-wise).</summary>
        public static Point<D, V> operator -(Point<D, V> lhs, Point<D, V> rhs)
            => new(lhs._coords.Zip(rhs._coords, (a, b) => a.Minus(b)).ToArray(), lhs.Dimension);

        // ===== Instance methods =====

        /// <summary>中点 / Midpoint between two points.</summary>
        public Point<D, V> Midpoint(Point<D, V> other)
        {
            var two = ((IRealNumberConstants<V>)_coords[0].Constants).Two;
            return new(_coords.Zip(other._coords, (a, b) => a.Plus(b).Div(two)).ToArray(), Dimension);
        }

        // ===== Static factory methods =====

        /// <summary>2D 零点 / 2D origin point.</summary>
        public static Point<Dim2, Flt64> Origin2
            => new(new[] { Flt64.Zero, Flt64.Zero }, Dim2.Instance);

        /// <summary>3D 零点 / 3D origin point.</summary>
        public static Point<Dim3, Flt64> Origin3
            => new(new[] { Flt64.Zero, Flt64.Zero, Flt64.Zero }, Dim3.Instance);

        /// <summary>创建 2D Flt64 点 / Create 2D Flt64 point.</summary>
        public static Point<Dim2, Flt64> Point2(Flt64 x, Flt64 y)
            => new(new[] { x, y }, Dim2.Instance);

        /// <summary>创建 3D Flt64 点 / Create 3D Flt64 point.</summary>
        public static Point<Dim3, Flt64> Point3(Flt64 x, Flt64 y, Flt64 z)
            => new(new[] { x, y, z }, Dim3.Instance);

        /// <summary>创建 2D 泛型点 / Create 2D generic point.</summary>
        public static Point<Dim2, V> Create(V x, V y)
            => new(new[] { x, y }, Dim2.Instance);

        /// <inheritdoc/>
        public override string ToString()
            => $"({string.Join(", ", _coords.Select(c => c.ToString()))})";
    }

    /// <summary>
    /// Point 工厂方法 / Point factory methods.
    /// </summary>
    public static class PointFactory
    {
        /// <summary>创建 2D 点 / Create 2D point.</summary>
        public static Point<Dim2, V> point2<V>(V x, V y)
            where V : struct, IFloatingNumber<V>
            => new(new[] { x, y }, Dim2.Instance);

        /// <summary>创建 2D 零点 / Create 2D zero point.</summary>
        public static Point<Dim2, V> point2<V>()
            where V : struct, IFloatingNumber<V>
        {
            var c = default(V).Constants;
            return new Point<Dim2, V>(new[] { c.Zero, c.Zero }, Dim2.Instance);
        }

        /// <summary>创建 3D 点 / Create 3D point.</summary>
        public static Point<Dim3, V> point3<V>(V x, V y, V z)
            where V : struct, IFloatingNumber<V>
            => new(new[] { x, y, z }, Dim3.Instance);

        /// <summary>创建 3D 零点 / Create 3D zero point.</summary>
        public static Point<Dim3, V> point3<V>()
            where V : struct, IFloatingNumber<V>
        {
            var c = default(V).Constants;
            return new Point<Dim3, V>(new[] { c.Zero, c.Zero, c.Zero }, Dim3.Instance);
        }

        /// <summary>创建 4D 点 / Create 4D point.</summary>
        public static Point<Dim4, V> point4<V>(V x, V y, V z, V w)
            where V : struct, IFloatingNumber<V>
            => new(new[] { x, y, z, w }, Dim4.Instance);

        /// <summary>创建 4D 零点 / Create 4D zero point.</summary>
        public static Point<Dim4, V> point4<V>()
            where V : struct, IFloatingNumber<V>
        {
            var c = default(V).Constants;
            return new Point<Dim4, V>(new[] { c.Zero, c.Zero, c.Zero, c.Zero }, Dim4.Instance);
        }
    }

    /// <summary>Point 坐标扩展方法 / Point coordinate extension methods.</summary>
    public static class PointExtensions
    {
        /// <summary>二维点 X 坐标 / 2D point X coordinate.</summary>
        public static Flt64 X(this Point<Dim2, Flt64> p) => p[0];

        /// <summary>二维点 Y 坐标 / 2D point Y coordinate.</summary>
        public static Flt64 Y(this Point<Dim2, Flt64> p) => p[1];

        /// <summary>三维点 X 坐标 / 3D point X coordinate.</summary>
        public static Flt64 X(this Point<Dim3, Flt64> p) => p[0];

        /// <summary>三维点 Y 坐标 / 3D point Y coordinate.</summary>
        public static Flt64 Y(this Point<Dim3, Flt64> p) => p[1];

        /// <summary>三维点 Z 坐标 / 3D point Z coordinate.</summary>
        public static Flt64 Z(this Point<Dim3, Flt64> p) => p[2];
    }
}
