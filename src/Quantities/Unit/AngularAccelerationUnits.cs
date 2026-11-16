#nullable enable

using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit;

/// <summary>
/// 角加速度单位 / Angular acceleration units.
/// </summary>
/// <remarks>
/// **按新建，2 个单位。**
///
/// **它们全部基于刚建的 `AngularVelocityUnits`**——
/// **所以这两个文件必须一起存在**：**单独发布角加速度而漏掉角速度，编译不过，
/// 而那正是"依赖关系把顺序钉住了"的好处。**
///
/// **Created, two units.**
///
/// **Both are built on the `AngularVelocityUnits` added in the same batch** —
/// **so the two files have to exist together**: **shipping angular acceleration without angular velocity does not compile,
/// and that is exactly the benefit of the dependency pinning the order.**
/// </remarks>
public static class AngularAccelerationUnits
{
    /// <summary>弧度每二次方秒 / Radians per second squared.</summary>
    public static readonly PhysicalUnit RadianPerSecondSquared = new DerivedUnit(
        AngularVelocityUnits.RadianPerSecond.Divide(SIBaseUnits.Second), "radians per second squared", "rad/s2");

    /// <summary>度每二次方秒 / Degrees per second squared.</summary>
    public static readonly PhysicalUnit DegreePerSecondSquared = new DerivedUnit(
        AngularVelocityUnits.DegreePerSecond.Divide(SIBaseUnits.Second), "degrees per second squared", "deg/s2");

    private sealed class DerivedUnit : PhysicalUnit
    {
        private readonly PhysicalUnit _inner;
        private readonly string _name;
        private readonly string _symbol;

        public DerivedUnit(PhysicalUnit inner, string name, string symbol)
        {
            _inner = inner;
            _name = name;
            _symbol = symbol;
        }

        public override string Name => _name;

        public override string Symbol => _symbol;

        public override DerivedQuantity Quantity => _inner.Quantity;

        public override UnitConversionRule ConversionRule => _inner.ConversionRule;
    }
}
