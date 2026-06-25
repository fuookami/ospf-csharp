#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 向量 / Vector.
/// 表示几何空间中的向量，支持任意维度和浮点数类型。
/// Represents a vector in geometric space, supporting arbitrary dimensions and floating-point types.
/// </summary>
public class Vector<D, V> : INormedSpace<Vector<D, V>, V>
    where D : struct, IDimension
    where V : struct, IFloatingNumber<V>
{
    /// <summary>分量列表 / Components.</summary>
    public IReadOnlyList<V> Components { get; }

    /// <summary>维度信息 / Dimension information.</summary>
    public D Dim { get; }

    public Vector(IReadOnlyList<V> components, D dim)
    {
        if (components.Count != dim.Size) throw new ArgumentException("components.Count must equal dim.Size");
        Components = components;
        Dim = dim;
        _norm = new Lazy<V>(() => NormOf(components));
        _unit = new Lazy<Vector<D, V>>(() => new(components.Select(c => c.Div(Norm)).ToArray(), dim));
    }

    /// <summary>维度大小 / Dimension size.</summary>
    public int Size => Dim.Size;

    /// <summary>维度索引范围 / Dimension index range.</summary>
    public IEnumerable<int> Indices => Dim.Indices;

    /// <summary>获取指定索引的分量 / Component at index.</summary>
    public V this[int i] => Components[i];

    private readonly Lazy<V> _norm;

    /// <summary>范数（模长）/ Norm (magnitude).</summary>
    public V Norm => _norm.Value;

    private readonly Lazy<Vector<D, V>> _unit;

    /// <summary>单位向量 / Unit vector.</summary>
    public Vector<D, V> Unit => _unit.Value;

    private static V NormOf(IReadOnlyList<V> v)
    {
        var c = v[0].Constants;
        var sum = v.Select((_, i) => v[i].Sqr()).Aggregate(c.Zero, (acc, x) => acc.Plus(x));
        return sum.Sqrt();
    }

    // --- factories ---

    /// <summary>泛型二维向量 / Generic 2D vector.</summary>
    public static Vector<D, V> Create(V x, V y) => new(new[] { x, y, }, default!);

    /// <summary>泛型三维向量 / Generic 3D vector.</summary>
    public static Vector<D, V> Create(V x, V y, V z) => new(new[] { x, y, z, }, default!);

    /// <summary>二维 Flt64 向量 / 2D Flt64 vector.</summary>
    public static Vector<Dim2, Flt64> Create(Flt64 x, Flt64 y) => new(new[] { x, y, }, default);

    /// <summary>三维 Flt64 向量 / 3D Flt64 vector.</summary>
    public static Vector<Dim3, Flt64> Create(Flt64 x, Flt64 y, Flt64 z) => new(new[] { x, y, z, }, default);

    /// <summary>二维向量工厂 / 2D vector factory.</summary>
    public static Vector<Dim2, Flt64> Vector2(Flt64 x = default, Flt64 y = default) => Create(x, y);

    /// <summary>三维向量工厂 / 3D vector factory.</summary>
    public static Vector<Dim3, Flt64> Vector3(Flt64 x = default, Flt64 y = default, Flt64 z = default) => Create(x, y, z);

    // --- inner-product-space ops ---

    /// <inheritdoc/>
    public Vector<D, V> Plus(Vector<D, V> rhs) =>
        new(Indices.Select(i => this[i].Plus(rhs[i])).ToArray(), Dim);

    /// <inheritdoc/>
    public Vector<D, V> Minus(Vector<D, V> rhs) =>
        new(Indices.Select(i => this[i].Minus(rhs[i])).ToArray(), Dim);

    public static Vector<D, V> operator +(Vector<D, V> lhs, Vector<D, V> rhs) => lhs.Plus(rhs);

    public static Vector<D, V> operator -(Vector<D, V> lhs, Vector<D, V> rhs) => lhs.Minus(rhs);

    /// <inheritdoc/>
    public Vector<D, V> Scale(V rhs) => new(Indices.Select(i => this[i].Times(rhs)).ToArray(), Dim);

    public static Vector<D, V> operator *(Vector<D, V> lhs, V rhs) => lhs.Scale(rhs);

    public static Vector<D, V> operator *(V lhs, Vector<D, V> rhs) => rhs.Scale(lhs);

    /// <inheritdoc/>
    public V Dot(Vector<D, V> rhs)
    {
        var c = this[0].Constants;
        return Indices.Select(i => this[i].Times(rhs[i])).Aggregate(c.Zero, (acc, x) => acc.Plus(x));
    }

    /// <summary>向量点积（乘法形式）/ Vector dot product (multiplication form).</summary>
    public static V operator *(Vector<D, V> lhs, Vector<D, V> rhs) => lhs.Dot(rhs);

    /// <summary>向量加点 / Vector plus point.</summary>
    public static Point<D, V> operator +(Vector<D, V> lhs, Point<D, V> rhs) =>
        new(lhs.Indices.Select(i => lhs[i].Plus(rhs[i])).ToArray(), lhs.Dim);

    /// <summary>夹角（弧度），零向量返回 null / Angle in radians, null for zero vector.</summary>
    public V? Angle(Vector<D, V> rhs)
    {
        var denom = Norm.Times(rhs.Norm);
        if (denom.Eq(default)) return null;
        return Dot(rhs).Div(denom).Acos();
    }

    /// <summary>是否正交 / Whether orthogonal.</summary>
    public bool IsOrthogonal(Vector<D, V> rhs, V epsilon) => Dot(rhs).Abs().Ls(epsilon);

    /// <summary>余弦相似度 / Cosine similarity.</summary>
    public V? CosineSimilarity(Vector<D, V> rhs)
    {
        var denom = Norm.Times(rhs.Norm);
        return denom.Eq(default) ? default(V?) : Dot(rhs).Div(denom);
    }

    /// <inheritdoc/>
    public Vector<D, V>? Project(Vector<D, V> rhs) =>
        rhs.Norm.Eq(default) ? null : rhs.Scale(Dot(rhs).Div(rhs.Dot(rhs)));

    /// <inheritdoc/>
    public Vector<D, V>? OrthogonalComponent(Vector<D, V> rhs) => Project(rhs) is { } p ? this - p : null;

    /// <inheritdoc/>
    public V NormSquared() => Norm.Times(Norm);

    /// <inheritdoc/>
    public Vector<D, V>? Normalize() => Norm.Eq(default) ? null : Unit;

    /// <summary>在另一向量上的投影 / Projection onto another vector.</summary>
    public Vector<D, V>? ProjectionOn(Vector<D, V> rhs) => Project(rhs);

    /// <summary>正交分量 / Orthogonal component relative to another vector.</summary>
    public Vector<D, V>? OrthogonalComponentTo(Vector<D, V> rhs) => OrthogonalComponent(rhs);

    /// <inheritdoc/>
    public override string ToString() => "[" + string.Join(",", Components) + "]";

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is Vector<D, V> other &&
        Dim.Equals(other.Dim) &&
        Indices.All(i => this[i].Eq(other[i]));

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Dim, Components.Count);
}

