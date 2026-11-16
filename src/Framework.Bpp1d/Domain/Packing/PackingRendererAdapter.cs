// 一维渲染适配 / One-dimensional render adapter.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;

/// <summary>一维渲染件，坐标一律为方案声明单位的数值 / A rendered item whose coordinates are in the schema's declared unit.</summary>
/// <param name="ItemId">物品 / Item.</param>
/// <param name="Start">起点 / Start.</param>
/// <param name="Length">长度 / Length.</param>
public sealed record RenderItem1Dto(string ItemId, double Start, double Length);

/// <summary>渲染箱 / A rendered bin.</summary>
/// <param name="Id">箱身份 / Bin identity.</param>
/// <param name="BinTypeId">箱型 / Bin type.</param>
/// <param name="Length">箱长 / Bin length.</param>
/// <param name="Items">放置 / Placements.</param>
public sealed record RenderBin1Dto(string Id, string BinTypeId, double Length, IReadOnlyList<RenderItem1Dto> Items);

/// <summary>一维外部渲染契约 / The external 1D render contract.</summary>
/// <param name="LengthUnit">长度单位符号 / Length unit symbol.</param>
/// <param name="Bins">箱体 / Bins.</param>
/// <param name="Utilization">长度利用率 / Length utilization.</param>
public sealed record RenderSchema1Dto(string LengthUnit, IReadOnlyList<RenderBin1Dto> Bins, double Utilization);

/// <summary>
/// 渲染数值转换边界 / Render numeric conversion boundary.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **输出前复核显式一维几何。** 渲染层是最容易被"看起来差不多"糊过去的地方：重叠的放置、
/// 长度为 0 的放置、越出箱长的放置，在图上都是"挤在一起"，**但都不会报错**。
///
/// | 检查 | 若放过 |
/// | --- | --- |
/// | `start &lt; end`（不得向前重叠） | 两件物品互相压住，图上只是"挤" |
/// | `size &gt; 0` | 零长度物品占了位置却看不见 |
/// | `start + size ≤ 箱长 + 1e-8` | 物品伸出箱外 |
///
/// **`1e-8` 是给浮点噪声的**，不是给"稍微超一点"的：真实越界一旦超过 1e-8 就必须失败。
///
/// **Explicit 1D geometry is re-checked before output.** The render layer is where "looks about right" hides problems: overlapping placements, zero-length
/// placements and placements past the bin edge all merely **look cramped** on a chart and **never raise an error**. The checks are in the table. **`1e-8` is slack for
/// floating noise**, not for a slightly-over placement: real overflow beyond 1e-8 must fail.
/// </remarks>
public sealed class PackingRendererAdapter<V>
    where V : struct, IFloatingNumber<V> {
    private readonly IPackingValueAdapter<V> _adapter;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="adapter">数值适配 / Numeric adapter.</param>
    public PackingRendererAdapter(IPackingValueAdapter<V> adapter) {
        _adapter = adapter;
    }

    /// <summary>
    /// 转到渲染契约 / Convert into the render contract.
    /// </summary>
    /// <param name="result">领域结果 / Domain result.</param>
    /// <param name="unit">输出单位 / Output unit.</param>
    /// <returns>渲染契约或失败 / Render contract or failure.</returns>
    public Result<RenderSchema1Dto, ErrorCode, Error<ErrorCode>> ToSchema(PackingResult<V> result, PhysicalUnit unit) {
        var bins = new List<RenderBin1Dto>();
        foreach (PackingPlan<V> plan in result.Plans) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> length =
                _adapter.Quantity(plan.Bin.Type.Length, unit);
            if (length is not Ok<Flt64, ErrorCode, Error<ErrorCode>> lengthOk) {
                return Propagate<RenderSchema1Dto, Flt64>(length);
            }

            double end = 0.0;
            var items = new List<RenderItem1Dto>();
            foreach (Placement<V> placement in plan.Placements) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> start = _adapter.Quantity(placement.Start, unit);
                if (start is not Ok<Flt64, ErrorCode, Error<ErrorCode>> startOk) {
                    return Propagate<RenderSchema1Dto, Flt64>(start);
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> size = _adapter.Quantity(placement.Length, unit);
                if (size is not Ok<Flt64, ErrorCode, Error<ErrorCode>> sizeOk) {
                    return Propagate<RenderSchema1Dto, Flt64>(size);
                }
                double startValue = startOk.Value.ToDouble();
                double sizeValue = sizeOk.Value.ToDouble();
                if (startValue < end
                    || sizeValue <= 0.0
                    || startValue + sizeValue > lengthOk.Value.ToDouble() + 1e-8) {
                    return Results.Failed<RenderSchema1Dto>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "渲染几何无效 / Invalid render geometry"));
                }
                items.Add(new RenderItem1Dto(placement.ItemId, startValue, sizeValue));
                end = startValue + sizeValue;
            }
            bins.Add(new RenderBin1Dto(plan.Bin.Id, plan.Bin.Type.Id, lengthOk.Value.ToDouble(), items));
        }

        return Results.Ok(new RenderSchema1Dto(
            LengthUnit: unit.ToString(),
            Bins: bins,
            // **利用率直接取领域值再降成 `double`**，不绕 `FromSolver`：`FromSolver(Flt64) -> V` 是求解器方向的转换。
            Utilization: result.Utilization.ToFlt64().ToDouble()));
    }

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<S, ErrorCode, Error<ErrorCode>> Propagate<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<S>(new Err<ErrorCode>(ErrorCode.ApplicationError, "unreachable")),
        };
}
