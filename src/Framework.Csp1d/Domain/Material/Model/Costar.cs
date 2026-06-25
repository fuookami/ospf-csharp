#nullable enable

using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 配规/副产物，可填充切割方案剩余宽度 / Costar/byproduct that can fill remaining width in cutting plans
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public class Costar<V> : IProduction<V> where V : struct {
    /// <summary>
    /// 构造配规实例 / Construct a costar instance
    /// </summary>
    /// <param name="id">物料标识 / Material identifier.</param>
    /// <param name="name">产品名称 / Product name.</param>
    /// <param name="width">宽度列表 / List of widths.</param>
    /// <param name="length">长度 / Length.</param>
    /// <param name="unitWeight">单位重量 / Unit weight.</param>
    public Costar(
        string id,
        string name,
        IReadOnlyList<Quantity<V>> width,
        Quantity<V>? length = null,
        Quantity<V>? unitWeight = null) {
        Id = id;
        Name = name;
        Width = width;
        Length = length;
        UnitWeight = unitWeight;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <summary>产品名称 / Product name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<Quantity<V>> Width { get; }

    /// <inheritdoc/>
    public Quantity<V>? Length { get; }

    /// <inheritdoc/>
    public Quantity<V>? UnitWeight { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) {
        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj is not Costar<V> other) {
            return false;
        }

        return Id == other.Id;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => $"Costar({Id}, {Name})";
}
