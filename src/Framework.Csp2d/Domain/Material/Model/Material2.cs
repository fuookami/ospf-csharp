// 物料、设备与副产物 / Materials, machines and by-products.
// 与固定 Kotlin 基线 `csp2d/domain/material/model/Material2.kt` 对应（几何与校验部分见文件末尾说明）。
// Mirrors the pinned Kotlin baseline's `Material2.kt` (geometry and validation are scoped out; see the tail note).

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;

/// <summary>物料形态 / Material shape kind.</summary>
public enum Material2Kind {
    /// <summary>卷材：幅宽固定，长度待定 / A coil: fixed width, length to be determined.</summary>
    Coil,

    /// <summary>定尺板材：宽高固定 / A fixed-size sheet: both width and height fixed.</summary>
    Sheet,
}

/// <summary>
/// 物料 / A material.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">物料编号 / The material identifier.</param>
/// <param name="Name">物料名称 / The material name.</param>
/// <param name="Width">幅宽（卷材）或板宽（定尺） / Coil width or sheet width.</param>
/// <param name="Height">板高；卷材为空 / Sheet height; null for a coil.</param>
/// <param name="Length">卷长；空表示待推导 / Coil length; null means to be derived.</param>
/// <param name="UnitWeight">单位重量 / The unit weight.</param>
/// <param name="MachineId">所属设备 / The owning machine.</param>
/// <param name="AvailableBatches">可用卷数/张数 / The available coil or sheet count.</param>
/// <param name="MinRemnantArea">最小可用余料面积 / The minimum usable remnant area.</param>
/// <remarks>
/// **`height` 为空就是卷材**，没有额外的模式字段——基线就是靠这一个可空值区分条带排样与定尺排样。
/// 多一个枚举字段就有两个真值来源，而它们**迟早会不一致**。
///
/// **A null `height` *is* the coil mode**; there is no separate mode field — the baseline distinguishes strip packing from fixed-size packing by that one nullable value alone.
/// A second enum field would give two sources of truth, and they **eventually disagree**.
/// </remarks>
public sealed record Material2<V>(
    IMaterialId2 Id,
    string Name,
    Quantity<V> Width,
    Quantity<V>? Height = null,
    Quantity<V>? Length = null,
    Quantity<V>? UnitWeight = null,
    IMachineId2? MachineId = null,
    ulong AvailableBatches = ulong.MaxValue,
    Quantity<V>? MinRemnantArea = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>物料形态 / The material kind.</summary>
    public Material2Kind Kind => Height is null ? Material2Kind.Coil : Material2Kind.Sheet;

    /// <summary>是否为卷材 / Whether this is a coil.</summary>
    public bool IsCoil => Kind == Material2Kind.Coil;

    /// <summary>
    /// 判断某块余料是否仍可复用 / Decide whether a remnant is still reusable.
    /// </summary>
    /// <param name="remnantArea">余料面积 / The remnant area.</param>
    /// <returns>是否可用 / Whether it is usable.</returns>
    /// <remarks>
    /// 没有阈值时**要求面积严格为正**；有阈值时**必须不小于阈值**。
    /// **单位不可比时返回 `false`**——判不了就不说"能用"，**把不可比当成"够大"会白白留下不可用的余料**。
    ///
    /// Without a threshold the area **must be strictly positive**; with one it must **be at least the threshold**. **An incomparable unit yields `false`** — when it cannot
    /// be judged, do not claim "usable": **treating incomparability as "big enough" keeps unusable remnants alive**.
    /// </remarks>
    public bool IsUsableRemnant(Quantity<V> remnantArea) {
        if (MinRemnantArea is { } threshold) {
            return remnantArea.GreaterThanOrEqual(threshold) is true;
        }
        return remnantArea.Value.ToFlt64().ToDouble() > 0.0;
    }

    /// <summary>
    /// 校验物料几何与配置 / Validate the material geometry and configuration.
    /// </summary>
    /// <returns>校验结果 / The validation result.</returns>
    /// <remarks>
    /// **卷长给定是允许的**：它把问题退化成"按固定长度切段的定尺问题"。基线只留了注释没有拦——
    /// **能跑的组合就不该被当成错误**，语义差异写进文档即可。
    ///
    /// **A given coil length is allowed**: it degrades the problem into fixed-length segments. The baseline keeps only a comment rather than a rejection: **a
    /// combination that runs should not be reported as an error**, and the semantic difference belongs in the docs.
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (Width.Value.ToFlt64().ToDouble() <= 0.0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "物料 " + Id.Value + " 的幅宽必须为正 / The material width must be positive: " + Width));
        }
        if (Height is { } height && height.Value.ToFlt64().ToDouble() <= 0.0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "物料 " + Id.Value + " 的板高必须为正 / The material height must be positive: " + height));
        }
        if (Length is { } length && length.Value.ToFlt64().ToDouble() <= 0.0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "物料 " + Id.Value + " 的卷长必须为正 / The material length must be positive: " + length));
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 判断设备是否能加工本物料 / Decide whether a machine can process this material.
    /// </summary>
    /// <param name="machine">设备 / The machine.</param>
    /// <returns>是否可加工 / Whether it is processable.</returns>
    /// <remarks>
    /// 设备没有幅宽上限时一律可加工；有上限时**必须不大于上限**，单位不可比同样返回 `false`。
    ///
    /// A machine without a width limit processes anything; with one the width **must not exceed it**, and an incomparable unit again yields `false`.
    /// </remarks>
    public bool EnabledBy(Machine2<V> machine) =>
        machine.MaxWidth is { } limit ? Width.LessThanOrEqual(limit) is true : true;
}

