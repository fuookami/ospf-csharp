#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;

/// <summary>
/// 终态装箱几何契约 / Final packing geometry contract.
/// Provides error message templates for geometry validation failures.
/// </summary>
internal static class PackingGeometryContract {
    /// <summary>
    /// 横向圆柱支撑不足错误信息 / Horizontal cylinder support violation message.
    /// </summary>
    /// <param name="source">调用来源 / Call source</param>
    /// <param name="binName">箱号 / Bin name</param>
    /// <param name="itemIndex">物品序号 / Item index</param>
    /// <param name="diagnostic">形状诊断信息 / Shape diagnostic</param>
    /// <returns>错误信息 / Error message</returns>
    public static string UnsupportedHorizontalCylinderSupportMessage(
        string source,
        string binName,
        int itemIndex,
        string diagnostic)
        => $"Unsupported placement geometry in {source}: type=horizontal_support, bin={binName}, item[{itemIndex}] {diagnostic} must be placed on bin floor or cuboid support coverage.";

    /// <summary>
    /// 越界放置错误信息 / Outside-bin placement violation message.
    /// </summary>
    /// <param name="source">调用来源 / Call source</param>
    /// <param name="binName">箱号 / Bin name</param>
    /// <param name="itemIndex">物品序号 / Item index</param>
    /// <param name="diagnostic">形状诊断信息 / Shape diagnostic</param>
    /// <returns>错误信息 / Error message</returns>
    public static string UnsupportedOutsideBinGeometryMessage(
        string source,
        string binName,
        int itemIndex,
        string diagnostic)
        => $"Unsupported placement geometry in {source}: type=outside_bin, bin={binName}, item[{itemIndex}] {diagnostic} is outside bin.";

    /// <summary>
    /// 终态放置重叠错误信息 / Final placement overlap violation message.
    /// </summary>
    /// <param name="source">调用来源 / Call source</param>
    /// <param name="binName">箱号 / Bin name</param>
    /// <param name="lhsIndex">左侧物品序号 / Left item index</param>
    /// <param name="lhsDiagnostic">左侧形状诊断信息 / Left shape diagnostic</param>
    /// <param name="rhsIndex">右侧物品序号 / Right item index</param>
    /// <param name="rhsDiagnostic">右侧形状诊断信息 / Right shape diagnostic</param>
    /// <returns>错误信息 / Error message</returns>
    public static string UnsupportedPlacementOverlapMessage(
        string source,
        string binName,
        int lhsIndex,
        string lhsDiagnostic,
        int rhsIndex,
        string rhsDiagnostic)
        => $"Unsupported placement geometry in {source}: type=overlap, bin={binName}, item[{lhsIndex}] {lhsDiagnostic} overlaps item[{rhsIndex}] {rhsDiagnostic}.";

    /// <summary>
    /// 单层圆柱轴向混用错误信息 / Single-layer cylinder axis mixing violation message.
    /// </summary>
    /// <param name="source">调用来源 / Call source</param>
    /// <param name="layerIndex">层序号 / Layer index</param>
    /// <param name="axes">轴向集合 / Axis set</param>
    /// <returns>错误信息 / Error message</returns>
    public static string UnsupportedMixedCylinderAxesInLayerMessage(
        string source,
        int layerIndex,
        IEnumerable<string> axes) {
        string axisText = string.Join(", ", axes.OrderBy(a => a));
        return $"Unsupported placement geometry in {source}: layer[{layerIndex}] mixes cylinder axes [{axisText}].";
    }
}
