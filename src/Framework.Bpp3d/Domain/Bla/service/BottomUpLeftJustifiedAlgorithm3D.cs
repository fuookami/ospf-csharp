#nullable enable

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Bla.Service;

/// <summary>
/// 三维自下向左调整算法 / 3D Bottom-Up Left-Justified Algorithm.
/// 三维 BLA 实现，在三维空间中将货物从底部开始、向左靠拢放置。
/// 3D BLA implementation, places items starting from the bottom and left-justifying in 3D space.
/// </summary>
public sealed class BottomUpLeftJustifiedAlgorithm3D : IBLAAlgorithm {
    /// <summary>算法名称 / Algorithm name.</summary>
    public string Name => nameof(BottomUpLeftJustifiedAlgorithm3D);
}
