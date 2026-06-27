#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Service;

/// <summary>
/// 深度优先搜索算法 / Depth-first search algorithm.
/// 使用 DFS 策略将块装入箱子，支持并行分支。
/// Uses DFS strategy to pack blocks into bins, with parallel branching support.
/// </summary>
public sealed class DepthFirstSearchAlgorithm : IBlockLoadingAlgorithm {
    /// <summary>算法名称 / Algorithm name.</summary>
    public string Name => nameof(DepthFirstSearchAlgorithm);

    /// <summary>分支数上限 / Branch count upper bound.</summary>
    public ulong Branch { get; init; } = ulong.MaxValue;

    /// <summary>
    /// 空间方向顺序 / Space direction order.
    /// 控制 DFS 中空间扩展的优先方向 / Controls priority direction for space expansion in DFS.
    /// </summary>
    public IReadOnlyList<string> SpaceDirectionOrder { get; init; } = new[] { "Front", "Bottom", "Side" };
}
