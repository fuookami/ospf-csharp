// 分区列生成预算与轨迹 / Partition column generation budget and trace.
// 与固定 Kotlin 基线 `application/PartitionColumnGenerationApplicationService.kt` 的头部对应。
// Mirrors the header of the pinned Kotlin baseline's `PartitionColumnGenerationApplicationService.kt`.

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp.Application;

/// <summary>
/// 分区列生成预算 / Partition-column generation budget.
/// </summary>
/// <param name="IterationLimit">包含可行性阶段的根节点迭代上限 / Root iteration limit including the feasibility phase.</param>
/// <param name="ColumnLimit">根节点列数上限 / Root column budget.</param>
/// <param name="Tolerance">定价及人工变量容差 / Pricing and artificial-variable tolerance.</param>
/// <param name="BatchPricingEnabled">是否批量准备非种子定价模型 / Whether to batch-prepare non-seed pricing models.</param>
public sealed record PartitionColumnGenerationConfig(
    int IterationLimit = 64,
    int ColumnLimit = 10000,
    double Tolerance = 1e-6,
    bool BatchPricingEnabled = false) {
    /// <summary>
    /// 预算是否有效 / Whether the budget is valid.
    /// </summary>
    /// <param name="partitionCount">分区数 / The number of partitions.</param>
    /// <returns>判断结果 / The verdict.</returns>
    /// <remarks>
    /// 四条规则：迭代上限至少一次、**列数上限至少能容纳每个分区一列**、容差有限、容差为正。
    ///
    /// **第二条最要紧**：列数上限小于分区数时，种子阶段拿到负数可用列数，**每个分区连一列都放不进去**，
    /// 而失败会发生在更靠后的主问题构建里，错误信息指向"主问题不可行"，与真正的原因无关。
    ///
    /// Four rules: at least one iteration, **a column budget that fits one column per partition**, a finite tolerance, and a positive tolerance.
    /// **The second matters most**: with a column budget below the partition count the seed phase computes a negative number of available columns and **not even one
    /// column per partition fits**, while the failure surfaces later in master construction blaming "infeasible master", unrelated to the real cause.
    /// </remarks>
    public bool IsValidFor(int partitionCount) =>
        IterationLimit >= 1
        && ColumnLimit >= partitionCount
        && double.IsFinite(Tolerance)
        && Tolerance > 0;
}

/// <summary>分区列生成轨迹 / Partition-column generation trace.</summary>
/// <param name="PhaseOne">是否人工变量可行性阶段 / Whether in the artificial-variable feasibility phase.</param>
/// <param name="Objective">当前受限 LP 目标 / The current restricted LP objective.</param>
/// <param name="AddedColumns">新增列数 / The number of added columns.</param>
/// <param name="PricingProven">所有分区本轮定价是否有证明 / Whether every partition pricing problem was proven this round.</param>
public sealed record PartitionColumnGenerationIteration(
    bool PhaseOne,
    Flt64 Objective,
    int AddedColumns,
    bool PricingProven);
