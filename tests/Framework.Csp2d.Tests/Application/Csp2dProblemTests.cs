// 二维套裁问题判据 / Criteria for the 2D cutting stock problem.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp2d.Application.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Application;

/// <summary>
/// **问题校验、主目标选择与件数上界** / Problem validation, primary objective selection and the piece bound.
/// </summary>
/// <remarks>
/// 对应。`sheetConfig`（板长分配配置）随 `SheetAssignmentContext` 一起推迟，见批次记录。
///
///. `sheetConfig` (the sheet assignment configuration) is deferred together with `SheetAssignmentContext`; see the batch record.
/// </remarks>
public class Csp2dProblemTests {
    /// <summary>**问题校验拦住五类错误** / Validation stops five kinds of mistake.</summary>
    [Fact]
    public void Validate_ShouldRejectEmptyBadAndDuplicatedDefinitions() {
        Message(ProblemOf(new List<Material2<Flt64>>(), Demands(("P1", 4UL))).Validate())
            .Should().Contain("没有物料");
        Message(ProblemOf(Materials(), new List<ProductDemand2<Flt64>>()).Validate())
            .Should().Contain("没有需求");

        Material2<Flt64> badWidth = new(Csp2dIds.MaterialId2Of("M0"), "bad", Metres(0.0), Metres(2.0));
        Message(ProblemOf(new List<Material2<Flt64>> { badWidth }, Demands(("P1", 4UL))).Validate())
            .Should().Contain("幅宽必须为正");

        Message(ProblemOf(
            Materials(),
            new List<ProductDemand2<Flt64>> { Demand("P1", 4UL), Demand("P1", 5UL) }).Validate())
            .Should().Contain("重复产品");

        Costar2<Flt64> badCostar = new(Csp2dIds.CostarId2Of("C0"), "filler", Metres(0.0), Metres(1.0));
        Message(ProblemOf(Materials(), Demands(("P1", 4UL)), new List<Costar2<Flt64>> { badCostar }).Validate())
            .Should().Contain("宽高必须为正");

        ProblemOf(Materials(), Demands(("P1", 4UL))).Validate().IsOk.Should().BeTrue(
            "a well-formed problem must pass");
    }

    /// <summary>**全卷材才用段高口径，混料一律用张数** / The segment measure applies only to pure coils; mixed material uses sheet count.</summary>
    /// <remarks>
    /// 混料时用段高会把定尺板也算进"长度"，那不是同一个量纲的口径。
    ///
    /// Mixing materials under the segment measure would count sheets as "length", which is not the same measure at all.
    /// </remarks>
    [Fact]
    public void PrimaryObjective_ShouldFollowTheMaterialKindsUnlessConfigured() {
        Csp2dProblem coils = ProblemOf(CoilMaterials(), Demands(("P1", 4UL)));
        Csp2dProblem sheets = ProblemOf(Materials(), Demands(("P1", 4UL)));

        coils.PrimaryObjective().Should().Be(Csp2dObjectiveKind.ConsumedLength);
        sheets.PrimaryObjective().Should().Be(Csp2dObjectiveKind.SheetCount);

        Csp2dProblem configured = sheets with {
            Config = new Csp2dConfiguration(Primary: Csp2dObjectiveKind.Waste),
        };
        configured.PrimaryObjective().Should().Be(Csp2dObjectiveKind.Waste,
            "an explicit configuration always wins");
    }

    /// <summary>**件数上界只统计件数口径，且最低为 1** / The piece bound counts amount-unit demands only and is floored at 1.</summary>
    [Fact]
    public void TotalPieceDemand_ShouldSumAmountUnitsAndFloorAtOne() {
        ProblemOf(Materials(), Demands(("P1", 4UL), ("P2", 7UL))).TotalPieceDemand()
            .Should().Be(11UL);

        var nonAmount = new List<ProductDemand2<Flt64>> {
            new(Product("P1"), new DemandValue2<Flt64>.Area(SquareMetres(2.0))),
            new(Product("P1"), new DemandValue2<Flt64>.Weight(Kilograms(3.0))),
        };
        ProblemOf(Materials(), nonAmount).TotalPieceDemand().Should().Be(1UL,
            "a zero bound would make every plan unusable");
    }

