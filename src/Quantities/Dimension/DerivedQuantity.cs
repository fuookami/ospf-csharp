#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Quantities.Dimension;
/// <summary>
/// 导出量纲类 / Derived quantity class.
/// 由基础量纲的幂次组成，如 L^2*M*T^-2.
/// Composed of powers of fundamental dimensions, e.g. L^2*M*T^-2.
/// </summary>
public sealed class DerivedQuantity : IEquatable<DerivedQuantity> {
    /// <summary>排序后的量纲列表 / Sorted list of fundamental quantities.</summary>
    public IReadOnlyList<FundamentalQuantity> Quantities { get; }

    /// <summary>量纲名称 / Name of the quantity.</summary>
    public string? Name { get; }

    /// <summary>量纲符号 / Symbol of the quantity.</summary>
    public string? Symbol { get; }

    /// <summary>取值域 / Value domain.</summary>
    public QuantityDomain Domain { get; }

    /// <summary>
    /// 从基础量纲值列表构造 / Construct from list of fundamental quantities.
    /// </summary>
    public DerivedQuantity(
        IEnumerable<FundamentalQuantity> quantities,
        string? name = null,
        string? symbol = null,
        QuantityDomain domain = QuantityDomain.Continuous) {
        Quantities = quantities.OrderBy(q => q.Dimension.Symbol).ToList();
        Name = name;
        Symbol = symbol;
        Domain = domain;
    }

    /// <summary>
    /// 从单个基础量纲构造 / Construct from a single fundamental dimension.
    /// </summary>
    public DerivedQuantity(
        IFundamentalQuantityDimension dimension,
        string? name = null,
        string? symbol = null,
        QuantityDomain domain = QuantityDomain.Continuous)
        : this(new[] { new FundamentalQuantity(dimension) }, name, symbol, domain) {
    }

    /// <summary>
    /// 从单个基础量纲值构造 / Construct from a single fundamental quantity.
    /// </summary>
    public DerivedQuantity(
        FundamentalQuantity quantity,
        string? name = null,
        string? symbol = null,
        QuantityDomain domain = QuantityDomain.Continuous)
        : this(new[] { quantity }, name, symbol, domain) {
    }

    /// <summary>
    /// 从另一个导出量纲构造（拷贝构造）/ Copy constructor from another derived quantity.
    /// </summary>
    public DerivedQuantity(
        DerivedQuantity quantity,
        string? name = null,
        string? symbol = null,
        QuantityDomain? domain = null)
        : this(quantity.Quantities, name ?? quantity.Name, symbol ?? quantity.Symbol, domain ?? quantity.Domain) {
    }

    /// <summary>无量纲单例 / Dimensionless singleton.</summary>
    public static readonly DerivedQuantity Dimensionless = new(Array.Empty<FundamentalQuantity>(), "Dimensionless", "1");

    /// <summary>获取指定量纲的幂次 / Get the power of a specified dimension.</summary>
    public int GetPower(IFundamentalQuantityDimension dimension) =>
        Quantities.FirstOrDefault(q => q.Dimension == dimension)?.Index ?? 0;

    /// <summary>添加量纲幂次 / Add power to a dimension.</summary>
    public DerivedQuantity AddPower(IFundamentalQuantityDimension dimension, int power) {
        if (power == 0) {
            return this;
        }

        var list = Quantities.ToList();
        int idx = list.FindIndex(q => q.Dimension == dimension);
        if (idx >= 0) {
            FundamentalQuantity existing = list[idx];
            int newIndex = existing.Index + power;
            if (newIndex == 0) {
                list.RemoveAt(idx);
            }
            else {
                list[idx] = new FundamentalQuantity(dimension, newIndex);
            }
        }
        else {
            list.Add(new FundamentalQuantity(dimension, power));
        }
        return new DerivedQuantity(list, Name, Symbol, QuantityDomain.Continuous);
    }

    /// <summary>是否无量纲 / Check if dimensionless.</summary>
    public bool IsNone() => Quantities.Count == 0;

    /// <summary>获取量纲符号表示 / Get dimension symbol representation.</summary>
    public string DimensionSymbol() {
        if (IsNone()) {
            return "1";
        }

        return string.Join("·", Quantities.Select(dim =>
            dim.Index == 1 ? dim.Dimension.Symbol : $"{dim.Dimension.Symbol}^{dim.Index}"));
    }

    /// <summary>幂次运算 / Power operation.</summary>
    public DerivedQuantity Pow(int index) {
        if (index == 0) {
            return Dimensionless;
        }

        if (index == 1) {
            return this;
        }

        return new DerivedQuantity(
            Quantities.Select(q => q * index),
            Name, Symbol,
            QuantityDomainOps.Pow(Domain, index));
    }

    /// <summary>倒数 / Reciprocal.</summary>
    public DerivedQuantity Reciprocal() => Negate();

    /// <summary>取负（所有幂次取反）/ Negate (invert all powers).</summary>
    public DerivedQuantity Negate() => new(
        Quantities.Select(q => -q),
        null, null,
        QuantityDomain.Continuous);

    /// <inheritdoc/>
    public override string ToString() =>
        Symbol ?? Name ?? (IsNone() ? "1" : string.Join("·", Quantities.Select(q => $"{q.Dimension}^{q.Index}")));

