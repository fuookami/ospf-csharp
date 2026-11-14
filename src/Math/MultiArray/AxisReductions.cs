#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.MultiArray;

/// <summary>
/// 多维数组的按轴归约 / Axis reductions for multi-dimensional arrays.
/// </summary>
/// <remarks>
/// 移植基线 `ospf-kotlin-math/.../multiarray/FastSum.kt`（12922 B）里的归约族：
/// `sumAll`、`sumAxis(Safe)`、`sumAxes(Safe)`、`cumsumAxis(Safe)`。
///
/// **两条路径**：形状是 `Shape1..Shape4` / `DynShape` 且步长与存储顺序一致时走**进位直通**（不逐元素求坐标）；
/// 其余形状走**通用路径**（逐元素取向量、再去掉被归约的轴算目标下标）。两条路径必须给出相同结果——
/// 这正是配套测试要钉住的东西。
///
/// Ports the reduction family from the baseline `FastSum.kt`: `sumAll`, `sumAxis(Safe)`, `sumAxes(Safe)`, `cumsumAxis(Safe)`.
/// **Two paths**: when the shape is `Shape1..Shape4` / `DynShape` and the strides match the storage order it takes the **carry fast path** (no
/// per-element coordinate computation); every other shape takes the **general path** (fetch the vector, drop the reduced axes, compute the target
/// index). Both paths must agree — that is exactly what the companion tests pin down.
/// </remarks>
public static class AxisReductions {
    /// <summary>对全部元素求和 / Sum every element.</summary>
    /// <param name="source">源数组 / Source array.</param>
    /// <param name="zero">累加初值 / Accumulation seed.</param>
    /// <returns>元素之和 / The sum.</returns>
    public static T SumAll<T, S>(this AbstractMultiArray<T, S> source, T zero)
        where T : struct, IRing<T>
        where S : IShape {
        ArgumentNullException.ThrowIfNull(source);
        T sum = zero;
        for (int index = 0; index < source.Count; ++index) {
            sum = sum.Plus(source[index]);
        }
        return sum;
    }

    /// <summary>沿单个轴求和，移除该轴 / Sum along one axis, removing it.</summary>
    /// <param name="source">源数组 / Source array.</param>
    /// <param name="axis">归约轴 / The reduced axis.</param>
    /// <param name="zero">累加初值 / Accumulation seed.</param>
    /// <returns>结果数组或非法参数 / The result, or an illegal-argument error.</returns>
    public static Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> SumAxisSafe<T, S>(
        this AbstractMultiArray<T, S> source,
        int axis,
        T zero)
        where T : struct, IRing<T>
        where S : IShape {
        ArgumentNullException.ThrowIfNull(source);
        return source.SumAxesSafe(new[] { axis }, zero);
    }

    /// <summary>沿单个轴求和 / Sum along one axis.</summary>
    /// <param name="source">源数组 / Source array.</param>
    /// <param name="axis">归约轴 / The reduced axis.</param>
    /// <param name="zero">累加初值 / Accumulation seed.</param>
    /// <returns>结果数组或非法参数 / The result, or an illegal-argument error.</returns>
    public static Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> SumAxis<T, S>(
        this AbstractMultiArray<T, S> source,
        int axis,
        T zero)
        where T : struct, IRing<T>
        where S : IShape =>
        source.SumAxisSafe(axis, zero);

    /// <summary>沿多个轴求和，移除这些轴 / Sum along several axes, removing them.</summary>
    /// <param name="source">源数组 / Source array.</param>
    /// <param name="axes">归约轴集合 / The reduced axes.</param>
    /// <param name="zero">累加初值 / Accumulation seed.</param>
    /// <returns>结果数组或非法参数 / The result, or an illegal-argument error.</returns>
    public static Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> SumAxesSafe<T, S>(
        this AbstractMultiArray<T, S> source,
        IReadOnlyList<int> axes,
        T zero)
        where T : struct, IRing<T>
        where S : IShape {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(axes);

        int dimension = source.Shape.Dimension;
        int[] dimensions = new int[dimension];
        for (int axis = 0; axis < dimension; ++axis) {
            dimensions[axis] = source.Shape[axis];
        }

        if (axes.Count == 0) {
            // **零个轴不是"归约成标量"**：基线语义是"什么都不归约，只换成 DynShape 的副本"。
            // **Zero axes is not "reduce to a scalar"**: the baseline semantics are "reduce nothing, just return a DynShape copy".
            MultiArray<T, DynShape> copy = MultiArray<T, DynShape>.Factory.NewBy(
                DynShape.Invoke(dimensions),
                (index, _) => source[index]);
            return Results.Ok(copy);
        }

        HashSet<int> removed = new(axes);
        foreach (int axis in axes) {
            if (axis < 0 || axis >= dimension) {
                return Results.Failed<MultiArray<T, DynShape>>(
                    new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        $"轴 {axis} 越界（最大 {dimension - 1}）/ Axis {axis} out of bounds (max {dimension - 1})"));
            }
        }

