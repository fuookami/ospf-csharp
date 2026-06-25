#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits
{
    /// <summary>
    /// BPP3D 目标接口 / BPP3D objective interface.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public interface IBpp3dObjective<V> where V : struct, IFloatingNumber<V>
    {
        /// <summary>目标名称 / Objective name.</summary>
        string Name { get; }
    }

    /// <summary>
    /// 更优层最大化 / Better layer maximization.
    /// </summary>
    public sealed class BetterLayerMaximization<V> : IBpp3dObjective<V>
        where V : struct, IFloatingNumber<V>
    {
        public string Name => nameof(BetterLayerMaximization<V>);
    }

    /// <summary>
    /// 箱数量最小化 / Bin amount minimization.
    /// </summary>
    public sealed class BinAmountMinimization<V> : IBpp3dObjective<V>
        where V : struct, IFloatingNumber<V>
    {
        public string Name => nameof(BinAmountMinimization<V>);
    }

    /// <summary>
    /// 剩余量最小化 / Rest amount minimization.
    /// </summary>
    public sealed class RestAmountMinimization<V> : IBpp3dObjective<V>
        where V : struct, IFloatingNumber<V>
    {
        public string Name => nameof(RestAmountMinimization<V>);
    }

    /// <summary>
    /// 尾箱装载率最小化 / Tail bin loading rate minimization.
    /// </summary>
    public sealed class TailBinLoadingRateMinimization<V> : IBpp3dObjective<V>
        where V : struct, IFloatingNumber<V>
    {
        public string Name => nameof(TailBinLoadingRateMinimization<V>);
    }

    /// <summary>
    /// 体积最小化 / Volume minimization.
    /// </summary>
    public sealed class VolumeMinimization<V> : IBpp3dObjective<V>
        where V : struct, IFloatingNumber<V>
    {
        public string Name => nameof(VolumeMinimization<V>);
    }
}
