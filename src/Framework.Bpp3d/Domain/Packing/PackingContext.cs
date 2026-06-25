#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing
{
    /// <summary>
    /// 装箱上下文接口 / Packing context interface.
    /// </summary>
    public interface IPackingContext
    {
    }

    /// <summary>
    /// 装箱上下文 / Packing context.
    /// 装箱求解编排 / Orchestrates packing solve.
    /// </summary>
    public sealed class PackingContext : IPackingContext
    {
        /// <summary>剩余货物 / Rest items.</summary>
        public IReadOnlyDictionary<string, ulong> RestItems { get; init; } = new Dictionary<string, ulong>();

        /// <summary>剩余物料 / Rest materials.</summary>
        public IReadOnlyDictionary<string, ulong> RestMaterials { get; init; } = new Dictionary<string, ulong>();

        /// <summary>附加信息 / Additional info.</summary>
        public IReadOnlyDictionary<string, string> Info { get; init; } = new Dictionary<string, string>();
    }
}