    /// <inheritdoc/>
    public bool Equals(DerivedQuantity? other) {
        if (other is null) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        if (Quantities.Count != other.Quantities.Count) {
            return false;
        }

        for (int i = 0; i < Quantities.Count; i++) {
            if (!Quantities[i].Equals(other.Quantities[i])) {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as DerivedQuantity);

    /// <inheritdoc/>
    public override int GetHashCode() {
        var hash = new HashCode();
        foreach (FundamentalQuantity q in Quantities) {
            hash.Add(q);
        }

        return hash.ToHashCode();
    }

    // ===== DerivedQuantity 运算符 / Operators =====

    /// <summary>两个导出量纲相乘 / Multiply two derived quantities.</summary>
    public static DerivedQuantity operator *(DerivedQuantity lhs, DerivedQuantity rhs) {
        var dict = new Dictionary<IFundamentalQuantityDimension, int>();
        foreach (FundamentalQuantity q in lhs.Quantities) {
            dict[q.Dimension] = dict.GetValueOrDefault(q.Dimension, 0) + q.Index;
        }

        foreach (FundamentalQuantity q in rhs.Quantities) {
            dict[q.Dimension] = dict.GetValueOrDefault(q.Dimension, 0) + q.Index;
        }

        return new DerivedQuantity(
            dict.Where(kv => kv.Value != 0).Select(kv => new FundamentalQuantity(kv.Key, kv.Value)),
            domain: QuantityDomainOps.Multiply(lhs.Domain, rhs.Domain));
    }

    /// <summary>两个导出量纲相除 / Divide two derived quantities.</summary>
    public static DerivedQuantity operator /(DerivedQuantity lhs, DerivedQuantity rhs) {
        var dict = new Dictionary<IFundamentalQuantityDimension, int>();
        foreach (FundamentalQuantity q in lhs.Quantities) {
            dict[q.Dimension] = dict.GetValueOrDefault(q.Dimension, 0) + q.Index;
        }

        foreach (FundamentalQuantity q in rhs.Quantities) {
            dict[q.Dimension] = dict.GetValueOrDefault(q.Dimension, 0) - q.Index;
        }

        return new DerivedQuantity(
            dict.Where(kv => kv.Value != 0).Select(kv => new FundamentalQuantity(kv.Key, kv.Value)),
            domain: QuantityDomainOps.Divide(lhs.Domain, rhs.Domain));
    }

    /// <summary>导出量纲乘以基础量纲值 / Multiply derived quantity by fundamental quantity.</summary>
    public static DerivedQuantity operator *(DerivedQuantity lhs, FundamentalQuantity rhs) {
        var dict = new Dictionary<IFundamentalQuantityDimension, int>();
        foreach (FundamentalQuantity q in lhs.Quantities) {
            dict[q.Dimension] = dict.GetValueOrDefault(q.Dimension, 0) + q.Index;
        }

        dict[rhs.Dimension] = dict.GetValueOrDefault(rhs.Dimension, 0) + rhs.Index;
        return new DerivedQuantity(
            dict.Where(kv => kv.Value != 0).Select(kv => new FundamentalQuantity(kv.Key, kv.Value)));
    }

    /// <summary>导出量纲除以基础量纲值 / Divide derived quantity by fundamental quantity.</summary>
    public static DerivedQuantity operator /(DerivedQuantity lhs, FundamentalQuantity rhs) {
        var dict = new Dictionary<IFundamentalQuantityDimension, int>();
        foreach (FundamentalQuantity q in lhs.Quantities) {
            dict[q.Dimension] = dict.GetValueOrDefault(q.Dimension, 0) + q.Index;
        }

        dict[rhs.Dimension] = dict.GetValueOrDefault(rhs.Dimension, 0) - rhs.Index;
        return new DerivedQuantity(
            dict.Where(kv => kv.Value != 0).Select(kv => new FundamentalQuantity(kv.Key, kv.Value)));
    }

    /// <summary>基础量纲值乘以导出量纲 / Multiply fundamental quantity by derived quantity.</summary>
    public static DerivedQuantity operator *(FundamentalQuantity lhs, DerivedQuantity rhs) => rhs * lhs;

    /// <summary>导出量纲乘以整数 / Multiply derived quantity by int.</summary>
    public static DerivedQuantity operator *(DerivedQuantity lhs, int rhs) {
        return new DerivedQuantity(
            lhs.Quantities.Select(q => q * rhs),
            lhs.Name, lhs.Symbol,
            QuantityDomainOps.Pow(lhs.Domain, rhs));
    }

    /// <summary>导出量纲除以整数 / Divide derived quantity by int.</summary>
    public static DerivedQuantity operator /(DerivedQuantity lhs, int rhs) {
        return new DerivedQuantity(
            lhs.Quantities.Select(q => q / rhs));
    }

    /// <summary>两个基础量纲值相乘 / Multiply two fundamental quantities.</summary>
    public static DerivedQuantity Multiply(FundamentalQuantity lhs, FundamentalQuantity rhs) {
        var dict = new Dictionary<IFundamentalQuantityDimension, int>();
        dict[lhs.Dimension] = dict.GetValueOrDefault(lhs.Dimension, 0) + lhs.Index;
        dict[rhs.Dimension] = dict.GetValueOrDefault(rhs.Dimension, 0) + rhs.Index;
        return new DerivedQuantity(
            dict.Where(kv => kv.Value != 0).Select(kv => new FundamentalQuantity(kv.Key, kv.Value)));
    }

    /// <summary>两个基础量纲值相除 / Divide two fundamental quantities.</summary>
    public static DerivedQuantity Divide(FundamentalQuantity lhs, FundamentalQuantity rhs) {
        var dict = new Dictionary<IFundamentalQuantityDimension, int>();
        dict[lhs.Dimension] = dict.GetValueOrDefault(lhs.Dimension, 0) + lhs.Index;
        dict[rhs.Dimension] = dict.GetValueOrDefault(rhs.Dimension, 0) - rhs.Index;
        return new DerivedQuantity(
            dict.Where(kv => kv.Value != 0).Select(kv => new FundamentalQuantity(kv.Key, kv.Value)));
    }
}
