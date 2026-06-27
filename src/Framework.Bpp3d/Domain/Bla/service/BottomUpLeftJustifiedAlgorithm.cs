#nullable enable

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Bla.Service;

/// <summary>
/// 自下向左调整算法 / Bottom-Up Left-Justified Algorithm.
/// 二维 BLA 实现，将货物从底部开始、向左靠拢放置。
/// 2D BLA implementation, places items starting from the bottom and left-justifying.
/// </summary>
public sealed class BottomUpLeftJustifiedAlgorithm : IBLAAlgorithm {
    /// <summary>算法名称 / Algorithm name.</summary>
    public string Name => nameof(BottomUpLeftJustifiedAlgorithm);
}
