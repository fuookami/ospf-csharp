#nullable enable

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;

/// <summary>
/// 装箱求解信息 / Packing solve info.
/// </summary>
/// <param name="Status">求解状态 / Solve status</param>
/// <param name="RawStatus">原始状态字符串 / Raw status string</param>
public sealed record PackingSolveInfo(PackingStatus Status, string? RawStatus = null);
