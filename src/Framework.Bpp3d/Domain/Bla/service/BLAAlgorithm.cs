#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Bla.Service
{
    /// <summary>
    /// BLA 算法接口 / BLA algorithm interface.
    /// </summary>
    public interface IBLAAlgorithm
    {
        /// <summary>算法名称 / Algorithm name.</summary>
        string Name { get; }
    }

    /// <summary>
    /// 自下向左调整算法 / Bottom-Up Left-Justified Algorithm.
    /// </summary>
    public sealed class BottomUpLeftJustifiedAlgorithm : IBLAAlgorithm
    {
        public string Name => nameof(BottomUpLeftJustifiedAlgorithm);
    }

    /// <summary>
    /// 三维自下向左调整算法 / 3D Bottom-Up Left-Justified Algorithm.
    /// </summary>
    public sealed class BottomUpLeftJustifiedAlgorithm3D : IBLAAlgorithm
    {
        public string Name => nameof(BottomUpLeftJustifiedAlgorithm3D);
    }
}
