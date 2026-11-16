#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;
// ===== Core infrastructure types =====

/// <summary>三维容器形状 / 3D container shape.</summary>
public sealed record Container3Shape<V>(Quantity<V> Width, Quantity<V> Height, Quantity<V> Depth)
    where V : struct, IFloatingNumber<V>;

/// <summary>量纲三维点 / Quantity 3D point.</summary>
public sealed record QuantityPoint3<V>(Quantity<V> X, Quantity<V> Y, Quantity<V> Z)
    where V : struct, IFloatingNumber<V>;

/// <summary>三维放置 / 3D placement.</summary>
public sealed record QuantityPlacement3<T, V>(T Unit, QuantityPoint3<V> Position, Orientation Orientation = Orientation.Upright)
    where V : struct, IFloatingNumber<V> {
    public Quantity<V> Z => Position.Z;
    public Quantity<V> X => Position.X;
    public Quantity<V> Y => Position.Y;

    /// <summary>
    /// 解析该放置的真实装箱形状 / Resolve the real packing shape of this placement.
    /// </summary>
    /// <remarks>
    /// **原先这里返回的是 `new { Axis = (Axis3?)null }`** —— 一个"形状"占位对象：既没有尺寸也没有半径，
    /// 于是任何基于它算包围盒的代码都只能得到零尺寸。现在改为**向货物要形状**，货物没有形状时返回 `null`，
    /// 由调用方显式处理"没有几何"这件事。
    ///
    /// **This used to return `new { Axis = (Axis3?)null }`** — a placeholder "shape" with neither extents nor a radius, so any bounding box derived
    /// from it could only be zero-sized. It now **asks the item for its shape** and returns `null` when the item carries none, leaving the caller to
    /// handle "no geometry" explicitly.
    /// </remarks>
    /// <returns>装箱形状，或 null / The packing shape, or null.</returns>
    public IPackingShape3<FltX>? ResolvedPackingShape() => Unit is Item item ? item.Shape : null;
}

/// <summary>批次号 / Batch number.</summary>
public sealed record BatchNo(string Value);

