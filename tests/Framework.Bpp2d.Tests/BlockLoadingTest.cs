// 空间区域、块与旋转校验测试 / Tests for space regions, blocks and the rotation guard.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 空间区域、块与旋转校验 / Space regions, blocks and the rotation guard.
/// </summary>
public sealed class BlockLoadingTest {
    /// <summary>区域面积为宽高之积 / A region's area is width times height.</summary>
    [Fact]
    public void Space2_ShouldComputeArea() =>
        D(Space(0, 0, 4, 5).Area).Should().BeApproximately(20.0, 1e-9);

    /// <summary>
    /// **恰好等宽等高视为放得下**：改成严格小于会把正好填满的区域判为不可用，白白留下一条恰好够宽的缝。
    /// **Exactly equal counts as fitting**: strict inequality would mark a region filled exactly as unusable and leave
    /// a gap that is precisely wide enough.
    /// </summary>
    [Fact]
    public void Space2_ShouldAcceptExactFit() =>
        Space(0, 0, 4, 5).Enabled(Meters(4), Meters(5)).Should().BeTrue();

    /// <summary>超出任一维都放不下 / Exceeding either dimension does not fit.</summary>
    [Fact]
    public void Space2_ShouldRejectOversize() {
        Space2<FltX> space = Space(0, 0, 4, 5);

        space.Enabled(Meters(5), Meters(5)).Should().BeFalse();
        space.Enabled(Meters(4), Meters(6)).Should().BeFalse();
    }

    /// <summary>形状按包围尺寸判定能否放入 / A shape is judged by its bounding size.</summary>
    [Fact]
    public void Space2_ShouldTestShape() =>
        Space(0, 0, 4, 5)
            .Enabled(new RectanglePackingShape2<FltX>(Meters(4), Meters(5)))
            .Should().BeTrue();

    /// <summary>X 切片给出左块与右侧余量 / Splitting on X yields the left piece and the right remainder.</summary>
    [Fact]
    public void SplitX_ShouldProduceLeftAndRemainder() {
        (Space2<FltX> left, Space2<FltX> right) = Space(1, 2, 10, 5).SplitX(Meters(3)).Value;

        D(left.X).Should().BeApproximately(1.0, 1e-9);
        D(left.Width).Should().BeApproximately(3.0, 1e-9);
        D(left.Height).Should().BeApproximately(5.0, 1e-9);
        D(right.X).Should().BeApproximately(4.0, 1e-9);
        D(right.Width).Should().BeApproximately(7.0, 1e-9);
    }

