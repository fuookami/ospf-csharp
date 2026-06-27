#nullable enable

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Service;

/// <summary>
/// 复杂块生成器 / Complex block generator.
/// 将简单块沿 x/y/z 轴合并为复合块。
/// Merges simple blocks along x/y/z axes into composite blocks.
/// </summary>
public sealed class ComplexBlockGenerator : IBlockGenerator {
    /// <summary>生成器名称 / Generator name.</summary>
    public string Name => nameof(ComplexBlockGenerator);

    /// <summary>是否沿 X 轴合并 / Whether to merge along X axis.</summary>
    public bool WithX { get; init; } = true;

    /// <summary>是否沿 Y 轴合并 / Whether to merge along Y axis.</summary>
    public bool WithY { get; init; } = true;

    /// <summary>是否沿 Z 轴合并 / Whether to merge along Z axis.</summary>
    public bool WithZ { get; init; } = false;
}
