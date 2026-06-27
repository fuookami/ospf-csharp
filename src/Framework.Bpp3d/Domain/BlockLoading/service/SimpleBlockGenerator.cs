#nullable enable

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Service;

/// <summary>
/// 简单块生成器 / Simple block generator.
/// 为每种货物方向枚举 xyz 维度上所有可能的块大小。
/// Enumerates all possible block sizes in xyz dimensions for each item orientation.
/// </summary>
public sealed class SimpleBlockGenerator : IBlockGenerator {
    /// <summary>生成器名称 / Generator name.</summary>
    public string Name => nameof(SimpleBlockGenerator);

    /// <summary>是否合并为模式块 / Whether to merge as pattern block.</summary>
    public bool MergeAsPatternBlock { get; init; } = true;

    /// <summary>是否支持旋转 / Whether rotation is supported.</summary>
    public bool WithRotation { get; init; } = true;

    /// <summary>是否生成余数块 / Whether to generate remainder blocks.</summary>
    public bool WithRemainder { get; init; } = false;
}
