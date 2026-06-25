#nullable enable

using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Core.Symbol;
/// <summary>
/// 量纲符号转换工具 / Quantity symbol conversion utilities.
/// <para>提供带有物理单位的符号量纲转换扩展函数。</para>
/// <para>Provides extension functions for converting symbol quantities with physical units.</para>
/// </summary>
public static class QuantitySymbolConversion {
    /// <summary>
    /// 将量纲符号转换为目标单位 / Convert a quantity symbol to the target unit.
    /// </summary>
    /// <typeparam name="V">符号类型 / Symbol type</typeparam>
    /// <param name="quantity">源量纲符号 / Source quantity symbol</param>
    /// <param name="unit">目标物理单位 / Target physical unit</param>
    /// <returns>转换后的量纲符号，若单位不可转换则返回 null / Converted quantity symbol, or null if units are incompatible</returns>
    public static Quantity<V>? ToUnit<V>(this Quantity<V> quantity, PhysicalUnit unit)
        where V : ISymbol {
        if (quantity.Unit.CanConvertTo(unit)) {
            return new Quantity<V>(quantity.Value, unit);
        }
        return null;
    }
}
