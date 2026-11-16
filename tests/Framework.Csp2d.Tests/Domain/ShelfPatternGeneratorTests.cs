// 货架生成器判据 / Criteria for the shelf pattern generator.

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
/// **货架排样的工艺约束与行距守卫** / The process constraint and the row-pitch guard of shelf nesting.
/// </summary>
/// <remarks>
/// 对应。混高与等高的差别不是开关，而是**合法排样的族**。
///
///. Mixed versus uniform rows is not a switch but a **family of legal layouts**.
/// </remarks>
public class ShelfPatternGeneratorTests {
    /// <summary>**行内混高不能当两级断头台** / Mixed rows cannot be a two-stage guillotine.</summary>
    [Fact]
    public void Generate_ShouldRejectMixedRowsForTwoStage() {
        Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> mixed =
            new ShelfPatternGenerator2(sameHeightRows: false).Generate(RequestOf(process: CuttingProcess2.GuillotineTwoStage));

        mixed.IsFailed.Should().BeTrue("a mixed-height shelf layout needs a third cut");
        ((Failed<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>>)mixed).Error.Message
            .Should().Contain("不是两级断头台");

        new ShelfPatternGenerator2(sameHeightRows: true)
            .Generate(RequestOf(process: CuttingProcess2.GuillotineTwoStage))
            .IsOk.Should().BeTrue("uniform rows are exactly a two-stage guillotine");
    }

    /// <summary>**卷材把料域收缩到实际段高，定尺保留整块板** / Coil mode shrinks the region; sheet mode keeps the whole sheet.</summary>
    [Fact]
    public void Generate_ShouldShrinkTheRegionOnlyInCoilMode() {
        PatternGenerationResult2 coil = Single(RequestOf(material: MaterialOf(coil: true)));
        PatternGenerationResult2 sheet = Single(RequestOf(material: MaterialOf(coil: false)));

        coil.Pattern.Region.Height.Value.ToDouble().Should().BeApproximately(1.0, 1e-12,
            "one row of 1 metre is the consumed segment height");
        sheet.Pattern.Region.Height.Value.ToDouble().Should().BeApproximately(2.0, 1e-12,
            "the sheet keeps its full height, the rest becomes a remnant");
    }

    /// <summary>**行距按"行高 + 刀缝"判定，最后一行不会被挤出料域** / The pitch guard keeps the last row inside the region.</summary>
    /// <remarks>
    /// 只用裸行高比较会产出超出料域的排样，随后 `Validate` 失败——错误看起来像"没有合法方案"，
    /// 而真实原因是那行本来就不该放进去。
    ///
    /// Comparing the bare height would emit an out-of-region layout and then fail validation — the failure would read as "no feasible pattern" while the real cause is
    /// that the row never belonged there.
    /// </remarks>
    [Fact]
    public void Generate_ShouldRespectTheRowPitch() {
        Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> result =
            new ShelfPatternGenerator2(sameHeightRows: true).Generate(RequestOf(amount: 9UL, kerf: 0.5));

        result.IsOk.Should().BeTrue();
        Single(RequestOf(amount: 9UL, kerf: 0.5), sameHeight: true).Pattern.Placements.Count.Should().Be(3,
            "a 2 metre sheet holds one 1 metre row plus its 0.5 metre kerf, and 9 pieces do not fit");
    }

    /// <summary>**候选过多时保留末尾（覆盖最大）** / Too many candidates keep the tail, which covers the most.</summary>
    [Fact]
    public void Generate_ShouldKeepTheLongestPrefixes() {
        Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> result =
            new ShelfPatternGenerator2(sameHeightRows: true).Generate(
                RequestOf(amount: 9UL, kerf: 0.0, material: MaterialOf(coil: false, height: 4.0), maxCandidates: 2));

        IReadOnlyList<PatternGenerationResult2> patterns = result.ValueOrThrow();
        patterns.Should().HaveCount(2, "the candidate limit is two");

        patterns[0].Pattern.Placements.Count.Should().Be(8, "the shortest prefix is dropped, not the longest");
        patterns[1].Pattern.Placements.Count.Should().Be(9, "the longest prefix covers everything");
    }

    /// <summary>**行内等高只接受与行高等高的件** / Uniform rows accept only pieces as tall as the row.</summary>
    [Fact]
    public void Generate_ShouldRejectShorterPiecesInUniformRows() {
        PatternGenerationResult2 uniform = Single(MixedRowRequest(), sameHeight: true);
        PatternGenerationResult2 mixed = Single(MixedRowRequest());

        // **判据看"矮件被放进了哪一行"，不看件数**：两种模式最后都可能用满料域，
        // 差别恰恰在于等高模式把矮件推到下一行，而混高模式让它在同一行里贴着高件。
        // **The criterion is which row the shorter piece lands in, not the count**: both modes can fill the region, and the difference is exactly that uniform rows push
        // the short piece onto its own row while mixed rows keep it beside the tall one.
        ShortPieceY(uniform).Should().Be(1.0, "uniform rows put the 0.5 metre piece on a row of its own");
        ShortPieceY(mixed).Should().Be(0.0, "mixed rows keep it on the 1 metre row");

        uniform.Pattern.Validate(Metres(0.0)).IsOk.Should().BeTrue("both layouts must remain geometrically legal");
    }

