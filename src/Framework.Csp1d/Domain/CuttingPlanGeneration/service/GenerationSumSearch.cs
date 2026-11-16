#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;

/// <summary>
/// "求和类"生成器共用的深度优先搜索 / Depth-first search shared by the sum-style generators.
/// </summary>
/// <remarks>
/// `FullSumGenerator` 与 `DFSGenerator` 的搜索循环**逐行相同**，唯一差别是
/// `DFSGenerator` 在扩展分支时多做一次**剪枝约束检查**（`satisfiesPruningConstraints`），
/// 而 `FullSumGenerator` 只在叶节点检查。
///
/// **把这段逻辑写两遍是没有理由的**：两份 150 行的循环里，任何一处修正都会漏掉另一份——
/// 本项目已经因为"同一个量值算术各写一份"漏过一次符号检查。
///
/// `FullSumGenerator` and `DFSGenerator` have **line-for-line identical** search loops. The only difference: `DFSGenerator` performs an extra
/// **pruning constraint check** when expanding a branch, while `FullSumGenerator` checks only at the leaf.
///
/// **There is no reason to write this twice**: with two 150-line copies, any fix applied to one will miss the other — and this project has already missed a
/// sign check once by keeping separate copies of the same quantity arithmetic.
/// </remarks>
internal static class GenerationSumSearch {
    /// <summary>执行搜索 / Run the search.</summary>
    /// <param name="material">物料 / Material.</param>
    /// <param name="input">生成输入 / Generation input.</param>
    /// <param name="widthIndex">宽度索引 / Width index.</param>
    /// <param name="collector">收集器 / Collector.</param>
    /// <param name="constraints">约束列表 / Constraint list.</param>
    /// <param name="planIdPrefix">方案 ID 前缀 / Plan id prefix.</param>
    /// <param name="applyPruningConstraints">扩展时是否检查剪枝约束 / Whether to check pruning constraints while expanding.</param>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <remarks>
    /// 栈帧保存"(入口下标, 已选切片, 已用宽度)"。弹栈时：
    ///
    /// 1. 计算非负剩余宽度；
    /// 2. **只在叶子条件下产出**（入口耗尽，或剩余宽度容不下任何入口）；
    /// 3. 否则压入"跳过当前入口"与"取 1..maxAmount 刀"两个分支。
    ///
    /// **为什么只在叶子产出**：跳过分支会把**同一组切片**沿下标链一路传播，若每个栈帧都产出，
    /// 同一条方案会在下标 1、2、3 各被产出一次。**去重会把重复静默吸收，方案集合看起来完全正常**，
    /// 唯一痕迹是候选计数——这正是 撞到的那一处。
    ///
    /// A frame holds "(entry index, chosen slices, used width)". On pop it: computes the non-negative remainder; **emits only at a leaf** (entries exhausted, or
    /// nothing fits any more); otherwise pushes the "skip this entry" and "cut 1..maxAmount times" branches.
    ///
    /// **Why emit only at a leaf**: the skip branch propagates **the same slice list** along the index chain, so emitting from every frame emits one plan again at
    /// index 1, 2 and 3. **De-duplication silently absorbs the repeats and the plan set looks entirely normal** — the only trace is the candidate counter,
    /// exactly what ran into.
    /// </remarks>
    public static void Run<V>(
        Material<V> material,
        GenerationInput<V> input,
        GenerationWidthIndex<V> widthIndex,
        GenerationCollector<CuttingPlan<V>> collector,
        IReadOnlyList<ICuttingPlanConstraint<V>> constraints,
        string planIdPrefix,
        bool applyPruningConstraints) where V : struct, IComparable<V> {
        Quantity<V> upperBound = material.WidthRange.UpperBound;
        IQuantityArithmetic<V>? arithmetic = GenerationQuantityMath.Resolve(upperBound.Value);
        if (arithmetic is null) {
            return;
        }

        var stack = new Stack<Frame<V>>();
        stack.Push(new Frame<V>(
            Index: 0,
            Slices: new List<CuttingPlanSlice<V>>(),
            UsedWidth: arithmetic.Zero(upperBound.Unit)
        ));

        int planIndex = 0;
        while (stack.Count > 0 && !collector.ShouldStop()) {
            collector.VisitNode();
            Frame<V> frame = stack.Pop();

            Quantity<V>? remaining = GenerationQuantityMath.RemainingNonNegative(upperBound, frame.UsedWidth, arithmetic);
            // **减法成功不等于剩余宽度非负**：算术层不检查符号，必须在这里判。
            // **A successful subtraction does not mean a non-negative remainder**: the arithmetic layer does not check sign, so it must be checked here.
            if (remaining is null || GenerationQuantityMath.IsNegative(remaining, arithmetic)) {
                continue;
            }

            bool isLeaf = frame.Index >= widthIndex.Entries.Count
                || !widthIndex.HasFittableFrom(frame.Index, remaining);
            if (isLeaf) {
                if (frame.Slices.Count > 0) {
                    Emit(material, frame, collector, constraints, planIdPrefix, ref planIndex);
                }
                continue;
            }

            // 分支一：跳过当前入口（数量 0）。
            // Branch one: skip the current entry (amount zero).
            stack.Push(new Frame<V>(Index: frame.Index + 1, Slices: frame.Slices, UsedWidth: frame.UsedWidth));

            // 分支二：取 1..maxAmount 刀。
            // Branch two: cut 1..maxAmount times.
            GenerationProductWidthEntry<V> entry = widthIndex.Entries[frame.Index];
            if (!material.WidthRange.CanCut(entry.Width)) {
                continue;
            }

            Product<V>? product = FindProduct(input, entry.ProductId);
            if (product is null) {
                continue;
            }

            ulong maxAmount = GenerationQuantityMath.MaxRepeatCount(entry.Width, remaining, arithmetic);
            for (ulong amount = 1UL; amount <= maxAmount; ++amount) {
                Quantity<V>? added = GenerationQuantityMath.Repeat(entry.Width, amount, arithmetic);
                if (added is null) {
                    continue;
                }
                Quantity<V>? used = GenerationQuantityMath.Add(frame.UsedWidth, added, arithmetic);
                if (used is null) {
                    continue;
                }

                var slices = new List<CuttingPlanSlice<V>>(frame.Slices.Count + 1);
                slices.AddRange(frame.Slices);
                slices.Add(new CuttingPlanSlice<V>(
                    Production: product,
                    Width: entry.Width,
                    Amount: new UInt64(amount)
                ));

                // `DFSGenerator` 与 `FullSumGenerator` 的唯一差别就在这一行有没有。
                // The only difference between `DFSGenerator` and `FullSumGenerator` is whether this line exists.
                if (applyPruningConstraints
                    && !SatisfiesPruningConstraints(slices, frame.UsedWidth, added, upperBound, material, constraints, arithmetic)) {
                    continue;
                }

                stack.Push(new Frame<V>(Index: frame.Index + 1, Slices: slices, UsedWidth: used));
            }
        }
    }

