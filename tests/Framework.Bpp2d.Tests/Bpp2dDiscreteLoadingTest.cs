// 二维离散装载内核的判据 / Criteria for the two-dimensional discrete-loading kernel.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.DiscreteLoading;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using StowageConflict = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// **二维内核：矩形相交，而不是区间重叠** / The 2-D kernel: rectangles intersect, intervals do not.
/// </summary>
/// <remarks>
/// 补的是 Demo2 `LoadingContextFactory` 类注释里记在案的缺口——"`Framework.Bpp2d` 没有 `Context` 工厂，于是所有机型都走一维"。
/// **判据必须能区分"二维"与"一维"**：只断言"构造成功"的话，一维内核也能满足。所以这里造一对**纵向重叠、横向不重叠**的舱位——
/// 一维会把它们判成冲突，二维不会。
///
/// This fills the gap recorded in Demo2's `LoadingContextFactory` remarks: "`Framework.Bpp2d` has no `Context` factory, so every aircraft goes 1-D". **The criterion
/// must distinguish 2-D from 1-D**: asserting only "it builds" would let the 1-D kernel satisfy it. So the fixture puts a pair of positions that **overlap
/// longitudinally but not laterally** — 1-D calls them conflicting, 2-D does not.
/// </remarks>
public class Bpp2dDiscreteLoadingTest
{
    /// <summary>**纵向重叠但横向错开的两个舱位不算冲突** / Two positions overlapping in x but separated in y are not conflicting.</summary>
    /// <remarks>
    /// 这是**二维与一维唯一的分水岭**：一维只看 `[x, x+width)`，两个舱位都从 0 开始，必然重叠。
    /// 二维看整个矩形，`y` 不同就不相交。**判据红了就说明内核退化成了一维。**
    ///
    /// This is **the one place 2-D and 1-D differ**: 1-D sees only `[x, x+width)` and both positions start at 0, so they must overlap. 2-D sees the whole
    /// rectangle, and different `y` means no intersection. **A red criterion means the kernel degraded back to 1-D.**
    /// </remarks>
    [Fact]
    public void PositionsOverlappingInXButNotInY_ShouldNotConflict() {
        DiscreteLoadingProblem<Flt64> problem = Problem(
            positions: ["LEFT", "RIGHT"],
            assignments: [("I1", "LEFT"), ("I1", "RIGHT")]);

        Result<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> built =
            Bpp2dDiscreteLoading.Context(
                problem,
                [
                    Rectangle("LEFT", x: 0.0, y: 0.0, width: 1.0, height: 1.0),
                    Rectangle("RIGHT", x: 0.0, y: 1.0, width: 1.0, height: 1.0),
                ],
                new Flt64PackingValueAdapter());

        built.IsOk.Should().BeTrue(built.ToString());
        Ok(built).Problem.Conflicts.Should().BeEmpty(
            "the two rectangles touch along an edge but do not overlap in the interior; a 1-D kernel would report a conflict here");
    }

    /// <summary>**真的重叠时必须报冲突** / Genuine overlap must be reported.</summary>
    /// <remarks>
    /// 少了这条，"永远返回空冲突集"也能让上一条绿。**一条能被"什么都不做"满足的判据，等于没有判据。**
    ///
    /// Without this, "always return no conflicts" would also keep the criterion above green. **A criterion that "do nothing" satisfies is no criterion.**
    /// </remarks>
    [Fact]
    public void OverlappingRectangles_ShouldConflict() {
        DiscreteLoadingProblem<Flt64> problem = Problem(
            positions: ["A", "B"],
            assignments: [("I1", "A"), ("I1", "B")]);

        Result<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> built =
            Bpp2dDiscreteLoading.Context(
                problem,
                [
                    Rectangle("A", x: 0.0, y: 0.0, width: 2.0, height: 2.0),
                    Rectangle("B", x: 0.5, y: 0.5, width: 1.0, height: 1.0),
                ],
                new Flt64PackingValueAdapter());

        built.IsOk.Should().BeTrue(built.ToString());
        Ok(built).Problem.Conflicts.Should().ContainSingle(
            "the smaller rectangle sits strictly inside the larger one");
    }

