#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model;

/// <summary>
/// 层分配标量别名 / Layer assignment scalar aliases.
/// Provides zero/one constants and scalar provider for the layer-assignment domain.
/// </summary>
public static class LayerAssignmentAliases {
    /// <summary>标量零 / Scalar zero.</summary>
    public static FltX Zero() => FltX.Zero;

    /// <summary>标量一 / Scalar one.</summary>
    public static FltX One() => FltX.One;

    /// <summary>标量提供者 / Scalar provider.</summary>
    public static FltX ScalarProvider() => FltX.Zero;
}
