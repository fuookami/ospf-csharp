// 尾板优化器的判据 / Criteria for the tail sheet optimiser.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Application;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// **覆盖与超出是两个数，选列是词典序，裁剪逐件消耗** / Coverage and overshoot are two numbers, selection is lexicographic, and trimming consumes piece by piece.
/// </summary>
/// <remarks>
/// 三处都容易合并成一个"看起来更简洁"的量：把覆盖减超出会得到净收益，把词典序换成加权和会选错列，
/// 把逐件消耗换成按比例会造出半件物品。
///
/// All three invite a "cleaner-looking" collapse: subtracting overshoot from coverage yields a net gain, replacing the lexicographic order with a weighted sum picks the
/// wrong column, and scaling instead of consuming piece by piece produces fractions of a piece.
/// </remarks>
public class TailSheetOptimizerTests {
    /// **覆盖封顶在需求上，超出单独计数** / Coverage is capped at the demand and the excess is counted separately.
    [Fact]
    public void Evaluate_ShouldCapCoverageAndCountTheOvershoot() {
        Layer<FltX> layer = LayerOf(("A", 10));

        LayerEvaluation evaluation = TailSheetOptimizer.Evaluate(
            layer, new Dictionary<string, ulong> { ["A"] = 4UL });

        evaluation.Covered.Should().Be(4, "producing 10 against a demand of 4 covers 4");
        evaluation.Wasted.Should().Be(6, "the other 6 are wasted, not negative coverage");
    }

    /// <summary>
    /// **覆盖多者胜，即使它浪费更多** / More coverage wins, even when it wastes more.
    /// </summary>
    /// <remarks>
    /// 这条区分"词典序"与"净收益"：宽列覆盖 5 浪费 5（净值 0），窄列覆盖 3 浪费 0（净值 3）。
    /// 若按净值排，窄列会赢——而它对尾板的意义更小，因为它少装了 2 件。
    ///
    /// This separates the lexicographic order from a net gain: the wide column covers 5 and wastes 5 (net 0) while the narrow one covers 3 and wastes 0 (net 3). Ranked by
    /// net gain the narrow column wins — yet it matters less for a tail sheet because it packs 2 fewer pieces.
    /// </remarks>
    [Fact]
    public void Choose_ShouldPreferCoverageOverALowerNetGain() {
        // 宽列：A 恰好、B 过量 ⇒ 覆盖 6、超出 7，净值 -1。窄列：只有 A ⇒ 覆盖 3、超出 0，净值 3。
        // 按净值排窄列会赢；按词典序排宽列该赢，因为它多装 3 件。
        // Wide: A exact, B over-produced => covers 6, wastes 7, net -1. Narrow: A only => covers 3, wastes 0, net 3.
        // Ranked by net the narrow one wins; ranked lexicographically the wide one must win because it packs 3 more pieces.
        Layer<FltX> wide = LayerOf(("A", 3), ("B", 10));
        Layer<FltX> narrow = LayerOf(("A", 3));
        var remaining = new Dictionary<string, ulong> { ["A"] = 3UL, ["B"] = 3UL };

        LayerEvaluation? best = TailSheetOptimizer.Choose(
            original: null, new List<Layer<FltX>> { narrow, wide }, remaining);

        best.Should().NotBeNull();
        best!.Covered.Should().Be(6, "coverage is the first key, so the column covering more wins");
        best.Wasted.Should().Be(7, "the overshoot is counted separately and does not cancel the coverage");
    }

    /// **覆盖相同时浪费少者胜** / When coverage ties, less waste wins.
    [Fact]
    public void Choose_ShouldPreferLessWasteOnEqualCoverage() {
        Layer<FltX> wasteful = LayerOf(("A", 20));
        Layer<FltX> tight = LayerOf(("A", 5));
        var remaining = new Dictionary<string, ulong> { ["A"] = 5UL };

        LayerEvaluation? best = TailSheetOptimizer.Choose(
            original: null, new List<Layer<FltX>> { wasteful, tight }, remaining);

        best.Should().NotBeNull();
        best!.Covered.Should().Be(5, "both cover the whole demand");
        best.Wasted.Should().Be(0, "the tight column wastes nothing");
    }

