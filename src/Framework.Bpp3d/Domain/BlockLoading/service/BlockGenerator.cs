#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Service
{
    /// <summary>
    /// 块生成器接口 / Block generator interface.
    /// </summary>
    public interface IBlockGenerator
    {
        /// <summary>生成器名称 / Generator name.</summary>
        string Name { get; }
    }

    /// <summary>
    /// 块装载算法接口 / Block loading algorithm interface.
    /// </summary>
    public interface IBlockLoadingAlgorithm
    {
        /// <summary>算法名称 / Algorithm name.</summary>
        string Name { get; }
    }

    /// <summary>
    /// 简单块生成器 / Simple block generator.
    /// </summary>
    public sealed class SimpleBlockGenerator : IBlockGenerator
    {
        public string Name => nameof(SimpleBlockGenerator);
    }

    /// <summary>
    /// 复杂块生成器 / Complex block generator.
    /// </summary>
    public sealed class ComplexBlockGenerator : IBlockGenerator
    {
        public string Name => nameof(ComplexBlockGenerator);
    }

    /// <summary>
    /// 深度优先搜索算法 / Depth-first search algorithm.
    /// </summary>
    public sealed class DepthFirstSearchAlgorithm : IBlockLoadingAlgorithm
    {
        public string Name => nameof(DepthFirstSearchAlgorithm);
    }

    /// <summary>
    /// 多层启发式搜索算法 / Multi-layer heuristic search algorithm.
    /// </summary>
    public sealed class MultiLayerHeuristicSearchAlgorithm : IBlockLoadingAlgorithm
    {
        public string Name => nameof(MultiLayerHeuristicSearchAlgorithm);
    }
}
