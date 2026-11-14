#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 把行来源映射回原始约束 / Mapping row origins back onto original constraints.
/// </summary>
/// <remarks>
/// **两条规则都会"安静地"出错，而且都是多给一条。** 多给一条比少给一条更难发现：
/// **读者看到有数值就以为验过了。**
///
/// **Both rules fail quietly, and both give one value too many.** One too many is harder to spot than one too few: **a reader who sees a number assumes it was checked.**
/// </remarks>
public class ProvenanceDualMappingTests {
    private static readonly ConstraintId RowA = new("c:row-a");
    private static readonly ConstraintId RowB = new("c:row-b");
    private static readonly ConstraintId Intermediate = new("c:intermediate");

    private static IReadOnlySet<ConstraintId> Originals() => new HashSet<ConstraintId> { RowA, RowB };

    /// <summary>**正常映射** / The ordinary mapping.</summary>
    [Fact]
    public void TheOrdinaryMapping_ShouldCarryEveryProvenancedRow() {
        IReadOnlyDictionary<ConstraintId, Flt64> duals = ProvenanceDualMapping.MapDuals(
            rowProvenance: [RowA, RowB],
            rowOrigins: ["row-0", "row-1"],
            dualsByOrigin: new Dictionary<string, Flt64> {
                ["row-0"] = new Flt64(1.5),
                ["row-1"] = new Flt64(2.5),
            },
            originalIds: Originals());

        duals.Should().HaveCount(2);
        duals[RowA].ToDouble().Should().Be(1.5);
        duals[RowB].ToDouble().Should().Be(2.5);
    }

    /// <summary>**两条行指向同一来源时，整条丢弃** / A source with two rows is dropped entirely.</summary>
    /// <remarks>
    /// **这是"多给一条"的第一种形状。** 取哪一条都是编的，**而两条都在 `dualsByOrigin` 里，所以代码不会报错。**
    ///
    /// **The first shape of "one too many".** Picking either would be inventing it, **and since both keys exist in `dualsByOrigin`, nothing throws.**
    /// </remarks>
    [Fact]
    public void TheDuplicatedSource_ShouldBeDropped() {
        IReadOnlyDictionary<ConstraintId, Flt64> duals = ProvenanceDualMapping.MapDuals(
            rowProvenance: [RowA, RowA, RowB],
            rowOrigins: ["row-0", "row-1", "row-2"],
            dualsByOrigin: new Dictionary<string, Flt64> {
                ["row-0"] = new Flt64(1.5),
                ["row-1"] = new Flt64(9.5),
                ["row-2"] = new Flt64(2.5),
            },
            originalIds: Originals());

        duals.Should().NotContainKey(RowA,
            "two rows point at it, so any single value would be invented");
        duals.Should().ContainKey(RowB, "the unambiguous source still maps");
        duals.Should().HaveCount(1);
    }

    /// <summary>**不在原始集合里的来源不算** / A source outside the original set does not count.</summary>
    /// <remarks>
    /// **"多给一条"的第二种形状。** 中间层自己造的行会被当成原始约束的敏感度报出去。
    ///
    /// **The second shape.** An intermediate-layer row would be reported as an original constraint's sensitivity.
    /// </remarks>
    [Fact]
    public void TheIntermediateSource_ShouldBeIgnored() {
        IReadOnlyDictionary<ConstraintId, Flt64> duals = ProvenanceDualMapping.MapDuals(
            rowProvenance: [RowA, Intermediate],
            rowOrigins: ["row-0", "row-1"],
            dualsByOrigin: new Dictionary<string, Flt64> {
                ["row-0"] = new Flt64(1.5),
                ["row-1"] = new Flt64(7.5),
            },
            originalIds: Originals());

        duals.Should().NotContainKey(Intermediate,
            "an intermediate row is not an original constraint");
        duals.Should().HaveCount(1);
    }

    /// <summary>**没有来源或没有后端键的行不算** / A row without a source or without an origin does not count.</summary>
    [Fact]
    public void TheUnprovenancedRows_ShouldBeSkipped() {
        IReadOnlyDictionary<ConstraintId, Flt64> duals = ProvenanceDualMapping.MapDuals(
            rowProvenance: [null, RowA, RowB],
            rowOrigins: [null, "row-1", "row-2"],
            dualsByOrigin: new Dictionary<string, Flt64> {
                ["row-1"] = new Flt64(1.5),
                ["row-2"] = new Flt64(2.5),
            },
            originalIds: Originals());

        duals.Should().HaveCount(2, "only the two fully provenanced rows map");
    }

    /// <summary>**两个列表长度不一致时不抛异常** / Mismatched list lengths do not throw.</summary>
    /// <remarks>
    /// 基线取 `getOrNull`：**短的那一侧只是"这行没有来源"，不是错误。**
    /// 用索引器会抛异常，**而抛异常会让整份敏感度报告消失。**
    ///
    /// The baseline takes `getOrNull`: **the short side simply means "this row has no origin", not an error.** The indexer throws, **and a throw loses the whole sensitivity report.**
    /// </remarks>
    [Fact]
    public void TheMismatchedLengths_ShouldNotThrow() {
        Action act = () => ProvenanceDualMapping.MapDuals(
            rowProvenance: [RowA, RowB],
            rowOrigins: ["row-0"],
            dualsByOrigin: new Dictionary<string, Flt64> { ["row-0"] = new Flt64(1.5) },
            originalIds: Originals());

        act.Should().NotThrow("a short origin list means some rows have no origin, not a failure");
    }

    /// <summary>**区间映射与对偶映射用同一套规则** / The range mapping follows the same rules.</summary>
    /// <remarks>
    /// **反向对照**：只测对偶那一侧，一个"区间映射忘了查 `originalIds`"的实现照样通过。
    ///
    /// **The reverse control**: testing only the dual side lets an implementation whose range mapping forgets `originalIds` pass.
    /// </remarks>
    [Fact]
    public void TheRangeMapping_ShouldFollowTheSameRules() {
        var range = new SensitivityRange(new Flt64(-1.0), new Flt64(1.0));

        IReadOnlyDictionary<ConstraintId, SensitivityRange> ranges = ProvenanceDualMapping.MapRanges(
            rowProvenance: [RowA, Intermediate, RowB, RowB],
            rowOrigins: ["row-0", "row-1", "row-2", "row-3"],
            rangesByOrigin: new Dictionary<string, SensitivityRange> {
                ["row-0"] = range,
                ["row-1"] = range,
                ["row-2"] = range,
                ["row-3"] = range,
            },
            originalIds: Originals());

        ranges.Should().HaveCount(1, "only RowA is unambiguous and original");
        ranges.Should().ContainKey(RowA);
    }
}
