#nullable enable

using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 生产物料接口，定义物料的基本属性 / Production material interface defining basic material properties
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface IProduction<V> where V : struct {
    /// <summary>物料标识 / Material identifier.</summary>
    string? Id { get; }

    /// <summary>物料宽度列表 / List of material widths.</summary>
    IReadOnlyList<Quantity<V>> Width { get; }

    /// <summary>物料长度 / Material length.</summary>
    Quantity<V>? Length { get; }

    /// <summary>单位重量 / Unit weight.</summary>
    Quantity<V>? UnitWeight { get; }
}
