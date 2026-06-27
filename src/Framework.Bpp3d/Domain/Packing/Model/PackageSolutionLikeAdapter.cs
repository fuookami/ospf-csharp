#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;

/// <summary>
/// 包装解决方案数量类型 / Package solution quantity type.
/// Sealed hierarchy for amount, weight, or both.
/// </summary>
public abstract record PackageSolutionLikeQuantity {
    /// <summary>仅数量 / Amount only.</summary>
    public sealed record Amount(UInt64 Value) : PackageSolutionLikeQuantity;

    /// <summary>仅重量 / Weight only.</summary>
    public sealed record Weight(Flt64 Value) : PackageSolutionLikeQuantity;

    /// <summary>数量和重量 / Amount and weight.</summary>
    public sealed record AmountAndWeight(UInt64 AmountValue, Flt64 WeightValue) : PackageSolutionLikeQuantity;
}

/// <summary>
/// 包装解决方案物料项 / Package solution material item.
/// </summary>
/// <param name="Material">物料键 / Material key</param>
/// <param name="Quantity">数量 / Quantity</param>
public sealed record PackageSolutionLikeMaterialItem(
    MaterialKey Material,
    PackageSolutionLikeQuantity Quantity);

/// <summary>
/// 包装解决方案节点 / Package solution node.
/// Recursive tree structure representing a packing program.
/// </summary>
/// <param name="Shape">包装形状 / Package shape</param>
/// <param name="MaterialItems">物料项列表 / Material items</param>
/// <param name="Children">子节点列表 / Child nodes</param>
public sealed record PackageSolutionLikeNode(
    PackageShape? Shape = null,
    IReadOnlyList<PackageSolutionLikeMaterialItem>? MaterialItems = null,
    IReadOnlyList<PackageSolutionLikeNode>? Children = null);
