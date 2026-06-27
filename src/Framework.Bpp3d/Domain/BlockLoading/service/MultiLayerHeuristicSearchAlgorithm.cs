#nullable enable

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Service;

/// <summary>
/// 多层启发式搜索算法 / Multi-layer heuristic search algorithm.
/// 使用多层启发式策略进行块装载 / Uses multi-layer heuristic strategy for block loading.
/// </summary>
public sealed class MultiLayerHeuristicSearchAlgorithm : IBlockLoadingAlgorithm {
    /// <summary>算法名称 / Algorithm name.</summary>
    public string Name => nameof(MultiLayerHeuristicSearchAlgorithm);
}
