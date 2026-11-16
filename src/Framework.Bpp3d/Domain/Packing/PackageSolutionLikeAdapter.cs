// 类包装解决方案到装箱程序的适配 / Adapter from a package-solution-like tree to a packing program.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing;

/// <summary>
/// 包装解决方案中的数量 / A quantity inside a package solution.
/// </summary>
/// <remarks>
/// **三种口径不是三种精度，而是三种"知道什么"**：只知道件数、只知道重量、或者两者都知道。
/// 用 `null` 表示"不知道"，而不是用 `0` —— `0` 是"知道，且为零"，这两者在合并时行为完全不同。
///
/// **The three variants are not three precisions but three states of knowledge**: only the count is known, only the weight is known, or both are. `null` means "unknown"
/// rather than `0`; `0` means "known to be zero", and the two behave differently under merging.
/// </remarks>
public abstract record PackageSolutionLikeQuantity {
    /// <summary>按件数 / By item count.</summary>
    /// <param name="Value">件数 / The count.</param>
    public sealed record Amount(ulong Value) : PackageSolutionLikeQuantity;

    /// <summary>按重量 / By weight.</summary>
    /// <param name="Value">重量 / The weight.</param>
    public sealed record Weight(Quantity<FltX> Value) : PackageSolutionLikeQuantity;

    /// <summary>件数与重量 / Both count and weight.</summary>
    /// <param name="AmountValue">件数 / The count.</param>
    /// <param name="WeightValue">重量 / The weight.</param>
    public sealed record AmountAndWeight(ulong AmountValue, Quantity<FltX> WeightValue) : PackageSolutionLikeQuantity;

    /// <summary>
    /// 转为装箱程序材料值 / Convert to a packing program material value.
    /// </summary>
    /// <returns>材料值 / The material value.</returns>
    public PackingProgramMaterialValue ToMaterialValue() => this switch {
        Amount count => new PackingProgramMaterialValue(count.Value),
        Weight weight => new PackingProgramMaterialValue(null, weight.Value),
        AmountAndWeight both => new PackingProgramMaterialValue(both.AmountValue, both.WeightValue),
        _ => throw new System.InvalidOperationException("unknown quantity variant"),
    };
}

/// <summary>
/// 解决方案中的物料项 / A material item inside a solution.
/// </summary>
/// <param name="Material">物料键 / The material key.</param>
/// <param name="QuantityValue">数量 / The quantity.</param>
public sealed record PackageSolutionLikeMaterialItem(
    string Material,
    PackageSolutionLikeQuantity QuantityValue);

/// <summary>
/// 解决方案中的节点 / A node inside a solution tree.
/// </summary>
/// <param name="Shape">形状 / The shape.</param>
/// <param name="MaterialItems">本节点直接持有的物料 / The materials this node holds directly.</param>
/// <param name="Children">子节点 / The child nodes.</param>
public sealed record PackageSolutionLikeNode(
    PackageShape Shape,
    IReadOnlyList<PackageSolutionLikeMaterialItem>? MaterialItems = null,
    IReadOnlyList<PackageSolutionLikeNode>? Children = null) {
    /// <summary>
    /// 递归转换为装箱程序 / Convert recursively into a packing program.
    /// </summary>
    /// <returns>装箱程序或合并失败 / The packing program, or a merge failure.</returns>
    /// <remarks>
    /// **先合并子节点，再合并自己的物料**：反过来的话，同名物料以自己那份为准，而子节点里更多的数量会被静默丢掉。
    /// 合并复用 `PackingProgram.MergeMaterialValue`，不另写一份——两份合并逻辑迟早会分叉。
    ///
    /// **Children are merged first, then this node's own materials**: the other way round, a repeated material would take this node's figure and silently drop the
    /// larger amount held by a child. The merge reuses `PackingProgram.MergeMaterialValue` instead of being written again — two merge implementations would drift apart.
    /// </remarks>
    public Result<PackingProgram, ErrorCode, Error<ErrorCode>> ToPackingProgram() {
        var childPrograms = new List<PackingProgram>();
        if (Children is not null) {
            foreach (PackageSolutionLikeNode child in Children) {
                Result<PackingProgram, ErrorCode, Error<ErrorCode>> converted = child.ToPackingProgram();
                if (converted is not Ok<PackingProgram, ErrorCode, Error<ErrorCode>> convertedOk) {
                    return Propagate(converted);
                }
                childPrograms.Add(convertedOk.Value);
            }
        }

        var merged = new Dictionary<string, PackingProgramMaterialValue>();
        foreach (PackingProgram child in childPrograms) {
            if (child.Materials is null) {
                continue;
            }
            foreach (KeyValuePair<string, PackingProgramMaterialValue> entry in child.Materials) {
                merged.TryGetValue(entry.Key, out PackingProgramMaterialValue? existing);
                Result<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> mergeResult =
                    PackingProgram.MergeMaterialValue(existing, entry.Value);
                if (mergeResult is not Ok<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> mergedOk) {
                    return Propagate(mergeResult is Failed<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> failed
                        ? Results.Failed<PackingProgram>(failed.Error)
                        : (Result<PackingProgram, ErrorCode, Error<ErrorCode>>)new Fatal<PackingProgram, ErrorCode, Error<ErrorCode>>(
                            ((Fatal<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>>)mergeResult).Errors));
                }
                merged[entry.Key] = mergedOk.Value;
            }
        }

        if (MaterialItems is not null) {
            foreach (PackageSolutionLikeMaterialItem item in MaterialItems) {
                merged.TryGetValue(item.Material, out PackingProgramMaterialValue? existing);
                Result<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> mergeResult =
                    PackingProgram.MergeMaterialValue(existing, item.QuantityValue.ToMaterialValue());
                if (mergeResult is not Ok<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> mergedOk) {
                    return Propagate(mergeResult is Failed<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>> failed
                        ? Results.Failed<PackingProgram>(failed.Error)
                        : (Result<PackingProgram, ErrorCode, Error<ErrorCode>>)new Fatal<PackingProgram, ErrorCode, Error<ErrorCode>>(
                            ((Fatal<PackingProgramMaterialValue, ErrorCode, Error<ErrorCode>>)mergeResult).Errors));
                }
                merged[item.Material] = mergedOk.Value;
            }
        }

        return Results.Ok(new PackingProgram(
            Shape: Shape,
            Pattern: null,
            Packages: childPrograms.Count == 0 ? null : childPrograms,
            Materials: merged));
    }

    /// <summary>把节点的失败提升为装箱程序的失败 / Lift a node failure to a program failure.</summary>
    /// <param name="converted">子节点的结果 / The child result.</param>
    /// <returns>装箱程序结果 / The program result.</returns>
    private static Result<PackingProgram, ErrorCode, Error<ErrorCode>> Propagate(
        Result<PackingProgram, ErrorCode, Error<ErrorCode>> converted) => converted switch {
            Failed<PackingProgram, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<PackingProgram>(failed.Error),
            Fatal<PackingProgram, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<PackingProgram, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => throw new System.InvalidOperationException("a non-Ok child must be Failed or Fatal"),
        };
}
