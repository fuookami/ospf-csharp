#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Solver.Heuristic;
/// <summary>
/// 选择策略接口。
/// Selection strategy interface.
/// </summary>
public interface ISelection {
    /// <summary>选择策略名称 / Selection strategy name</summary>
    string Name { get; }
}

/// <summary>
/// 局部选择基类 / Locality-aware selection base class.
/// </summary>
public abstract class LocalSelection : ISelection {
    /// <inheritdoc/>
    public abstract string Name { get; }
    /// <summary>邻域大小 / Neighborhood size</summary>
    public int NeighborhoodSize { get; init; } = 5;
}

/// <summary>
/// 轮盘赌选择 / Roulette wheel selection.
/// </summary>
public sealed record RouletteSelection : ISelection {
    /// <inheritdoc/>
    public string Name => "Roulette";
}

/// <summary>
/// 锦标赛选择 / Tournament selection.
/// </summary>
public sealed record TournamentSelection : ISelection {
    /// <inheritdoc/>
    public string Name => "Tournament";
    /// <summary>锦标赛大小 / Tournament size</summary>
    public int TournamentSize { get; init; } = 3;
}

/// <summary>
/// 随机均匀选择 / Stochastic universal selection.
/// </summary>
public sealed record StochasticUniversalSelection : ISelection {
    /// <inheritdoc/>
    public string Name => "StochasticUniversal";
}

/// <summary>
/// 截断选择 / Truncation selection.
/// </summary>
public sealed record TruncationSelection : ISelection {
    /// <inheritdoc/>
    public string Name => "Truncation";
    /// <summary>截断比例 / Truncation ratio</summary>
    public double TruncationRatio { get; init; } = 0.5;
}

/// <summary>
/// 玻尔兹曼选择 / Boltzmann selection.
/// </summary>
public sealed record BoltzmannSelection : ISelection {
    /// <inheritdoc/>
    public string Name => "Boltzmann";
    /// <summary>温度 / Temperature</summary>
    public double Temperature { get; init; } = 1.0;
}

/// <summary>
/// 环形局部选择 / Ring local selection.
/// </summary>
public sealed class RingLocalSelection : LocalSelection {
    /// <inheritdoc/>
    public override string Name => "RingLocal";
}
