// 二维装箱错误类型与几何契约消息 / 2D packing error types and geometry contract messages.
// 对应：用语义化错误子类型替代裸 `ErrorCode + message`，几何违规整理成可检索的中英双语消息。
// Semantic error subtypes replace bare `ErrorCode + message`, and geometry violations
// are rendered as searchable bilingual messages.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 二维装箱校验错误：输入数据、配置或几何关系不合法 / 2D packing validation error.
/// </summary>
/// <param name="Detail">错误详情，为空时使用默认消息 / Detail, defaulting when null.</param>
public sealed record Bpp2dValidationError(string? Detail = null)
    : Err<ErrorCode>(ErrorCode.IllegalArgument, Detail ?? "BPP2D validation error.");

/// <summary>
/// 二维装箱求解错误：模型注册、求解或结果提取失败 / 2D packing solving error.
/// </summary>
/// <param name="Detail">错误详情，为空时使用默认消息 / Detail, defaulting when null.</param>
public sealed record Bpp2dSolvingError(string? Detail = null)
    : Err<ErrorCode>(ErrorCode.ApplicationFailed, Detail ?? "BPP2D solving error.");

/// <summary>
/// 二维装箱能力不支持错误：请求了当前路径未覆盖的形状或排样方式 / 2D packing capability error.
/// </summary>
/// <param name="Capability">不支持的能力 / The unsupported capability.</param>
public sealed record Bpp2dCapabilityError(string? Capability = null)
    : Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        Capability is null
            ? "Capability not supported."
            : string.Format(
                CultureInfo.InvariantCulture,
                "Capability not supported: {0}",
                Capability));

/// <summary>
/// 二维装箱内部错误：不应到达的分支或状态 / 2D packing internal error.
/// </summary>
/// <param name="Detail">错误详情，为空时使用默认消息 / Detail, defaulting when null.</param>
public sealed record Bpp2dInternalError(string? Detail = null)
    : Err<ErrorCode>(ErrorCode.ApplicationError, Detail ?? "BPP2D internal error.");

/// <summary>
/// 装箱几何契约消息 / Packing geometry contract messages.
/// </summary>
/// <remarks>
/// 所有消息统一采用 <c>Unsupported placement geometry in $source: type=..., bin=..., item[i] ...</c> 结构，
/// 便于按 <c>type</c> 与 <c>bin</c> 检索定位问题；同时保留中文前缀，读日志的人不用先翻译。
/// Every message follows the <c>Unsupported placement geometry in $source: type=..., bin=..., item[i] ...</c>
/// shape so problems can be located by <c>type</c> and <c>bin</c>, while keeping the Chinese prefix so whoever reads
/// the log does not have to translate first.
/// </remarks>
public static class PackingGeometryContract {
    /// <summary>
    /// 生成单个放置的形状诊断信息 / Build the shape diagnostic of one placement.
    /// </summary>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <param name="rotated">是否旋转 90 度 / Whether rotated by 90 degrees.</param>
    /// <returns>形状诊断信息 / Shape diagnostic.</returns>
    public static string PlacementShapeDiagnostic(PackingShape2<Math.Algebra.Number.FltX> shape, bool rotated) {
        ArgumentNullException.ThrowIfNull(shape);
        string typeText = shape.ShapeType switch {
            PackingShape2Type.Rectangle => "rectangle",
            _ => "circle",
        };
        return string.Format(
            CultureInfo.InvariantCulture,
            "形状 shape={0}, rotated={1}",
            typeText,
            rotated ? "true" : "false");
    }

