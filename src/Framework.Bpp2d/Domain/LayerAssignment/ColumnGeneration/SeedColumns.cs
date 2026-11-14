#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;

/// <summary>
/// 种子列构造 / Seed-column construction.
/// </summary>
/// <remarks>
/// 与基线 `ColumnGenerationApplicationService.seedColumns` 对齐。三处必须照抄：
///
/// 1. **种子状态是"迭代 0 + 空列"**：种子阶段与已有列无关，把当前列传进去会让第一次生成受上一轮影响，
///    而种子恰恰是"从零开始的初始列"。
/// 2. **配置带调用方的两个上限**（迭代上限与每轮列数上限），不是执行器配置里的默认值——种子生成与列生成
///    用的是同一套上限，混用会让第一轮就超出调用方的预算。
/// 3. **结果去重**：生成器可能对同一形状给出多个等价列，重复列在 RMP 里只会让约束矩阵多一列而目标相同。
///    `Layer` 是值相等对象（`Equals` 比板材类型、形状与件数），所以 `Distinct()` 是真的按值去重。
///
/// Aligned with the baseline's `seedColumns`. Three things must be copied: **the seed state is "iteration 0 with empty columns"**, since the seed stage is independent of existing columns and
/// passing the current ones in would let the first generation inherit the previous round, whereas seeding means the initial columns from scratch; **the config carries the caller's two limits**
/// (iteration limit and per-round column limit) rather than the executor defaults, because seeding and column generation share one budget and mixing them would exceed the caller's allowance on the
/// very first round; and **results are deduplicated**, since a generator may emit several equivalent columns for one shape and a duplicate only adds a column to the RMP matrix with an identical
/// objective. `Layer` has value equality (`Equals` compares sheet type, shape and unit count), so `Distinct()` genuinely deduplicates by value.
/// </remarks>
public static class SeedColumns
{
    /// <summary>
    /// 生成种子列 / Generate the seed columns.
    /// </summary>
    /// <param name="generator">层生成器 / The layer generator.</param>
    /// <param name="requestBuilder">请求构造器 / The request builder.</param>
    /// <param name="iterationLimit">迭代上限 / The iteration limit.</param>
    /// <param name="maxColumnsPerIteration">每轮列数上限 / The per-round column limit.</param>
    /// <returns>去重后的种子列 / The deduplicated seed columns.</returns>
    public static IReadOnlyList<Layer<FltX>> Build(
        IBpp2dLayerGenerator<FltX> generator,
        IColumnGenerationLayerRequestBuilder requestBuilder,
        int iterationLimit,
        int maxColumnsPerIteration)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(requestBuilder);

        // **迭代 0 + 空列**：种子与已有列无关。
        // **Iteration 0 with empty columns**: seeding is independent of existing ones.
        Bpp2dLayerGenerationRequest<FltX> seedRequest = requestBuilder.Build(
            new ColumnGenerationState(0, Array.Empty<Layer<FltX>>()),
            new ColumnGenerationConfig(
                IterationLimit: iterationLimit,
                MaxColumnsPerIteration: maxColumnsPerIteration));

        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> generated = generator.Generate(seedRequest);
        return generated.Select(result => result.Layer).Distinct().ToArray();
    }
}
