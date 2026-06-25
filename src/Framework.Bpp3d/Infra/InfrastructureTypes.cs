#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
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
    public object ResolvedPackingShape() => new { Axis = (Axis3?)null };
}

/// <summary>批次号 / Batch number.</summary>
public sealed record BatchNo(string Value);

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
public sealed record Item(string Id, string Name);

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
    object? ScoreByShadowPrice = null)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>层生成结果 / Layer generation result.</summary>
public sealed record Bpp3dLayerGenerationResult<V>(BinLayer Layer, V ReducedCost, string Source = "")
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