/// <summary>
/// 量纲三维向量 / A 3D quantity vector.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="X">x 分量 / The x component.</param>
/// <param name="Y">y 分量 / The y component.</param>
/// <param name="Z">z 分量 / The z component.</param>
/// <remarks>
/// 与 `QuantityPoint3` 的区别是**语义**：点是位置，向量是位移。两者字段一样，但把点当向量相加在几何上没有意义，
/// 所以两个独立类型隔开——这里保持同样的区分。
///
/// The difference from `QuantityPoint3` is **semantics**: a point is a location, a vector is a displacement. They share their fields, but adding two points is not a
/// geometric operation, so the code keeps two distinct types — the same distinction is kept here.
/// </remarks>
public sealed record QuantityVector3<V>(Quantity<V> X, Quantity<V> Y, Quantity<V> Z)
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 向量加法 / Vector addition.
    /// </summary>
    /// <param name="rhs">右操作数 / The right-hand operand.</param>
    /// <returns>加法结果或量纲错误 / The sum, or a dimension error.</returns>
    /// <remarks>
    /// **任一分量量纲不符，整体失败**：只报错一个分量而把其余分量照样加出来，会得到一个部分正确、部分错误的向量，
    /// 而它在后续的包围盒计算里看起来完全合法。
    ///
    /// **One dimension mismatch fails the whole vector**: reporting one component and still adding the others yields a partly correct, partly wrong vector that then
    /// looks entirely legal inside any later bounding-box computation.
    /// </remarks>
    public Result<QuantityVector3<V>, ErrorCode, Error<ErrorCode>> Add(QuantityVector3<V> rhs) {
        ArgumentNullException.ThrowIfNull(rhs);
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> x = QuantityOperators.Add(X, rhs.X);
        if (x is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> xOk) {
            return Propagate(x);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> y = QuantityOperators.Add(Y, rhs.Y);
        if (y is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> yOk) {
            return Propagate(y);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> z = QuantityOperators.Add(Z, rhs.Z);
        if (z is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> zOk) {
            return Propagate(z);
        }
        return Results.Ok(new QuantityVector3<V>(xOk.Value, yOk.Value, zOk.Value));
    }

    /// <summary>
    /// 向量减法 / Vector subtraction.
    /// </summary>
    /// <param name="rhs">右操作数 / The right-hand operand.</param>
    /// <returns>减法结果或量纲错误 / The difference, or a dimension error.</returns>
    public Result<QuantityVector3<V>, ErrorCode, Error<ErrorCode>> Subtract(QuantityVector3<V> rhs) {
        ArgumentNullException.ThrowIfNull(rhs);
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> x = QuantityOperators.Subtract(X, rhs.X);
        if (x is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> xOk) {
            return Propagate(x);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> y = QuantityOperators.Subtract(Y, rhs.Y);
        if (y is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> yOk) {
            return Propagate(y);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> z = QuantityOperators.Subtract(Z, rhs.Z);
        if (z is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> zOk) {
            return Propagate(z);
        }
        return Results.Ok(new QuantityVector3<V>(xOk.Value, yOk.Value, zOk.Value));
    }

    /// <summary>把分量失败原样升级为向量失败 / Lift a component failure to a vector failure.</summary>
    /// <param name="component">分量结果 / The component result.</param>
    /// <returns>向量结果 / The vector result.</returns>
    private static Result<QuantityVector3<V>, ErrorCode, Error<ErrorCode>> Propagate(
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> component) => component switch {
            Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<QuantityVector3<V>>(failed.Error),
            Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<QuantityVector3<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => throw new System.InvalidOperationException("a non-Ok component must be Failed or Fatal"),
        };
}

/// <summary>层 BinLayer / Layer bin layer.</summary>
public sealed class BinLayer {
    public long Iteration { get; init; }
    public Type? From { get; init; }
    public Domain.Item.Model.BinType<FltX>? Bin { get; init; }
    public Container3Shape<FltX> Shape { get; init; } = null!;
    public IReadOnlyList<QuantityPlacement3<Item, FltX>> Units { get; init; } = Array.Empty<QuantityPlacement3<Item, FltX>>();
    public Quantity<FltX> Depth => Shape.Depth;
    public BinLayer Copy() => new() { Iteration = Iteration, From = From, Bin = Bin, Shape = Shape, Units = Units };
    public override bool Equals(object? obj) => obj is BinLayer other && Shape == other.Shape;
    public override int GetHashCode() => Shape?.GetHashCode() ?? 0;
}

/// <summary>货物（非泛型）/ Item (non-generic).</summary>
/// <param name="Id">货物编号 / Item identifier.</param>
/// <param name="Name">货物名称 / Item name.</param>
/// <param name="Shape">
/// 货物装载形状；null 表示该货物没有几何信息 / The item's packing shape; null means the item carries no geometry.
/// </param>
/// <param name="Weight">
/// 货物重量；null 表示该货物没有重量信息 / The item's weight; null means the item carries no weight.
/// </param>
public sealed record Item(
    string Id,
    string Name,
    IPackingShape3<FltX>? Shape = null,
    Quantity<FltX>? Weight = null);

/// <summary>箱体 / Bin.</summary>
public sealed record Bin<TLayer, V>(Domain.Item.Model.BinType<V> Type, IReadOnlyList<QuantityPlacement3<TLayer, V>> Units, BatchNo? BatchNo = null)
    where V : struct, IFloatingNumber<V> {
    public Container3Shape<V> Shape => new(Type.Width, Type.Height, Type.Depth);
}

// ===== Demand mode / key / entry =====

/// <summary>需求模式 / Demand mode.</summary>
public abstract record Bpp3dDemandMode {
    public sealed record ItemMode : Bpp3dDemandMode;
    public sealed record MaterialMode : Bpp3dDemandMode;
    public sealed record ItemAmount : Bpp3dDemandMode;
    public sealed record ItemWeight : Bpp3dDemandMode;
    public sealed record ItemMaterialAmount : Bpp3dDemandMode;
    public sealed record ItemMaterialWeight : Bpp3dDemandMode;
}

/// <summary>需求键 / Demand key.</summary>
public abstract record Bpp3dDemandKey {
    public sealed record ItemKey(string ItemId) : Bpp3dDemandKey;
    public sealed record MaterialKeyRef(Domain.Item.Model.MaterialKey MaterialKeyValue) : Bpp3dDemandKey;
}

/// <summary>需求条目 / Demand entry.</summary>
public sealed record Bpp3dDemandEntry<V>(Bpp3dDemandMode Mode, Bpp3dDemandKey Key, V Demand, object? DemandRange = null)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>需求模式键 / Demand mode key.</summary>
public sealed record DemandModeKey(Bpp3dDemandMode Mode, Bpp3dDemandKey Key, string? QuantityUnit = null);

/// <summary>层生成需求条目 / Layer generation demand entry.</summary>
public sealed record LayerGenerationDemandEntry(Bpp3dDemandMode Mode, Bpp3dDemandKey Key, string? QuantityUnit = null);

// ===== Layer generation =====

/// <summary>层生成请求 / Layer generation request.</summary>
public sealed record Bpp3dLayerGenerationRequest<V>(
    int Iteration,
    IReadOnlyList<object> Items,
    IReadOnlyList<BinLayer> ExistingLayers,
    IReadOnlyDictionary<DemandModeKey, V>? ShadowPrices = null,
    int MaxCandidates = 64,
    Domain.Item.Model.BinType<FltX>? Bin = null,
    IReadOnlyList<LayerGenerationDemandEntry>? DemandEntries = null,
    Func<BinLayer, Bpp3dLayerGenerationRequest<V>, double>? ScoreByShadowPrice = null)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>层生成结果 / Layer generation result.</summary>
public sealed record Bpp3dLayerGenerationResult<V>(
    BinLayer Layer,
    V ReducedCost,
    string Source = "",
    double? Score = null)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>层生成器接口 / Layer generator interface.</summary>
public interface IBpp3dLayerGenerator<V> where V : struct, IRealNumber<V>, INumberField<V> {
    System.Threading.Tasks.Task<IReadOnlyList<Bpp3dLayerGenerationResult<V>>> GenerateAsync(Bpp3dLayerGenerationRequest<V> request);
}

// ===== Continuous radius =====

/// <summary>连续圆柱半径 solver 原型 / Continuous cylinder radius solver prototype.</summary>
public sealed record ContinuousCylinderRadiusSolverPrototype(
    string Source, string VariableName, Axis3 Axis,
    Quantity<FltX>? InitialRadius = null, Quantity<FltX>? RadiusMin = null, Quantity<FltX>? RadiusMax = null,
    string? RadiusWeightFunctionKey = null, bool IsPWLRegisterable = false) {
    public static ContinuousCylinderRadiusSolverPrototype Empty { get; } = new("", "", Axis3.Y);
}

// ===== Default layer generators =====

public sealed class BlockLayerGenerator : IBpp3dLayerGenerator<FltX> {
    public System.Threading.Tasks.Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(Bpp3dLayerGenerationRequest<FltX> r)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(Array.Empty<Bpp3dLayerGenerationResult<FltX>>());
}
public sealed class BLLocalLayerGenerator : IBpp3dLayerGenerator<FltX> {
    public System.Threading.Tasks.Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(Bpp3dLayerGenerationRequest<FltX> r)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(Array.Empty<Bpp3dLayerGenerationResult<FltX>>());
}
public sealed class BLGlobalLayerGenerator : IBpp3dLayerGenerator<FltX> {
    public System.Threading.Tasks.Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(Bpp3dLayerGenerationRequest<FltX> r)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(Array.Empty<Bpp3dLayerGenerationResult<FltX>>());
}
public sealed class PatternLayerGenerator : IBpp3dLayerGenerator<FltX> {
    public System.Threading.Tasks.Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(Bpp3dLayerGenerationRequest<FltX> r)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(Array.Empty<Bpp3dLayerGenerationResult<FltX>>());
}
public sealed class PileLayerGenerator : IBpp3dLayerGenerator<FltX> {
    public System.Threading.Tasks.Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(Bpp3dLayerGenerationRequest<FltX> r)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(Array.Empty<Bpp3dLayerGenerationResult<FltX>>());
}
public sealed class CirclePackingLayerGenerator : IBpp3dLayerGenerator<FltX> {
    public System.Threading.Tasks.Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(Bpp3dLayerGenerationRequest<FltX> r)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(Array.Empty<Bpp3dLayerGenerationResult<FltX>>());
}
public sealed class HistoricalLayerGenerator : IBpp3dLayerGenerator<FltX> {
    public System.Threading.Tasks.Task<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>> GenerateAsync(Bpp3dLayerGenerationRequest<FltX> r)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<Bpp3dLayerGenerationResult<FltX>>>(Array.Empty<Bpp3dLayerGenerationResult<FltX>>());
}

// ===== Helpers =====

public static class BinLayerHelpers {
    public static QuantityPoint3<FltX> Point3FltX(Quantity<FltX>? x = null, Quantity<FltX>? y = null, Quantity<FltX>? z = null) {
        var d = new Quantity<FltX>(FltX.Zero, SIBaseUnits.Meter);
        return new QuantityPoint3<FltX>(x ?? d, y ?? d, z ?? d);
    }

    public static Bin<TLayer, V> LayerBinOf<TLayer, V>(Domain.Item.Model.BinType<V> shape, IReadOnlyList<QuantityPlacement3<TLayer, V>> units, BatchNo? batchNo = null)
        where V : struct, IFloatingNumber<V>
        => new(shape, units, batchNo);

    public static QuantityPlacement3<BinLayer, FltX> BinLayerPlacementOf(BinLayer view, QuantityPoint3<FltX> position, Orientation orientation = Orientation.Upright)
        => new(view, position, orientation);
}

public static class ContinuousRadiusHelpers {
    public static string Source(object item) => item is Item i ? $"item-{i.Id}" : "unknown";

    public static ContinuousCylinderRadiusSolverPrototype Prototype(
        string source, string? key = null, Axis3 axis = Axis3.Y,
        Quantity<FltX>? min = null, Quantity<FltX>? max = null)
        => new(source, $"cr_{source}_{axis}", axis, RadiusMin: min, RadiusMax: max, RadiusWeightFunctionKey: key, IsPWLRegisterable: key != null);
}