    private static bool SatisfiesPruningConstraints<V>(
        IReadOnlyList<CuttingPlanSlice<V>> slices,
        Quantity<V> usedWidth,
        Quantity<V> addedWidth,
        Quantity<V> upperBound,
        Material<V> material,
        IReadOnlyList<ICuttingPlanConstraint<V>> constraints,
        IQuantityArithmetic<V> arithmetic) where V : struct {
        bool any = false;
        foreach (ICuttingPlanConstraint<V> constraint in constraints) {
            if (constraint.IsPruning) {
                any = true;
                break;
            }
        }
        if (!any) {
            return true;
        }

        Quantity<V>? total = GenerationQuantityMath.Add(usedWidth, addedWidth, arithmetic);
        if (total is null) {
            return false;
        }

        var stubs = new List<CuttingPlanSliceStub<V>>(slices.Count);
        foreach (CuttingPlanSlice<V> slice in slices) {
            stubs.Add(new CuttingPlanSliceStub<V>(
                ProductionType: slice is ICuttingPlanSliceForCanonicalKey key ? key.ProductionType : "Product",
                ProductionLength: null,
                Width: slice.Width,
                Amount: slice.Amount
            ));
        }

        var context = new CuttingPlanConstraintContext<V>(
            Slices: stubs,
            TotalWidth: total,
            UpperBound: upperBound,
            MaterialId: material.Id
        );

        foreach (ICuttingPlanConstraint<V> constraint in constraints) {
            if (constraint.IsPruning && !constraint.IsSatisfied(context)) {
                return false;
            }
        }
        return true;
    }

