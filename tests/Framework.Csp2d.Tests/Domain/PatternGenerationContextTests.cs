// 三级断头台与生成上下文的判据 / Criteria for the three-stage generator and the dispatching context.

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
/// **刀序级数由生成器决定，分派由工艺决定** / The generator decides the stage count; the process decides the dispatch.
/// </summary>
/// <remarks>
/// 对应（三级生成器与上下文两部分）。
///
/// Mirrors the three-stage generator and context parts of the implementation.
/// </remarks>
public class PatternGenerationContextTests {
    /// <summary>**三级生成器产出三级刀序，且不受请求里的工艺影响** / The three-stage generator emits a three-stage order regardless of the requested process.</summary>
    [Fact]
    public void ThreeStage_ShouldEmitAThreeStageTree() {
        PatternGenerationResult2 result = Single(
            new ThreeStagePatternGenerator2(),
            FullRowRequest(CuttingProcess2.Freeform));

        result.Pattern.Process.Should().Be(CuttingProcess2.GuillotineThreeStage,
            "the generator decides the process, not the caller");
        result.Pattern.CutTree.Should().NotBeNull();
        result.Pattern.CutTree!.MaxStage().Should().Be(3,
            "a mixed-height row needs a third cut to separate the piece from the waste above");
        // **每一件都必须在第 3 级**：占满整列的那件上方没有余料，但那依然是"第 3 刀"的结果，
        // 报成第 2 级会让刀序自相矛盾（同一级里既有片又有列）。
        // **Every piece must sit at stage 3**: the piece filling its whole column has no waste above, but it is still the result of the third cut, and reporting it as
        // stage 2 would make the order contradict itself (stage 2 holding both columns and pieces).
        result.Pattern.CutTree!.Leaves()
            .Should().OnlyContain(leaf => leaf.PieceId == null || leaf.Stage == 3);
        result.Pattern.Validate(Metres(0.0)).IsOk.Should().BeTrue();
    }

    /// <summary>**等高行货架只要两级** / Uniform rows need two stages only.</summary>
    [Fact]
    public void TwoStage_ShouldEmitATwoStageTree() {
        PatternGenerationResult2 result = Single(
            new ShelfPatternGenerator2(sameHeightRows: true),
            MixedRequest(CuttingProcess2.GuillotineTwoStage));

        result.Pattern.CutTree!.MaxStage().Should().Be(2,
            "the 0.5 metre piece does not join a 1 metre row, so no stage-3 cut is needed");
    }

    /// <summary>**分派只走与工艺相容的那一桶** / Dispatch uses only the bucket compatible with the process.</summary>
    [Fact]
    public void Context_ShouldDispatchByProcess() {
        StubGenerator freeform = new("freeform");
        StubGenerator twoStage = new("two-stage");
        StubGenerator threeStage = new("three-stage");
        var context = new PatternGenerationContext2(
            new List<IPatternGenerator2> { freeform },
            new List<IPatternGenerator2> { twoStage },
            new List<IPatternGenerator2> { threeStage });

        context.Generate(MixedRequest(CuttingProcess2.GuillotineThreeStage)).IsOk.Should().BeTrue();

        threeStage.Calls.Should().Be(1);
        twoStage.Calls.Should().Be(0, "a two-stage generator must not run for a three-stage request");
        freeform.Calls.Should().Be(0);
    }

