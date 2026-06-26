#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 包装形状规格（密封接口）/ Package shape specification (sealed interface).
/// </summary>
public abstract record PackageShapeSpec {
    /// <summary>长方体 / Cuboid.</summary>
    public sealed record Cuboid : PackageShapeSpec;

    /// <summary>垂直圆柱 / Vertical cylinder.</summary>
    public sealed record VerticalCylinder(
        Quantity<FltX> Radius,
        Axis3 Axis = Axis3.Y,
        IReadOnlyList<Quantity<FltX>>? RadiusCandidates = null,
        Quantity<FltX>? RadiusMin = null,
        Quantity<FltX>? RadiusMax = null) : PackageShapeSpec;
}

/// <summary>
/// 包装底面形状 / Package bottom shape.
/// </summary>
public sealed record PackageBottomShape(
    Quantity<FltX> Width,
    Quantity<FltX> Depth,
    Quantity<FltX> Weight,
    PackageType PackageType) {
    /// <summary>底面面积 / Area.</summary>
    public Quantity<FltX> Area => Width.Multiply(Depth);
}

/// <summary>
/// 包装形状 / Package shape.
/// 包装的完整三维形状描述。
/// </summary>
public sealed record PackageShape(
    Quantity<FltX> Width,
    Quantity<FltX> Height,
    Quantity<FltX> Depth,
    Quantity<FltX> Weight,
    PackageType PackageType,
    PackageShapeSpec? ShapeSpec = null) {
    /// <summary>底面形状 / Bottom shape.</summary>
    public PackageBottomShape BottomShape => new(Width, Depth, Weight, PackageType);

    /// <summary>体积 / Volume.</summary>
    public Quantity<FltX> Volume => Width.Multiply(Height).Multiply(Depth);
}

/// <summary>
/// 装箱程序材料值 / Packing program material value.
/// </summary>
public sealed record PackingProgramMaterialValue(
    ulong? Amount = null,
    Quantity<FltX>? Weight = null);

/// <summary>
/// 装箱程序 / Packing program.
/// 描述包装的层次结构和材料组成。
/// Describes the hierarchical structure and material composition of packaging.
/// </summary>
public sealed record PackingProgram(
    PackageShape Shape,
    string? Pattern,
    IReadOnlyList<PackingProgram>? Packages,
    IReadOnlyDictionary<string, PackingProgramMaterialValue>? Materials) {
    /// <summary>包装类别 / Classification.</summary>
    public PackageCategory Classification => PackageType switch {
        PackageType.Box or PackageType.Pallet => PackageCategory.Standard,
        PackageType.Barrel or PackageType.Roll => PackageCategory.Cylindrical,
        _ => PackageCategory.Irregular
    };

    /// <summary>包装类型 / Package type.</summary>
    public PackageType PackageType => Shape.PackageType;

    /// <summary>物料总量 / Material amount.</summary>
    public ulong MaterialAmount() {
        if (Materials is null) return 0;
        ulong total = 0;
        foreach (PackingProgramMaterialValue v in Materials.Values) {
            if (v.Amount.HasValue) total += v.Amount.Value;
        }
        return total;
    }
}