    /// **覆盖与浪费都相同时原列优先** / When both tie, the original column wins.
    [Fact]
    public void Choose_ShouldPreferTheOriginalColumnOnAFullTie() {
        Layer<FltX> original = LayerOf(("A", 5));
        Layer<FltX> other = LayerOf(("A", 5));
        var remaining = new Dictionary<string, ulong> { ["A"] = 5UL };

        LayerEvaluation? best = TailSheetOptimizer.Choose(
            original, new List<Layer<FltX>> { other }, remaining);

        best.Should().NotBeNull();
        best!.Layer.Should().BeSameAs(original,
            "a needless swap makes the tail layout inconsistent with the earlier sheets");
    }

    /// **没有任何候选时返回空** / No candidates yields null.
    [Fact]
    public void Choose_ShouldReturnNullWithoutCandidates() {
        TailSheetOptimizer.Choose(
                original: null,
                System.Array.Empty<Layer<FltX>>(),
                new Dictionary<string, ulong> { ["A"] = 1UL })
            .Should().BeNull();
    }

    /// <summary>
    /// **裁剪逐件消耗预算，超出预算的件被丢掉** / Trimming consumes the budget piece by piece and drops the excess.
    /// </summary>
    /// <remarks>
    /// 层里有 3 件 `A`，剩余需求 2 件：裁剪后必须**恰好**剩 2 件。按比例缩放的实现会给出 2 件（这里碰巧相同）
    /// 或非整数件；而把预算判断写成"小于零才跳过"的实现会留下 3 件。
    ///
    /// The layer holds 3 pieces of `A` and the residual demand is 2: after trimming it must hold **exactly** 2. A proportional implementation gives 2 here by coincidence
    /// (or a fractional count), while one whose budget test is "skip only when negative" leaves all 3.
    /// </remarks>
    [Fact]
    public void TrimLayer_ShouldKeepExactlyTheRemainingBudget() {
        Layer<FltX> layer = LayerOf(("A", 3));

        Layer<FltX> trimmed = TailSheetOptimizer.TrimLayer(
            layer, new Dictionary<string, ulong> { ["A"] = 2UL });

        trimmed.Units.Should().HaveCount(2);
        trimmed.ItemAmounts[new ItemId("A")].Should().Be(2UL);
    }

    /// **需求为空时不返回原板材** / An empty demand does not return the input sheets.
    [Fact]
    public void Optimize_ShouldReturnEmptyWhenTheDemandIsEmpty() {
        var optimizer = new TailSheetOptimizer();
        var sheets = new List<Sheet<FltX>> {
            new(new SheetType<FltX>(Meters(10), Meters(10), Meters(0), new SheetTypeId("T")), null, null),
        };

        optimizer.Optimize(sheets, new Dictionary<string, ulong>())
            .Should().BeEmpty("nothing needs packing, so no sheet has to be produced");

        optimizer.Optimize(sheets, new Dictionary<string, ulong> { ["A"] = 0UL })
            .Should().BeEmpty("a zero demand is still no demand");
    }

    /// <summary>构造含若干物料的层 / Build a layer holding the given items.</summary>
    /// <param name="items">物料与件数 / The items and their piece counts.</param>
    /// <returns>层 / The layer.</returns>
    private static Layer<FltX> LayerOf(params (string Item, int Count)[] items) {
        var units = new List<Placement2<FltX>>();
        int offset = 0;
        foreach ((string item, int count) in items) {
            for (int index = 0; index < count; index++) {
                units.Add(new Placement2<FltX>(
                    new RectanglePackingShape2<FltX>(Meters(1), Meters(1)),
                    new QuantityPoint2<FltX>(Meters(offset + index), Meters(0)),
                    false,
                    item));
            }
            offset += count;
        }
        return new Layer<FltX>(0, null, null, new Sheet2Shape<FltX>(Meters(offset), Meters(1)), units);
    }

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
