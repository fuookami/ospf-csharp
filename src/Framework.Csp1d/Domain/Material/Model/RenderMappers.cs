#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Infrastructure.Dto;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 渲染映射扩展方法 / Render mapper extension methods
/// </summary>
public static class RenderMappers {
    /// <summary>
    /// 将领域 Product 映射到渲染 DTO / Map domain Product to render DTO
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="product">产品 / Product.</param>
    /// <param name="x">起始坐标 / Start coordinate.</param>
    /// <param name="productionType">生产类型 / Production type.</param>
    /// <param name="info">附加信息 / Additional info.</param>
    /// <returns>渲染 DTO / Render DTO.</returns>
    public static RenderCuttingPlanProductionDTO ToRenderDto<V>(
        this Product<V> product,
        FltX x,
        RenderProductionType productionType = RenderProductionType.Product,
        IReadOnlyDictionary<string, string>? info = null) where V : struct {
        Quantity<V>? maxWidth = product.MaxWidth();
        FltX renderWidth = maxWidth != null ? ConvertToFltX(maxWidth.Value) : FltX.Zero;
        FltX? renderUnitLength = product.Length != null ? ConvertToFltX(product.Length.Value) : (FltX?)null;
        return new RenderCuttingPlanProductionDTO(
            Name: product.Name,
            X: x,
            Width: renderWidth,
            UnitLength: renderUnitLength,
            ProductionType: productionType,
            Info: info ?? new Dictionary<string, string>()
        );
    }

    // **转换收进 `NumberConversions.ToFltXValue`**：原先这里只认四种类型，`Flt32` / `Int32` / `UInt32` 运行期抛。
    // 基线的 `RealNumber<V>.toFltX` 十二种全通，所以这里不该有只覆盖三分之一的 `is` 链。
    // **The conversion now lives in `NumberConversions.ToFltXValue`**: this spot used to recognise four types only and threw for `Flt32` / `Int32` / `UInt32` at run time. The baseline's
    // `RealNumber<V>.toFltX` covers all twelve, so a chain covering a third of them did not belong here.
    private static FltX ConvertToFltX<V>(V value) where V : struct
        => Fuookami.Ospf.Math.NumberConversions.ToFltXValue(value);
}
