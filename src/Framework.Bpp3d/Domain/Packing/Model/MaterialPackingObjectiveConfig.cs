#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;

/// <summary>
/// 物料装箱目标权重配置 / Material packing objective weights.
/// 控制 MIP 目标函数中各项的权重 / Controls weights in the MIP objective function.
/// </summary>
public sealed record MaterialPackingObjectiveConfig(
    FltX? PackageCountWeight = null,
    FltX? VolumeWeight = null,
    FltX? SlackWeight = null);
