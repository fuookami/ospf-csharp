// 离散装载解与其独立复核 / Discrete-loading solution and its solver-independent verification.
// 与固定 Kotlin 基线 `bpp/domain/discrete_loading/DiscreteLoadingContext.kt` 的 `verify` 对应。
// Mirrors `verify` from the pinned Kotlin baseline's `bpp/domain/discrete_loading/DiscreteLoadingContext.kt`.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;

namespace Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;

/// <summary>
/// 已验证离散解 / Independently verified discrete solution.
/// </summary>
/// <param name="Assignments">货物到舱位 / Cargo-to-position map.</param>
/// <param name="OccupiedPositions">启用舱位（包含预留）/ Occupied positions, including reservations.</param>
/// <param name="SelectedLayouts">选择组到布局 / Group-to-layout map.</param>
public sealed record DiscreteLoadingSolution(
    IReadOnlyDictionary<string, string> Assignments,
    IReadOnlySet<string> OccupiedPositions,
    IReadOnlyDictionary<string, string> SelectedLayouts);
