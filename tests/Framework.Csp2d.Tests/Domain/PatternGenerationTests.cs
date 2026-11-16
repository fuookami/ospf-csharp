// 图案生成判据 / Criteria for pattern generation.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.PatternGeneration;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Domain;

/// <summary>
/// **料域上界、检验数与候选整理** / Region bounds, reduced cost and candidate selection.
/// </summary>
/// <remarks>
/// 对应基线 `PatternGeneration.kt`。这里全是纯函数与纯校验，**不需要求解器**。
///
/// Mirrors the baseline `PatternGeneration.kt`. is pure validation and pure functions: **no solver needed**.
/// </remarks>
public class PatternGenerationTests {
    /// <summary>**请求校验拦住四类错误** / Request validation stops four kinds of mistake.</summary>
    [Fact]
    public void Validate_ShouldRejectNegativeKerfEmptyProductsAndZeroAmounts() {
        Message(RequestOf(kerf: -0.5).Validate()).Should().Contain("刀缝不能为负");
        Message(RequestOf(products: new List<(Product2<Flt64>, ulong)>()).Validate()).Should().Contain("没有可选产品");
        Message(RequestOf(amount: 0UL).Validate()).Should().Contain("剩余件数为零");

        Product2<Flt64> bad = new(Csp2dIds.ProductId2Of("P0"), "bad", Metres(0.0), Metres(1.0));
        Message(RequestOf(product: bad).Validate()).Should().Contain("宽高必须为正");

        RequestOf().Validate().IsOk.Should().BeTrue("a well-formed request must pass");
    }

    /// <summary>**定尺取板高，卷材按"每件一行"相加并扣掉一次刀缝** / Sheet mode uses the height; coil mode sums per-piece rows and removes one kerf.</summary>
    /// <remarks>
    /// 只按单件高度算会让两块以上的排样装不下，而报错看起来像"物料不够"。
    ///
    /// Using one piece's height alone would make any multi-piece layout overflow, and the failure would look like "not enough material".
    /// </remarks>
    [Fact]
    public void RegionHeightBound_ShouldSumPerPieceRowsInCoilMode() {
        PatternGenerationRequest2 sheet = RequestOf(material: MaterialOf(coil: false));
        PatternGenerationRequest2 coil = RequestOf(material: MaterialOf(coil: true));

        sheet.RegionHeightBound().Value.ToDouble().Should().BeApproximately(2.0, 1e-12);
        coil.RegionHeightBound().Value.ToDouble().Should().BeApproximately(2.5, 1e-12,
            "two 1 metre rows plus two kerfs, minus one kerf");
        coil.TargetRegionHeight().Value.ToDouble().Should().BeApproximately(2.5, 1e-12);
        sheet.TargetRegionHeight().Value.ToDouble().Should().BeApproximately(2.0, 1e-12,
            "sheet mode ignores the derived bound entirely");
    }

    /// <summary>**最大段高只截断更小的情形** / The maximum segment height only truncates a larger bound.</summary>
    [Fact]
    public void RegionHeightBound_ShouldRespectTheMaximumSegmentHeight() {
        (double small, double large) = (Frame2Of(maxSegment: 1.0).RegionHeightBound().Value.ToDouble(),
            Frame2Of(maxSegment: 5.0).RegionHeightBound().Value.ToDouble());

        small.Should().BeApproximately(1.0, 1e-12, "the derived 2.5 metres is truncated");
        large.Should().BeApproximately(2.5, 1e-12, "a larger limit does not stretch the bound");
    }

    /// <summary>**检验数 = 余料×权重 − 影子价格×面积** / The reduced cost is waste times weight minus price times area.</summary>
    [Fact]
    public void PatternReducedCost_ShouldSubtractTheDualValue() {
        Pattern2<Flt64> pattern = PatternOf(placements: 1);
        var prices = new Dictionary<string, Flt64> { ["P1"] = new Flt64(5.0) };

        Csp2dPatternGenerationSupport.PatternReducedCost(pattern, prices, Flt64.One)
            .Value.ToDouble().Should().BeApproximately(2.0, 1e-12, "7 square metres of waste minus a dual of 5");

        prices["P1"] = new Flt64(10.0);
        Csp2dPatternGenerationSupport.PatternReducedCost(pattern, prices, Flt64.One)
            .Value.ToDouble().Should().BeApproximately(-3.0, 1e-12, "a dual above the waste makes the pattern improving");

        Csp2dPatternGenerationSupport.PatternReducedCost(pattern, new Dictionary<string, Flt64>(), Flt64.One)
            .Value.ToDouble().Should().BeApproximately(7.0, 1e-12, "no prices means the bare waste");
    }