    /// <summary>**只有需要切分树的工艺才产出切分树** / Only processes requiring a cut tree emit one.</summary>
    [Fact]
    public void Generate_ShouldEmitACutTreeOnlyWhenTheProcessRequiresIt() {
        PatternGenerationResult2 twoStage = Single(
            RequestOf(process: CuttingProcess2.GuillotineTwoStage), sameHeight: true);
        PatternGenerationResult2 freeform = Single(RequestOf(process: CuttingProcess2.Freeform), sameHeight: true);

        twoStage.Pattern.CutTree.Should().NotBeNull("a two-stage guillotine must document its cut sequence");
        freeform.Pattern.CutTree.Should().BeNull("arbitrary nesting has no cut sequence to document");
        twoStage.Pattern.Validate(Metres(0.0)).IsOk.Should().BeTrue(
            "the emitted tree must match the placements it was built from");
    }

    /// <summary>取出唯一的候选 / Take the single candidate.</summary>
    /// <param name="request">请求 / The request.</param>
    /// <param name="sameHeight">是否要求行内等高 / Whether rows must be uniform.</param>
    /// <returns>候选 / The candidate.</returns>
    private static PatternGenerationResult2 Single(PatternGenerationRequest2 request, bool sameHeight = false) {
        IReadOnlyList<PatternGenerationResult2> results =
            new ShelfPatternGenerator2(sameHeight).Generate(request).ValueOrThrow();
        return results[^1];
    }

    /// <summary>矮件所在的 y 坐标 / The y coordinate the short piece landed on.</summary>
    /// <param name="result">候选 / The candidate.</param>
    /// <returns>y 坐标 / The y coordinate.</returns>
    private static double ShortPieceY(PatternGenerationResult2 result) {
        foreach (Placement2<Flt64> placement in result.Pattern.Placements) {
            if (placement.ItemId == "P2") {
                return placement.Y.Value.ToDouble();
            }
        }
        return double.NaN;
    }

    /// <summary>一个高件加两个矮件的请求 / A request with one tall and two short pieces.</summary>
    /// <returns>请求 / The request.</returns>
    private static PatternGenerationRequest2 MixedRowRequest() =>
        new(MaterialOf(coil: false, height: 1.5, width: 2.0),
            new List<(Product2<Flt64> Product, ulong Amount)> {
                (ProductOf("P1", 1.0), 1UL),
                (ProductOf("P2", 0.5), 2UL),
            },
            Metres(0.0));

    /// <summary>构造请求 / Build a request.</summary>
    /// <param name="material">物料 / The material.</param>
    /// <param name="kerf">刀缝 / The kerf.</param>
    /// <param name="amount">件数 / The piece count.</param>
    /// <param name="process">工艺 / The process.</param>
    /// <param name="mixedProduct">是否混入一个矮件 / Whether to add a shorter piece.</param>
    /// <param name="maxCandidates">最大候选数 / The candidate limit.</param>
    /// <returns>请求 / The request.</returns>
    private static PatternGenerationRequest2 RequestOf(
        Material2<Flt64>? material = null,
        double kerf = 0.0,
        ulong amount = 3UL,
        CuttingProcess2 process = CuttingProcess2.Freeform,
        bool mixedProduct = false,
        int maxCandidates = 32) {
        var products = new List<(Product2<Flt64> Product, ulong Amount)> {
            (ProductOf("P1", 1.0), amount),
        };
        if (mixedProduct) {
            products.Add((ProductOf("P2", 0.5), 1UL));
        }
        return new PatternGenerationRequest2(
            material ?? MaterialOf(coil: false),
            products,
            Metres(kerf),
            process,
            MaxCandidates: maxCandidates);
    }

    /// <summary>构造物料 / Build a material.</summary>
    /// <param name="coil">是否为卷材 / Whether it is a coil.</param>
    /// <param name="height">板高 / The sheet height.</param>
    /// <param name="width">板宽 / The sheet width.</param>
    /// <returns>物料 / The material.</returns>
    private static Material2<Flt64> MaterialOf(bool coil, double height = 2.0, double width = 4.0) =>
        new(Csp2dIds.MaterialId2Of("MAT1"), "mat", Metres(width), coil ? null : Metres(height));

    /// <summary>构造产品 / Build a product.</summary>
    /// <param name="productId">产品编号 / The product identifier.</param>
    /// <param name="height">件高 / The piece height.</param>
    /// <returns>产品 / The product.</returns>
    private static Product2<Flt64> ProductOf(string productId, double height) =>
        new(Csp2dIds.ProductId2Of(productId), "part-" + productId, Metres(1.0), Metres(height));

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
