#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 目标值归一化接口，将目标值列表归一化为权重。
/// Objective normalization interface, normalizing objective value lists into weights.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public interface IObjectiveNormalization<ObjValue, V> {
    /// <summary>
    /// 将目标值列表归一化为权重。
    /// Normalize objective value list into weights.
    /// </summary>
    /// <param name="compareObjective">目标值比较函数 / Objective value comparison function</param>
    /// <param name="objectives">目标值列表 / Objective value list</param>
    /// <returns>归一化后的权重列表 / Normalized weight list</returns>
    IReadOnlyList<double> Normalize(
        Func<ObjValue, ObjValue, int> compareObjective,
        IReadOnlyList<ObjValue> objectives);
}

/// <summary>
/// 最小-最大归一化，将目标值映射到 [0, 1] 区间。
/// Min-max normalization, mapping objective values to [0, 1] range.
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class MinMaxNormalization<ObjValue, V> : IObjectiveNormalization<ObjValue, V> {
    /// <inheritdoc/>
    public IReadOnlyList<double> Normalize(
        Func<ObjValue, ObjValue, int> compareObjective,
        IReadOnlyList<ObjValue> objectives) {
        if (objectives.Count == 0) {
            return Array.Empty<double>();
        }

        if (objectives.Count == 1) {
            return new[] { 1.0 };
        }

        // For ObjValue = double, cast directly; otherwise use rank-based approach.
        // 对于 ObjValue = double，直接转换；否则使用基于排名的方法。
        var doubles = objectives.Select(o => Convert.ToDouble(o)).ToList();
        double minVal = doubles.Min();
        double maxVal = doubles.Max();
        double range = maxVal - minVal;

        if (range < 1e-12) {
            // All values are essentially equal; return uniform weights.
            // 所有值基本相等，返回均匀权重。
            return objectives.Select(_ => 1.0 / objectives.Count).ToList();
        }

        // Determine if lower is better (minimization) by comparing first two distinct values.
        // 通过比较前两个不同值判断是否越小越好（最小化）。
        bool lowerIsBetter = compareObjective(objectives[0], objectives[1]) < 0;

        if (lowerIsBetter) {
            // For minimization: best (lowest) maps to 1.0, worst (highest) maps to 0.0.
            // 最小化：最佳（最小）映射到1.0，最差（最大）映射到0.0。
            return doubles.Select(v => 1.0 - (v - minVal) / range).ToList();
        }
        else {
            // For maximization: best (highest) maps to 1.0, worst (lowest) maps to 0.0.
            // 最大化：最佳（最大）映射到1.0，最差（最小）映射到0.0。
            return doubles.Select(v => (v - minVal) / range).ToList();
        }
    }
}

/// <summary>
/// 求和归一化，将目标值转换为占比权重（先做最小-最大归一化，再求和归一化）。
/// Sum normalization, converting objective values to proportionate weights (min-max then sum).
/// </summary>
/// <typeparam name="ObjValue">目标值类型 / Objective value type</typeparam>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public sealed class SumNormalization<ObjValue, V> : IObjectiveNormalization<ObjValue, V> {
    /// <inheritdoc/>
    public IReadOnlyList<double> Normalize(
        Func<ObjValue, ObjValue, int> compareObjective,
        IReadOnlyList<ObjValue> objectives) {
        if (objectives.Count == 0) {
            return Array.Empty<double>();
        }

        if (objectives.Count == 1) {
            return new[] { 1.0 };
        }

        var doubles = objectives.Select(o => Convert.ToDouble(o)).ToList();
        double minVal = doubles.Min();
        double maxVal = doubles.Max();
        double range = maxVal - minVal;

        if (range < 1e-12) {
            // All values are essentially equal; return uniform weights.
            // 所有值基本相等，返回均匀权重。
            return objectives.Select(_ => 1.0 / objectives.Count).ToList();
        }

        // Min-max normalize first.
        // 先做最小-最大归一化。
        var minMaxNorm = doubles.Select(v => (v - minVal) / range).ToList();
        double sum = minMaxNorm.Sum();

        // Determine if lower is better (minimization).
        // 判断是否越小越好（最小化）。
        bool lowerIsBetter = compareObjective(objectives[0], objectives[1]) < 0;

        if (sum < 1e-12) {
            return objectives.Select(_ => 1.0 / objectives.Count).ToList();
        }

        if (lowerIsBetter) {
            // For minimization: invert so lower original values get higher weights.
            // 最小化：反转使原始较小值获得较高权重。
            return minMaxNorm.Select(v => (1.0 - v / sum)).ToList();
        }
        else {
            // For maximization: higher original values get higher weights.
            // 最大化：原始较大值获得较高权重。
            return minMaxNorm.Select(v => v / sum).ToList();
        }
    }
}
