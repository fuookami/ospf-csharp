#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 分段线性半径选择元数据 / Metadata for a piecewise-linear radius selection.
/// </summary>
/// <param name="SolverRadiusSquared">求解器使用的半径平方 / The squared radius the solver used.</param>
/// <param name="ActualRadiusSquared">实际半径平方 / The actual squared radius.</param>
/// <param name="PwlAbsoluteError">分段逼近的绝对误差 / The piecewise approximation's absolute error.</param>
/// <param name="PwlRelativeError">分段逼近的相对误差 / The piecewise approximation's relative error.</param>
/// <param name="MaxPwlRelativeError">分段逼近的最大相对误差 / The piecewise approximation's maximum relative error.</param>
/// <param name="NumSegments">段数 / The segment count.</param>
/// <param name="IsWithinEnvelope">是否在包络内 / Whether it lies inside the envelope.</param>
/// <param name="SelectionSource">选择来源 / The selection source.</param>
/// <remarks>
/// **两个体积函数分开**：`ActualVolume` 用实际半径平方，
/// `PwlVolume` 用求解器那个（近似值）——这样报表能同时给出"真实体积"与"模型以为的体积"，
/// 两者的差就是逼近误差带来的偏差。
///
/// **The two volume helpers stay apart**: `ActualVolume` uses the actual squared radius and `PwlVolume` the solver's approximated one, so a
/// report can show both the true volume and the one the model believed, whose difference is the bias introduced by the approximation.
/// </remarks>
public sealed record PWLRadiusSelectionMetadata(
    FltX SolverRadiusSquared,
    FltX ActualRadiusSquared,
    FltX PwlAbsoluteError,
    FltX PwlRelativeError,
    FltX MaxPwlRelativeError,
    int NumSegments,
    bool IsWithinEnvelope,
    string SelectionSource = "pwl") {
    /// <summary>
    /// 校验不变量 / Validate the invariants.
    /// </summary>
    /// <remarks>
    /// **段数至少为 1、来源不自空**：段数为零的分段逼近不是"退化"，是根本没有逼近；
    /// 来源为空则报表无法追溯这次选择是怎么来的。构造期 `require` 守，这里用 `ArgumentException`。
    ///
    /// **At least one segment and a non-blank source**: a zero-segment approximation is not degenerate, it is absent, and a blank source leaves the report unable to trace where the selection came
 /// from. The implementation guards with construction-time `require`; here it is `ArgumentException`.
    /// </remarks>
    public void Validate() {
        if (NumSegments < 1) {
            throw new ArgumentOutOfRangeException(nameof(NumSegments), "numSegments must be at least 1");
        }
        if (string.IsNullOrWhiteSpace(SelectionSource)) {
            throw new ArgumentException("selectionSource must not be blank", nameof(SelectionSource));
        }
    }

    /// <summary>真实体积 / The actual volume</summary>
    /// <param name="height">高 / The height.</param>
    /// <param name="pi">圆周率 / Pi.</param>
    /// <returns>体积 / The volume.</returns>
    public FltX ActualVolume(FltX height, FltX pi) => pi.Times(ActualRadiusSquared).Times(height);

    /// <summary>模型以为的体积 / The volume the model believed</summary>
    /// <param name="height">高 / The height.</param>
    /// <param name="pi">圆周率 / Pi.</param>
    /// <returns>体积 / The volume.</returns>
    public FltX PwlVolume(FltX height, FltX pi) => pi.Times(SolverRadiusSquared).Times(height);
}

/// <summary>
/// 圆柱半径选择结果 / The result of a cylinder radius selection.
/// </summary>
/// <param name="Key">键 / The key.</param>
/// <param name="SelectedRadius">选中的半径 / The selected radius.</param>
/// <param name="Axis">轴向 / The axis.</param>
/// <param name="RadiusMin">半径下界 / The radius lower bound.</param>
/// <param name="RadiusMax">半径上界 / The radius upper bound.</param>
/// <param name="VariableName">变量名 / The variable name.</param>
/// <param name="Source">来源 / The source.</param>
/// <param name="PwlMetadata">分段逼近元数据 / The piecewise metadata.</param>
/// <remarks>
/// **四条不变量各有其意**：键不为空（否则结果无法回指）、
/// 变量名与来源若给出则不为空（空字符串等于"给了个没用的名字"）、**选中半径必须为正**
/// （零或负半径不是选择结果，是求解出错）。
///
/// **Each of the four invariants has a reason**: the key must be non-blank, since a result without one cannot be traced back; a supplied
/// variable name or source must be non-blank, an empty string being as good as a useless name; and **the selected radius must be positive**, zero or negative being a solve failure rather than a
/// selection.
/// </remarks>
public sealed record CylinderRadiusSelectionResult(
    string Key,
    Quantity<FltX> SelectedRadius,
    Axis3 Axis,
    Quantity<FltX>? RadiusMin = null,
    Quantity<FltX>? RadiusMax = null,
    string? VariableName = null,
    string? Source = null,
    PWLRadiusSelectionMetadata? PwlMetadata = null) {
    /// <summary>
    /// 校验不变量 / Validate the invariants.
    /// </summary>
    public void Validate() {
        if (string.IsNullOrWhiteSpace(Key)) {
            throw new ArgumentException("Cylinder radius selection key must not be blank.", nameof(Key));
        }
        if (VariableName is not null && string.IsNullOrWhiteSpace(VariableName)) {
            throw new ArgumentException("variable name must not be blank when given", nameof(VariableName));
        }
        if (Source is not null && string.IsNullOrWhiteSpace(Source)) {
            throw new ArgumentException("source must not be blank when given", nameof(Source));
        }
        if (!(SelectedRadius.Value.ToFlt64().ToDouble() > 0.0)) {
            throw new ArgumentOutOfRangeException(nameof(SelectedRadius), "selected radius must be positive");
        }
        PwlMetadata?.Validate();
    }
}
