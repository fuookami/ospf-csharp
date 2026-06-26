#nullable enable

using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 变量转储数据，包含变量的边界、名称和初始解信息。
/// Variable dumping data, containing bounds, names, and initial solution information for variables.
/// </summary>
/// <param name="LowerBounds">下界数组 / Lower bounds array</param>
/// <param name="UpperBounds">上界数组 / Upper bounds array</param>
/// <param name="Names">变量名称数组 / Variable names array</param>
/// <param name="InitialResults">初始解列表（索引-值对）/ Initial results list (index-value pairs)</param>
public sealed record VariableDumpingData(
    double[] LowerBounds,
    double[] UpperBounds,
    string[] Names,
    IReadOnlyList<(int Index, double Value)> InitialResults);

/// <summary>
/// 建模准备工具，提供变量转储数据准备和约束分段大小计算。
/// Modeling preparation utility, providing variable dumping data preparation and constraint segment size computation.
/// </summary>
public static class ModelingPreparation {
    /// <summary>
    /// 从变量列表准备转储数据，提取边界、名称和初始解。
    /// Prepare dumping data from a variable list, extracting bounds, names, and initial results.
    /// </summary>
    /// <param name="variables">变量项列表 / Variable item list</param>
    /// <param name="scopeName">作用域名称 / Scope name</param>
    /// <returns>变量转储数据 / Variable dumping data</returns>
    public static VariableDumpingData PrepareVariableDumpingData(
        IReadOnlyList<IVariableItem> variables,
        string scopeName) {
        int count = variables.Count;
        double[] lowerBounds = new double[count];
        double[] upperBounds = new double[count];
        string[] names = new string[count];
        var initialResults = new List<(int Index, double Value)>();

        for (int i = 0; i < count; i++) {
            IVariableItem variable = variables[i];

            lowerBounds[i] = variable.LowerBound?.Value?.ToFlt64().ToDouble() ?? double.NegativeInfinity;
            upperBounds[i] = variable.UpperBound?.Value?.ToFlt64().ToDouble() ?? double.PositiveInfinity;
            names[i] = variable.Name;
        }

        return new VariableDumpingData(lowerBounds, upperBounds, names, initialResults);
    }

    /// <summary>
    /// 计算约束分段大小，用于并行处理时的任务划分。
    /// Compute constraint segment size for task partitioning during parallel processing.
    /// </summary>
    /// <param name="constraintSize">约束总数 / Total constraint count</param>
    /// <param name="availableProcessors">可用处理器数（可选，默认为当前处理器数）/ Available processor count (optional, defaults to current processor count)</param>
    /// <returns>每段约束数 / Constraints per segment</returns>
    public static int ComputeConstraintSegmentSize(int constraintSize, int? availableProcessors = null) {
        if (constraintSize <= 0) {
            return 0;
        }

        int processors = availableProcessors ?? Environment.ProcessorCount;
        if (processors <= 0) {
            processors = 1;
        }

        int segmentSize = (int)System.Math.Ceiling((double)constraintSize / processors);
        return System.Math.Max(segmentSize, 1);
    }
}
