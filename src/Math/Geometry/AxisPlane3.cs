#nullable enable

namespace Fuookami.Ospf.Math.Geometry;

/// <summary>
/// 三维主平面 / 3D principal plane.
/// </summary>
public sealed class AxisPlane3 {
    /// <summary>第一轴 / First axis.</summary>
    public Axis3 FirstAxis { get; }
    /// <summary>第二轴 / Second axis.</summary>
    public Axis3 SecondAxis { get; }
    /// <summary>法向轴 / Normal axis.</summary>
    public Axis3 NormalAxis { get; }

    private AxisPlane3(Axis3 first, Axis3 second, Axis3 normal) {
        FirstAxis = first;
        SecondAxis = second;
        NormalAxis = normal;
    }

    /// <summary>轴是否属于该平面 / Whether an axis belongs to this plane.</summary>
    public bool Contains(Axis3 axis) => axis == FirstAxis || axis == SecondAxis;

    /// <summary>XY 平面，法向 Z / XY plane, normal Z.</summary>
    public static readonly AxisPlane3 XY = new(Axis3.X, Axis3.Y, Axis3.Z);
    /// <summary>XZ 平面，法向 Y / XZ plane, normal Y.</summary>
    public static readonly AxisPlane3 XZ = new(Axis3.X, Axis3.Z, Axis3.Y);
    /// <summary>YZ 平面，法向 X / YZ plane, normal X.</summary>
    public static readonly AxisPlane3 YZ = new(Axis3.Y, Axis3.Z, Axis3.X);
}
