#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service;

/// <summary>
/// 软性安全聚合初始化器 / The soft-security aggregation initializer.
/// </summary>
/// <remarks>
/// 与对齐：**两条校验**，都不做任何 DTO 翻译——
/// 软安全完全复用上游的货物、舱位、装载与压舱模型，只额外找一个主甲板。
///
/// | 校验 | 失败条件 |
/// | --- | --- |
/// | 主甲板存在 | 飞机配置里没有 `DeckLocation.Main` 的甲板 |
/// | 舱位非空 | 上游配载聚合的舱位表为空 |
///
/// **为什么不翻译 DTO**：软安全的每一个输入都已经在 `StowageAggregation` 里了。再翻译一遍会造出两份互不相等的
/// 货物对象，而 `Stowage` 的符号是按**下标**索引的——两份对象的下标顺序一旦不同，约束就会挂在错误的货位上。
///
/// **two validations** and no DTO translation — soft security fully reuses the upstream item, position, load and ballast models, only locating a main
/// deck. The table above lists the two. **Why no DTO translation**: every input is already in `StowageAggregation`, and translating again would produce two unequal item objects
/// while `Stowage`'s symbols are indexed **by position** — the moment the two lists' orders differ, constraints attach to the wrong holds.
/// </remarks>
internal static class AggregationInitializer
{
    /// <summary>
    /// 从飞机与配载聚合初始化软性安全聚合 / Initialize the soft-security aggregation from the aircraft and stowage aggregations.
    /// </summary>
    /// <param name="aircraftAggregation">飞机聚合 / The aircraft aggregation.</param>
    /// <param name="stowageAggregation">配载聚合 / The stowage aggregation.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>聚合结果 / The aggregation result.</returns>
    public static Result<SoftSecurityAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        AircraftAggregation aircraftAggregation,
        StowageAggregation stowageAggregation,
        RequestDTO input)
    {
        Deck? mainDeck = null;
        foreach (Deck deck in aircraftAggregation.Decks)
        {
            if (deck.Location == DeckLocation.Main)
            {
                mainDeck = deck;
                break;
            }
        }
        if (mainDeck is null)
        {
            return Results.Failed<SoftSecurityAggregation>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "软安全聚合需要主舱甲板 / soft security aggregation requires a main-deck configuration"));
        }
        if (stowageAggregation.Positions.Count == 0)
        {
            return Results.Failed<SoftSecurityAggregation>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "软安全聚合至少需要一个装载位置 / soft security aggregation requires at least one stowage position"));
        }

        return Results.Ok<SoftSecurityAggregation>(new SoftSecurityAggregation(
            aircraftAggregation.AircraftModel,
            mainDeck,
            stowageAggregation.Items,
            stowageAggregation.Positions,
            stowageAggregation.Stowage,
            stowageAggregation.Load,
            stowageAggregation.Ballast));
    }
}
