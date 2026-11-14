// BLA 领域上下文 / BLA domain context.
// 与固定 Kotlin 基线 `bpp2d/domain/bla/BlaContext.kt`（41 行）对应：把排样算法包成一个可复用的上下文对象，
// 调用方不必自己组装 `BottomUpLeftJustifiedAlgorithm`。
// Mirrors the pinned baseline: wraps the layout algorithm into a reusable context so callers need not assemble
// `BottomUpLeftJustifiedAlgorithm` themselves.
//
// 至此 `bla` 域在 C# 侧闭合：`Bla2dLayoutResult` + `BottomUpLeftJustifiedAlgorithm` + `BlaContext`。
// The `bla` domain is now closed on the C# side: `Bla2dLayoutResult` + `BottomUpLeftJustifiedAlgorithm` + `BlaContext`.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// BLA 领域上下文 / BLA domain context.
/// </summary>
/// <remarks>
/// 无状态，可安全复用：每次 <see cref="Layout"/> 都新建一个算法实例，配置与板材不跨调用残留。
/// 其余上下文类都持有可变的求解状态，BLA 没有这种状态，因此这里不引入任何字段——少一个共享状态就少一类
/// "上一趟的参数漏到这一趟"的缺陷。
///
/// Stateless and safe to reuse: each <see cref="Layout"/> builds a fresh algorithm instance, so configuration and
/// sheets never leak between calls. Other context classes carry mutable solver state; BLA has none, so no fields are
/// held here — one less shared state is one less class of "last run's parameters bled into this run" defect.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class BlaContext<V> where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 在给定板材上执行二维左对齐下对齐排样 / Run 2D left-bottom justified layout on the given sheet.
    /// </summary>
    /// <param name="sheet">板材形状 / Sheet shape.</param>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <param name="config">算法配置，缺省时使用默认配置 / Algorithm configuration, defaulting when null.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>排样结果 / The layout result.</returns>
    public Result<Bla2dLayoutResult<V>, ErrorCode, Error<ErrorCode>> Layout(
        Sheet2Shape<V> sheet,
        IReadOnlyList<(IAbstractItem<V> Item, int Amount)> items,
        BottomUpLeftJustifiedAlgorithm<V>.Config? config = null,
        Quantity<V>? kerf = null) {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(items);
        return new BottomUpLeftJustifiedAlgorithm<V>(
            sheet: sheet,
            config: config,
            kerf: kerf).Invoke(items);
    }
}
