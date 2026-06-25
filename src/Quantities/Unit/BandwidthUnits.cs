#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit
{
    /// <summary>
    /// 带宽/数据速率单位 / Bandwidth/data rate units.
    /// </summary>
    public static class BandwidthUnits
    {
        /// <summary>比特每秒 / Bits per second.</summary>
        public static readonly PhysicalUnit BitsPerSecond = new DerivedUnit(
            SIBaseUnits.Bit.Divide(SIBaseUnits.Second), "bits per second", "bit/s");
        /// <summary>千比特每秒 / Kilobits per second.</summary>
        public static readonly PhysicalUnit KilobitsPerSecond = new DerivedUnit(BitsPerSecond.ScaleMultiply(Scale.Kilo), "kilobits per second", "kbit/s");
        /// <summary>兆比特每秒 / Megabits per second.</summary>
        public static readonly PhysicalUnit MegabitsPerSecond = new DerivedUnit(BitsPerSecond.ScaleMultiply(Scale.Mega), "megabits per second", "Mbit/s");
        /// <summary>吉比特每秒 / Gigabits per second.</summary>
        public static readonly PhysicalUnit GigabitsPerSecond = new DerivedUnit(BitsPerSecond.ScaleMultiply(Scale.Giga), "gigabits per second", "Gbit/s");
        /// <summary>字节每秒 / Bytes per second.</summary>
        public static readonly PhysicalUnit BytesPerSecond = new DerivedUnit(BitsPerSecond.ScaleMultiply(Scale.Invoke(8)), "bytes per second", "B/s");

        private sealed class DerivedUnit : PhysicalUnit
        {
            private readonly PhysicalUnit _inner;
            private readonly string _name;
            private readonly string _symbol;
            public DerivedUnit(PhysicalUnit inner, string name, string symbol) { _inner = inner; _name = name; _symbol = symbol; }
            public override string Name => _name;
            public override string Symbol => _symbol;
            public override DerivedQuantity Quantity => _inner.Quantity;
            public override UnitConversionRule ConversionRule => _inner.ConversionRule;
        }
    }
}
