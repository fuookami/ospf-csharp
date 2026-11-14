#nullable enable

using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 把"行来源"对偶/敏感度区间映射回原始约束 / Map row-origin duals and sensitivity ranges back onto original constraints.
/// </summary>
/// <remarks>
/// **这是 `FixedIntegerLpSensitivity.kt` 里 `mapExplicitProvenanceDuals` 与 `mapExplicitProvenanceRanges` 的移植。**
///
/// **两条规则决定了结果对不对，而且都会"安静地"出错：**
///
/// | 规则 | 写错会怎样 |
/// | --- | --- |
/// | **一个来源只能有一条行** | 多条行指向同一来源时**取哪一条都是编的**；必须整条丢弃 |
/// | **来源必须在 `originalIds` 里** | 否则会把中间层自己造的行**当成原始约束的敏感度**报出去 |
///
/// **两条都是"多给一条"的错误**，而多给一条比少给一条更难发现：**读者看到有数值就以为验过了。**
///
/// **These are `mapExplicitProvenanceDuals` and `mapExplicitProvenanceRanges` from `FixedIntegerLpSensitivity.kt`.** **Two rules decide correctness, and both fail quietly**: a source with
/// more than one row **cannot pick one without inventing it** (the whole source must be dropped), and a source outside `originalIds` would **report an intermediate-layer row as an
/// original constraint's sensitivity.** **Both errors give one value too many**, and one too many is harder to spot than one too few: **a reader who sees a number assumes it was
/// checked.**
/// </remarks>
public static class ProvenanceDualMapping
{
    /// <summary>
    /// 把行对偶映射回原始约束 / Map row duals back onto the original constraints.
    /// </summary>
    /// <typeparam name="T">行来源键的类型 / The row-origin key type.</typeparam>
    /// <param name="rowProvenance">每行对应的原始约束，无来源为 `null` / The original constraint per row, `null` when unprovenanced.</param>
    /// <param name="rowOrigins">每行的后端来源键 / The backend origin key per row.</param>
    /// <param name="dualsByOrigin">按来源键给出的对偶值 / The dual value keyed by origin.</param>
    /// <param name="originalIds">允许出现的原始约束集合 / The set of admissible original constraints.</param>
    /// <returns>原始约束到对偶的映射 / A map from original constraint to dual.</returns>
    public static IReadOnlyDictionary<ConstraintId, Flt64> MapDuals<T>(
        IReadOnlyList<ConstraintId?> rowProvenance,
        IReadOnlyList<T?> rowOrigins,
        IReadOnlyDictionary<T, Flt64> dualsByOrigin,
        IReadOnlySet<ConstraintId> originalIds)
        where T : notnull =>
        Map(rowProvenance, rowOrigins, dualsByOrigin, originalIds);

    /// <summary>
    /// 把行敏感度区间映射回原始约束 / Map row sensitivity ranges back onto the original constraints.
    /// </summary>
    /// <typeparam name="T">行来源键的类型 / The row-origin key type.</typeparam>
    /// <param name="rowProvenance">每行对应的原始约束，无来源为 `null` / The original constraint per row, `null` when unprovenanced.</param>
    /// <param name="rowOrigins">每行的后端来源键 / The backend origin key per row.</param>
    /// <param name="rangesByOrigin">按来源键给出的区间 / The range keyed by origin.</param>
    /// <param name="originalIds">允许出现的原始约束集合 / The set of admissible original constraints.</param>
    /// <returns>原始约束到区间的映射 / A map from original constraint to range.</returns>
    public static IReadOnlyDictionary<ConstraintId, SensitivityRange> MapRanges<T>(
        IReadOnlyList<ConstraintId?> rowProvenance,
        IReadOnlyList<T?> rowOrigins,
        IReadOnlyDictionary<T, SensitivityRange> rangesByOrigin,
        IReadOnlySet<ConstraintId> originalIds)
        where T : notnull =>
        Map(rowProvenance, rowOrigins, rangesByOrigin, originalIds);

    /// <summary>
    /// 两个映射共用的唯一实现 / The single implementation both mappings share.
    /// </summary>
    /// <typeparam name="T">行来源键的类型 / The row-origin key type.</typeparam>
    /// <typeparam name="TValue">被映射的值的类型 / The mapped value type.</typeparam>
    /// <param name="rowProvenance">每行对应的原始约束 / The original constraint per row.</param>
    /// <param name="rowOrigins">每行的后端来源键 / The backend origin key per row.</param>
    /// <param name="valuesByOrigin">按来源键给出的值 / The value keyed by origin.</param>
    /// <param name="originalIds">允许出现的原始约束集合 / The set of admissible original constraints.</param>
    /// <returns>映射结果 / The mapping.</returns>
    /// <remarks>
    /// **`rowOrigins` 用 `ElementAtOrDefault` 而不是索引器**：两个列表长度不一致时，
    /// 索引器会抛异常，而基线取 `getOrNull` —— **短的那一侧只是"这行没有来源"，不是错误。**
    ///
    /// **`rowOrigins` uses `ElementAtOrDefault` rather than the indexer**: when the two lists differ in length the indexer throws, while the baseline takes `getOrNull` — **the short side
    /// simply means "this row has no origin", not an error.**
    /// </remarks>
    private static IReadOnlyDictionary<ConstraintId, TValue> Map<T, TValue>(
        IReadOnlyList<ConstraintId?> rowProvenance,
        IReadOnlyList<T?> rowOrigins,
        IReadOnlyDictionary<T, TValue> valuesByOrigin,
        IReadOnlySet<ConstraintId> originalIds)
        where T : notnull {
        ArgumentNullException.ThrowIfNull(rowProvenance);
        ArgumentNullException.ThrowIfNull(rowOrigins);
        ArgumentNullException.ThrowIfNull(valuesByOrigin);
        ArgumentNullException.ThrowIfNull(originalIds);

        var originsBySource = new Dictionary<ConstraintId, List<T>>();
        for (int index = 0; index < rowProvenance.Count; index++) {
            ConstraintId? source = rowProvenance[index];
            T? origin = index < rowOrigins.Count ? rowOrigins[index] : default;

            // **三个条件缺一不可**：有来源、有后端键、且来源是原始约束。
            // **All three conditions are required**: a source, a backend key, and a source that is an original constraint.
            if (source is not { } sourceId || origin is null || !originalIds.Contains(sourceId)) {
                continue;
            }
            if (!originsBySource.TryGetValue(sourceId, out List<T>? origins)) {
                origins = [];
                originsBySource[sourceId] = origins;
            }
            origins.Add(origin);
        }

        var result = new Dictionary<ConstraintId, TValue>();
        foreach (KeyValuePair<ConstraintId, List<T>> pair in originsBySource) {
            // **一个来源恰好一条行才映射。** 两条行指向同一来源时，取哪一条都是编的。
            // **A source maps only when exactly one row points at it.** With two rows, picking one would be inventing it.
            if (pair.Value.Count != 1) {
                continue;
            }
            if (valuesByOrigin.TryGetValue(pair.Value[0], out TValue? value) && value is not null) {
                result[pair.Key] = value;
            }
        }

        return result;
    }
}
