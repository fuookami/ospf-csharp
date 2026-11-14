// 断头台切分树判据 / Criteria for the guillotine cut tree.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Infrastructure;

/// <summary>
/// **级数与刀缝的一致性** / Stage and kerf consistency.
/// </summary>
/// <remarks>
/// 对应基线 `CutTree2.kt`。断头台的排样**必须由切分树产生**，而这棵树就是交给车间的下刀顺序。
///
/// Mirrors the baseline `CutTree2.kt`. A guillotine layout **must be produced by a cut tree**, and that tree is the cut sequence handed to the workshop.
/// </remarks>
public class CutTree2Tests {
    /// <summary>**合法的两级断头台通过校验** / A legal two-stage guillotine passes validation.</summary>
    [Fact]
    public void Validate_ShouldAcceptALegalTwoStageTree() {
        CutTree2<Flt64> tree = LegalTwoStageTree();

        tree.Leaves().Should().HaveCount(3, "one strip is split into two pieces, plus one unsplit strip");
        tree.CutLines().Should().HaveCount(2);
        tree.CutLines()[0].Stage.Should().Be(1, "cut lines are reported in depth-first order");
        tree.MaxStage().Should().Be(2);
        tree.Validate(CuttingProcess2.GuillotineTwoStage, Metres(0.5)).IsOk.Should().BeTrue();
    }

    /// <summary>**同向刀必须同级** / Cuts in the same direction must stay on the same stage.</summary>
    /// <remarks>
    /// 一律 +1 会让"把整板切成多条同向条带"无法表达——而那正是 2 级工艺的第一步。
    ///
    /// Always adding one would make "cut the sheet into several parallel strips" inexpressible — and that is the first step of a two-stage process.
    /// </remarks>
    [Fact]
    public void Validate_ShouldRejectAStageAdvanceWithinTheSameDirection() {
        Region2<Flt64> region = Region(Metres(4.0), Metres(2.0));
        Region2<Flt64> left = Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(2.0), Metres(2.0));

        // 同向刀却写成了第 2 级 / same orientation but written as stage 2.
        CutTree2<Flt64> firstHalf = LeafStub(Metres(0.0), Metres(0.0), Metres(1.0), Metres(2.0));
        CutTree2<Flt64> secondHalf = LeafStub(Metres(1.0), Metres(0.0), Metres(1.0), Metres(2.0));
        Result<CutTree2<Flt64>.Split2, ErrorCode, Error<ErrorCode>> inner = CutTree2<Flt64>.Split2Of(
            left, CutOrientation2.Vertical, Metres(1.0), 2, firstHalf, secondHalf);
        inner.IsOk.Should().BeTrue(inner.ToString());

        CutTree2<Flt64> tree = CutTree2<Flt64>.Split2Of(
            region, CutOrientation2.Vertical, Metres(2.0), 1,
            inner.ValueOrThrow(), LeafStub(Metres(2.0), Metres(0.0), Metres(2.0), Metres(2.0))
        ).ValueOrThrow();

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            tree.Validate(CuttingProcess2.GuillotineTwoStage, Metres(0.0));

