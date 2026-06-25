#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Solver.Heuristic;
/// <summary>
/// 变异算子接口。
/// Mutation operator interface.
/// </summary>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public interface IMutation<V>
    where V : struct, IRealNumber<V> {
    /// <summary>变异算子名称 / Mutation operator name</summary>
    string Name { get; }
    /// <summary>变异概率 / Mutation probability</summary>
    double Probability { get; }
    /// <summary>执行变异 / Apply mutation</summary>
    IReadOnlyList<V> Mutate(IReadOnlyList<V> solution);
}

/// <summary>
/// 交叉算子接口。
/// Cross-over operator interface.
/// </summary>
/// <typeparam name="V">决策变量值类型 / Decision variable value type</typeparam>
public interface ICross<V>
    where V : struct, IRealNumber<V> {
    /// <summary>交叉算子名称 / Cross-over operator name</summary>
    string Name { get; }
    /// <summary>交叉概率 / Cross-over probability</summary>
    double Probability { get; }
    /// <summary>执行交叉 / Apply cross-over</summary>
    (IReadOnlyList<V> Child1, IReadOnlyList<V> Child2) Cross(IReadOnlyList<V> parent1, IReadOnlyList<V> parent2);
}

/// <summary>
/// 交叉模式接口 / Cross-over mode interface.
/// </summary>
public interface ICrossMode {
    /// <summary>模式名称 / Mode name</summary>
    string Name { get; }
}

/// <summary>
/// 单父交叉模式 / One-parent cross-over mode.
/// </summary>
public sealed record OneParentCrossMode : ICrossMode {
    /// <inheritdoc/>
    public string Name => "OneParent";
}

/// <summary>
/// 双父交叉模式 / Two-parent cross-over mode.
/// </summary>
public sealed record TwoParentCrossMode : ICrossMode {
    /// <inheritdoc/>
    public string Name => "TwoParent";
}

/// <summary>
/// 多父交叉模式 / Multi-parent cross-over mode.
/// </summary>
public sealed record MultiParentCrossMode : ICrossMode {
    /// <inheritdoc/>
    public string Name => "MultiParent";
    /// <summary>父代数量 / Parent count</summary>
    public int ParentCount { get; init; } = 3;
}

/// <summary>
/// 变异模式接口 / Mutation mode interface.
/// </summary>
public interface IMutationMode {
    /// <summary>模式名称 / Mode name</summary>
    string Name { get; }
    /// <summary>获取当前变异概率 / Get current mutation probability</summary>
    double GetProbability(int iteration, int maxIterations);
}

/// <summary>
/// 静态变异模式 / Static mutation mode.
/// </summary>
public sealed record StaticMutationMode : IMutationMode {
    /// <inheritdoc/>
    public string Name => "Static";
    /// <summary>变异概率 / Mutation probability</summary>
    public double Probability { get; init; } = 0.1;
    /// <inheritdoc/>
    public double GetProbability(int iteration, int maxIterations) => Probability;
}

/// <summary>
/// 随机变异模式 / Random mutation mode.
/// </summary>
public sealed record RandomMutationMode : IMutationMode {
    /// <inheritdoc/>
    public string Name => "Random";
    /// <summary>最小变异概率 / Minimum mutation probability</summary>
    public double MinProbability { get; init; } = 0.01;
    /// <summary>最大变异概率 / Maximum mutation probability</summary>
    public double MaxProbability { get; init; } = 0.5;
    /// <inheritdoc/>
    public double GetProbability(int iteration, int maxIterations) =>
        MinProbability + (MaxProbability - MinProbability) * Random.Shared.NextDouble();
}

/// <summary>
/// 自适应动态变异模式 / Adaptive dynamic mutation mode.
/// </summary>
public sealed class AdaptiveDynamicMutationMode : IMutationMode {
    /// <inheritdoc/>
    public string Name => "AdaptiveDynamic";
    /// <summary>初始变异概率 / Initial mutation probability</summary>
    public double InitialProbability { get; init; } = 0.1;
    /// <summary>最终变异概率 / Final mutation probability</summary>
    public double FinalProbability { get; init; } = 0.01;
    /// <inheritdoc/>
    public double GetProbability(int iteration, int maxIterations) {
        if (maxIterations <= 0) {
            return InitialProbability;
        }

        double ratio = (double)iteration / maxIterations;
        return InitialProbability + (FinalProbability - InitialProbability) * ratio;
    }
}
