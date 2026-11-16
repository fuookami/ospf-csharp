// 渲染用 DTO / Render DTOs.

using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp2d.Infrastructure.Dto;

/// <summary>
/// 渲染刀线描述 / Render cut line description.
/// </summary>
/// <param name="Orientation">刀线方向，`vertical` 或 `horizontal` / The cut line orientation.</param>
/// <param name="Position">刀缝起始坐标 / The coordinate where the kerf strip starts.</param>
/// <param name="Stage">工序级数 / The process stage.</param>
/// <param name="StageLabel">工序标签 / The process stage label.</param>
/// <remarks>
/// 方向用**字符串**而不是枚举：这一层是给渲染与前端用的，**枚举值改名不应该顺带改前端契约**。
///
/// The orientation is a **string** rather than an enum: this layer feeds renderers and front ends, and **renaming an enum member must not silently change the front-end
/// contract**.
/// </remarks>
public sealed record RenderCutLine2Dto(string Orientation, double Position, int Stage, string StageLabel);

/// <summary>
/// 渲染裁切件描述 / Render cut piece description.
/// </summary>
/// <param name="PieceId">成品件标识，余料为空 / The finished part identifier; null for a remnant.</param>
/// <param name="X">左下角 x 坐标 / The lower-left x coordinate.</param>
/// <param name="Y">左下角 y 坐标 / The lower-left y coordinate.</param>
/// <param name="Width">宽度 / The width.</param>
/// <param name="Height">高度 / The height.</param>
/// <param name="Area">面积 / The area.</param>
/// <param name="Rotated">是否旋转 90° / Whether it is rotated 90 degrees.</param>
public sealed record RenderCutPiece2Dto(
    string? PieceId, double X, double Y, double Width, double Height, double Area, bool Rotated);

/// <summary>
/// 渲染裁切图案描述：一张板材（或一段卷材）上的排样与刀序 / Render cutting pattern description: the layout and cut sequence on one sheet.
/// </summary>
/// <param name="Id">图案编号 / The pattern identifier.</param>
/// <param name="SheetTypeCode">板材类型编码 / The sheet type code.</param>
/// <param name="Width">宽度 / The width.</param>
/// <param name="Height">高度 / The height.</param>
/// <param name="Area">面积 / The area.</param>
/// <param name="UsedArea">成品占用面积 / The area occupied by finished parts.</param>
/// <param name="WasteArea">余料面积 / The remnant area.</param>
/// <param name="Utilization">利用率 / The utilization.</param>
/// <param name="Process">裁切工艺 / The cutting process.</param>
/// <param name="Pieces">裁切件列表 / The cut piece list.</param>
/// <param name="CutLines">刀线序列，为空即空表 / The cut sequence; null means an empty list.</param>
public sealed record RenderCuttingPattern2Dto(
    string Id,
    string SheetTypeCode,
    double Width,
    double Height,
    double Area,
    double UsedArea,
    double WasteArea,
    double Utilization,
    string Process,
    IReadOnlyList<RenderCutPiece2Dto> Pieces,
    IReadOnlyList<RenderCutLine2Dto>? CutLines = null) {
    /// <summary>刀序，为空时返回空表而不是 null / The cut sequence, returning an empty list instead of null.</summary>
    /// <remarks>
    /// **任意排样没有刀序**，而渲染层不该为"这一项可能是 null"多写一个分支。
    ///
    /// **Arbitrary nesting has no cut sequence**, and the render layer should not carry an extra branch for "this might be null".
    /// </remarks>
    public IReadOnlyList<RenderCutLine2Dto> Sequence => CutLines ?? new List<RenderCutLine2Dto>();
}

/// <summary>
/// 渲染裁切方案描述：一个方案是若干图案按次数的组合 / Render cutting plan description: a plan is a combination of patterns with usage counts.
/// </summary>
/// <param name="Id">方案编号 / The plan identifier.</param>
/// <param name="MaterialCode">物料编码 / The material code.</param>
/// <param name="Amount">方案使用次数 / The plan usage count.</param>
/// <param name="Patterns">图案列表 / The pattern list.</param>
/// <param name="WasteArea">方案总余料面积 / The total remnant area of the plan.</param>
/// <param name="Utilization">方案利用率 / The plan utilization.</param>
public sealed record RenderCuttingPlan2Dto(
    string Id,
    string MaterialCode,
    long Amount,
    IReadOnlyList<RenderCuttingPattern2Dto> Patterns,
    double WasteArea,
    double Utilization);

/// <summary>
/// 渲染顶层结构 / The top-level render schema.
/// </summary>
/// <param name="Plans">裁切方案列表 / The cutting plan list.</param>
/// <param name="Kpi">关键指标，为空即空表 / The key indicators; null means an empty map.</param>
public sealed record RenderCuttingSchema2Dto(
    IReadOnlyList<RenderCuttingPlan2Dto> Plans,
    IReadOnlyDictionary<string, string>? Kpi = null) {
    /// <summary>关键指标，为空时返回空表而不是 null / The key indicators, returning an empty map instead of null.</summary>
    public IReadOnlyDictionary<string, string> Indicators =>
        Kpi ?? new Dictionary<string, string>();
}
