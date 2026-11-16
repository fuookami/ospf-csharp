// 物品域 id 语义标记接口 / Item domain id semantic marker interfaces.
//
// 裸 `String` id 改为**语义化标记接口**，业务侧实现接口、子类协变 override，以保留类型安全。C# 侧此前 `ActualItem.Id`
// 仍是裸 `string`，物品编号与箱型编号、物料编号在类型上无法区分——把物品编号传给要箱型编号的方法，编译能过。
// The code replaced raw `String` ids with **semantic marker interfaces** so implementations and covariant overrides
// keep type safety. On the C# side `ActualItem.Id` was still a raw `string`, leaving item, bin-type and material identifiers
// indistinguishable at the type level — passing an item id where a bin-type id was expected compiled fine.

using System;
using System.Globalization;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain;

/// <summary>
/// 货物 id 标记接口 / Item id marker interface.
/// </summary>
public interface IItemId : IComparable<IItemId>, IEquatable<IItemId> {
    /// <summary>裸字符串值 / The raw string value.</summary>
    string Value { get; }
}

/// <summary>
/// 箱型 id 标记接口 / Bin type id marker interface.
/// </summary>
public interface IBinTypeId : IComparable<IBinTypeId>, IEquatable<IBinTypeId> {
    /// <summary>裸字符串值 / The raw string value.</summary>
    string Value { get; }
}

/// <summary>
/// 物料 id 标记接口 / Material id marker interface.
/// </summary>
/// <remarks>
/// 这里只声明接口，实际仍用 infrastructure 侧的 `MaterialNo` 值类——因为 infrastructure 不依赖 item-context，值类无法
/// 实现该接口。因此本接口留给未来的实现者，现有代码继续用 <see cref="Infra.MaterialNo"/>。
/// Only the interface is declared here, and the infrastructure-side `MaterialNo` value class is still used, because
/// infrastructure does not depend on item-context and a value class cannot implement it. Hence the split:
/// this interface is for future implementers while existing code keeps using <see cref="Infra.MaterialNo"/>.
/// </remarks>
public interface IMaterialId : IComparable<IMaterialId>, IEquatable<IMaterialId> {
    /// <summary>裸字符串值 / The raw string value.</summary>
    string Value { get; }
}

/// <summary>
/// 包装 id 标记接口 / Package id marker interface.
/// </summary>
/// <remarks>
/// 与 <see cref="IMaterialId"/> 同理：`PackageCode` 值类保留，本接口供未来实现者使用。
/// Same rationale as <see cref="IMaterialId"/>: the `PackageCode` value class stays and this interface is for
/// future implementers.
/// </remarks>
public interface IPackageId : IComparable<IPackageId>, IEquatable<IPackageId> {
    /// <summary>裸字符串值 / The raw string value.</summary>
    string Value { get; }
}

/// <summary>
/// <see cref="IItemId"/> 的默认实现：承载 String 裸值 / Default <see cref="IItemId"/> implementation wrapping a raw string.
/// </summary>
/// <remarks>
/// 用于拼接生成 `ActualItem.Id`（如物料装箱器、层生成程序适配器），以及测试构造物品实例。
/// Used for concatenated `ActualItem.Id` values (material packers, layer-generation program adapters) and for building items
/// in tests.
/// </remarks>
/// <param name="Value">裸字符串值 / Raw string value.</param>
public sealed record ItemIdImpl(string Value) : IItemId {
    /// <inheritdoc/>
    public int CompareTo(IItemId? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public bool Equals(IItemId? other) =>
        other is ItemIdImpl typed && string.Equals(Value, typed.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// <see cref="IBinTypeId"/> 的默认实现：承载 String 裸值 / Default <see cref="IBinTypeId"/> implementation.
/// </summary>
/// <param name="Value">裸字符串值 / Raw string value.</param>
public sealed record BinTypeIdImpl(string Value) : IBinTypeId {
    /// <inheritdoc/>
    public int CompareTo(IBinTypeId? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc/>
    public bool Equals(IBinTypeId? other) =>
        other is BinTypeIdImpl typed && string.Equals(Value, typed.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>
/// id 工厂 / Id factories.
/// </summary>
/// <remarks>
/// 工厂函数存在的理由是**不直接暴露实现类构造器**：调用方只拿到接口，将来换实现不必改动调用点。
/// The factories exist so **implementation constructors are not exposed directly**: callers only hold the interface, and a
/// future implementation swap needs no call-site changes.
/// </remarks>
public static class IdFactory {
    /// <summary>
    /// 由 String 裸值构造 <see cref="IItemId"/> / Build an <see cref="IItemId"/> from a raw string value.
    /// </summary>
    /// <param name="value">原始字符串标识符 / Raw string identifier.</param>
    /// <returns>包装给定值的 id / An id wrapping the given value.</returns>
    public static IItemId ItemIdOf(string value) {
        ArgumentNullException.ThrowIfNull(value);
        return new ItemIdImpl(value);
    }

    /// <summary>
    /// 由 String 裸值构造 <see cref="IBinTypeId"/> / Build an <see cref="IBinTypeId"/> from a raw string value.
    /// </summary>
    /// <param name="value">原始字符串标识符 / Raw string identifier.</param>
    /// <returns>包装给定值的 id / An id wrapping the given value.</returns>
    public static IBinTypeId BinTypeIdOf(string value) {
        ArgumentNullException.ThrowIfNull(value);
        return new BinTypeIdImpl(value);
    }
}
