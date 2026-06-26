#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 货物容器接口 / Item container interface.
/// 包含货物放置的容器基类。
/// </summary>
public interface IItemContainer {
    /// <summary>所有货物放置 / All item placements.</summary>
    IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> Items { get; }
    /// <summary>包装类型 / Package type.</summary>
    PackageType PackageType { get; }
    /// <summary>包装类别 / Package category.</summary>
    PackageCategory PackageCategory { get; }
    /// <summary>仅底部放置 / Bottom only.</summary>
    bool BottomOnly { get; }
    /// <summary>顶部平整 / Top flat.</summary>
    bool TopFlat { get; }
}

/// <summary>
/// 货物容器基类 / Item container base class.
/// </summary>
public abstract class ItemContainerBase : IItemContainer {
    public IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> Units { get; }

    protected ItemContainerBase(IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> units) {
        Units = units;
    }

    /// <inheritdoc/>
    public IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> Items => Units;

    /// <inheritdoc/>
    public virtual PackageType PackageType => Units.Count > 0 ? Units[0].Unit.PackageType : PackageType.Box;

    /// <inheritdoc/>
    public virtual PackageCategory PackageCategory => PackageType switch {
        PackageType.Box or PackageType.Pallet => PackageCategory.Standard,
        PackageType.Barrel or PackageType.Roll => PackageCategory.Cylindrical,
        _ => PackageCategory.Irregular
    };

    /// <inheritdoc/>
    public virtual bool BottomOnly => Units.All(u => u.Unit.BottomOnly);

    /// <inheritdoc/>
    public virtual bool TopFlat => Units.All(u => u.Unit.TopFlat);
}
