#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 模式步 / Pattern step.
/// 描述模式中单个放置步骤。
/// </summary>
public sealed record PatternStep(
    Orientation LengthOrientation,
    Func<QuantityPoint2, QuantityPoint2> NextPointExtractor);

/// <summary>
/// 模式配置 / Pattern configuration.
/// </summary>
public sealed record PatternConfig(
    ulong WithPiling = 1,
    bool WithRemainder = false);

/// <summary>
/// 模式基类 / Pattern base class.
/// 定义货物放置模式，用于生成层平面布局。
/// Defines item placement patterns for generating layer plane layouts.
/// </summary>
public abstract class Pattern {
    /// <summary>底面长度范围 / Bottom length range.</summary>
    public abstract (double Min, double Max) BottomLengthRange { get; }
    /// <summary>底面宽度范围 / Bottom width range.</summary>
    public abstract (double Min, double Max) BottomWidthRange { get; }
    /// <summary>模式步骤列表 / Pattern steps.</summary>
    public abstract IReadOnlyList<IReadOnlyList<PatternStep>> Patterns { get; }

    /// <summary>
    /// 右下角点提取器 / Right-bottom point extractor.
    /// </summary>
    public static readonly Func<QuantityPoint2, QuantityPoint2> RightBottom = p =>
        new QuantityPoint2(p.X, p.Y);

    /// <summary>
    /// 左上角点提取器 / Left-upper point extractor.
    /// </summary>
    public static readonly Func<QuantityPoint2, QuantityPoint2> LeftUpper = p =>
        new QuantityPoint2(p.X, p.Y);

    /// <summary>
    /// 禁用方向集合 / Disabled orientations.
    /// </summary>
    public static readonly IReadOnlySet<Orientation> DisabledOrientations =
        new HashSet<Orientation> { Orientation.Side, Orientation.Lie };
}
