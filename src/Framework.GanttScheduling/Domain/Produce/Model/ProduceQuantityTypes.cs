#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>
/// 物料数量类型定义 / Material quantity type definitions
/// </summary>
/// <remarks>
/// 本文件定义物料数量相关的类型，用于表示物理量及范围。
/// Defines material quantity types for representing physical quantities and ranges.
/// </remarks>
public static class ProduceQuantityTypes {
    /// <summary>获取 Flt64 零值 / Get Flt64 zero value</summary>
    public static Flt64 Zero => Flt64.Zero;

    /// <summary>获取 Flt64 一值 / Get Flt64 one value</summary>
    public static Flt64 One => Flt64.One;
}
