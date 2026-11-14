#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 航班。Flight information for stowage planning.
/// </summary>
/// <param name="FlightNo">航班号 / Flight number</param>
/// <param name="Source">始发站 / Source station</param>
/// <param name="Destination">目的站 / Destination station</param>
public sealed record Flight(
    Infrastructure.FlightNo FlightNo,
    Infrastructure.IATACode Source,
    Infrastructure.IATACode Destination)
{
    /// <summary>
    /// 需要复重的始发站集合 / The source stations that require reweighing.
    /// </summary>
    /// <remarks>
    /// **基线里这个集合是空的**（`private val reweighNeededTerminals = setOf&lt;IATA&gt;()`），所以
    /// `ReweighNeeded` 恒为 `false`，`ItemReweighNeededLimit` 在基线里**永远不会被加进管线列表**。
    ///
    /// 照抄这个效果，但**保留配置入口**：它是运营侧的可调参数，把空集合硬编码进判定里就再也没法打开。
    /// 一旦有人填充此集合，`ReweighNeeded` 就会在预配载下开始返回 `true`——那是行为变化，须同步补判据。
    ///
    /// **The baseline's set is empty** (`private val reweighNeededTerminals = setOf&lt;IATA&gt;()`), so `ReweighNeeded` is always `false` and `ItemReweighNeededLimit` is **never added to the
    /// pipeline list**. That effect is copied, but **the knob is kept**: it is an operational parameter, and hard-coding the emptiness into the test would make it impossible to turn on.
    /// Filling this set makes `ReweighNeeded` start returning `true` under predistribution — a behaviour change that needs its own criteria.
    /// </remarks>
    public static IReadOnlySet<string> ReweighNeededStations { get; } = new HashSet<string>();

    /// <summary>
    /// 是否需要复重 / Whether reweighing is needed.
    /// </summary>
    /// <remarks>
    /// 与基线 `Flight.reweighNeeded` 对齐：**只有预配载**才可能为真，其余模式恒 `false`；预配载下取决于
    /// 始发站是否在 <see cref="ReweighNeededStations"/> 里——而该集合默认为空，所以实际恒 `false`。
    ///
    /// Aligned with the baseline: **only predistribution** can be true, every other mode returning `false` unconditionally; under predistribution it depends on whether the source station
    /// is in <see cref="ReweighNeededStations"/>, which defaults to empty, so in practice it is always `false`.
    /// </remarks>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <returns>是否需要复重 / Whether reweighing is needed.</returns>
    public bool ReweighNeeded(StowageMode stowageMode) =>
        stowageMode == StowageMode.Predistribution && ReweighNeededStations.Contains(Source.Code);
}
