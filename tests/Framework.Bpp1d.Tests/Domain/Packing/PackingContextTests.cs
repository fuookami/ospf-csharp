// 一维装箱物理结果提取判据 / Criteria for one-dimensional packing physical extraction.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.Packing;

/// <summary>
/// **一维装箱物理结果提取** / One-dimensional packing physical extraction.
/// </summary>
/// <remarks>
/// 对应`PackingContext.pack`。**本函数有四道校验，缺一道就会静默给出错结果**：
///
/// | 校验 | 漏掉的后果 | 判据 |
/// | --- | --- | --- |
/// | 精确需求 | 少切或多切都算通过 | `ShouldRejectAnUnderfilledDemand` |
/// | 箱型库存 | 超用库存无人发现 | `ShouldRejectExceedingTheBinInventory` |
/// | 展开预算 | 大件数把内存打爆 | `ShouldRejectExceedingTheExpansionBudget` |
/// | 物料汇总溢出 | `ulong` 回绕成小数 | （见备注） |
///
/// **物料溢出判据没写**：要触发 `ulong` 溢出，单个物品的物料贡献得接近 `ulong.MaxValue`，
/// 而这样的输入在 `ValidatePattern` 之前就会被需求复核挡住。**这是真实的可达性问题，不是遗漏**——
/// 为了覆盖它得造一个自相矛盾的夹具，那会让判据失去意义。
///
/// **The material-overflow criterion is not written**.
/// triggering a `ulong` overflow needs a per-item contribution near `ulong.MaxValue`, and such an input is already rejected by the demand check before it can reach the
/// summary. **That is a real reachability gap, not an omission** — covering it would require a self-contradictory fixture, which would make the criterion meaningless.
/// </remarks>
public class PackingContextTests {
    /// <summary>**正常展开：放置位置、余量与利用率** / Normal expansion: placements, remnants and utilisation.</summary>
    /// <remarks>
    /// 箱长 10、物品长 2、间距 1、需求 2 件 ⇒ 一箱装 2 件，占用 `2+1+2=5`，余量 5。
    /// 利用率 = 已装箱长 4 / 总箱长 10 = 0.4。
    ///
    /// Bin length 10, item length 2, spacing 1, demand 2 ⇒ one bin holds both pieces, usage `2+1+2=5`, remnant 5. Utilisation = packed 4 / total bin length 10 = 0.4.
    /// </remarks>
    [Fact]
    public void Pack_ShouldPlacePiecesAndReportTheRemnant() {
        PackingProblem<Flt64> problem = MakeProblem(binLength: 10.0, itemLength: 2.0, spacing: 1.0, demand: 2);
        var usages = new Dictionary<PackingPattern<Flt64>, ulong> {
            [new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["I1"] = 2 })] = 1,
        };

        Result<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PackingContext<Flt64>().Pack(problem, usages);

        result.IsOk.Should().BeTrue(result.ToString());
        PackingResult<Flt64> packed = ((Ok<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        packed.Plans.Should().HaveCount(1, "two pieces fit in one bin");
        packed.Plans[0].Placements.Should().HaveCount(2);
        packed.Plans[0].Placements[0].Start.Value.ToDouble().Should().BeApproximately(
            0.0, 1e-9, "the first piece starts at the origin");
        packed.Plans[0].Placements[1].Start.Value.ToDouble().Should().BeApproximately(
            3.0, 1e-9, "the second piece starts after the first (2) plus one gap (1)");
        packed.Remnants.Should().HaveCount(1);
        packed.Remnants[0].Length.Value.ToDouble().Should().BeApproximately(
            5.0, 1e-9, "usage is 2 + 1 + 2 = 5, so 5 remains; a trailing gap would make it 4");
        packed.Utilization.ToDouble().Should().BeApproximately(
            0.4, 1e-9, "packed 4 over a total bin length of 10");
    }

    /// <summary>**需求少切必须被拒** / Under-filling the demand must be rejected.</summary>
    [Fact]
    public void Pack_ShouldRejectAnUnderfilledDemand() {
        PackingProblem<Flt64> problem = MakeProblem(binLength: 10.0, itemLength: 2.0, spacing: 1.0, demand: 3);
        var usages = new Dictionary<PackingPattern<Flt64>, ulong> {
            [new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["I1"] = 2 })] = 1,
        };

        Result<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PackingContext<Flt64>().Pack(problem, usages);

        result.IsFailed.Should().BeTrue("the demand is 3 but the patterns only cover 2");
        ((Failed<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.ApplicationError, "this is a verification failure, not an argument error");
    }

    /// <summary>**超出箱型库存必须被拒** / Exceeding the bin-type inventory must be rejected.</summary>
    [Fact]
    public void Pack_ShouldRejectExceedingTheBinInventory() {
        PackingProblem<Flt64> problem = MakeProblem(
            binLength: 10.0, itemLength: 2.0, spacing: 1.0, demand: 4, inventory: 1);
        var usages = new Dictionary<PackingPattern<Flt64>, ulong> {
            [new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["I1"] = 2 })] = 2,
        };

        Result<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PackingContext<Flt64>().Pack(problem, usages);

        result.IsFailed.Should().BeTrue("two bins are used but only one is available");
        ((Failed<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.ApplicationError);
    }

    /// <summary>**展开预算必须拦住多个方案累计超限** / The budget must stop the cumulative total across patterns.</summary>
    /// <remarks>
    /// **第一版判据太弱，变异存活。** 原来用"一个方案 100 件、预算 10"，
    /// 于是**乘法保护**（`amount > limit / copies`）先拦住了，
    /// **累计检查**（`expanded > limit`）根本没被执行——把累计检查改成恒假，判据仍然绿。
    ///
    /// 现在用**两个方案各 8 件、预算 10**：单个都不超（8 ≤ 10），但累计 16 超限。
    /// **乘法保护对单个方案不触发**，只有累计检查能拦住。
    ///
    /// **The first criterion was too weak and the mutant survived.** It used "one pattern with 100 pieces, budget 10", so the **multiplication guard**
    /// (`amount > limit / copies`) fired first and the **cumulative check** (`expanded > limit`) never ran — disabling the cumulative check kept the criterion green.
    /// Now **two patterns of 8 pieces each with a budget of 10**: neither exceeds alone (8 &lt;= 10) but the total 16 does, so only the cumulative check can catch it.
    /// </remarks>
    [Fact]
    public void Pack_ShouldRejectExceedingTheExpansionBudgetCumulatively() {
        PackingProblem<Flt64> problem = MakeProblem(binLength: 1000.0, itemLength: 1.0, spacing: 0.0, demand: 16);
        var usages = new Dictionary<PackingPattern<Flt64>, ulong> {
            [new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["I1"] = 8 })] = 1,
            [new PackingPattern<Flt64>("B2", new Dictionary<string, ulong> { ["I1"] = 8 })] = 1,
        };

        Result<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PackingContext<Flt64>().Pack(problem, usages, expansionLimit: 10);

        result.IsFailed.Should().BeTrue(
            "8 pieces alone fit the budget of 10, but 8 + 8 = 16 exceeds it; only the cumulative check can catch this");
    }

    /// <summary>**负预算必须被拒** / A negative budget must be rejected.</summary>
    [Fact]
    public void Pack_ShouldRejectANegativeExpansionBudget() {
        PackingProblem<Flt64> problem = MakeProblem(binLength: 10.0, itemLength: 2.0, spacing: 1.0, demand: 2);
        var usages = new Dictionary<PackingPattern<Flt64>, ulong> {
            [new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["I1"] = 2 })] = 1,
        };

        Result<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PackingContext<Flt64>().Pack(problem, usages, expansionLimit: -1);

        result.IsFailed.Should().BeTrue("a negative budget cannot be satisfied");
    }

    /// <summary>**物料汇总按件累加** / Material totals accumulate per piece.</summary>
    [Fact]
    public void Pack_ShouldAccumulateMaterialsPerPiece() {
        var problem = new PackingProblem<Flt64>(
            Items: new List<Item<Flt64>> {
                new(
                    "I1", Millimetres(2.0), Grams(1.0), 3,
                    new Dictionary<string, ulong> { ["M1"] = 2 }),
            },
            BinTypes: new List<BinType<Flt64>> {
                new("B1", Millimetres(10.0), Grams(1000.0), new Flt64(1.0)),
            },
            Spacing: Millimetres(0.0));
        var usages = new Dictionary<PackingPattern<Flt64>, ulong> {
            [new PackingPattern<Flt64>("B1", new Dictionary<string, ulong> { ["I1"] = 3 })] = 1,
        };

        Result<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new PackingContext<Flt64>().Pack(problem, usages);

        result.IsOk.Should().BeTrue(result.ToString());
        PackingResult<Flt64> packed = ((Ok<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        packed.Materials.Should().ContainKey("M1");
        packed.Materials["M1"].Should().Be(
            6UL,
            "three pieces at two material units each is six; a per-pattern instead of per-piece accumulation would give two");
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="binLength">箱长（毫米）/ Bin length in millimetres.</param>
    /// <param name="itemLength">物品长（毫米）/ Item length in millimetres.</param>
    /// <param name="spacing">间距（毫米）/ Spacing in millimetres.</param>
    /// <param name="demand">需求量 / Demand.</param>
    /// <param name="inventory">箱型库存，`null` 表示不限 / Bin inventory, `null` for unlimited.</param>
    /// <returns>问题 / The problem.</returns>
    private static PackingProblem<Flt64> MakeProblem(
        double binLength,
        double itemLength,
        double spacing,
        ulong demand,
        ulong? inventory = null) =>
        new(
            Items: new List<Item<Flt64>> {
                new("I1", Millimetres(itemLength), Grams(1.0), demand),
            },
            BinTypes: new List<BinType<Flt64>> {
                new("B1", Millimetres(binLength), Grams(1000.0), new Flt64(1.0), inventory),
                new("B2", Millimetres(binLength), Grams(1000.0), new Flt64(1.0), inventory),
            },
            Spacing: Millimetres(spacing));

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造克量 / Build a gram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Grams(double value) =>
        new(new Flt64(value), MassUnits.Gram);
}
