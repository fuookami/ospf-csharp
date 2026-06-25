#nullable enable

using System;

namespace Fuookami.Ospf.Core.Solver.Heuristic;
/// <summary>
/// 启发式策略抽象接口。
/// Heuristic policy abstraction interface.
/// </summary>
public interface IAbstractHeuristicPolicy {
    /// <summary>策略名称 / Policy name</summary>
    string Name { get; }
    /// <summary>最大迭代次数 / Maximum iterations</summary>
    int MaxIterations { get; }
    /// <summary>种群大小 / Population size</summary>
    int PopulationSize { get; }
    /// <summary>无改进最大迭代次数 / Maximum iterations without improvement</summary>
    int MaxIterationsWithoutImprovement { get; }
}

/// <summary>
/// 启发式策略基类 / Heuristic policy base class.
/// </summary>
public abstract class HeuristicPolicy : IAbstractHeuristicPolicy {
    /// <inheritdoc/>
    public abstract string Name { get; }
    /// <inheritdoc/>
    public virtual int MaxIterations { get; init; } = 1000;
    /// <inheritdoc/>
    public virtual int PopulationSize { get; init; } = 100;
    /// <inheritdoc/>
    public virtual int MaxIterationsWithoutImprovement { get; init; } = 100;

    /// <summary>
    /// 判断搜索是否结束 / Check if the search should terminate.
    /// </summary>
    /// <param name="iteration">当前迭代状态 / current iteration state</param>
    /// <returns>是否结束 / whether finished</returns>
    public virtual bool Finished(Iteration iteration) =>
        iteration.Current >= MaxIterations
        || iteration.NotBetterIteration >= MaxIterationsWithoutImprovement;
}

/// <summary>
/// 启发式迭代计数器，跟踪迭代次数和无改进迭代。
/// Heuristic iteration counter, tracking iteration count and iterations without improvement.
/// </summary>
public class Iteration {
    /// <summary>当前迭代 / Current iteration</summary>
    public int Current { get; private set; }
    /// <summary>无改进迭代次数 / Iterations without improvement</summary>
    public int NotBetterIteration { get; private set; }
    /// <summary>已用时间 / Elapsed time</summary>
    public TimeSpan Time { get; private set; }

    private DateTime _startTime;

    /// <summary>
    /// 初始化迭代计数器 / Initialize iteration counter
    /// </summary>
    public Iteration() {
        Current = 0;
        NotBetterIteration = 0;
        Time = TimeSpan.Zero;
        _startTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 推进到下一次迭代 / Advance to next iteration
    /// </summary>
    /// <param name="better">是否有所改进 / Whether improved</param>
    public void Next(bool better) {
        Current++;
        Time = DateTime.UtcNow - _startTime;
        if (better) {
            NotBetterIteration = 0;
        }
        else {
            NotBetterIteration++;
        }
    }

    /// <summary>重置 / Reset</summary>
    public void Reset() {
        Current = 0;
        NotBetterIteration = 0;
        Time = TimeSpan.Zero;
        _startTime = DateTime.UtcNow;
    }
}
