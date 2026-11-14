#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
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

    /// <summary>
    /// 创建外层包装程序 / Create an outer packing program.
    /// </summary>
    /// <param name="shape">外层包装形状 / The outer package shape.</param>
    /// <param name="packages">子包装程序列表 / The child packing programs.</param>
    /// <returns>外层包装程序或失败 / The outer packing program or a failure.</returns>
    /// <remarks>
    /// **外层自己不装材料，它的材料表是子包装的合并结果**：数量相加，重量按量纲安全相加。
    /// **合并失败要向上报**：两个子包装的重量量纲不兼容时静默丢掉一侧，会让"总重"看起来正常却少了一块。
    ///
    /// **An outer program carries no material of its own; its map is the merge of its children**: amounts add and weights add dimension-safely. **A merge failure must
    /// propagate**: silently dropping one side of an incompatible weight pair would make the total look plausible while missing a piece.
    /// </remarks>
    public static Result<PackingProgram, ErrorCode, Error<ErrorCode>> OuterPackage(
        PackageShape shape,
        IReadOnlyList<PackingProgram> packages) {
        var materials = new Dictionary<string, PackingProgramMaterialValue>();
        foreach (PackingProgram package in packages) {
            if (package.Materials is null) {
                continue;
            }
            foreach (KeyValuePair<string, PackingProgramMaterialValue> entry in package.Materials) {
                materials.TryGetValue(entry.Key, out PackingProgramMaterialValue? existing);
                Result<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> merged =
                    MergeMaterialValue(existing, entry.Value);
                if (merged is Failed<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> failed) {
                    return Results.Failed<PackingProgram>(failed.Error);
                }
                if (merged is Fatal<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> fatal) {
                    return new Fatal<PackingProgram, ErrorCode, Error<ErrorCode>>(fatal.Errors);
                }
                materials[entry.Key] = ((Ok<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>>)merged).Value;
            }
        }
        return Results.Ok(new PackingProgram(shape, null, packages, materials));
    }

    /// <summary>
    /// 创建内层包装程序 / Create an inner packing program.
    /// </summary>
    /// <param name="shape">内层包装形状 / The inner package shape.</param>
    /// <param name="materials">物料键到数量的映射 / The material key to amount map.</param>
    /// <returns>内层包装程序 / The inner packing program.</returns>
    public static PackingProgram InnerPackage(
        PackageShape shape,
        IReadOnlyDictionary<string, ulong> materials) {
        var values = new Dictionary<string, PackingProgramMaterialValue>();
        foreach (KeyValuePair<string, ulong> entry in materials) {
            values[entry.Key] = new PackingProgramMaterialValue(entry.Value);
        }
        return new PackingProgram(shape, null, null, values);
    }

    /// <summary>
    /// 合并两个材料贡献值 / Merge two material contribution values.
    /// </summary>
    /// <param name="lhs">已有值，可为空 / The existing value, possibly null.</param>
    /// <param name="rhs">新值 / The new value.</param>
    /// <returns>合并结果或失败 / The merged value or a failure.</returns>
    /// <remarks>
    /// **任一侧缺数量时取另一侧，而不是当成 0**：缺值是"没写"，不是"写了零"。
    /// **重量相加失败必须报错**：`千克 + 米` 不是重量，静默取一侧会让总重凭空变化。
    ///
    /// **A missing amount takes the other side rather than counting as zero**: absent means "not stated", not "stated as zero". **A failed weight sum must surface**:
    /// `kg + m` is not a weight, and silently taking one side makes the total change for no reason.
    /// </remarks>
    public static Result<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> MergeMaterialValue(
        PackingProgramMaterialValue? lhs,
        PackingProgramMaterialValue rhs) {
        ulong? amount = lhs?.Amount is null
            ? rhs.Amount
            : rhs.Amount is null ? lhs.Amount : lhs.Amount + rhs.Amount;

        Quantity<FltX>? weight;
        if (lhs?.Weight is null) {
            weight = rhs.Weight;
        } else if (rhs.Weight is null) {
            weight = lhs.Weight;
        } else if (QuantityOperators.Add<FltX>(lhs.Weight, rhs.Weight) is
            Ok<Quantity<FltX>, ErrorCode, Error<ErrorCode>> sumOk) {
            weight = sumOk.Value;
        } else {
            return Results.Failed<PackingProgramMaterialValue>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "包装程序重量量纲不兼容 / Incompatible packing program weight dimensions"));
        }
        return Results.Ok(new PackingProgramMaterialValue(amount, weight));
    }
}
