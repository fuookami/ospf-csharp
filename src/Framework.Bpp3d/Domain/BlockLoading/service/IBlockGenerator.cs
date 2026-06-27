#nullable enable

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.BlockLoading.Service;

/// <summary>
/// 块生成器接口 / Block generator interface.
/// </summary>
public interface IBlockGenerator {
    /// <summary>生成器名称 / Generator name.</summary>
    string Name { get; }
}