    /// <summary>**覆盖判定只看件数口径** / Coverage only checks amount-unit demands.</summary>
    [Fact]
    public void Covers_ShouldOnlyCheckAmountDemands() {
        Csp2dProblem problem = ProblemOf(Materials(), Demands(("P1", 4UL)));
        IReadOnlyDictionary<string, int> enough = new Dictionary<string, int> { ["P1"] = 4 };
        IReadOnlyDictionary<string, int> short_ = new Dictionary<string, int> { ["P1"] = 3 };

        SolutionOf(enough).Covers(problem).Should().BeTrue();
        SolutionOf(short_).Covers(problem).Should().BeFalse();

        var areaOnly = new List<ProductDemand2<Flt64>> {
            new(Product("P2"), new DemandValue2<Flt64>.Area(SquareMetres(2.0))),
        };
        SolutionOf(new Dictionary<string, int>()).Covers(ProblemOf(Materials(), areaOnly)).Should().BeTrue(
            "an area demand has no piece count to compare against");
    }

    /// <summary>**产品映射按编号索引** / The product map is keyed by identifier.</summary>
    [Fact]
    public void ProductsById_ShouldKeyByValue() {
        Csp2dProblem problem = ProblemOf(
            Materials(),
            Demands(("P1", 1UL)),
            products: new List<Product2<Flt64>> { Product("P1"), Product("P2") });

        problem.ProductsById().Should().HaveCount(2).And.ContainKey("P2");
    }

    /// <summary>取出失败消息 / Extract the failure message.</summary>
    /// <param name="result">校验结果 / The validation result.</param>
    /// <returns>消息 / The message.</returns>
    private static string Message(Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        result is Failed<Success, ErrorCode, Error<ErrorCode>> failed ? failed.Error.Message : string.Empty;

    /// <summary>构造一个解 / Build a solution.</summary>
    /// <param name="coverage">覆盖表 / The coverage map.</param>
    /// <returns>解 / The solution.</returns>
    private static Csp2dSolution SolutionOf(IReadOnlyDictionary<string, int> coverage) =>
        new(new List<Csp2dPlanUsage>(), 0.0, 1, 0.0, 0.0, 0.0, coverage, 1);

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="materials">物料 / The materials.</param>
    /// <param name="demands">需求 / The demands.</param>
    /// <param name="costars">副产物 / The by-products.</param>
    /// <param name="products">产品 / The products.</param>
    /// <returns>问题 / The problem.</returns>
    private static Csp2dProblem ProblemOf(
        IReadOnlyList<Material2<Flt64>> materials,
        IReadOnlyList<ProductDemand2<Flt64>> demands,
        IReadOnlyList<Costar2<Flt64>>? costars = null,
        IReadOnlyList<Product2<Flt64>>? products = null) =>
        new(materials, products ?? new List<Product2<Flt64>> { Product("P1") }, demands, Metres(0.5),
            Costars: costars, YieldConfig: new Csp2dYieldConfig());

    /// <summary>定尺物料 / A fixed-size material.</summary>
    /// <returns>物料列表 / The material list.</returns>
    private static IReadOnlyList<Material2<Flt64>> Materials() =>
        new List<Material2<Flt64>> {
            new(Csp2dIds.MaterialId2Of("MAT1"), "steel", Metres(4.0), Metres(2.0)),
        };

    /// <summary>卷材 / A coil material.</summary>
    /// <returns>物料列表 / The material list.</returns>
    private static IReadOnlyList<Material2<Flt64>> CoilMaterials() =>
        new List<Material2<Flt64>> {
            new(Csp2dIds.MaterialId2Of("MAT1"), "coil", Metres(4.0), null),
        };

    /// <summary>构造需求列表 / Build a demand list.</summary>
    /// <param name="entries">编号与件数 / Identifiers and piece counts.</param>
    /// <returns>需求列表 / The demand list.</returns>
    private static IReadOnlyList<ProductDemand2<Flt64>> Demands(params (string Id, ulong Count)[] entries) {
        var demands = new List<ProductDemand2<Flt64>>();
        foreach ((string id, ulong count) in entries) {
            demands.Add(Demand(id, count));
        }
        return demands;
    }

    /// <summary>构造一条件数需求 / Build one amount demand.</summary>
    /// <param name="productId">产品编号 / The product identifier.</param>
    /// <param name="count">件数 / The piece count.</param>
    /// <returns>需求 / The demand.</returns>
    private static ProductDemand2<Flt64> Demand(string productId, ulong count) =>
        new(Product(productId), new DemandValue2<Flt64>.Amount(count));

    /// <summary>构造产品 / Build a product.</summary>
    /// <param name="productId">产品编号 / The product identifier.</param>
    /// <returns>产品 / The product.</returns>
    private static Product2<Flt64> Product(string productId) =>
        new(Csp2dIds.ProductId2Of(productId), "part-" + productId, Metres(1.0), Metres(1.0));

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造平方米量 / Build an area in square metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> SquareMetres(double value) =>
        new(new Flt64(value), AreaUnits.SquareMeter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);
}