    private static bool SatisfiesLeafConstraints<V>(
        IReadOnlyList<CuttingPlanSlice<V>> slices,
        Quantity<V> usedWidth,
        Material<V> material,
        IReadOnlyList<ICuttingPlanConstraint<V>> constraints) where V : struct {
        bool any = false;
        foreach (ICuttingPlanConstraint<V> constraint in constraints) {
            if (!constraint.IsPruning) {
                any = true;
                break;
            }
        }
        if (!any) {
            return true;
        }

        var stubs = new List<CuttingPlanSliceStub<V>>(slices.Count);
        foreach (CuttingPlanSlice<V> slice in slices) {
            stubs.Add(new CuttingPlanSliceStub<V>(
                ProductionType: "Product",
                ProductionLength: null,
                Width: slice.Width,
                Amount: slice.Amount
            ));
        }

        var context = new CuttingPlanConstraintContext<V>(
            Slices: stubs,
            TotalWidth: usedWidth,
            UpperBound: material.WidthRange.UpperBound,
            MaterialId: material.Id
        );

        foreach (ICuttingPlanConstraint<V> constraint in constraints) {
            if (!constraint.IsPruning && !constraint.IsSatisfied(context)) {
                return false;
            }
        }
        return true;
    }

    private static Product<V>? FindProduct<V>(GenerationInput<V> input, string productId) where V : struct {
        foreach (ProductDemand<V> demand in input.Demands) {
            if (string.Equals(demand.Product.Id, productId, StringComparison.Ordinal)) {
                return demand.Product;
            }
        }
        return null;
    }

    private static void Emit<V>(
        Material<V> material,
        Frame<V> frame,
        GenerationCollector<CuttingPlan<V>> collector,
        IReadOnlyList<ICuttingPlanConstraint<V>> constraints,
        string planIdPrefix,
        ref int planIndex) where V : struct {
        var contributions = new List<CuttingPlanDemandContribution<V>>(frame.Slices.Count);
        foreach (CuttingPlanSlice<V> slice in frame.Slices) {
            if (slice.Production is Product<V> product) {
                contributions.Add(new CuttingPlanDemandContribution<V>(product, slice.Width));
            }
        }
        if (contributions.Count == 0) {
            return;
        }

        // **叶节点必须检查非剪枝约束**：那些约束（`IsPruning == false`）只在方案成型时才判定，
        // 中间节点尚未满足是正常的。若这里跳过，**约束就等于不存在**。
        //
        // **Leaves must check non-pruning constraints**: those (`IsPruning == false`) are judged only once a plan is formed, and being unsatisfied at an
        // intermediate node is normal. Skipping this makes **the constraints behave as if absent**.
        if (!SatisfiesLeafConstraints(frame.Slices, frame.UsedWidth, material, constraints)) {
            return;
        }

        var plan = new CuttingPlan<V>(
            id: $"{planIdPrefix}-{material.Id}-{planIndex}",
            material: material,
            slices: frame.Slices,
            demandContributions: contributions
        );
        ++planIndex;

        collector.Record(
            plan,
            candidate => CuttingPlanCanonicalKey.From(candidate),
            feasible: true
        );
    }

    private sealed record Frame<V>(
        int Index,
        IReadOnlyList<CuttingPlanSlice<V>> Slices,
        Quantity<V> UsedWidth
    ) where V : struct;
}