    /// <summary>**工艺没有生成器时明确拒绝** / A process with no generator is rejected explicitly.</summary>
    [Fact]
    public void Context_ShouldRejectAnEmptyBucket() {
        var context = new PatternGenerationContext2(
            new List<IPatternGenerator2>(),
            new List<IPatternGenerator2> { new StubGenerator("two-stage") },
            new List<IPatternGenerator2> { new StubGenerator("three-stage") });

        Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> result =
            context.Generate(MixedRequest(CuttingProcess2.Freeform));

        result.IsFailed.Should().BeTrue("silently emitting nothing would hide a configuration mistake");
        ((Failed<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("没有配置生成器");
    }

    /// <summary>**历史图案回放并标注来源** / Historical patterns are replayed with their source.</summary>
    [Fact]
    public void Context_ShouldReplayHistoricalPatterns() {
        Pattern2<Flt64> historical = HistoricalPattern("HIST1", 2.0);
        var context = new PatternGenerationContext2(
            new List<IPatternGenerator2> { new StubGenerator("freeform") },
            new List<IPatternGenerator2> { new StubGenerator("two-stage") },
            new List<IPatternGenerator2> { new StubGenerator("three-stage") });

        IReadOnlyList<PatternGenerationResult2> results =
            context.Generate(MixedRequest(CuttingProcess2.Freeform) with {
                ExistingPatterns = new List<Pattern2<Flt64>> { historical },
            }).ValueOrThrow();

        results.Should().Contain(result => result.Pattern.Id.Value == "HIST1" && result.Source == "historical");
        results.Should().HaveCount(2, "the generated pattern and the historical one");
    }

    /// <summary>**回放的重复列不算两次** / A replayed duplicate is not counted twice.</summary>
    [Fact]
    public void Context_ShouldDeduplicateReplaysAgainstGeneratedPatterns() {
        StubGenerator generator = new("dup");
        var context = new PatternGenerationContext2(
            new List<IPatternGenerator2> { generator },
            new List<IPatternGenerator2> { new StubGenerator("two-stage") },
            new List<IPatternGenerator2> { new StubGenerator("three-stage") });

        generator.Emitted = HistoricalPattern("SAME");
        IReadOnlyList<PatternGenerationResult2> results =
            context.Generate(MixedRequest(CuttingProcess2.Freeform) with {
                ExistingPatterns = new List<Pattern2<Flt64>> { HistoricalPattern("SAME") },
            }).ValueOrThrow();

        results.Should().HaveCount(1, "the replayed column is already in the candidate list");
        results[0].Source.Should().Be("dup", "the generated one is kept first");
    }

    /// <summary>按来源计入调用次数、可指定产出的桩生成器 / A stub generator counting calls and emitting one pattern.</summary>
    private sealed class StubGenerator : IPatternGenerator2 {
        public StubGenerator(string source) => Source = source;

        public string Source { get; }

        public int Calls { get; private set; }

        public Pattern2<Flt64>? Emitted { get; set; }

        public Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> Generate(
            PatternGenerationRequest2 request) {
            Calls++;
            Pattern2<Flt64> pattern = Emitted ?? HistoricalPattern("STUB");
            return Results.Ok<IReadOnlyList<PatternGenerationResult2>>(
                new List<PatternGenerationResult2> { new(pattern, Source) });
        }
    }

    /// <summary>取出唯一的候选 / Take the single candidate.</summary>
    /// <param name="generator">生成器 / The generator.</param>
    /// <param name="request">请求 / The request.</param>
    /// <returns>候选 / The candidate.</returns>
    private static PatternGenerationResult2 Single(IPatternGenerator2 generator, PatternGenerationRequest2 request) =>
        generator.Generate(request).ValueOrThrow()[^1];

    /// <summary>构造一个历史图案 / Build a historical pattern.</summary>
    /// <param name="patternId">图案编号 / The pattern identifier.</param>
    /// <param name="size">料域边长，改动它会得到不同的去重键 / The region side; changing it yields a different dedup key.</param>
    /// <returns>图案 / The pattern.</returns>
    /// <remarks>
    /// **去重键不含图案编号**：编号是给外部用的标签，去重按几何——所以"同编号不同几何"与"不同编号同几何"都是两个不同的判定。
    ///
    /// **The key does not contain the pattern id**: the id is an external label while dedup is geometric, so "same id, different geometry" and "different id, same
    /// geometry" are two different decisions.
    /// </remarks>
    private static Pattern2<Flt64> HistoricalPattern(string patternId, double size = 1.0) =>
        new(Csp2dIds.PatternId2Of(patternId),
            MaterialOf(),
            Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(size), Metres(size)),
            new List<Placement2<Flt64>> {
                new(new RectanglePackingShape2<Flt64>(Metres(1.0), Metres(1.0)),
                    new QuantityPoint2<Flt64>(Metres(0.0), Metres(0.0)), false, "P1"),
            },
            new List<Placement2<Flt64>>());

    /// <summary>
    /// 一行正好排满的请求：第二件占满剩余宽度，必然走到"最后一列"那条分支。
    /// / A request whose row is exactly filled: the second piece fills the remaining width and takes the "last column" branch.
    /// </summary>
    /// <param name="process">工艺 / The process.</param>
    /// <returns>请求 / The request.</returns>
    private static PatternGenerationRequest2 FullRowRequest(CuttingProcess2 process) =>
        new(new Material2<Flt64>(Csp2dIds.MaterialId2Of("MAT2"), "mat2", Metres(2.0), Metres(2.0)),
            new List<(Product2<Flt64> Product, ulong Amount)> { (ProductOf("P1", 1.0), 3UL) },
            Metres(0.0),
            process);

    /// <summary>一个高件加一个矮件的请求 / A request with one tall and one short piece.</summary>
    /// <param name="process">工艺 / The process.</param>
    /// <returns>请求 / The request.</returns>
    private static PatternGenerationRequest2 MixedRequest(CuttingProcess2 process) =>
        new(MaterialOf(),
            new List<(Product2<Flt64> Product, ulong Amount)> {
                (ProductOf("P1", 1.0), 1UL),
                (ProductOf("P2", 0.5), 1UL),
            },
            Metres(0.0),
            process);

    /// <summary>构造物料 / Build a material.</summary>
    /// <returns>物料 / The material.</returns>
    private static Material2<Flt64> MaterialOf() =>
        new(Csp2dIds.MaterialId2Of("MAT1"), "mat", Metres(4.0), Metres(2.0));

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
