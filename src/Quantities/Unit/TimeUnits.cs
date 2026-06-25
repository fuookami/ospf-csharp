#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit
{
    /// <summary>
    /// 时间单位 / Time units.
    /// </summary>
    public static class TimeUnits
    {
        /// <summary>分钟 / Minute (60 s).</summary>
        public static readonly PhysicalUnit Minute = new DerivedUnit(SIBaseUnits.Second, Scale.Invoke(60), "minute", "min");
        /// <summary>小时 / Hour (3600 s).</summary>
        public static readonly PhysicalUnit Hour = new DerivedUnit(SIBaseUnits.Second, Scale.Invoke(3600), "hour", "h");
        /// <summary>天 / Day (86400 s).</summary>
        public static readonly PhysicalUnit Day = new DerivedUnit(SIBaseUnits.Second, Scale.Invoke(86400), "day", "d");
        /// <summary>毫秒 / Millisecond.</summary>
        public static readonly PhysicalUnit Millisecond = new DerivedUnit(SIBaseUnits.Second, Scale.Milli, "millisecond", "ms");
        /// <summary>微秒 / Microsecond.</summary>
        public static readonly PhysicalUnit Microsecond = new DerivedUnit(SIBaseUnits.Second, Scale.Micro, "microsecond", "us");
        /// <summary>纳秒 / Nanosecond.</summary>
        public static readonly PhysicalUnit Nanosecond = new DerivedUnit(SIBaseUnits.Second, Scale.Nano, "nanosecond", "ns");

        private sealed class DerivedUnit : PhysicalUnit
        {
            private readonly PhysicalUnit _base;
            private readonly Scale _factor;
            private readonly string _name;
            private readonly string _symbol;

            public DerivedUnit(PhysicalUnit baseUnit, Scale factor, string name, string symbol)
            {
                _base = baseUnit;
                _factor = factor;
                _name = name;
                _symbol = symbol;
            }

            public override string Name => _name;
            public override string Symbol => _symbol;
            public override DerivedQuantity Quantity => _base.Quantity;
            public override UnitConversionRule ConversionRule => new UnitConversionRule.Linear(_factor);
        }
    }
}
