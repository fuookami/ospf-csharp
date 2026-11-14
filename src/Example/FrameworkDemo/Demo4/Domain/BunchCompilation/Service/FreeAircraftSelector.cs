#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;

/// <summary>
/// 自由飞机选择器的配置 / The free-aircraft selector's configuration.
/// </summary>
/// <param name="BadReducedCostAmount">按检验数放出的机数上限 / How many aircraft the reduced-cost pass releases.</param>
/// <param name="HighCostAmount">按成本放出的机数上限 / How many the cost pass releases.</param>
/// <param name="HighAircraftChangeAmount">按换机次数放出的机数上限 / How many the aircraft-change pass releases.</param>
/// <param name="RandomAmount">随机放出的机数上限 / How many the random pass releases.</param>
/// <param name="TabuAmount">禁忌表容量 / The tabu list's capacity.</param>
/// <param name="FixBar">检验数门槛，超过它才放出 / The reduced-cost bar above which an aircraft is released.</param>
/// <param name="RandomSeed">随机种子 / The random seed.</param>
public sealed record FreeAircraftSelectorConfiguration(
    ulong BadReducedCostAmount = 3UL,
    ulong HighCostAmount = 3UL,
    ulong HighAircraftChangeAmount = 3UL,
    ulong RandomAmount = 3UL,
    ulong TabuAmount = 10UL,
    Flt64? FixBar = null,
    int RandomSeed = 42) {
    /// <summary>检验数门槛 / The reduced-cost bar.</summary>
    public Flt64 ResolvedFixBar => FixBar ?? Flt64.Zero;
}

/// <summary>
/// 自由飞机选择器 / The free-aircraft selector.
/// </summary>
/// <remarks>
/// 基线 `FreeAircraftSelector.kt`：每轮列生成开头，从"当前没被占用"的飞机里放出若干架，让定价子问题有东西可选。
/// 四道放行：**检验数高**（值的飞机先放）、**成本高**（贵的束先放）、**换机次数多**（改动大的先放）、**随机**（打散，防局部最优）。
///
/// 两条容易写错的规则：**禁忌表**——放过一次的飞机在后续通道里要被跳过（否则第二、三通道立刻把同一架再放一次，等于只有一个通道）；
/// 禁忌表**容量固定**，满了先踢最早的那条。还有一道小门槛：**`Indexed` 的飞机才放**，没入索引的飞机拿不到列。
///
/// **一处有意偏离基线**：通道上限为 0 时**一架都不放**。基线的 `freeByStrategy` 是先加再判上限，上限 0 时会放出一架；
/// 那样"关掉某个通道"就没法表达，而默认配置全是 3，实际行为不受影响。
///
/// **天花板：检验数由调用方给。** 基线的 `shadowPriceMap.reducedCost(bunch)` 在 C# 的影子价格映射上没有对应成员，
/// 所以这里收一个"束到检验数"的函数，缺省恒零——**恒零意味着第一通道一架都不放**，其余三道照常。
/// 升级路径：等影子价格映射补上 `ReducedCost` 之后改成默认从映射取。
///
/// The baseline's `FreeAircraftSelector.kt` runs at the start of each column-generation round: it releases aircraft from those currently unoccupied so the pricing subproblem has
/// candidates. Four passes: **high reduced cost** (valuable aircraft first), **high cost** (expensive bunches first), **many aircraft changes** (biggest disruption first), **random**
/// (to break local optima).
///
/// Two rules are easy to get wrong: the **tabu list** — an aircraft released once must be skipped by later passes, otherwise the second and third passes immediately release the same
/// aircraft again and the selector degenerates into one pass; and the tabu list has a **fixed capacity**, dropping the oldest entry first. One more small gate: **only `Indexed`
/// aircraft are released**, since an unindexed aircraft has no column to attach to.
///
/// **One deliberate deviation from the baseline**: a pass cap of 0 releases nothing. The baseline's `freeByStrategy` adds first and checks the cap afterwards, so a cap of 0 releases one
/// aircraft; that makes "switch this pass off" inexpressible, and since every default is 3 the real behaviour is unchanged.
///
/// **Ceiling: the reduced cost comes from the caller.** The baseline's `shadowPriceMap.reducedCost(bunch)` has no counterpart on C#'s shadow-price map, so this takes a
/// a bunch-to-reduced-cost delegate defaulting to zero — **zero means the first pass releases nothing**, while the other three run as usual. Upgrade path: read it from the map once the
/// map grows a `ReducedCost`.
/// </remarks>
public sealed class FreeAircraftSelector {
    private const int RandomAttempts = 10090;

    private readonly FreeAircraftSelectorConfiguration _configuration;

    private readonly List<Aircraft> _tabuAircrafts = new();

    private readonly Random _random;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="configuration">配置 / The configuration.</param>
    public FreeAircraftSelector(FreeAircraftSelectorConfiguration? configuration = null) {
        _configuration = configuration ?? new FreeAircraftSelectorConfiguration();
        _random = new Random(_configuration.RandomSeed);
    }

