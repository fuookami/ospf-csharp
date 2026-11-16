// 方案与判据 / Plans and small checks.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Domain.Material;

/// <summary>
/// **方案与图案的一致性** / Plan and pattern consistency.
/// </summary>
/// <remarks>
/// 对应的后半段：`CuttingPlan2`、`cuttingPlan2Of`、`fitsIn`、`remnantReaches`。
///
/// Mirrors the tail of the implementation: `CuttingPlan2`, `cuttingPlan2Of`, `fitsIn`, `remnantReaches`.
/// </remarks>
public class CuttingPlan2Tests {
    /// <summary>**贡献由图案件数自动推导** / Contributions are derived from the pattern piece counts.</summary>
    [Fact]
    public void CuttingPlan2Of_ShouldDeriveContributionsFromThePattern() {
        Pattern2<Flt64> pattern = PatternOf("PAT1", MaterialOf("MAT1"), "P1", pieces: 2);
        IReadOnlyDictionary<string, Product2<Flt64>> products = Catalogue();

        Result<CuttingPlan2<Flt64>, ErrorCode, Error<ErrorCode>> plan = CuttingPlan2Factory.CuttingPlan2Of(
            Csp2dIds.PlanId2Of("PLAN1"), pattern, products);

        plan.IsOk.Should().BeTrue(plan.ToString());
        plan.ValueOrThrow().ContributionList.Should().ContainSingle();
        plan.ValueOrThrow().ContributionList[0].Amount.Should().Be(2UL);
        plan.ValueOrThrow().MachineId.Should().BeNull("the material declares no machine");
        plan.ValueOrThrow().Validate(Metres(0.0)).IsOk.Should().BeTrue();
    }

    /// <summary>**未知产品必须让构造失败，不能静默跳过** / An unknown product must fail the build rather than be skipped.</summary>
    /// <remarks>
    /// 少一条贡献会让**需求凭空变小**，而方案本身看上去完全合法。
    ///
    /// A skipped contribution **shrinks the demand out of thin air** while the plan still looks perfectly legal.
    /// </remarks>
    [Fact]
    public void CuttingPlan2Of_ShouldRejectAnUnknownProduct() {
        Pattern2<Flt64> pattern = PatternOf("PAT1", MaterialOf("MAT1"), "P-unknown", pieces: 1);

        Result<CuttingPlan2<Flt64>, ErrorCode, Error<ErrorCode>> plan = CuttingPlan2Factory.CuttingPlan2Of(
            Csp2dIds.PlanId2Of("PLAN1"), pattern, Catalogue());

        plan.IsFailed.Should().BeTrue();
        ((Failed<CuttingPlan2<Flt64>, ErrorCode, Error<ErrorCode>>)plan).Error.Message
            .Should().Contain("未知产品");
    }

    /// <summary>**物料不一致被拒** / A material mismatch is rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectAMaterialMismatch() {
        Pattern2<Flt64> pattern = PatternOf("PAT1", MaterialOf("MAT1"), "P1", pieces: 1);
        Material2<Flt64> other = MaterialOf("MAT2");

        CuttingPlan2<Flt64> plan = new(
            Csp2dIds.PlanId2Of("PLAN1"), other, pattern, null, null);