        result.IsFailed.Should().BeTrue();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("子刀线级数应为 1", "a same-direction child must keep the parent's stage");
    }

    /// <summary>**子区域与刀线矛盾时必须被拒** / A child region contradicting the cut line must be rejected.</summary>
    /// <remarks>
    /// 手搓的树很容易写出"刀在 2.0、子区域却从 1.5 开始"，**校验能过、车间切不出来**。
    ///
    /// A hand-built tree easily puts the cut at 2.0 while the child starts at 1.5 — **it passes validation and cannot be cut in the workshop**.
    /// </remarks>
    [Fact]
    public void Split2Of_ShouldRejectAChildRegionThatContradictsTheCut() {
        Region2<Flt64> region = Region(Metres(4.0), Metres(2.0));

        CutTree2<Flt64> leftHalf = LeafStub(Metres(0.0), Metres(0.0), Metres(2.0), Metres(2.0));
        CutTree2<Flt64> shiftedHalf = LeafStub(Metres(2.5), Metres(0.0), Metres(1.5), Metres(2.0));
        Result<CutTree2<Flt64>.Split2, ErrorCode, Error<ErrorCode>> result = CutTree2<Flt64>.Split2Of(
            region, CutOrientation2.Vertical, Metres(2.0), 1, leftHalf, shiftedHalf);

        result.IsFailed.Should().BeTrue("with kerf 0 the right region must start at 2.0, not 2.5");
        ((Failed<CutTree2<Flt64>.Split2, ErrorCode, Error<ErrorCode>>)result).Error
            .Should().BeOfType<Csp2dValidationError>();
    }

    /// <summary>**刀缝参与矛盾判定** / The kerf participates in the contradiction check.</summary>
    [Fact]
    public void Split2Of_ShouldAcceptTheKerfShiftedChildRegion() {
        Region2<Flt64> region = Region(Metres(4.0), Metres(2.0));

        CutTree2<Flt64> leftHalf = LeafStub(Metres(0.0), Metres(0.0), Metres(2.0), Metres(2.0));
        CutTree2<Flt64> shiftedHalf = LeafStub(Metres(2.5), Metres(0.0), Metres(1.5), Metres(2.0));
        Result<CutTree2<Flt64>.Split2, ErrorCode, Error<ErrorCode>> result = CutTree2<Flt64>.Split2Of(
            region, CutOrientation2.Vertical, Metres(2.0), 1, leftHalf, shiftedHalf, Metres(0.5));

        result.IsOk.Should().BeTrue(result.ToString());
    }

    /// <summary>**落在边界上的刀线不算在内部** / A cut on the boundary does not count as inside.</summary>
    [Fact]
    public void IsInside_ShouldBeStrictOnBothSides() {
        Region2<Flt64> region = Region(Metres(4.0), Metres(2.0));

        CutTree2Checks.IsInside(region, CutOrientation2.Vertical, Metres(0.0)).Should().BeFalse(
            "a cut on the left edge produces a zero-width piece");
        CutTree2Checks.IsInside(region, CutOrientation2.Vertical, Metres(4.0)).Should().BeFalse(
            "a cut on the right edge produces a zero-width piece");
        CutTree2Checks.IsInside(region, CutOrientation2.Vertical, Metres(2.0)).Should().BeTrue();
        CutTree2Checks.IsInside(region, CutOrientation2.Horizontal, Metres(1.0)).Should().BeTrue();
    }

    /// <summary>**面积判据** / The area check.</summary>
    [Fact]
    public void HasPositiveArea_ShouldRejectDegenerateRegions() {
        Region2<Flt64> region = Region(Metres(4.0), Metres(2.0));

        CutTree2Checks.HasPositiveArea(region, SquareMetres(0.0)).Should().BeTrue();
        CutTree2Checks.HasPositiveArea(Region(Metres(0.0), Metres(2.0)), SquareMetres(0.0)).Should().BeFalse();
    }

    /// <summary>**手搓的树也必须被校验拦住** / A hand-built tree must still be caught by validation.</summary>
    /// <remarks>
    /// `Split2Of` 已经保证子区域与刀线一致，所以**经工厂构造的树永远过不了这一关**——这一条判据专门用手搓结点，
    /// 证明 `Validate` 里那道检查不是白写的：**绕过工厂就会留下能过校验、切不出来的树**。
    ///
    /// `Split2Of` already guarantees that a child region matches the cut line, so **a factory-built tree can never reach this check**; this criterion hand-builds the node
    /// instead, proving the check inside `Validate` is not dead code: **bypassing the factory leaves a tree that passes validation and cannot be cut**.
    /// </remarks>
    [Fact]
    public void Validate_ShouldRejectAHandBuiltTreeWithAContradictingChild() {
        Region2<Flt64> region = Region(Metres(4.0), Metres(2.0));
        CutTree2<Flt64> correctLeft = LeafStub(Metres(0.0), Metres(0.0), Metres(2.0), Metres(2.0));
        CutTree2<Flt64> shiftedRight = LeafStub(Metres(2.5), Metres(0.0), Metres(1.5), Metres(2.0));

        // 直接构造结点，跳过 split2Of 的一致性检查。
        // Build the node directly, bypassing the consistency check of split2Of.
        CutTree2<Flt64> handBuilt = new CutTree2<Flt64>.Split2(
            region, new CutLine2<Flt64>(CutOrientation2.Vertical, Metres(2.0), 1), correctLeft, shiftedRight);

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            handBuilt.Validate(CuttingProcess2.GuillotineTwoStage, Metres(0.0));

        result.IsFailed.Should().BeTrue("the right child starts at 2.5 although the cut sits at 2.0");
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("右/上侧子区域与切分结果不一致");
    }

    /// <summary>一个合法的两级断头台树 / A legal two-stage guillotine tree.</summary>
    /// <returns>切分树 / The cut tree.</returns>
    private static CutTree2<Flt64> LegalTwoStageTree() {
        // 刀缝 0.5 全程一致：右块从 2.5 起，上块从 1.5 起。
        // Kerf 0.5 throughout: the right region starts at 2.5 and the upper one at 1.5.
        Region2<Flt64> region = Region(Metres(4.0), Metres(2.0));
        Region2<Flt64> leftStrip = Region2Factory.Region2Of(
            Metres(0.0), Metres(0.0), Metres(2.0), Metres(2.0));

        CutTree2<Flt64> bottom = LeafStub(Metres(0.0), Metres(0.0), Metres(2.0), Metres(1.0));
        CutTree2<Flt64> top = LeafStub(Metres(0.0), Metres(1.5), Metres(2.0), Metres(0.5));
        CutTree2<Flt64> left = CutTree2<Flt64>.Split2Of(
            leftStrip, CutOrientation2.Horizontal, Metres(1.0), 2, bottom, top, Metres(0.5)).ValueOrThrow();

        CutTree2<Flt64> right = LeafStub(Metres(2.5), Metres(0.0), Metres(1.5), Metres(2.0));
        return CutTree2<Flt64>.Split2Of(
            region, CutOrientation2.Vertical, Metres(2.0), 1, left, right, Metres(0.5)).ValueOrThrow();
    }

    /// <summary>构造一片叶子 / Build a leaf.</summary>
    /// <param name="x">左下角 x / The lower-left x.</param>
    /// <param name="y">左下角 y / The lower-left y.</param>
    /// <param name="width">宽度 / The width.</param>
    /// <param name="height">高度 / The height.</param>
    /// <returns>叶子 / The leaf.</returns>
    private static CutTree2<Flt64> LeafStub(
        Quantity<Flt64> x,
        Quantity<Flt64> y,
        Quantity<Flt64> width,
        Quantity<Flt64> height) =>
        new CutTree2<Flt64>.Leaf2(Region2Factory.Region2Of(x, y, width, height), 1);

    /// <summary>以原点构造区域 / Build a region anchored at the origin.</summary>
    /// <param name="width">宽度 / The width.</param>
    /// <param name="height">高度 / The height.</param>
    /// <param name="ignoredX">忽略的第一参数 / An ignored first parameter.</param>
    /// <param name="ignoredHeight">忽略的第二参数 / An ignored second parameter.</param>
    /// <returns>区域 / The region.</returns>
    private static Region2<Flt64> Region(Quantity<Flt64> width, Quantity<Flt64> height) =>
        Region2Factory.Region2Of(Metres(0.0), Metres(0.0), width, height);

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
