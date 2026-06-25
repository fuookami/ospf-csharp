#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Infrastructure.Dto;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using System;
using System.Collections.Generic;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

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

    private static FltX ConvertToFltX<V>(V value) where V : struct {
        if (value is Flt64 f64) {
            return f64.ToFltX();
        }

        if (value is FltX fx) {
            return fx;
        }

        if (value is Int64 i64) {
            return i64.ToFltX();
        }

        if (value is UInt64 u64) {
            return u64.ToFltX();
        }

        throw new NotSupportedException($"Conversion to FltX not supported for type {typeof(V).Name}");
    }
}