        Result<Success, ErrorCode, Error<ErrorCode>> result = plan.Validate(Metres(0.0));
        result.IsFailed.Should().BeTrue();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("物料与图案物料不一致");
    }

    /// <summary>**重复贡献与数量不符都被拒** / Duplicate contributions and a count mismatch are both rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectDuplicateAndMismatchedContributions() {
        Pattern2<Flt64> pattern = PatternOf("PAT1", MaterialOf("MAT1"), "P1", pieces: 2);
        IReadOnlyDictionary<string, Product2<Flt64>> products = Catalogue();

        CuttingPlan2<Flt64> duplicated = new(
            Csp2dIds.PlanId2Of("PLAN-DUP"),
            pattern.Material,
            pattern,
            new List<CuttingPlanDemandContribution2<Flt64>> {
                new(products["P1"], 1UL),
                new(products["P1"], 1UL),
            },
            null);
        Message(duplicated.Validate(Metres(0.0))).Should().Contain("重复产品");

        CuttingPlan2<Flt64> mismatched = new(
            Csp2dIds.PlanId2Of("PLAN-MIS"),
            pattern.Material,
            pattern,
            new List<CuttingPlanDemandContribution2<Flt64>> { new(products["P1"], 3UL) },
            null);
        Message(mismatched.Validate(Metres(0.0))).Should().Contain("贡献与图案件数不一致");
    }

    /// <summary>**产能消耗必须为正** / The capacity consumption must be positive.</summary>
    [Fact]
    public void Validate_ShouldRejectNonPositiveCapacity() {
        Pattern2<Flt64> pattern = PatternOf("PAT1", MaterialOf("MAT1"), "P1", pieces: 1);
        CuttingPlan2<Flt64> plan = new(
            Csp2dIds.PlanId2Of("PLAN1"),
            pattern.Material,
            pattern,
            new List<CuttingPlanDemandContribution2<Flt64>> { new(Catalogue()["P1"], 1UL) },
            Metres(0.0));

        Message(plan.Validate(Metres(0.0))).Should().Contain("产能消耗必须为正");
    }

    /// <summary>**旋转只在允许时才算一种容纳方式** / A rotation only counts as a way to fit when it is allowed.</summary>
    [Fact]
    public void FitsIn_ShouldOnlyRotateWhenAllowed() {
        Region2<Flt64> region = Region(1.0, 3.0);

        Pattern2Checks.FitsIn(region, Metres(1.0), Metres(2.0), false).Should().BeTrue("it fits directly");
        Pattern2Checks.FitsIn(region, Metres(2.0), Metres(1.0), false).Should().BeFalse(
            "a 2 metre width does not fit a 1 metre region without rotating");
        Pattern2Checks.FitsIn(region, Metres(2.0), Metres(1.0), true).Should().BeTrue(
            "rotated, the 2 metre side lies along the 3 metre axis");
    }

    /// <summary>**余料阈值判定** / The remnant threshold check.</summary>
    [Fact]
    public void RemnantReaches_ShouldHandleMissingAndPresentThresholds() {
        Pattern2Checks.RemnantReaches(SquareMetres(0.5), null).Should().BeTrue();
        Pattern2Checks.RemnantReaches(SquareMetres(0.0), null).Should().BeFalse();
        Pattern2Checks.RemnantReaches(SquareMetres(0.5), SquareMetres(0.5)).Should().BeTrue();
        Pattern2Checks.RemnantReaches(SquareMetres(0.4), SquareMetres(0.5)).Should().BeFalse();
    }

    /// <summary>取出失败消息 / Extract the failure message.</summary>
    /// <param name="result">校验结果 / The validation result.</param>
    /// <returns>消息 / The message.</returns>
    private static string Message(Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        result is Failed<Success, ErrorCode, Error<ErrorCode>> failed ? failed.Error.Message : string.Empty;

    /// <summary>产品目录 / The product catalogue.</summary>
    /// <returns>目录 / The catalogue.</returns>
    private static IReadOnlyDictionary<string, Product2<Flt64>> Catalogue() {
        var products = new Dictionary<string, Product2<Flt64>>();
        products["P1"] = new(Csp2dIds.ProductId2Of("P1"), "part", Metres(1.0), Metres(1.0), true, null);
        return products;
    }

    /// <summary>构造图案 / Build a pattern.</summary>
    /// <param name="patternId">图案编号 / The pattern identifier.</param>
    /// <param name="material">物料 / The material.</param>
    /// <param name="itemId">产品编号 / The product identifier.</param>
    /// <param name="pieces">件数 / The piece count.</param>
    /// <returns>图案 / The pattern.</returns>
    private static Pattern2<Flt64> PatternOf(
        string patternId,
        Material2<Flt64> material,
        string itemId,
        int pieces) {
        var placements = new List<Placement2<Flt64>>();
        for (int index = 0; index < pieces; index++) {
            placements.Add(new Placement2<Flt64>(
                new RectanglePackingShape2<Flt64>(Metres(1.0), Metres(1.0)),
                new QuantityPoint2<Flt64>(Metres(index), Metres(0.0)),
                false,
                itemId));
        }
        return new(Csp2dIds.PatternId2Of(patternId), material, Region(4.0, 2.0), placements,
            new List<Placement2<Flt64>>());
    }

    /// <summary>构造物料 / Build a material.</summary>
    /// <param name="materialId">物料编号 / The material identifier.</param>
    /// <returns>物料 / The material.</returns>
    private static Material2<Flt64> MaterialOf(string materialId) =>
        new(Csp2dIds.MaterialId2Of(materialId), "steel", Metres(4.0), Metres(2.0));

    /// <summary>以原点构造区域 / Build a region anchored at the origin.</summary>
    /// <param name="width">宽度 / The width.</param>
    /// <param name="height">高度 / The height.</param>
    /// <returns>区域 / The region.</returns>
    private static Region2<Flt64> Region(double width, double height) =>
        Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(width), Metres(height));

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造平方米量 / Build an area in square metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> SquareMetres(double value) =>
        new(new Flt64(value), AreaUnits.SquareMeter);
}
