#nullable enable

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Service;

/// <summary>
/// 块装载算法接口 / Block loading algorithm interface.
/// </summary>
public interface IBlockLoadingAlgorithm {
    /// <summary>算法名称 / Algorithm name.</summary>
    string Name { get; }
}
