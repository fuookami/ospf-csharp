#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// 保守半径包络 / Conservative radius envelope.
/// 在求解器建模阶段使用 rMax 作为安全上界；求解后使用 real* 方法计算精确尺寸。
/// Uses rMax as safe upper bound during solver modeling; real* methods compute exact dimensions post-solve.
/// </summary>
public sealed record ConservativeRadiusEnvelope(Quantity<FltX> RMin, Quantity<FltX> RMax) {
    /// <summary>包络半径（= RMax）/ Envelope radius (= RMax).</summary>
    public Quantity<FltX> EnvelopeRadius => RMax;

    /// <summary>包络直径 / Envelope diameter.</summary>
    public Quantity<FltX> EnvelopeDiameter => new(RMax.Value.Plus(RMax.Value), RMax.Unit);

    /// <summary>保守底面宽度 / Conservative footprint width.</summary>
    public Quantity<FltX> FootprintWidth(Axis3 axis, Quantity<FltX> cylinderHeight) =>
        axis == Axis3.Y ? EnvelopeDiameter : cylinderHeight;

    /// <summary>保守底面深度 / Conservative footprint depth.</summary>
    public Quantity<FltX> FootprintDepth(Axis3 axis, Quantity<FltX> cylinderHeight) =>
        axis == Axis3.Z ? cylinderHeight : EnvelopeDiameter;

    /// <summary>保守包围宽度 / Conservative bounding width.</summary>
    public Quantity<FltX> BoundingWidth(Axis3 axis, Quantity<FltX> cylinderHeight) =>
        axis == Axis3.X ? cylinderHeight : EnvelopeDiameter;

    /// <summary>保守包围高度 / Conservative bounding height.</summary>
    public Quantity<FltX> BoundingHeight(Axis3 axis, Quantity<FltX> cylinderHeight) =>
        axis == Axis3.Y ? cylinderHeight : EnvelopeDiameter;

    /// <summary>保守包围深度 / Conservative bounding depth.</summary>
    public Quantity<FltX> BoundingDepth(Axis3 axis, Quantity<FltX> cylinderHeight) =>
        axis == Axis3.Z ? cylinderHeight : EnvelopeDiameter;

    /// <summary>支撑覆盖半径 / Support coverage radius.</summary>
    public Quantity<FltX> SupportCoverageRadius() => RMax;

    /// <summary>碰撞边距 / Collision margin.</summary>
    public Quantity<FltX> CollisionMargin() => new(new FltX(1e-7), RMax.Unit);

    /// <summary>精确底面宽度 / Real footprint width.</summary>
    public Quantity<FltX> RealFootprintWidth(Axis3 axis, Quantity<FltX> cylinderHeight, Quantity<FltX> actualRadius) =>
        axis == Axis3.Y
            ? new Quantity<FltX>(actualRadius.Value.Plus(actualRadius.Value), actualRadius.Unit)
            : cylinderHeight;

    /// <summary>精确底面深度 / Real footprint depth.</summary>
    public Quantity<FltX> RealFootprintDepth(Axis3 axis, Quantity<FltX> cylinderHeight, Quantity<FltX> actualRadius) =>
        axis == Axis3.Z
            ? cylinderHeight
            : new Quantity<FltX>(actualRadius.Value.Plus(actualRadius.Value), actualRadius.Unit);

    /// <summary>精确包围宽度 / Real bounding width.</summary>
    public Quantity<FltX> RealBoundingWidth(Axis3 axis, Quantity<FltX> cylinderHeight, Quantity<FltX> actualRadius) =>
        axis == Axis3.X
            ? cylinderHeight
            : new Quantity<FltX>(actualRadius.Value.Plus(actualRadius.Value), actualRadius.Unit);

    /// <summary>精确包围高度 / Real bounding height.</summary>
    public Quantity<FltX> RealBoundingHeight(Axis3 axis, Quantity<FltX> cylinderHeight, Quantity<FltX> actualRadius) =>
        axis == Axis3.Y
            ? cylinderHeight
            : new Quantity<FltX>(actualRadius.Value.Plus(actualRadius.Value), actualRadius.Unit);

    /// <summary>精确包围深度 / Real bounding depth.</summary>
    public Quantity<FltX> RealBoundingDepth(Axis3 axis, Quantity<FltX> cylinderHeight, Quantity<FltX> actualRadius) =>
        axis == Axis3.Z
            ? cylinderHeight
            : new Quantity<FltX>(actualRadius.Value.Plus(actualRadius.Value), actualRadius.Unit);

    /// <summary>
    /// 验证求解器选择的半径是否在 [RMin, RMax] 范围内。
    /// Validates that solver-selected radius is within [RMin, RMax].
    /// </summary>
    public bool IsRadiusValid(Quantity<FltX> solverRadius) {
        double r = solverRadius.Value.ToFlt64().ToDouble();
        double rMin = RMin.Value.ToFlt64().ToDouble();
        double rMax = RMax.Value.ToFlt64().ToDouble();
        return r >= rMin - 1e-9 && r <= rMax + 1e-9;
    }
}