/// <summary>向量扩展方法 / Vector extension methods.</summary>
public static class VectorExtensions
{
    /// <summary>二维向量 X 分量 / 2D vector X component.</summary>
    public static Flt64 X(this Vector<Dim2, Flt64> v) => v[0];

    /// <summary>二维向量 Y 分量 / 2D vector Y component.</summary>
    public static Flt64 Y(this Vector<Dim2, Flt64> v) => v[1];

    /// <summary>三维向量 X 分量 / 3D vector X component.</summary>
    public static Flt64 X(this Vector<Dim3, Flt64> v) => v[0];

    /// <summary>三维向量 Y 分量 / 3D vector Y component.</summary>
    public static Flt64 Y(this Vector<Dim3, Flt64> v) => v[1];

    /// <summary>三维向量 Z 分量 / 3D vector Z component.</summary>
    public static Flt64 Z(this Vector<Dim3, Flt64> v) => v[2];

    /// <summary>二维叉积（标量）/ 2D cross product (scalar).</summary>
    public static Flt64 Cross(this Vector<Dim2, Flt64> lhs, Vector<Dim2, Flt64> rhs) =>
        lhs.X().Times(rhs.Y()).Minus(lhs.Y().Times(rhs.X()));

    /// <summary>三维叉积（向量）/ 3D cross product (vector).</summary>
    public static Vector<Dim3, Flt64> Cross(this Vector<Dim3, Flt64> lhs, Vector<Dim3, Flt64> rhs) =>
        Vector<Dim3, Flt64>.Vector3(
            lhs.Y().Times(rhs.Z()).Minus(lhs.Z().Times(rhs.Y())),
            lhs.Z().Times(rhs.X()).Minus(lhs.X().Times(rhs.Z())),
            lhs.X().Times(rhs.Y()).Minus(lhs.Y().Times(rhs.X())));
}