    /// <summary>**业务互斥必须保留，不能被几何覆盖** / Business conflicts must survive, not be replaced by geometry.</summary>
    /// <remarks>
    /// `Conflicts` 的回调只决定几何那一半。**替换而不是合并会静默丢掉调用方声明的业务互斥**——
    /// 那些恰恰是"区间完全不重叠但业务上不许同时用"的对子，**丢掉了求解器不报错，只给一个违反业务规则的解**。
    ///
    /// The callback decides only the geometric half. **Replacing instead of merging silently drops the caller's business conflicts** — precisely the pairs that
    /// "do not overlap geometrically but may not be used together". **Their absence raises no solver error; it just yields a solution violating the business rule.**
    /// </remarks>
    [Fact]
    public void BusinessConflicts_ShouldSurviveTheGeometricMerge() {
        var business = new HashSet<StowageConflict> { new("A", "B") };
        DiscreteLoadingProblem<Flt64> problem = Problem(
            positions: ["A", "B"],
            assignments: [("I1", "A"), ("I1", "B")],
            conflicts: business);

        Result<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> built =
            Bpp2dDiscreteLoading.Context(
                problem,
                [
                    // **几何上完全不重叠**：只有业务边该留下。
                    // **Geometrically disjoint**: only the business edge should remain.
                    Rectangle("A", x: 0.0, y: 0.0, width: 1.0, height: 1.0),
                    Rectangle("B", x: 5.0, y: 5.0, width: 1.0, height: 1.0),
                ],
                new Flt64PackingValueAdapter());

        built.IsOk.Should().BeTrue(built.ToString());
        Ok(built).Problem.Conflicts.Should().ContainSingle(
            "geometry found nothing, so the surviving edge must be the caller's business one");
        Ok(built).Problem.Conflicts.Should().Contain(new StowageConflict("A", "B"));
    }

    /// <summary>**舱位与几何必须一一对应** / Positions and rectangles must correspond one to one.</summary>
    [Fact]
    public void AMismatchedGeometrySet_ShouldBeRefused() {
        DiscreteLoadingProblem<Flt64> problem = Problem(
            positions: ["A", "B"],
            assignments: [("I1", "A"), ("I1", "B")]);

        Result<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> built =
            Bpp2dDiscreteLoading.Context(
                problem,
                [Rectangle("A", x: 0.0, y: 0.0, width: 1.0, height: 1.0)],
                new Flt64PackingValueAdapter());

        built.IsFailed.Should().BeTrue("B has no rectangle; silently ignoring it would lose every real conflict it has");
        ((Failed<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>>)built)
            .Error.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**重复的舱位矩形必须拒** / Duplicate rectangles must be refused.</summary>
    [Fact]
    public void DuplicateRectangles_ShouldBeRefused() {
        DiscreteLoadingProblem<Flt64> problem = Problem(positions: ["A"], assignments: [("I1", "A")]);

        Result<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> built =
            Bpp2dDiscreteLoading.Context(
                problem,
                [
                    Rectangle("A", x: 0.0, y: 0.0, width: 1.0, height: 1.0),
                    Rectangle("A", x: 2.0, y: 2.0, width: 1.0, height: 1.0),
                ],
                new Flt64PackingValueAdapter());

        built.IsFailed.Should().BeTrue("two rectangles for one position would make the conflict set depend on which one wins");
    }

    /// <summary>构造一个只有一件货物的装载问题 / Build a loading problem with a single item.</summary>
    /// <param name="positions">舱位名 / Position names.</param>
    /// <param name="assignments">允许的指派 / Permitted assignments.</param>
    /// <param name="conflicts">业务互斥 / Business conflicts.</param>
    /// <returns>问题 / The problem.</returns>
    private static DiscreteLoadingProblem<Flt64> Problem(
        string[] positions,
        (string Item, string Position)[] assignments,
        HashSet<StowageConflict>? conflicts = null) =>
        new(
            Items: [new LoadingItem<Flt64>("I1", Kilograms(1.0), Required: true, FixedPositionId: null)],
            Positions: [.. positions.Select(name => new LoadingPosition<Flt64>(
                name, MaxCount: 5UL, MaxWeight: Kilograms(5.0), FixedOccupied: false, Enabled: true))],
            AllowedAssignments: new HashSet<AssignmentKey>(assignments.Select(pair => new AssignmentKey(pair.Item, pair.Position))),
            Conflicts: conflicts ?? [],
            LayoutGroups: [],
            WeightUnit: SIBaseUnits.Kilogram);

    /// <summary>构造一个舱位矩形 / Build one position rectangle.</summary>
    /// <param name="id">舱位名 / Position name.</param>
    /// <param name="x">左下角 x / Lower-left x.</param>
    /// <param name="y">左下角 y / Lower-left y.</param>
    /// <param name="width">宽 / Width.</param>
    /// <param name="height">高 / Height.</param>
    /// <returns>矩形 / The rectangle.</returns>
    private static LoadingRectangle<Flt64> Rectangle(string id, double x, double y, double width, double height) =>
        new(id, "MAIN", Box2Geometry<Flt64>.Of(Meters(x), Meters(y), Meters(width), Meters(height)));

    /// <summary>构造一个米量 / Build a metre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Meters(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造一个千克量 / Build a kilogram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <param name="result">结果 / The result.</param>
    /// <returns>值 / The value.</returns>
    private static T Ok<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        ((Ok<T, ErrorCode, Error<ErrorCode>>)result).Value;
}
