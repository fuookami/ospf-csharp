#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 货物高度组合器 / Item height combinator.
/// 计算多货物堆叠的高度组合。
/// Computes height combinations for multi-item stacking.
/// </summary>
public static class ItemHeightCombinator {
    /// <summary>
    /// 两数之和：查找和接近目标高度的高度对。
    /// Two-sum: finds pairs of heights that sum to within offset of target height.
    /// </summary>
    public static IReadOnlyList<(double A, double B)> TwoSum(
        double targetHeight,
        IReadOnlyList<double> heights,
        double offset) {
        var result = new List<(double A, double B)>();
        for (int i = 0; i < heights.Count; i++) {
            for (int j = i; j < heights.Count; j++) {
                double sum = heights[i] + heights[j];
                if (global::System.Math.Abs(sum - targetHeight) <= offset) {
                    result.Add((heights[i], heights[j]));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 三数之和：查找和接近目标高度的高度三元组。
    /// Three-sum: finds triples of heights that sum to within offset of target height.
    /// </summary>
    public static IReadOnlyList<(double A, double B, double C)> ThreeSum(
        double targetHeight,
        IReadOnlyList<double> heights,
        double offset) {
        var result = new List<(double A, double B, double C)>();
        for (int i = 0; i < heights.Count; i++) {
            for (int j = i; j < heights.Count; j++) {
                for (int k = j; k < heights.Count; k++) {
                    double sum = heights[i] + heights[j] + heights[k];
                    if (global::System.Math.Abs(sum - targetHeight) <= offset) {
                        result.Add((heights[i], heights[j], heights[k]));
                    }
                }
            }
        }
        return result;
    }
}
