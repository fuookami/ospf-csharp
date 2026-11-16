// 舱位几何与冲突扫描判据 / Criteria for position geometry and conflict sweep.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.Packing;

/// <summary>
/// **舱位几何与冲突扫描** / Position geometry and conflict sweep.
/// </summary>
/// <remarks>
/// 对应`loadingConflicts`。
/// 扫描线实现，**三个边界最容易写错**：
///
/// | 点 | 错法 | 后果 |
/// | --- | --- | --- |
/// | 剔除条件用 `&lt;` 而非 `&lt;=` | `End &lt; Start` | **首尾相接**被判成重叠，多报冲突 |
/// | 先配对后剔除 | 顺序颠倒 | 已结束的区间仍参与配对 |
/// | 跨空间混扫 | 不按 `SpaceId` 分组 | 不同甲板被判成冲突 |
///
/// ; the table lists the three boundaries most likely to be wrong.
/// </remarks>
public class LoadingGeometryTests {
    /// <summary>**首尾相接不算重叠** / Touching intervals do not overlap.</summary>
    /// <remarks>
    /// 两个区间 `[0,1]` 与 `[1,2]`：**首尾相接**，不重叠。
    /// 若剔除条件写成 `End &lt; Start`，第一个不会被剔除，于是多报一条冲突。
    ///
    /// Two intervals `[0,1]` and `[1,2]` **touch** and do not overlap. An eviction condition of `End &lt; Start` would keep the first active and report a phantom conflict.
    /// </remarks>
    [Fact]
    public void Conflicts_ShouldNotReportTouchingIntervals() {
        var intervals = new List<LoadingInterval<Flt64>> {
            Interval("A", "D1", 0.0, 1.0),
            Interval("B", "D1", 1.0, 2.0),
        };

        Result<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>> result =
            LoadingGeometry.Conflicts(intervals);

        result.IsOk.Should().BeTrue(result.ToString());
        ((Ok<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>>)result).Value
            .Should().BeEmpty("[0,1] and [1,2] touch at exactly 1.0, which is not an overlap");
    }

    /// <summary>**真实重叠必须被报出** / A genuine overlap must be reported.</summary>
    /// <remarks>
    /// 反向对照：若判据只断言"相接不报"，一个**永不报冲突**的实现也能通过。
    ///
    /// Reverse control: if the criterion only asserted "touching is not reported", an implementation that **never reports anything** would also pass.
    /// </remarks>
    [Fact]
    public void Conflicts_ShouldReportAGenuineOverlap() {
        var intervals = new List<LoadingInterval<Flt64>> {
            Interval("A", "D1", 0.0, 2.0),
            Interval("B", "D1", 1.0, 3.0),
        };

        Result<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>> result =
            LoadingGeometry.Conflicts(intervals);

        result.IsOk.Should().BeTrue(result.ToString());
        IReadOnlySet<PositionConflict> conflicts =
            ((Ok<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>>)result).Value;
        conflicts.Should().HaveCount(1);
        conflicts.Should().Contain(
            new PositionConflict("A", "B"),
            "A starts earlier, so the directed edge runs A to B");
    }

    /// <summary>**不同空间之间不产生冲突** / Different spaces never conflict.</summary>
    [Fact]
    public void Conflicts_ShouldNotCrossSpaces() {
        var intervals = new List<LoadingInterval<Flt64>> {
            Interval("A", "D1", 0.0, 2.0),
            Interval("B", "D2", 1.0, 3.0),
        };

        Result<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>> result =
            LoadingGeometry.Conflicts(intervals);

        result.IsOk.Should().BeTrue(result.ToString());
        ((Ok<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>>)result).Value
            .Should().BeEmpty("the intervals overlap in time but sit on different decks");
    }

    /// <summary>**已结束的区间不再参与配对** / A finished interval no longer pairs.</summary>
    /// <remarks>
    /// `A[0,1]`、`B[2,3]`、`C[2.5,4]`：A 在 B 开始前已结束，所以只有 `B—C` 一条冲突。
    /// 若剔除发生在配对之后，A 会与 B、C 都配对，得到三条。
    ///
    /// `A[0,1]`, `B[2,3]`, `C[2.5,4]`: A finishes before B starts, so only `B—C` conflicts. Evicting after pairing would produce three edges.
    /// </remarks>
    [Fact]
    public void Conflicts_ShouldEvictFinishedIntervalsBeforePairing() {
        var intervals = new List<LoadingInterval<Flt64>> {
            Interval("A", "D1", 0.0, 1.0),
            Interval("B", "D1", 2.0, 3.0),
            Interval("C", "D1", 2.5, 4.0),
        };

        Result<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>> result =
            LoadingGeometry.Conflicts(intervals);

        result.IsOk.Should().BeTrue(result.ToString());
        IReadOnlySet<PositionConflict> conflicts =
            ((Ok<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>>)result).Value;
        conflicts.Should().HaveCount(1, "only B and C overlap");
        conflicts.Should().Contain(new PositionConflict("B", "C"));
    }

    /// <summary>**可选精确判交能过滤掉候选** / The optional exact test filters candidates.</summary>
    [Fact]
    public void Conflicts_ShouldHonourTheOptionalIntersectionTest() {
        var intervals = new List<LoadingInterval<Flt64>> {
            Interval("A", "D1", 0.0, 2.0),
            Interval("B", "D1", 1.0, 3.0),
        };

        Result<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>> result =
            LoadingGeometry.Conflicts(intervals, (_, _) => Results.Ok(false));

        result.IsOk.Should().BeTrue(result.ToString());
        ((Ok<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>>)result).Value
            .Should().BeEmpty("the exact test rejected the candidate pair");
    }

    /// <summary>**非法区间必须被拒** / Invalid intervals must be rejected.</summary>
    /// <param name="start">起点 / Start.</param>
    /// <param name="end">终点 / End.</param>
    [Theory]
    [InlineData(2.0, 2.0)]
    [InlineData(3.0, 2.0)]
    public void Conflicts_ShouldRejectInvalidIntervals(double start, double end) {
        var intervals = new List<LoadingInterval<Flt64>> { Interval("A", "D1", start, end) };

        Result<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>> result =
            LoadingGeometry.Conflicts(intervals);

        result.IsFailed.Should().BeTrue("an interval must satisfy start < end");
    }

    /// <summary>**重复舱位标识必须被拒** / Duplicate position identities must be rejected.</summary>
    [Fact]
    public void Conflicts_ShouldRejectDuplicateIdentities() {
        var intervals = new List<LoadingInterval<Flt64>> {
            Interval("A", "D1", 0.0, 1.0),
            Interval("A", "D1", 2.0, 3.0),
        };

        Result<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>> result =
            LoadingGeometry.Conflicts(intervals);

        result.IsFailed.Should().BeTrue("two intervals share the identity A");
    }

    /// <summary>构造区间 / Build an interval.</summary>
    /// <param name="id">舱位标识 / Position identity.</param>
    /// <param name="spaceId">空间 / Space.</param>
    /// <param name="start">起点（米）/ Start in metres.</param>
    /// <param name="end">终点（米）/ End in metres.</param>
    /// <returns>区间 / The interval.</returns>
    private static LoadingInterval<Flt64> Interval(string id, string spaceId, double start, double end) =>
        new(id, spaceId, Metres(start), Metres(end));

    /// <summary>构造米量 / Build a metre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) =>
        new(new Flt64(value), SIBaseUnits.Meter);
}
