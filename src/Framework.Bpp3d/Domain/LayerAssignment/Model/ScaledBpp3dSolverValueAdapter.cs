#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model;

/// <summary>
/// 求解器单位系统配置 / Solver unit system configuration.
/// </summary>
/// <param name="LengthUnit">长度单位 / Length unit</param>
/// <param name="AreaUnit">面积单位 / Area unit</param>
/// <param name="VolumeUnit">体积单位 / Volume unit</param>
/// <param name="WeightUnit">重量单位 / Weight unit</param>
public sealed record Bpp3dSolverUnitSystem(
    PhysicalUnit? LengthUnit = null,
    PhysicalUnit? AreaUnit = null,
    PhysicalUnit? VolumeUnit = null,
    PhysicalUnit? WeightUnit = null);

/// <summary>
/// 求解器浮点缩放因子配置 / Solver floating-point scale factor configuration.
/// </summary>
/// <param name="Amount">数量缩放因子 / Amount scale factor</param>
/// <param name="Length">长度缩放因子 / Length scale factor</param>
/// <param name="Area">面积缩放因子 / Area scale factor</param>
/// <param name="Volume">体积缩放因子 / Volume scale factor</param>
/// <param name="Depth">深度缩放因子 / Depth scale factor</param>
/// <param name="Weight">重量缩放因子 / Weight scale factor</param>
public sealed record Bpp3dSolverFltXScale(
    Flt64? Amount = null,
    Flt64? Length = null,
    Flt64? Area = null,
    Flt64? Volume = null,
    Flt64? Depth = null,
    Flt64? Weight = null);

/// <summary>
/// 带缩放的 BPP3D 求解器值适配器 / Scaled BPP3D solver value adapter.
/// 支持单位转换和数值缩放 / Supports unit conversion and numeric scaling.
/// </summary>
public sealed class ScaledBpp3dSolverValueAdapter : Bpp3dSolverValueAdapter {
    private readonly Bpp3dSolverUnitSystem _unitSystem;
    private readonly Bpp3dSolverFltXScale _scale;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="unitSystem">单位系统配置 / Unit system configuration</param>
    /// <param name="scale">缩放因子配置 / Scale factor configuration</param>
    public ScaledBpp3dSolverValueAdapter(
        Bpp3dSolverUnitSystem? unitSystem = null,
        Bpp3dSolverFltXScale? scale = null)
        : base(scale?.Volume ?? Flt64.One) {
        _unitSystem = unitSystem ?? new Bpp3dSolverUnitSystem();
        _scale = scale ?? new Bpp3dSolverFltXScale();
    }

    /// <inheritdoc/>
    public new Flt64 AmountToSolver(UInt64 value) {
        Flt64 baseValue = value.ToFlt64();
        Flt64 factor = _scale.Amount ?? Flt64.One;
        return baseValue * factor;
    }

    /// <inheritdoc/>
    public new Flt64 LengthToSolver(Quantity<Flt64> value) {
        Flt64 factor = _scale.Length ?? Flt64.One;
        return value.Value * factor;
    }

    /// <inheritdoc/>
    public new Flt64 AreaToSolver(Quantity<Flt64> value) {
        Flt64 factor = _scale.Area ?? Flt64.One;
        return value.Value * factor;
    }

    /// <inheritdoc/>
    public new Flt64 VolumeToSolver(Quantity<Flt64> value) {
        Flt64 factor = _scale.Volume ?? Flt64.One;
        return value.Value * factor;
    }

    /// <inheritdoc/>
    public new Flt64 DepthToSolver(Quantity<Flt64> value) {
        Flt64 factor = _scale.Depth ?? Flt64.One;
        return value.Value * factor;
    }

    /// <inheritdoc/>
    public new Flt64 WeightToSolver(Quantity<Flt64> value) {
        Flt64 factor = _scale.Weight ?? Flt64.One;
        return value.Value * factor;
    }
}
