// 标准执行器配置（B648）的判据 / Criteria for the standard-executor config (B648).

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **标准执行器配置（B648）** / The standard-executor config (B648).
/// </summary>
/// <remarks>
/// 判据的重点：**`SheetAmountWeight` 默认极小**（它是平局权重，不是普通权重）、**两个容差默认 `1e-6`**、
/// 以及**未给字典时是空表而不是 null**（下游遍历不会炸）。
///
/// The criteria focus on **`SheetAmountWeight` defaulting to a tiny value** (it is a tie-break weight, not an ordinary one), **both tolerances defaulting to `1e-6`**, and **unset dictionaries
/// being empty rather than null** so downstream iteration cannot throw.
/// </remarks>
public class ColumnGenerationStandardExecutorConfigTests {
    /// <summary>
    /// **板数权重默认 `1e-6`，不是 1** / The sheet-amount weight defaults to 1e-6, not 1.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 默认配置的板数权重是 `1e-6`，**比面积权重（1）小六个数量级**——两者相等就是"板数与余料同等重要"，会多选整板。
    /// 2. 面积权重默认 1。
    /// 3. 类型成本权重默认 0（未配置成本时不参与目标）。
    /// 4. **显式传值可以覆盖三个默认**。
    ///
    /// Four paired points: the default sheet-amount weight is `1e-6`, **six orders of magnitude below the area weight of 1**, and making them equal would treat sheets and waste alike and
    /// add perfectly filled sheets; the area weight defaults to 1; the type-cost weight defaults to 0, keeping unconfigured costs out of the objective; and **explicit values override all three**.
    /// </remarks>
    [Fact]
    public void Config_ShouldKeepTheSheetAmountWeightTiny() {
        ColumnGenerationStandardExecutorConfig config = new();

        config.SheetAmountWeightValue.ToFlt64().ToDouble().Should().Be(1e-6);
        config.SheetAreaWeightValue.ToFlt64().ToDouble().Should().Be(1.0);
        config.SheetTypeCostWeightValue.ToFlt64().ToDouble().Should().Be(0.0);
        config.SheetAmountWeightValue.ToFlt64().ToDouble()
            .Should().BeLessThan(config.SheetAreaWeightValue.ToFlt64().ToDouble() / 1000.0,
                "sheet count is a tie-break weight, not a peer of sheet area");

        ColumnGenerationStandardExecutorConfig tuned = config with
        {
            SheetAreaWeight = new FltX(2.0),
            SheetAmountWeight = new FltX(0.5),
            SheetTypeCostWeight = new FltX(0.25),
        };
        tuned.SheetAreaWeightValue.ToFlt64().ToDouble().Should().Be(2.0);
        tuned.SheetAmountWeightValue.ToFlt64().ToDouble().Should().Be(0.5);
        tuned.SheetTypeCostWeightValue.ToFlt64().ToDouble().Should().Be(0.25);
    }

    /// <summary>
    /// **两个容差默认 `1e-6`，名字前缀区分 RMP 与最终** / Both tolerances default to 1e-6, and the name prefixes distinguish RMP from final.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 整数性容差默认 `1e-6`——零容差会让整数解永远不可行。
    /// 2. 检验数容差默认 `1e-6`。
    /// 3. 两个模型名前缀不同（`bpp2d-rmp` / `bpp2d-final`）：同名会让求解器日志里两个阶段的模型混在一起。
    /// 4. 两个"是否记录模型"默认都关。
    ///
    /// Four paired points: the integrality tolerance defaults to `1e-6`, since a zero tolerance makes any integer solution infeasible; the reduced-cost tolerance defaults to `1e-6`; the two
    /// model-name prefixes differ, because a shared name would mix the two phases in the solver log; and both logging switches default to off.
    /// </remarks>
    [Fact]
    public void Config_ShouldCarryTwoTolerancesAndTwoPrefixes() {
        ColumnGenerationStandardExecutorConfig config = new();

        config.IntegralityToleranceValue.ToFlt64().ToDouble().Should().Be(1e-6);
        config.ReducedCostToleranceValue.ToFlt64().ToDouble().Should().Be(1e-6);
        config.RmpSolveNamePrefix.Should().Be("bpp2d-rmp");
        config.FinalSolveNamePrefix.Should().Be("bpp2d-final");
        config.RmpSolveNamePrefix.Should().NotBe(config.FinalSolveNamePrefix);
        config.RmpToLogModel.Should().BeFalse();
        config.FinalToLogModel.Should().BeFalse();
        config.EnableFinalCapacityConstraint.Should().BeTrue("the capacity constraint is on by default");
    }

    /// <summary>
    /// **未给字典时是空表，不是 null** / Unset dictionaries are empty, not null.
    /// </remarks>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 默认配置的两张表**可遍历且为空**——下游不必判空。
    /// 2. 显式给表时**原样返回**，不复制、不丢键。
    /// 3. 从显式表切回默认（`with { SheetTypeCosts = null }`）仍然是空表，不会退化成 null。
    ///
    /// Three paired points: the two default maps **iterate fine and are empty**, so downstream need not null-check; explicitly supplied maps **come back as given**, uncopied and with no keys
    /// dropped; and switching back to the default via `with { SheetTypeCosts = null }` still yields an empty map rather than null.
    /// </remarks>
    [Fact]
    public void Config_ShouldReturnEmptyMapsWhenUnset() {
        ColumnGenerationStandardExecutorConfig config = new();
        config.CostMap.Should().BeEmpty();
        config.AmountCoefficientMap.Should().BeEmpty();
        foreach (KeyValuePair<Fuookami.Ospf.Framework.Bpp2d.Domain.SheetTypeId, FltX> _ in config.CostMap) {
            Assert.Fail("the default cost map must be empty");
        }

        Dictionary<Fuookami.Ospf.Framework.Bpp2d.Domain.SheetTypeId, FltX> costs = new()
        {
            [new Fuookami.Ospf.Framework.Bpp2d.Domain.SheetTypeId("S1")] = new FltX(3.0),
        };
        ColumnGenerationStandardExecutorConfig configured = config with { SheetTypeCosts = costs };
        configured.CostMap.Should().HaveCount(1);
        configured.CostMap[new Fuookami.Ospf.Framework.Bpp2d.Domain.SheetTypeId("S1")]
            .ToFlt64().ToDouble().Should().Be(3.0);

        ColumnGenerationStandardExecutorConfig cleared = configured with { SheetTypeCosts = null };
        cleared.CostMap.Should().BeEmpty("clearing a map falls back to the default, never to null");
    }
}