    /// <summary>
    /// 生成形状构成诊断信息，用于混排矩形件与圆形件时排查 / Build the shape-composition diagnostic.
    /// </summary>
    /// <param name="counts">各形状类型的件数 / Amount per shape type.</param>
    /// <returns>形状构成诊断信息 / Shape composition diagnostic.</returns>
    public static string MixedShapeDiagnostic(
        IReadOnlyDictionary<PackingShape2Type, int> counts) {
        ArgumentNullException.ThrowIfNull(counts);
        string text = string.Join(
            ", ",
            counts.OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal)
                .Select(entry => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}={1}",
                    entry.Key.ToString().ToLowerInvariant(),
                    entry.Value)));
        return string.Format(CultureInfo.InvariantCulture, "形状构成 mixed shapes [{0}]", text);
    }

    /// <summary>
    /// 放置越过板材边界的错误信息 / Outside-sheet placement violation message.
    /// </summary>
    /// <param name="source">调用来源 / Call source.</param>
    /// <param name="sheetName">板材实例名称，即消息中的 <c>bin</c> / Sheet instance name.</param>
    /// <param name="itemIndex">物品序号 / Item index.</param>
    /// <param name="diagnostic">形状诊断信息 / Shape diagnostic.</param>
    /// <param name="mixedDiagnostic">形状构成诊断信息 / Shape composition diagnostic.</param>
    /// <returns>错误信息 / Error message.</returns>
    public static string UnsupportedOutsideSheetGeometryMessage(
        string source,
        string sheetName,
        int itemIndex,
        string diagnostic,
        string mixedDiagnostic) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "放置越过板材边界 / Placement outside sheet: Unsupported placement geometry in {0}: "
            + "type=outside_sheet, bin={1}, item[{2}] {3} is outside sheet; {4}.",
            source, sheetName, itemIndex, diagnostic, mixedDiagnostic);

    /// <summary>
    /// 放置相互重叠的错误信息 / Placement overlap violation message.
    /// </summary>
    /// <param name="source">调用来源 / Call source.</param>
    /// <param name="sheetName">板材实例名称 / Sheet instance name.</param>
    /// <param name="lhsIndex">左侧物品序号 / Left item index.</param>
    /// <param name="lhsDiagnostic">左侧形状诊断信息 / Left shape diagnostic.</param>
    /// <param name="rhsIndex">右侧物品序号 / Right item index.</param>
    /// <param name="rhsDiagnostic">右侧形状诊断信息 / Right shape diagnostic.</param>
    /// <param name="mixedDiagnostic">形状构成诊断信息 / Shape composition diagnostic.</param>
    /// <returns>错误信息 / Error message.</returns>
    public static string UnsupportedPlacementOverlapMessage(
        string source,
        string sheetName,
        int lhsIndex,
        string lhsDiagnostic,
        int rhsIndex,
        string rhsDiagnostic,
        string mixedDiagnostic) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "放置相互重叠 / Placement overlap: Unsupported placement geometry in {0}: "
            + "type=overlap, bin={1}, item[{2}] {3} overlaps item[{4}] {5}; {6}.",
            source, sheetName, lhsIndex, lhsDiagnostic, rhsIndex, rhsDiagnostic, mixedDiagnostic);

    /// <summary>
    /// 放置非法旋转的错误信息 / Illegal rotation violation message.
    /// </summary>
    /// <param name="source">调用来源 / Call source.</param>
    /// <param name="sheetName">板材实例名称 / Sheet instance name.</param>
    /// <param name="itemIndex">物品序号 / Item index.</param>
    /// <param name="diagnostic">形状诊断信息 / Shape diagnostic.</param>
    /// <param name="itemId">物品编号，未知时为 null / Item identifier, null when unknown.</param>
    /// <param name="mixedDiagnostic">形状构成诊断信息 / Shape composition diagnostic.</param>
    /// <returns>错误信息 / Error message.</returns>
    public static string UnsupportedIllegalRotationMessage(
        string source,
        string sheetName,
        int itemIndex,
        string diagnostic,
        string? itemId,
        string mixedDiagnostic) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "放置非法旋转 / Illegal placement rotation: Unsupported placement geometry in {0}: "
            + "type=illegal_rotation, bin={1}, item[{2}] {3} (item={4}) is rotated although the item forbids "
            + "rotation; {5}.",
            source, sheetName, itemIndex, diagnostic, itemId ?? "unknown", mixedDiagnostic);
}
