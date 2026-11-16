#nullable enable

using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
/// <summary>
/// 切片结构化去重键 / Structural deduplication key for a cutting plan slice.
/// </summary>
/// <param name="ProductionType">产出类型 / Production type.</param>
/// <param name="ProductionId">产出标识 / Production identifier.</param>
/// <param name="Width">宽度键 / Width key.</param>
/// <param name="Amount">数量 / Amount.</param>
public sealed record CuttingPlanSliceCanonicalKey(
    string ProductionType,
    string ProductionId,
    string Width,
    UInt64 Amount
);

/// <summary>
/// 需求贡献结构化去重键 / Structural deduplication key for demand contribution.
/// </summary>
/// <param name="ProductId">产品标识 / Product identifier.</param>
/// <param name="Unit">单位键 / Unit key.</param>
/// <param name="QuantityValue">需求贡献值 / Demand contribution value.</param>
public sealed record CuttingPlanDemandContributionCanonicalKey(
    string ProductId,
    string Unit,
    string QuantityValue
);

/// <summary>
/// 切割方案结构化去重键 / Structural deduplication key for a cutting plan.
/// </summary>
/// <param name="MaterialId">物料标识 / Material identifier.</param>
/// <param name="MachineId">设备标识 / Machine identifier.</param>
/// <param name="CapacityConsumption">设备产能消耗键 / Machine capacity consumption key.</param>
/// <param name="Slices">切片结构键 / Slice structural keys.</param>
/// <param name="DemandContributions">需求贡献结构键 / Demand contribution structural keys.</param>
public sealed record CuttingPlanCanonicalKey(
    string MaterialId,
    string? MachineId,
    string? CapacityConsumption,
    IReadOnlyList<CuttingPlanSliceCanonicalKey> Slices,
    IReadOnlyList<CuttingPlanDemandContributionCanonicalKey> DemandContributions
) {
    /// <summary>
    /// 从自定义字符串键构造简并 canonical key / Construct a degenerate canonical key from a custom string key.
    /// </summary>
    /// <param name="customKey">自定义键 / Custom key.</param>
    public CuttingPlanCanonicalKey(string customKey)
        : this(
            MaterialId: customKey,
            MachineId: null,
            CapacityConsumption: null,
            Slices: Array.Empty<CuttingPlanSliceCanonicalKey>(),
            DemandContributions: Array.Empty<CuttingPlanDemandContributionCanonicalKey>()
        ) {
    }

    /// <summary>按**内容**比较两个 canonical key / Compare two canonical keys by **content**.</summary>
    /// <remarks>
    /// **record 的默认相等性对列表用的是引用比较**，所以两个**内容完全相同**的 canonical key 会被判为不等
    /// ——**结构化去重因此完全失效**。
    ///
    /// 是在实现 NSame 生成器后由测试发现的：同一需求重复出现两次，产出**两条**方案，
    /// 而内部键集合的 Add 两次都返回 true。
    ///
    /// **这个缺陷比"生成器是空壳"更隐蔽**：生成器一实现，去重看起来就"在工作"
    /// （重复计数存在、逻辑存在），只是**永远不会命中**。
    ///
    /// **The record's default equality compares lists by reference**, so two canonical keys with **identical content** compare unequal — **structural
    /// de-duplication therefore does nothing at all**.
    ///
    /// found this through a test after implementing the NSame generator: the same demand appearing twice produced **two** plans, and the internal key
    /// set's Add returned true both times.
    ///
    /// **This defect is more hidden than "the generator is a shell"**: once the generator is implemented, de-duplication *looks* like it works (the duplicate
    /// counter is there, the logic is there) — it simply never hits.
    /// </remarks>
    /// <param name="other">另一个键 / The other key.</param>
    /// <returns>内容是否相同 / Whether the contents are the same.</returns>
    public bool Equals(CuttingPlanCanonicalKey? other) {
        if (other is null) {
            return false;
        }
        if (ReferenceEquals(this, other)) {
            return true;
        }
        return string.Equals(MaterialId, other.MaterialId, StringComparison.Ordinal)
            && string.Equals(MachineId, other.MachineId, StringComparison.Ordinal)
            && string.Equals(CapacityConsumption, other.CapacityConsumption, StringComparison.Ordinal)
            && SequenceEqual(Slices, other.Slices)
            && SequenceEqual(DemandContributions, other.DemandContributions);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
        var hash = new HashCode();
        hash.Add(MaterialId, StringComparer.Ordinal);
        hash.Add(MachineId, StringComparer.Ordinal);
        hash.Add(CapacityConsumption, StringComparer.Ordinal);
        foreach (CuttingPlanSliceCanonicalKey slice in Slices) {
            hash.Add(slice);
        }
        foreach (CuttingPlanDemandContributionCanonicalKey contribution in DemandContributions) {
            hash.Add(contribution);
        }
        return hash.ToHashCode();
    }

    private static bool SequenceEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right) {
        if (left.Count != right.Count) {
            return false;
        }
        for (int index = 0; index < left.Count; ++index) {
            if (!EqualityComparer<T>.Default.Equals(left[index], right[index])) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 从切割方案构建 canonical key / Build canonical key from a cutting plan.
    /// </summary>
    /// <typeparam name="TPlan">切割方案类型 / Cutting plan type.</typeparam>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <returns>结构化去重键 / Structural deduplication key.</returns>
    public static CuttingPlanCanonicalKey From<TPlan>(TPlan plan)
        where TPlan : ICuttingPlanForCanonicalKey {
        return new CuttingPlanCanonicalKey(
            MaterialId: plan.MaterialId,
            MachineId: plan.MachineId,
            CapacityConsumption: plan.CapacityConsumptionKey,
            Slices: BuildSliceKeys(plan.Slices),
            DemandContributions: BuildDemandContributionKeys(plan.DemandContributions)
        );
    }

    private static IReadOnlyList<CuttingPlanSliceCanonicalKey> BuildSliceKeys(
        IReadOnlyList<ICuttingPlanSliceForCanonicalKey> slices) {
        var grouped = new SortedDictionary<(string, string, string), UInt64>(
            Comparer<(string, string, string)>.Create((a, b) => {
                int c = string.Compare(a.Item1, b.Item1, StringComparison.Ordinal);
                if (c != 0) {
                    return c;
                }

                c = string.Compare(a.Item2, b.Item2, StringComparison.Ordinal);
                if (c != 0) {
                    return c;
                }

                return string.Compare(a.Item3, b.Item3, StringComparison.Ordinal);
            })
        );

        foreach (ICuttingPlanSliceForCanonicalKey slice in slices) {
            (string ProductionType, string, string WidthKey) key = (slice.ProductionType, slice.ProductionId ?? "", slice.WidthKey);
            if (grouped.TryGetValue(key, out UInt64 existing)) {
                grouped[key] = existing.Plus(slice.Amount);
            }
            else {
                grouped[key] = slice.Amount;
            }
        }

        return grouped.Select(kv => new CuttingPlanSliceCanonicalKey(
            ProductionType: kv.Key.Item1,
            ProductionId: kv.Key.Item2,
            Width: kv.Key.Item3,
            Amount: kv.Value
        )).ToList();
    }

    private static IReadOnlyList<CuttingPlanDemandContributionCanonicalKey> BuildDemandContributionKeys(
        IReadOnlyList<ICuttingPlanDemandContributionForCanonicalKey> contributions) {
        var grouped = new SortedDictionary<(string, string), string>();

        foreach (ICuttingPlanDemandContributionForCanonicalKey contribution in contributions) {
            (string ProductId, string UnitKey) key = (contribution.ProductId, contribution.UnitKey);
            if (grouped.ContainsKey(key)) {
                // Accumulate values as string representation for canonical comparison
                grouped[key] = contribution.QuantityValue;
            }
            else {
                grouped[key] = contribution.QuantityValue;
            }
        }

        return grouped.Select(kv => new CuttingPlanDemandContributionCanonicalKey(
            ProductId: kv.Key.Item1,
            Unit: kv.Key.Item2,
            QuantityValue: kv.Value
        )).ToList();
    }
}

/// <summary>
/// 用于 canonical key 构建的切割方案接口 / Cutting plan interface for canonical key construction.
/// </summary>
public interface ICuttingPlanForCanonicalKey {
    /// <summary>物料标识 / Material identifier.</summary>
    string MaterialId { get; }
    /// <summary>设备标识 / Machine identifier.</summary>
    string? MachineId { get; }
    /// <summary>产能消耗键 / Capacity consumption key.</summary>
    string? CapacityConsumptionKey { get; }
    /// <summary>切片列表 / Slice list.</summary>
    IReadOnlyList<ICuttingPlanSliceForCanonicalKey> Slices { get; }
    /// <summary>需求贡献列表 / Demand contribution list.</summary>
    IReadOnlyList<ICuttingPlanDemandContributionForCanonicalKey> DemandContributions { get; }
}

/// <summary>
/// 用于 canonical key 构建的切片接口 / Slice interface for canonical key construction.
/// </summary>
public interface ICuttingPlanSliceForCanonicalKey {
    /// <summary>产出类型 / Production type.</summary>
    string ProductionType { get; }
    /// <summary>产出标识 / Production identifier.</summary>
    string? ProductionId { get; }
    /// <summary>宽度键 / Width key.</summary>
    string WidthKey { get; }
    /// <summary>数量 / Amount.</summary>
    UInt64 Amount { get; }
}

/// <summary>
/// 用于 canonical key 构建的需求贡献接口 / Demand contribution interface for canonical key construction.
/// </summary>
public interface ICuttingPlanDemandContributionForCanonicalKey {
    /// <summary>产品标识 / Product identifier.</summary>
    string ProductId { get; }
    /// <summary>单位键 / Unit key.</summary>
    string UnitKey { get; }
    /// <summary>需求贡献值 / Demand contribution value.</summary>
    string QuantityValue { get; }
}

/// <summary>
/// Canonical key 扩展方法 / Canonical key extension methods.
/// </summary>
public static class CuttingPlanCanonicalKeyExtensions {
    /// <summary>
    /// 按结构化去重键保留首个切割方案 / Keep the first cutting plan for each structural deduplication key.
    /// </summary>
    /// <typeparam name="T">切割方案类型 / Cutting plan type.</typeparam>
    /// <param name="plans">切割方案列表 / Cutting plan list.</param>
    /// <param name="keySelector">键选择器 / Key selector.</param>
    /// <returns>去重后的切割方案列表 / Deduplicated cutting plans.</returns>
    public static List<T> DistinctByCanonicalKey<T>(
        this IEnumerable<T> plans,
        Func<T, CuttingPlanCanonicalKey> keySelector) {
        var seen = new HashSet<CuttingPlanCanonicalKey>();
        var result = new List<T>();
        foreach (T? plan in plans) {
            if (seen.Add(keySelector(plan))) {
                result.Add(plan);
            }
        }
        return result;
    }
}
