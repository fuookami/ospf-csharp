#nullable enable

using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 宽度范围，支持步进和单位一致性 / Width range with step and unit consistency
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
/// <param name="Width">宽度值域 / Width value range.</param>
/// <param name="Step">步进值 / Step value.</param>
public sealed record WidthRange<V>(
    QuantityRange<V> Width,
    Quantity<V> Step
) where V : struct {
    /// <summary>上界 / Upper bound.</summary>
    public Quantity<V> UpperBound => Width.UpperBound;

    /// <summary>下界 / Lower bound.</summary>
    public Quantity<V> LowerBound => Width.LowerBound;

    /// <summary>
    /// 判断给定宽度是否能在该幅宽范围的原料上分切 / Check whether a given width can be cut on material with this width range
    ///
    /// 产品只需要宽度不超过上界即可在下界及以上幅宽的原料上分切 / A product only needs width &lt;= upperBound to be cuttable
    /// </summary>
    /// <param name="productWidth">产品宽度 / Product width to check.</param>
    /// <returns>是否可分切 / Whether cuttable.</returns>
    public bool CanCut(Quantity<V> productWidth) {
        Result<Order, ErrorCode, Error<ErrorCode>> ordResult = QuantityRangeHelpers.CompareSafe(productWidth, Width.UpperBound, "width-range");
        return ordResult.IsOk && ordResult.Value is not Order.Greater;
    }
}
