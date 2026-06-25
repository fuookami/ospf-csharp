#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Quantities.Dimension;

namespace Fuookami.Ospf.Quantities.Unit
{
    /// <summary>
    /// 信息单位 / Information units.
    /// </summary>
    public static class InformationUnits
    {
        /// <summary>比特 / Bit.</summary>
        public static readonly PhysicalUnit Bit = SIBaseUnits.Bit;
        /// <summary>字节 / Byte (1 B = 8 bit).</summary>
        public static readonly PhysicalUnit Byte = new DerivedUnit(Bit.ScaleMultiply(Scale.Invoke(8)), "byte", "B");
        /// <summary>千字节 / Kilobyte (1 KB = 1024 B).</summary>
        public static readonly PhysicalUnit Kilobyte = new DerivedUnit(Byte.ScaleMultiply(Scale.Invoke(1024)), "kilobyte", "KB");
        /// <summary>兆字节 / Megabyte (1 MB = 1024 KB).</summary>
        public static readonly PhysicalUnit Megabyte = new DerivedUnit(Byte.ScaleMultiply(Scale.Invoke(1048576)), "megabyte", "MB");
        /// <summary>吉字节 / Gigabyte (1 GB = 1024 MB).</summary>
        public static readonly PhysicalUnit Gigabyte = new DerivedUnit(Byte.ScaleMultiply(Scale.Invoke(1073741824)), "gigabyte", "GB");

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