    /// <summary>Y 切片给出底块与上方余量 / Splitting on Y yields the bottom piece and the upper remainder.</summary>
    [Fact]
    public void SplitY_ShouldProduceBottomAndRemainder() {
        (Space2<FltX> bottom, Space2<FltX> top) = Space(1, 2, 10, 5).SplitY(Meters(2)).Value;

        D(bottom.Y).Should().BeApproximately(2.0, 1e-9);
        D(bottom.Height).Should().BeApproximately(2.0, 1e-9);
        D(top.Y).Should().BeApproximately(4.0, 1e-9);
        D(top.Height).Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>
    /// **单位不符时切片返回失败而不是抛异常**：单位不符是输入错误，不该用异常打断整批排样。
    /// **A unit mismatch fails the split instead of throwing**: mismatched units are an input error and should not abort
    /// a whole packing run.
    /// </summary>
    [Fact]
    public void SplitX_ShouldFailOnUnitMismatch() =>
        Space(0, 0, 10, 5)
            .SplitX(new Quantity<FltX>(new FltX(3), SIBaseUnits.Second))
            .IsOk.Should().BeFalse();

    /// <summary>零宽或零高视为退化 / Zero width or height counts as degenerate.</summary>
    [Fact]
    public void Space2_ShouldDetectDegenerate() {
        Space(0, 0, 0, 5).IsDegenerate().Should().BeTrue();
        Space(0, 0, 4, 0).IsDegenerate().Should().BeTrue();
        Space(0, 0, 4, 5).IsDegenerate().Should().BeFalse();
    }

    /// <summary>块内件数为放置个数 / A block's amount is its placement count.</summary>
    [Fact]
    public void Block2_ShouldCountPlacements() =>
        Block().Amount.Should().Be(2);

    /// <summary>块占用面积为各放置实际面积之和 / A block's area sums the actual placement areas.</summary>
    [Fact]
    public void Block2_ShouldSumArea() =>
        D(Block().Area).Should().BeApproximately(10.0, 1e-9);

    /// <summary>块利用率为占用面积比区域面积 / A block's utilisation is occupied over region area.</summary>
    [Fact]
    public void Block2_ShouldComputeUtilization() =>
        Block().Utilization.ToFlt64().ToDouble().Should().BeApproximately(0.4, 1e-9);

    /// <summary>退化区域时利用率为零，而不是除零 / A degenerate region yields zero utilisation, not division.</summary>
    [Fact]
    public void Block2_ShouldReturnZeroUtilizationForDegenerateSpace() =>
        (Block(space: Space(0, 0, 0, 0)).Utilization.ToFlt64().ToDouble())
            .Should().BeApproximately(0.0, 1e-9);

    /// <summary>件数映射只统计有编号的物品 / The amount map counts only items that carry an id.</summary>
    [Fact]
    public void Block2_ShouldGroupItemAmounts() {
        var block = new Block2<FltX>(
            Space(0, 0, 10, 10),
            new[] { Placed(0, 0, 2, 2, "a"), Placed(3, 0, 2, 2, "a"), Placed(6, 0, 2, 2, null) },
            "test");

        block.ItemAmounts.Should().HaveCount(1);
        block.ItemAmounts["a"].Should().Be(2);
    }

    /// <summary>
    /// **圆形件永远允许旋转**：旋转 90 度后占用区域不变，拦下它会让调用方以为圆形件不能旋转，凭空少掉一半候选位。
    /// **Circles always pass**: rotating by 90 degrees changes nothing, and rejecting it would make the caller believe
    /// circles cannot rotate and shrink the candidate set for no reason.
    /// </summary>
    [Fact]
    public void RequireRotationAllowed_ShouldAlwaysAllowCircle() {
        var circle = new ActualItem<FltX>(
            new ItemId("c"), "c",
            new CirclePackingShape2<FltX>(Meters(1)),
            false, Meters(0));

        RotationGuard.RequireRotationAllowed(circle).IsOk.Should().BeTrue();
    }

    /// <summary>禁止旋转的矩形件被拒绝，消息含物品编号 / A rotation-forbidding rectangle is rejected with its id.</summary>
    [Fact]
    public void RequireRotationAllowed_ShouldRejectLockedRectangle() {
        var locked = new ActualItem<FltX>(
            new ItemId("locked"), "locked",
            new RectanglePackingShape2<FltX>(Meters(2), Meters(5)),
            false, Meters(0));

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            RotationGuard.RequireRotationAllowed(locked);

        result.IsOk.Should().BeFalse();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("locked");
    }

    /// <summary>允许旋转的矩形件通过 / A rotation-allowing rectangle passes.</summary>
    [Fact]
    public void RequireRotationAllowed_ShouldAllowFreeRectangle() {
        var free = new ActualItem<FltX>(
            new ItemId("free"), "free",
            new RectanglePackingShape2<FltX>(Meters(2), Meters(5)),
            true, Meters(0));

        RotationGuard.RequireRotationAllowed(free).IsOk.Should().BeTrue();
    }

    /// <summary>构造区域 / Build a region.</summary>
    private static Space2<FltX> Space(double x, double y, double w, double h) =>
        new(Meters(x), Meters(y), Meters(w), Meters(h));

    /// <summary>构造块 / Build a block.</summary>
    private static Block2<FltX> Block(Space2<FltX>? space = null) => new(
        space ?? Space(0, 0, 5, 5),
        new[] { Placed(0, 0, 2, 3, "a"), Placed(3, 0, 2, 2, "b") },
        "test");

    /// <summary>构造放置 / Build a placement.</summary>
    private static Placement2<FltX> Placed(
        double x, double y, double w, double h, string? itemId = null) =>
        new(
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            new QuantityPoint2<FltX>(Meters(x), Meters(y)),
            false,
            itemId);

    /// <summary>取出数值 / Read the number.</summary>
    private static double D(Quantity<FltX> quantity) => quantity.Value.ToFlt64().ToDouble();

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