    /// <summary>禁忌表 / The tabu list.</summary>
    public IReadOnlyList<Aircraft> TabuAircrafts => _tabuAircrafts;

    /// <summary>
    /// 放出若干架自由飞机 / Release some free aircraft.
    /// </summary>
    /// <param name="fixedBunches">已固定的束 / The fixed bunches.</param>
    /// <param name="freeAircrafts">初始自由飞机集合，会被就地扩充 / The initial free aircraft, extended in place.</param>
    /// <param name="reducedCost">束的检验数 / The bunch reduced cost, zero when omitted.</param>
    /// <returns>放出的飞机集合 / The released aircraft.</returns>
    public Result<IReadOnlySet<Aircraft>, ErrorCode, Error<ErrorCode>> Invoke(
        IReadOnlyList<FlightTaskBunch> fixedBunches,
        ICollection<Aircraft> freeAircrafts,
        Func<FlightTaskBunch, Flt64>? reducedCost = null) {
        ArgumentNullException.ThrowIfNull(fixedBunches);
        ArgumentNullException.ThrowIfNull(freeAircrafts);
        Func<FlightTaskBunch, Flt64> cost = reducedCost ?? (_ => Flt64.Zero);

        ReleaseByReducedCost(fixedBunches, freeAircrafts, cost);
        ReleaseBy(
            fixedBunches.OrderByDescending(bunch => bunch.Cost.SolverCostOrNull(Flt64.Zero) ?? Flt64.Zero).ToList(),
            freeAircrafts,
            _configuration.HighCostAmount);
        ReleaseBy(
            fixedBunches.OrderByDescending(bunch => bunch.AircraftChangeCount).ToList(),
            freeAircrafts,
            _configuration.HighAircraftChangeAmount);
        ReleaseRandom(fixedBunches, freeAircrafts);

        return Results.Ok<IReadOnlySet<Aircraft>>(freeAircrafts.ToHashSet());
    }

    private void ReleaseByReducedCost(
        IReadOnlyList<FlightTaskBunch> fixedBunches,
        ICollection<Aircraft> freeAircrafts,
        Func<FlightTaskBunch, Flt64> reducedCost) {
        var ordered = fixedBunches
            .OrderByDescending(reducedCost)
            .ToList();
        ulong amount = 0UL;
        foreach (FlightTaskBunch bunch in ordered) {
            Aircraft aircraft = bunch.Executor;
            // **检验数门槛是严格大于**：刚好等于门槛不算"值得放出"，否则门槛写成 0 就等于全放。
            // **The bar is strictly greater than**: exactly at the bar is not "worth releasing", otherwise a bar of zero would release everything.
            if (freeAircrafts.Contains(aircraft) || !aircraft.Indexed || reducedCost(bunch).ToDouble() <= _configuration.ResolvedFixBar.ToDouble()) {
                continue;
            }
            ++amount;
            freeAircrafts.Add(aircraft);
            if (amount == _configuration.BadReducedCostAmount) {
                break;
            }
        }
    }

    private void ReleaseBy(IReadOnlyList<FlightTaskBunch> ordered, ICollection<Aircraft> freeAircrafts, ulong maximumAmount) {
        ulong amount = 0UL;
        foreach (FlightTaskBunch bunch in ordered) {
            Aircraft aircraft = bunch.Executor;
            if (freeAircrafts.Contains(aircraft) || !aircraft.Indexed || IsTabu(aircraft) || amount == maximumAmount) {
                continue;
            }
            ++amount;
            freeAircrafts.Add(aircraft);
            MarkTabu(aircraft);
            if (amount == maximumAmount) {
                break;
            }
        }
    }

    private void ReleaseRandom(IReadOnlyList<FlightTaskBunch> fixedBunches, ICollection<Aircraft> freeAircrafts) {
        if (fixedBunches.Count == 0) {
            return;
        }
        ulong amount = 0UL;
        for (int attempt = 0; attempt < RandomAttempts; ++attempt) {
            FlightTaskBunch bunch = fixedBunches[_random.Next(fixedBunches.Count)];
            Aircraft aircraft = bunch.Executor;
            if (!freeAircrafts.Contains(aircraft) && aircraft.Indexed && !IsTabu(aircraft) && amount != _configuration.RandomAmount) {
                ++amount;
                freeAircrafts.Add(aircraft);
                MarkTabu(aircraft);
            }
            if (amount == _configuration.RandomAmount) {
                break;
            }
        }
    }

    private bool IsTabu(Aircraft aircraft) => _tabuAircrafts.Contains(aircraft);

    private void MarkTabu(Aircraft aircraft) {
        if ((ulong)_tabuAircrafts.Count == _configuration.TabuAmount) {
            _tabuAircrafts.RemoveAt(0);
        }
        _tabuAircrafts.Add(aircraft);
    }
}
