#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 产品，描述物料的规格属性 / Product describing material specification properties
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public class Product<V> : IProduction<V> where V : struct {
    /// <summary>
    /// 构造产品实例 / Construct a product instance
    /// </summary>
    /// <param name="id">物料标识 / Material identifier.</param>
    /// <param name="name">产品名称 / Product name.</param>
    /// <param name="width">宽度列表 / List of widths.</param>
    /// <param name="length">长度 / Length.</param>
    /// <param name="unitWeight">单位重量 / Unit weight.</param>
    /// <param name="weight">重量 / Weight.</param>
    /// <param name="maxOverProduceLength">最大超产长度 / Maximum overproduce length.</param>
    /// <param name="dynamicLength">是否动态长度 / Whether length is dynamic.</param>
    public Product(
        string id,
        string name,
        IReadOnlyList<Quantity<V>> width,
        Quantity<V>? length = null,
        Quantity<V>? unitWeight = null,
        Quantity<V>? weight = null,
        Quantity<V>? maxOverProduceLength = null,
        bool dynamicLength = false) {
        Id = id;
        Name = name;
        Width = width;
        Length = length;
        UnitWeight = unitWeight;
        _explicitWeight = weight;
        MaxOverProduceLength = maxOverProduceLength;
        DynamicLength = dynamicLength;
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

    /// <summary>最大超产长度 / Maximum overproduce length.</summary>
    public Quantity<V>? MaxOverProduceLength { get; }

    /// <summary>是否动态长度 / Whether length is dynamic.</summary>
    public bool DynamicLength { get; }

    private readonly Quantity<V>? _explicitWeight;

    private Quantity<V>? _cachedWeight;
    private bool _weightCached;

    /// <summary>
    /// 创建动态长度产品实例 / Create a dynamic-length product instance
    /// </summary>
    /// <param name="id">物料标识 / Material identifier.</param>
    /// <param name="name">产品名称 / Product name.</param>
    /// <param name="width">宽度列表 / List of widths.</param>
    /// <param name="unitWeight">单位重量 / Unit weight.</param>
    /// <returns>动态长度产品实例 / Dynamic-length product instance.</returns>
    public static Product<V> DynamicLengthOf(
        string id,
        string name,
        IReadOnlyList<Quantity<V>> width,
        Quantity<V>? unitWeight = null) => new Product<V>(id, name, width, unitWeight: unitWeight, dynamicLength: true);

    /// <summary>
    /// 最大宽度 / Maximum width
    /// </summary>
    public Quantity<V>? MaxWidth() {
        if (Width.Count == 0) {
            return null;
        }

        Quantity<V> result = Width[0];
        for (int i = 1; i < Width.Count; i++) {
            Utils.Functional.Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxResult = QuantityMinMax.Max(result, Width[i]);
            if (maxResult.IsOk) {
                result = maxResult.Value;
            }
        }
        return result;
    }

    /// <summary>
    /// 产品重量（若未显式给出则按 max(width) * length * unitWeight 推导）/ Product weight (derived from max width when not explicitly provided)
    /// </summary>
    public Quantity<V>? Weight {
        get {
            if (_weightCached) {
                return _cachedWeight;
            }

            _weightCached = true;
            _cachedWeight = _explicitWeight ?? DeriveWeight(MaxWidth());
            return _cachedWeight;
        }
    }

    /// <summary>
    /// 根据指定宽度和长度计算重量 / Calculate weight for given width and length
    /// </summary>
    /// <param name="width">宽度 / Width.</param>
    /// <param name="length">长度 / Length.</param>
    /// <returns>重量 / Weight.</returns>
    public Quantity<V>? WeightFor(Quantity<V> width, Quantity<V>? length = null) => DeriveWeight(width, length ?? Length);

    private Quantity<V>? DeriveWeight(Quantity<V>? width, Quantity<V>? length = null) {
        Quantity<V>? currentLength = length ?? Length;
        Quantity<V>? currentUnitWeight = UnitWeight;
        if (currentLength == null || currentUnitWeight == null || width == null) {
            return null;
        }

        Quantity<V> intermediate = MultiplyQuantities(currentLength, currentUnitWeight);
        return MultiplyQuantities(intermediate, width);
    }

    private static Quantity<V> MultiplyQuantities(Quantity<V> lhs, Quantity<V> rhs) {
        PhysicalUnit unit = lhs.Unit.Multiply(rhs.Unit);
        V lhsVal = lhs.Value;
        if (lhsVal is Flt64 f64l && rhs.Value is Flt64 f64r) {
            return new Quantity<V>((V)(object)f64l.Times(f64r), unit);
        }

        if (lhsVal is FltX fxl && rhs.Value is FltX fxr) {
            return new Quantity<V>((V)(object)fxl.Times(fxr), unit);
        }

        throw new NotSupportedException($"Multiplication not supported for type {typeof(V).Name}");
    }

    /// <inheritdoc/>
    public override string ToString() {
        string widthStr = string.Join("x", Width.Select(w => w.Value.ToString()));
        return $"{Name}{widthStr}";
    }
}
