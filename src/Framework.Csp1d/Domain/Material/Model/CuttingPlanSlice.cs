#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Quantities.Quantity;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 切割方案切片，描述单次切割中的产出对象及其幅宽 / Cutting plan slice describing production target and width in one cut
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed record CuttingPlanSlice<V>(
    IProduction<V> Production,
    Quantity<V> Width,
    UInt64 Amount
) : ICuttingPlanSliceForCanonicalKey where V : struct {
    /// <summary>默认份数为 1 / Default amount is one.</summary>
    public CuttingPlanSlice(IProduction<V> production, Quantity<V> width)
        : this(production, width, UInt64.One) { }

    /// <inheritdoc/>
    string ICuttingPlanSliceForCanonicalKey.ProductionType => Production switch {
        Product<V> => "Product",
        Costar<V> => "Costar",
        _ => Production.GetType().Name
    };

    /// <inheritdoc/>
    string? ICuttingPlanSliceForCanonicalKey.ProductionId => Production.Id;

    /// <inheritdoc/>
    string ICuttingPlanSliceForCanonicalKey.WidthKey => Width.Value.ToString() ?? "";

    /// <inheritdoc/>
    UInt64 ICuttingPlanSliceForCanonicalKey.Amount => Amount;
}
