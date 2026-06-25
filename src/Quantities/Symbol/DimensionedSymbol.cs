#nullable enable

using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Quantities.Symbol;

/// <summary>
/// 携带量纲信息的符号 / Symbol with dimension information.
/// 用于量纲语义校验，确保符号表达式具备物理意义。
/// Used for dimension semantic validation to ensure symbol expressions have physical meaning.
/// </summary>
public sealed record DimensionedSymbol(
    string Name,
    string? DisplayName,
    DerivedQuantity Quantity,
    PhysicalUnit? PreferredUnit
) : ISymbol {
    /// <summary>
    /// 检查是否可以与另一个符号相加 / Check if this symbol can be added to another.
    /// 只有量纲相同的符号才能相加 / Only symbols with the same dimension can be added.
    /// </summary>
    public bool CanAddTo(DimensionedSymbol other) => this.Quantity.Equals(other.Quantity);

    /// <summary>
    /// 与另一个符号相乘得到的新量纲 / Get the resulting dimension from multiplying with another symbol.
    /// </summary>
    public DerivedQuantity MultiplyWith(DimensionedSymbol other) => this.Quantity * other.Quantity;

    /// <summary>
    /// 除以另一个符号得到的新量纲 / Get the resulting dimension from dividing by another symbol.
    /// </summary>
    public DerivedQuantity DivideBy(DimensionedSymbol other) => this.Quantity / other.Quantity;
}