        int[] retained = Enumerable.Range(0, dimension)
            .Where(axis => !removed.Contains(axis))
            .Select(axis => dimensions[axis])
            .ToArray();
        MutableMultiArray<T, DynShape> result = MutableMultiArray<T, DynShape>.Factory.NewWith(
            DynShape.Invoke(retained), zero);

        if (HasCanonicalReductionIndexing(source.Shape)) {
            SumCanonicalAxes(source, removed, dimensions, result);
            return Results.Ok(result.ToImmutable());
        }

        for (int index = 0; index < source.Count; ++index) {
            int[]? vector = source.Shape.Vector(index).Value;
            if (vector is null) {
                return Results.Failed<MultiArray<T, DynShape>>(
                    new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        $"线性下标 {index} 无法换算为坐标 / Linear index {index} has no coordinates"));
            }
            int[] target = vector
                .Where((_, axis) => !removed.Contains(axis))
                .ToArray();
            int? targetIndex = result.Shape.Index(target).Value;
            if (targetIndex is null) {
                return Results.Failed<MultiArray<T, DynShape>>(
                    new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        "目标坐标越界 / target coordinates are out of bounds"));
            }
            result[targetIndex.Value] = result[targetIndex.Value].Plus(source[index]);
        }
        return Results.Ok(result.ToImmutable());
    }

    /// <summary>沿多个轴求和 / Sum along several axes.</summary>
    /// <param name="source">源数组 / Source array.</param>
    /// <param name="axes">归约轴集合 / The reduced axes.</param>
    /// <param name="zero">累加初值 / Accumulation seed.</param>
    /// <returns>结果数组或非法参数 / The result, or an illegal-argument error.</returns>
    public static Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> SumAxes<T, S>(
        this AbstractMultiArray<T, S> source,
        IReadOnlyList<int> axes,
        T zero)
        where T : struct, IRing<T>
        where S : IShape =>
        source.SumAxesSafe(axes, zero);

    /// <summary>沿指定轴做前缀和，形状不变 / Prefix sum along one axis, keeping the shape.</summary>
    /// <param name="source">源数组 / Source array.</param>
    /// <param name="axis">前缀轴 / The prefix axis.</param>
    /// <param name="zero">累加初值 / Accumulation seed.</param>
    /// <returns>结果数组或非法参数 / The result, or an illegal-argument error.</returns>
    public static Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> CumsumAxisSafe<T, S>(
        this AbstractMultiArray<T, S> source,
        int axis,
        T zero)
        where T : struct, IRing<T>
        where S : IShape {
        ArgumentNullException.ThrowIfNull(source);

        int dimension = source.Shape.Dimension;
        if (axis < 0 || axis >= dimension) {
            return Results.Failed<MultiArray<T, DynShape>>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    $"轴 {axis} 越界（最大 {dimension - 1}）/ Axis {axis} out of bounds (max {dimension - 1})"));
        }

        int[] dimensions = Enumerable.Range(0, dimension)
            .Select(index => source.Shape[index])
            .ToArray();
        MutableMultiArray<T, DynShape> result = MutableMultiArray<T, DynShape>.Factory.NewWith(
            DynShape.Invoke(dimensions), zero);

        if (HasCanonicalReductionIndexing(source.Shape)) {
            int stride = source.Shape.Offsets[axis];
            for (int index = 0; index < source.Count; ++index) {
                result[index] = (index / stride) % dimensions[axis] == 0
                    ? source[index]
                    : result[index - stride].Plus(source[index]);
            }
            return Results.Ok(result.ToImmutable());
        }

        for (int index = 0; index < source.Count; ++index) {
            int[]? vector = source.Shape.Vector(index).Value;
            if (vector is null) {
                return Results.Failed<MultiArray<T, DynShape>>(
                    new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        $"线性下标 {index} 无法换算为坐标 / Linear index {index} has no coordinates"));
            }
            if (vector[axis] == 0) {
                result[index] = source[index];
            }
            else {
                int[] previous = (int[])vector.Clone();
                --previous[axis];
                int? previousIndex = source.Shape.Index(previous).Value;
                if (previousIndex is null) {
                    return Results.Failed<MultiArray<T, DynShape>>(
                        new Err<ErrorCode>(
                            ErrorCode.IllegalArgument,
                            "前缀前驱坐标越界 / the prefix predecessor is out of bounds"));
                }
                result[index] = result[previousIndex.Value].Plus(source[index]);
            }
        }
        return Results.Ok(result.ToImmutable());
    }

    /// <summary>沿指定轴做前缀和 / Prefix sum along one axis.</summary>
    /// <param name="source">源数组 / Source array.</param>
    /// <param name="axis">前缀轴 / The prefix axis.</param>
    /// <param name="zero">累加初值 / Accumulation seed.</param>
    /// <returns>结果数组或非法参数 / The result, or an illegal-argument error.</returns>
    public static Result<MultiArray<T, DynShape>, ErrorCode, Error<ErrorCode>> CumsumAxis<T, S>(
        this AbstractMultiArray<T, S> source,
        int axis,
        T zero)
        where T : struct, IRing<T>
        where S : IShape =>
        source.CumsumAxisSafe(axis, zero);

    /// <summary>
    /// 形状是否满足"步长与存储顺序一致" / Whether the strides agree with the storage order.
    /// </summary>
    /// <remarks>
    /// **只认五种具体形状**：自定义 `IShape` 的步长可能不是连乘积（例如切片视图），
    /// 用进位直通会把元素写到错误的下标——**而结果数组的形状仍然是对的**。
    ///
    /// **Only the five concrete shapes qualify**: a custom `IShape` may have strides that are not cumulative products (a sliced view, say), and the carry
    /// fast path would then write elements to the wrong index — **while the result shape still looks right**.
    /// </remarks>
    private static bool HasCanonicalReductionIndexing(IShape shape) {
        if (shape is not Shape1 && shape is not Shape2 && shape is not Shape3
            && shape is not Shape4 && shape is not DynShape) {
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

    private static void SumCanonicalAxes<T, S>(
        AbstractMultiArray<T, S> source,
        HashSet<int> removedAxes,
        int[] dimensions,
        MutableMultiArray<T, DynShape> result)
        where T : struct, IRing<T>
        where S : IShape {
        int[] coordinates = new int[dimensions.Length];
        int retainedAxis = 0;
        int[] outputStrides = new int[dimensions.Length];
        for (int axis = 0; axis < dimensions.Length; ++axis) {
            outputStrides[axis] = removedAxes.Contains(axis)
                ? 0
                : result.Shape.Offsets[retainedAxis++];
        }

        // 保留源侧线性遍历与每组左折叠，只用进位更新目标地址。
        // Keep the source-linear traversal and each group's left fold; update the target address purely through carries.
        IReadOnlyList<int> carryAxes = source.Shape.StorageOrder == StorageOrder.RowMajor
            ? Enumerable.Range(0, dimensions.Length).Reverse().ToList()
            : Enumerable.Range(0, dimensions.Length).ToList();
        int outputIndex = 0;
        for (int index = 0; index < source.Count; ++index) {
            result[outputIndex] = result[outputIndex].Plus(source[index]);
            foreach (int axis in carryAxes) {
                ++coordinates[axis];
                outputIndex += outputStrides[axis];
                if (coordinates[axis] < dimensions[axis]) {
                    break;
                }
                coordinates[axis] = 0;
                outputIndex -= dimensions[axis] * outputStrides[axis];
            }
        }
    }
}
