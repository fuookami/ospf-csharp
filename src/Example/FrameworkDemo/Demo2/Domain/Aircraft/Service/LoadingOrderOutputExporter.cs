// 装载顺序导出器 / The loading-order output exporter.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Service;

/// <summary>
/// 把算出来的装载顺序导出成响应 DTO / Export the computed loading order as a response DTO.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `demo2/domain/aircraft/service/LoadingOrderOutputExporter.kt`：把各甲板的舱位摊平，按
/// **甲板序号 → 装载顺序号 → 舱位 id 的字符串字典序**排好，每行写成 `"{id}: {spaceName} (order={order})"`。
///
/// **三处照抄而不是"顺手改好"**：
/// 1. 第三级排序键是 `id.toString()`，即**字符串字典序**——`"10"` 排在 `"2"` 前面。看着像 bug，但它是基线的行为，
///    改了会让同一批输入的输出顺序与基线不同；要改得先确认调用方不依赖这个顺序。
/// 2. 顺序号转成字符串再转回整数（`order.toString().toInt()`）——**等价于直接比数值**，保留这个形状是为了读代码时能一眼对上基线。
/// 3. `status` 恒为 `"Optimal"`，由被调用的 DTO 工厂写死。
///
/// **一处必须偏离**：基线的 `_loadingOrder` 是 `lateinit`，读得太早会抛 `UninitializedPropertyAccessException`；本仓把它建成可空的，
/// 于是这里**明确报错**而不是崩溃——错误码 + 点名是哪个舱位，比一个栈追踪有用。
///
/// Mirrors the baseline exporter: flatten each deck's positions, sort by **deck ordinal → loading-order number → the position id's string order**, and render each
/// line as `"{id}: {spaceName} (order={order})"`.
///
/// **Three things copied rather than "fixed while at it"**: (1) the third key is `id.toString()`, i.e. **lexicographic** — `"10"` sorts before `"2"`. It looks
/// like a bug, but it is the baseline behaviour, and changing it makes the same input produce a different order than the baseline; that change needs a confirmed
/// promise from callers first. (2) the order number is stringified then parsed back — **equivalent to a numeric compare**, kept only so this reads side by side
/// with the baseline. (3) `status` is always the literal `"Optimal"`, hard-coded in the DTO factory.
///
/// **One deliberate divergence**: the baseline's `_loadingOrder` is `lateinit`, so reading it too early throws `UninitializedPropertyAccessException`; this port
/// models it as nullable, and here that becomes an **explicit error** instead of a crash — an error code plus the position's name beats a stack trace.
/// </remarks>
public sealed class LoadingOrderOutputExporter
{
    private readonly AircraftAggregation _assembly;

    public LoadingOrderOutputExporter(AircraftAggregation assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assembly = assembly;
    }

    /// <summary>
    /// 导出装载顺序 / Export the loading order.
    /// </summary>
    /// <param name="input">原始请求 / The original request</param>
    /// <returns>响应 DTO / The response DTO</returns>
    /// <remarks>
    /// `input` 目前不参与计算：基线的签名收它，是为了将来把请求侧的信息（比如按分区过滤）带进排序。**保留参数，不为了"用上它"编逻辑。**
    /// `input` does not take part in the computation yet: the baseline takes it so request-side information (e.g. filtering by partition) can enter the ordering
    /// later. **Keep the parameter rather than inventing logic to "use" it.**
    /// </remarks>
    public Result<LoadingOrderResponseDTO, ErrorCode, Error<ErrorCode>> Invoke(RequestDTO input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<string> orders = new();
        foreach (Deck deck in _assembly.Decks) {
            List<(DeckLocation Deck, Position Position)> flattened = new();
            foreach (Position position in deck.Positions) {
                flattened.Add((deck.Location, position));
            }
            flattened.Sort((left, right) => {
                int byDeck = left.Deck.CompareTo(right.Deck);
                if (byDeck != 0) {
                    return byDeck;
                }
                if (left.Position.LoadingOrder is not { } leftOrder || right.Position.LoadingOrder is not { } rightOrder) {
                    return 0;
                }
                int byOrder = int.Parse(leftOrder.Order.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
                    .CompareTo(int.Parse(rightOrder.Order.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
                if (byOrder != 0) {
                    return byOrder;
                }
                return string.CompareOrdinal(
                    left.Position.Id.ToString(CultureInfo.InvariantCulture),
                    right.Position.Id.ToString(CultureInfo.InvariantCulture));
            });

            foreach ((DeckLocation _, Position position) in flattened) {
                if (position.LoadingOrder is not { } order) {
                    return Results.Failed<LoadingOrderResponseDTO>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "舱位 " + position.SpaceName + "（id=" + position.Id + "）尚未分配装载顺序 / the loading order of position "
                        + position.SpaceName + " (id=" + position.Id + ") has not been assigned yet"));
                }
                orders.Add(
                    position.Id.ToString(CultureInfo.InvariantCulture) + ": " + position.SpaceName
                    + " (order=" + order.Order.ToString(CultureInfo.InvariantCulture) + ")");
            }
        }

        return Results.Ok<LoadingOrderResponseDTO>(LoadingOrderResponseDTO.Success(orders));
    }
}