/// <summary>
/// 设备 / A machine.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">设备编号 / The machine identifier.</param>
/// <param name="Name">设备名称 / The machine name.</param>
/// <param name="MaxBatchCount">最大批次数量 / The maximum batch count.</param>
/// <param name="MaxSwitchCount">最大换型次数 / The maximum switch count.</param>
/// <param name="MaxWidth">可加工最大幅宽 / The maximum processable width.</param>
/// <param name="Capacity">产能 / The capacity.</param>
/// <remarks>
/// **`defaultProcess` 这里未移植**：它依赖 `CuttingProcess2`，而那是 `infrastructure` 里的类型，尚未落地。
/// **先不带这个字段，好过先用一个占位枚举假装它已经被建模。**
///
/// **`defaultProcess` is not ported here**: it depends on `CuttingProcess2`, a type from `infrastructure` that has not landed yet. **Leaving the field out
/// beats a placeholder enum pretending it is modelled.**
/// </remarks>
public sealed record Machine2<V>(
    IMachineId2 Id,
    string Name,
    ulong? MaxBatchCount = null,
    ulong? MaxSwitchCount = null,
    Quantity<V>? MaxWidth = null,
    Quantity<V>? Capacity = null)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 副产物（配规件） / A by-product (filler piece).
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">副产物编号 / The by-product identifier.</param>
/// <param name="Name">副产物名称 / The by-product name.</param>
/// <param name="Width">宽度 / The width.</param>
/// <param name="Height">高度 / The height.</param>
/// <param name="AllowRotate">是否允许旋转 90° / Whether a 90-degree rotation is allowed.</param>
/// <param name="UnitWeight">单位重量 / The unit weight.</param>
public sealed record Costar2<V>(
    ICostarId2 Id,
    string Name,
    Quantity<V> Width,
    Quantity<V> Height,
    bool AllowRotate = true,
    Quantity<V>? UnitWeight = null)
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 校验副产物几何 / Validate the by-product geometry.
    /// </summary>
    /// <returns>校验结果 / The validation result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        if (Width.Value.ToFlt64().ToDouble() <= 0.0 || Height.Value.ToFlt64().ToDouble() <= 0.0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "副产物 " + Id.Value + " 的宽高必须为正 / By-product dimensions must be positive"));
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 允许的摆放方向集合 / The set of allowed orientations.
    /// </summary>
    /// <returns>方向列表 / The orientation list.</returns>
    /// <remarks>
    /// `false` 表示不旋转、`true` 表示旋转 90°，与基线一致。**不允许旋转时只返回一项**——
    /// 返回两项会让排样把"旋转后的同一块件"当成另一个选择，**白白翻倍搜索空间**。
    ///
    /// `false` means unrotated and `true` means rotated 90 degrees, matching the baseline. **A non-rotatable piece yields exactly one entry**: two entries would let
    /// nesting treat "the same piece, rotated" as a separate choice and **double the search space for nothing**.
    /// </remarks>
    public IReadOnlyList<bool> Orientations() =>
        AllowRotate ? new List<bool> { false, true } : new List<bool> { false };
}