    /// <summary>**只保留改进图案，缺检验数的保留** / Only improving patterns survive, and an unpriced one is kept.</summary>
    [Fact]
    public void ImprovingPatterns_ShouldDropNonNegativeAndKeepUnpriced() {
        IReadOnlyList<PatternGenerationResult2> results = new List<PatternGenerationResult2> {
            Result2With(3.0),
            Result2With(-1.0),
            new(PatternOf(placements: 1), "generator"),
            Result2With(-4.0),
        };

        IReadOnlyList<PatternGenerationResult2> improving =
            Csp2dPatternGenerationSupport.ImprovingPatterns(results);

        improving.Should().HaveCount(3, "the +3 pattern is the only non-improving one");
        improving.Should().Contain(result => result.ReducedCost == null);
        improving[0].ReducedCost!.Value.ToDouble().Should().BeApproximately(-4.0, 1e-12,
            "improving patterns are ordered by ascending reduced cost");
    }

    /// <summary>**去重键与放置顺序无关，且截断前不重复占名额** / The key ignores placement order, and duplicates do not eat the quota.</summary>
    [Fact]
    public void DistinctPatterns_ShouldDeduplicateIndependentOfOrder() {
        Pattern2<Flt64> ordered = PatternOf(placements: 2, reversed: false);
        Pattern2<Flt64> reversed = PatternOf(placements: 2, reversed: true);

        Csp2dPatternGenerationSupport.PatternKey(ordered)
            .Should().Be(Csp2dPatternGenerationSupport.PatternKey(reversed),
                "the placement order does not change the geometry");

        IReadOnlyList<PatternGenerationResult2> deduplicated =
            Csp2dPatternGenerationSupport.DistinctPatterns(
                new List<PatternGenerationResult2> {
                    new(ordered, "gen"),
                    new(reversed, "gen"),
                    new(PatternOf(placements: 3), "gen"),
                }, 2);

        deduplicated.Should().HaveCount(2, "the duplicate must not consume the second slot");
    }

    /// <summary>构造请求 / Build a request.</summary>
    /// <param name="material">物料 / The material.</param>
    /// <param name="kerf">刀缝 / The kerf.</param>
    /// <param name="amount">件数 / The piece count.</param>
    /// <param name="product">产品 / The product.</param>
    /// <param name="products">产品与件数列表 / The product and amount list.</param>
    /// <returns>请求 / The request.</returns>
    private static PatternGenerationRequest2 RequestOf(
        Material2<Flt64>? material = null,
        double kerf = 0.5,
        ulong amount = 2UL,
        Product2<Flt64>? product = null,
        IReadOnlyList<(Product2<Flt64> Product, ulong Amount)>? products = null) =>
        new(
            Material: material ?? MaterialOf(coil: true),
            Products: products ?? new List<(Product2<Flt64>, ulong)> {
                (product ?? ProductOf(), amount),
            },
            Kerf: Metres(kerf));

    /// <summary>构造只用于上界的请求 / Build a request used only for the bound.</summary>
    /// <param name="maxSegment">最大段高 / The maximum segment height.</param>
    /// <returns>请求 / The request.</returns>
    private static PatternGenerationRequest2 Frame2Of(double maxSegment) =>
        RequestOf() with { MaxSegmentHeight = Metres(maxSegment) };

    /// <summary>构造带检验数的结果 / Build a result with a reduced cost.</summary>
    /// <param name="reducedCost">检验数数值 / The reduced cost value.</param>
    /// <returns>结果 / The result.</returns>
    private static PatternGenerationResult2 Result2With(double reducedCost) =>
        new(PatternOf(placements: 1), "gen", SquareMetres(reducedCost));

    /// <summary>构造图案 / Build a pattern.</summary>
    /// <param name="placements">放置件数 / The placement count.</param>
    /// <param name="reversed">放置是否倒序 / Whether the placements are reversed.</param>
    /// <returns>图案 / The pattern.</returns>
    private static Pattern2<Flt64> PatternOf(int placements, bool reversed = false) {
        var list = new List<Placement2<Flt64>>();
        for (int index = 0; index < placements; index++) {
            list.Add(new Placement2<Flt64>(
                new RectanglePackingShape2<Flt64>(Metres(1.0), Metres(1.0)),
                new QuantityPoint2<Flt64>(Metres(index), Metres(0.0)),
                false,
                "P1"));
        }
        if (reversed) {
            list.Reverse();
        }
        return new(
            Csp2dIds.PatternId2Of("PAT1"),
            MaterialOf(coil: false),
            Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(4.0), Metres(2.0)),
            list,
            new List<Placement2<Flt64>>());
    }

    /// <summary>构造物料 / Build a material.</summary>
    /// <param name="coil">是否为卷材 / Whether it is a coil.</param>
    /// <returns>物料 / The material.</returns>
    private static Material2<Flt64> MaterialOf(bool coil) =>
        new(Csp2dIds.MaterialId2Of("MAT1"), "mat", Metres(4.0), coil ? null : Metres(2.0));

    /// <summary>构造产品 / Build a product.</summary>
    /// <returns>产品 / The product.</returns>
    private static Product2<Flt64> ProductOf() =>
        new(Csp2dIds.ProductId2Of("P1"), "part", Metres(1.0), Metres(1.0));

    /// <summary>取出失败消息 / Extract the failure message.</summary>
    /// <param name="result">校验结果 / The validation result.</param>
    /// <returns>消息 / The message.</returns>
    private static string Message(Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        result is Failed<Success, ErrorCode, Error<ErrorCode>> failed ? failed.Error.Message : string.Empty;

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
