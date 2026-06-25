#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra
{
    /// <summary>求解器值适配器 / Solver-value adapter.</summary>
    public class Bpp3dSolverValueAdapter
    {
        public Flt64 Scale { get; }

        public Bpp3dSolverValueAdapter() : this(Flt64.One) { }

        public Bpp3dSolverValueAdapter(Flt64 scale)
        {
            Scale = scale;
        }

        /// <summary>数量转求解器值 / Amount to solver value.</summary>
        public Flt64 AmountToSolver(UInt64 value) => value.ToFlt64();

        /// <summary>长度转求解器值 / Length to solver value.</summary>
        public Flt64 LengthToSolver(Quantity<Flt64> value) => value.Value;

        /// <summary>面积转求解器值 / Area to solver value.</summary>
        public Flt64 AreaToSolver(Quantity<Flt64> value) => value.Value;

        /// <summary>体积转求解器值 / Volume to solver value.</summary>
        public Flt64 VolumeToSolver(Quantity<Flt64> value) => value.Value;

        /// <summary>深度转求解器值 / Depth to solver value.</summary>
        public Flt64 DepthToSolver(Quantity<Flt64> value) => LengthToSolver(value);

        /// <summary>重量转求解器值 / Weight to solver value.</summary>
        public Flt64 WeightToSolver(Quantity<Flt64> value) => value.Value;
    }

    /// <summary>缩放求解器值适配器 / Scaled solver-value adapter.</summary>
    public sealed class ScaledBpp3dSolverValueAdapter : Bpp3dSolverValueAdapter
    {
        public ScaledBpp3dSolverValueAdapter(Flt64 scale) : base(scale) { }
    }
}
