// 装箱几何守卫测试 / Tests for the packing geometry guard.

using System;
using System.Collections.Generic;
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
/// 装箱几何守卫 / Packing geometry guard.
/// </summary>
public sealed class PackingGeometryGuardTest {
    /// <summary>全部在板材内且不重叠时校验通过 / A clean layout passes.</summary>
    [Fact]
    public void Validate_ShouldAcceptCleanLayout() =>
        PackingGeometryGuard.Validate(Sheet(10, 10), new[] {
            Placed(0, 0, 4, 4),
            Placed(5, 0, 4, 4),
        }).IsOk.Should().BeTrue();

    /// <summary>
    /// **越界必须被拒绝，并在消息里给出 `type=outside_sheet`**：可检索是这套消息的全部意义，丢了这个字段
    /// 就等于只能靠人眼看日志。
    /// **Out-of-bounds must be rejected with `type=outside_sheet` in the message**: searchability is the entire point
    /// of this message format, and dropping it leaves only eyeballing logs.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectOutsideSheet() {
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            PackingGeometryGuard.Validate(Sheet(10, 10), new[] { Placed(8, 8, 4, 4) });

        result.IsOk.Should().BeFalse();
        Message(result).Should().Contain("type=outside_sheet").And.Contain("bin=sheet");
    }

    /// <summary>重叠必须被拒绝并带 `type=overlap` / Overlap is rejected with `type=overlap`.</summary>
    [Fact]
    public void Validate_ShouldRejectOverlap() {
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            PackingGeometryGuard.Validate(Sheet(10, 10), new[] {
                Placed(0, 0, 4, 4),
                Placed(2, 2, 4, 4),
            });

        result.IsOk.Should().BeFalse();
        Message(result).Should().Contain("type=overlap");
    }

    /// <summary>贴边不算重叠，校验通过 / Touching is not an overlap.</summary>
    [Fact]
    public void Validate_ShouldAcceptTouching() =>
        PackingGeometryGuard.Validate(Sheet(10, 10), new[] {
            Placed(0, 0, 5, 5),
            Placed(5, 0, 5, 5),
        }).IsOk.Should().BeTrue();

    /// <summary>
    /// **刀缝必须双向检查**：把任一侧膨胀后相交都算违规。只查左侧会漏掉"右侧件更大"的情形。
    /// **The kerf must be checked in both directions**: intersecting after inflating either side is a violation, and
    /// checking only the left misses the case where the right piece is larger.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectGapBelowKerf() {
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            PackingGeometryGuard.RequireSheetGeometry(
                Sheet(20, 20),
                new[] { Placed(0, 0, 5, 5), Placed(6, 0, 5, 5) },
                kerf: Meters(2));

        result.IsOk.Should().BeFalse("间隙 1 米小于刀缝 2 米 / a 1 m gap is below the 2 m kerf");
    }

    /// <summary>间隙足够时刀缝校验通过 / A sufficient gap passes the kerf check.</summary>
    [Fact]
    public void Validate_ShouldAcceptGapAtLeastKerf() =>
        PackingGeometryGuard.RequireSheetGeometry(
            Sheet(20, 20),
            new[] { Placed(0, 0, 5, 5), Placed(7, 0, 5, 5) },
            kerf: Meters(2)).IsOk.Should().BeTrue();

    /// <summary>非正刀缝等于无刀缝 / A non-positive kerf behaves as no kerf.</summary>
    [Fact]
    public void Validate_ShouldIgnoreNonPositiveKerf() =>
        PackingGeometryGuard.RequireSheetGeometry(
            Sheet(20, 20),
            new[] { Placed(0, 0, 5, 5), Placed(5, 0, 5, 5) },
            kerf: Meters(0)).IsOk.Should().BeTrue();

    /// <summary>
    /// **禁止旋转的物品被旋转后必须被拒绝**，消息带 `type=illegal_rotation` 与物品编号。
    /// **A rotated placement of a rotation-forbidding item must be rejected**, with `type=illegal_rotation` and the
    /// item id in the message.
    /// </summary>
    [Fact]
    public void Validate_ShouldRejectIllegalRotation() {
        var catalog = new Dictionary<string, IAbstractItem<FltX>> {
            ["locked"] = new ActualItem<FltX>(
                new ItemId("locked"), "locked",
                new RectanglePackingShape2<FltX>(Meters(2), Meters(5)),
                false, Meters(0)),
        };

        Result<Success, ErrorCode, Error<ErrorCode>> result = PackingGeometryGuard.Validate(
            Sheet(10, 10),
            new[] { Placed(0, 0, 5, 2, rotated: true, itemId: "locked") },
            rotationRule: PackingRotationRule.ByItemCatalog(catalog));

        result.IsOk.Should().BeFalse();
        Message(result).Should().Contain("type=illegal_rotation").And.Contain("item=locked");
    }

    /// <summary>目录中允许旋转的物品被旋转后通过 / A cataloged item that allows rotation passes.</summary>
    [Fact]
    public void Validate_ShouldAcceptCatalogedRotation() {
        var catalog = new Dictionary<string, IAbstractItem<FltX>> {
            ["free"] = new ActualItem<FltX>(
                new ItemId("free"), "free",
                new RectanglePackingShape2<FltX>(Meters(2), Meters(5)),
                true, Meters(0)),
        };

        PackingGeometryGuard.Validate(
            Sheet(10, 10),
            new[] { Placed(0, 0, 5, 2, rotated: true, itemId: "free") },
            rotationRule: PackingRotationRule.ByItemCatalog(catalog)).IsOk.Should().BeTrue();
    }

    /// <summary>
    /// 正方形旋转不变，因此即使物品禁止旋转也允许——旋转不改变占用区域，拒绝它没有几何依据。
    /// A square is rotation invariant, so rotation is allowed even when the item forbids it: rotating changes
    /// nothing geometrically, so rejecting it has no basis.
    /// </summary>
    [Fact]
    public void Validate_ShouldAllowRotationOfSquare() =>
        PackingGeometryGuard.Validate(
            Sheet(10, 10),
            new[] { Placed(0, 0, 3, 3, rotated: true, itemId: "locked") },
            rotationRule: PackingRotationRule.SymmetricOnly).IsOk.Should().BeTrue();

    /// <summary>非对称形状在 `SymmetricOnly` 下拒绝旋转 / An asymmetric shape is rejected by SymmetricOnly.</summary>
    [Fact]
    public void Validate_ShouldRejectAsymmetricRotationUnderSymmetricOnly() =>
        PackingGeometryGuard.Validate(
            Sheet(10, 10),
            new[] { Placed(0, 0, 5, 2, rotated: true) },
            rotationRule: PackingRotationRule.SymmetricOnly).IsOk.Should().BeFalse();

    /// <summary>几何校验先于旋转校验 / Geometry is validated before rotation.</summary>
    [Fact]
    public void Validate_ShouldReportGeometryBeforeRotation() {
        Result<Success, ErrorCode, Error<ErrorCode>> result = PackingGeometryGuard.Validate(
            Sheet(10, 10),
            new[] { Placed(8, 8, 5, 2, rotated: true) },
            rotationRule: PackingRotationRule.SymmetricOnly);

        result.IsOk.Should().BeFalse();
        Message(result).Should().Contain("type=outside_sheet", "越界先于旋转被报告 / bounds come first");
    }

    /// <summary>取错误消息 / Read the error message.</summary>
    private static string Message(Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message;

    /// <summary>构造板材 / Build a sheet.</summary>
    private static Sheet2Shape<FltX> Sheet(double w, double h) => new(Meters(w), Meters(h));

    /// <summary>构造放置 / Build a placement.</summary>
    private static Placement2<FltX> Placed(
        double x, double y, double w, double h, bool rotated = false, string? itemId = null) =>
        new(
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            new QuantityPoint2<FltX>(Meters(x), Meters(y)),
            rotated,
            itemId);

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
