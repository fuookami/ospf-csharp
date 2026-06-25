#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 维度接口 / Dimension interface.
/// 定义几何空间的维度大小，用于类型安全的几何计算。
/// Defines the dimension size of geometric space for type-safe geometric calculations.
/// </summary>
public interface IDimension
{
    /// <summary>维度大小 / Dimension size.</summary>
    int Size { get; }

    /// <summary>维度索引范围 / Dimension index range.</summary>
    IEnumerable<int> Indices => Enumerable.Range(0, Size);
}

/// <summary>一维空间（直线）/ 1D space (line), size 1.</summary>
public readonly struct Dim1 : IDimension
{
    /// <summary>单例实例 / Singleton instance.</summary>
    public static readonly Dim1 Instance = default;
    /// <inheritdoc/>
    public int Size => 1;
}

/// <summary>二维空间（平面）/ 2D space (plane), size 2.</summary>
public readonly struct Dim2 : IDimension
{
    /// <summary>单例实例 / Singleton instance.</summary>
    public static readonly Dim2 Instance = default;
    /// <inheritdoc/>
    public int Size => 2;
}

/// <summary>三维空间（立体）/ 3D space (solid), size 3.</summary>
public readonly struct Dim3 : IDimension
{
    /// <summary>单例实例 / Singleton instance.</summary>
    public static readonly Dim3 Instance = default;
    /// <inheritdoc/>
    public int Size => 3;
}

/// <summary>四维空间 / 4D space, size 4 (hyperchaos / spacetime).</summary>
public readonly struct Dim4 : IDimension
{
    /// <summary>单例实例 / Singleton instance.</summary>
    public static readonly Dim4 Instance = default;
    /// <inheritdoc/>
    public int Size => 4;
}
