#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 分段选择：端点容差、内部归属、夹紧后的返回值 / Endpoint tolerance, interior ownership, clamped value.
/// </summary>
/// <remarks>
/// B1202 按基线 `NativePiecewiseReport.kt` 的 `selectSegment` 补。
///
/// B1202 adds the baseline's `selectSegment`.
/// </remarks>
public class NativePiecewiseReportTests
{
    /// <summary>
    /// **正常落在段内的输入选出正确分段** / An input inside a segment selects it.
    /// </summary>
    [Fact]
    public void AnInputInsideASegment_ShouldSelectIt() {
        Result<SegmentSelection, ErrorCode, Error<ErrorCode>> result =
            Select(7.0, 0.0, 5.0, 10.0);

        result.Should().BeOfType<Ok<SegmentSelection, ErrorCode, Error<ErrorCode>>>();
        result.Value.Index.Should().Be(1);
        result.Value.Input.Should().Be(7.0);
    }

    /// <summary>
    /// **恰好落在内部断点上时归<b>前</b>一段** / A value exactly on an interior breakpoint belongs to the earlier segment.
    /// </summary>
    /// <remarks>
    /// **两端都闭合的查找，"第一个命中"就是左边那一段。**
    ///
    /// **这个约定必须有，因为分段线性在断点处连续当且仅当两段在该点的值相等；
    /// 不相等时这个点有两个"正确答案"，而报告必须给出一个——而且每次都给同一个。**
    ///
    /// **判据测 `5.0` 恰好是断点：下标必须是 `0` 而不是 `1`。**
    ///
    /// **The search closes both ends, so "the first hit" is the segment on the left.**
    ///
    /// **The convention has to exist because a piecewise function is continuous at a breakpoint exactly when the two
    /// segments agree there; when they disagree the point has two right answers and the report must pick one — the same
    /// one every time.**
    ///
    /// **The criterion uses `5.0` exactly on the breakpoint: the index must be `0`, not `1`.**
    /// </remarks>
    [Fact]
    public void AnInteriorBreakpoint_ShouldBelongToTheEarlierSegment() {
        Select(5.0, 0.0, 5.0, 10.0).Value.Index.Should().Be(
            0, "the search takes the first segment whose closed interval contains the value");
        Select(10.0, 0.0, 5.0, 10.0).Value.Index.Should().Be(
            1, "the last breakpoint can only belong to the last segment");
        Select(0.0, 0.0, 5.0, 10.0).Value.Index.Should().Be(0);
    }

    /// <summary>
    /// **端点容差夹紧而不拒绝，且返回的是夹紧后的值** /
    /// The endpoint tolerance clamps instead of rejecting, and returns the clamped value.
    /// </summary>
    /// <remarks>
    /// **容差是 `1e-6`。**
    ///
    /// | 输入 | 相对下界 `0` | 结果 |
    /// | --- | --- | --- |
    /// | `-1e-9` | **在容差内** | **夹到 `0`，成功** |
    /// | `-1e-3` | **超出容差** | **拒绝** |
    /// | `1e-9` 高于上界 | **在容差内** | **夹到 `10`，成功** |
    ///
    /// **关键在第三列与"返回值"那一列的结合：**
    /// **`-1e-9` 的调用方拿到的是 `Input == 0.0`，<b>不是</b> `-1e-9`。**
    /// **一个"几乎在界内"的输入会被静默改成一个界上的值——
    /// 如果调用方接着用它去算，它算的是一个没被要求过的数。**
    ///
    /// **The tolerance is `1e-6`.**
    ///
    /// **The point is the combination of the third column with the returned value:**
    /// **a caller passing `-1e-9` gets `Input == 0.0`, <b>not</b> `-1e-9`.**
    /// **An "almost inside" input is silently rewritten to a value on the bound — and if the caller computes with the
    /// return value it computes with a number it never asked for.**
    /// </remarks>
    [Fact]
    public void TheEndpointTolerance_ShouldClampAndReportTheClampedValue() {
        Result<SegmentSelection, ErrorCode, Error<ErrorCode>> below = Select(-1e-9, 0.0, 5.0, 10.0);
        below.Should().BeOfType<Ok<SegmentSelection, ErrorCode, Error<ErrorCode>>>(
            "the excess is inside the tolerance");
        below.Value.Input.Should().Be(
            0.0, "the clamped value is what comes back, not the one passed in");
        below.Value.Index.Should().Be(0);

        Result<SegmentSelection, ErrorCode, Error<ErrorCode>> above = Select(10.0 + 1e-9, 0.0, 5.0, 10.0);
        above.Should().BeOfType<Ok<SegmentSelection, ErrorCode, Error<ErrorCode>>>();
        above.Value.Input.Should().Be(10.0);
        above.Value.Index.Should().Be(1);

        Select(-1e-3, 0.0, 5.0, 10.0).Should()
            .BeOfType<Failed<SegmentSelection, ErrorCode, Error<ErrorCode>>>(
                "a tenth of a thousandth is far outside a 1e-6 tolerance");
        Select(10.001, 0.0, 5.0, 10.0).Should()
            .BeOfType<Failed<SegmentSelection, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **容差<b>不</b>作用于内部断点** / The tolerance does <b>not</b> apply to interior breakpoints.
    /// </summary>
    /// <remarks>
    /// **这是本批最容易写错的一处：容差只包住定义域的两个端点，
    /// 内部断点完全不在这个机制里。**
    ///
    /// **一个越过内部断点 `5` 的值直接落进第二段（下标 1），
    /// 既不被拒绝也不被夹紧——它的 `Input` 保持原样。**
    ///
    /// **判据用 `5.0 + 1e-9`：如果实现错误地把容差延伸到内部断点并夹紧，
    /// `Input` 会变成 `5.0`；正确实现返回 `5.000000001`。**
    ///
    /// **This is the easiest thing to get wrong here: the tolerance only wraps the two ends of the domain, and interior
    /// breakpoints are entirely outside it.**
    ///
    /// **A value past interior breakpoint `5` falls straight into the second segment (index 1), neither rejected nor
    /// clamped — its `Input` stays as given.**
    ///
    /// **The criterion uses `5.0 + 1e-9`: an implementation that wrongly extended the tolerance inward would report
    /// `Input == 5.0`, while the correct one returns `5.000000001`.**
    /// </remarks>
    [Fact]
    public void TheTolerance_ShouldNotApplyInsideTheDomain() {
        Result<SegmentSelection, ErrorCode, Error<ErrorCode>> just = Select(5.0 + 1e-9, 0.0, 5.0, 10.0);

        just.Value.Index.Should().Be(1);
        just.Value.Input.Should().Be(
            5.0 + 1e-9,
            "interior breakpoints have no tolerance, so nothing gets clamped there");
    }

    /// <summary>
    /// **断点数检查排在数值检查之前** / The breakpoint-count check precedes the numeric ones.
    /// </summary>
    /// <remarks>
    /// **"这个结构本身是否成立"与"这个数能不能用"是两个问题，
    /// 前者排在前面：一个只有一个断点的结构，无论输入多合法都产不出分段。**
    ///
    /// **判据用一个本身也不可用的输入（`NaN`）配一个断点，断言拿到的消息是"数量不足"。
    /// 如果顺序反了，拿到的是数值消息——两条消息完全不同，所以这个断言能区分顺序。**
    ///
    /// **"Does the structure hold" and "is this number usable" are two questions, and the former comes first: a
    /// structure with one breakpoint yields no segments however legal the input is.**
    ///
    /// **The criterion pairs an unusable input (`NaN`) with a single breakpoint and asserts the message is about the
    /// count.** **Reversed, the numeric message appears — the two are entirely different, so the assertion pins the
    /// order.**
    /// </remarks>
    [Fact]
    public void TheBreakpointCount_ShouldBeCheckedFirst() {
        Result<SegmentSelection, ErrorCode, Error<ErrorCode>> result =
            NativePiecewiseReport.SelectSegment(
                new Flt64(double.NaN), new[] { new Flt64(0.0) });

        result.Should().BeOfType<Failed<SegmentSelection, ErrorCode, Error<ErrorCode>>>();
        ((Failed<SegmentSelection, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should()
            .Contain("at least two breakpoints");

        // **有序之后再看数值那一支：两个断点、`NaN` 输入。**
        // **With the order pinned, the numeric branch: two breakpoints and a NaN input.**
        Result<SegmentSelection, ErrorCode, Error<ErrorCode>> nan =
            NativePiecewiseReport.SelectSegment(
                new Flt64(double.NaN), new[] { new Flt64(0.0), new Flt64(10.0) });
        ((Failed<SegmentSelection, ErrorCode, Error<ErrorCode>>)nan).Error.Message.Should()
            .Contain("must be finite");

        // **断点里的非有限值也要单独报出下标，否则"哪个断点坏了"无从查。**
        // **A non-finite breakpoint must name its index, otherwise there is no way to tell which one broke.**
        Result<SegmentSelection, ErrorCode, Error<ErrorCode>> badPoint =
            NativePiecewiseReport.SelectSegment(
                new Flt64(1.0),
                new[] { new Flt64(0.0), new Flt64(double.PositiveInfinity) });
        ((Failed<SegmentSelection, ErrorCode, Error<ErrorCode>>)badPoint).Error.Message.Should()
            .Contain("[1]");
    }

    /// <summary>
    /// **"没有有效分段"这一支<b>不可达</b>** / The "no valid segment" branch is <b>unreachable</b>.
    /// </summary>
    /// <remarks>
    /// **这是本方向第七条"不可测"发现，而这一条能被<b>证明</b>，不只是"没试出来"。**
    ///
    /// **论证：** **经过端点夹紧之后总有 `b_0 <= v <= b_n`（`b_0` 是下界、`b_n` 是上界）。**
    /// **取最小的 `i` 使 `b_i >= v`；因为 `b_n >= v`，这样的 `i` 一定存在。**
    ///
    /// - **若 `i >= 1`：** **由最小性知 `b_{i-1} < v`，即 `b_{i-1} <= v`；又 `b_i >= v`。
    ///   于是第 `i-1` 段 `[b_{i-1}, b_i]` <b>必然</b>包含 `v`，查找在它那里就命中了。**
    /// - **若 `i == 0`：** **说明 `b_0 >= v`，配合 `b_0 <= v` 得 `v == b_0`。
    ///   此时第 0 段可能漏（若 `b_1 < b_0`）。但要后面所有段都漏，
    ///   需要 `b_1 < v`；再对第 1 段要求 `b_2 < v`（因为 `b_1 < v <= b_2` 就会命中）；
    ///   依此类推得所有 `b_j < v = b_0`，特别地 `b_n < v`——
    ///   而 `b_n` 是上界，`v <= b_n` 矛盾。**
    ///
    /// **所以查找必然命中，`no valid segment` 是死代码。**
    ///
    /// **基线保留了它，本仓库照抄。判据不改这一点，而是<b>把不可达本身记下来</b>，
    /// 并抽样验证这个结论——一个没有判据的"死代码"下次读代码时会被当成"还有情形没处理"。**
    ///
    /// **This is the direction's seventh "untestable" finding, and this one can be <b>proved</b>, not merely "not
    /// observed".**
    ///
    /// **Proof**: **after endpoint clamping `b_0 <= v <= b_n` always holds.** **Let `i` be the smallest index with
    /// `b_i >= v`; it exists because `b_n >= v`.** **If `i >= 1`, minimality gives `b_{i-1} < v`, so segment `i-1` is
    /// `[b_{i-1}, b_i] ∋ v` and the search hits there.** **If `i == 0` then `v == b_0`; for segment 0 to miss we need
    /// `b_1 < v`, and for segment 1 to miss `b_2 < v`, and so on, giving `b_n < v` — contradicting `v <= b_n`.**
    ///
    /// **So the search always hits and `no valid segment` is dead code.** **The baseline keeps it and this batch copies
    /// the baseline; the criterion does not change that but <b>records the unreachability</b> and samples it, because dead
    /// code without a criterion gets read next time as "a case still unhandled".**
    /// </remarks>
    [Fact]
    public void TheNoValidSegmentBranch_ShouldBeUnreachable() {
        // **抽样：几组非单调断点 × 一批落在定义域内的值，没有一个能落到那条分支上。**
        // **Sampling: several non-monotonic breakpoint sets times a range of in-domain values, none reaching that branch.**
        double[][] sets = {
            new[] { 0.0, 5.0, 3.0 },
            new[] { 0.0, 10.0, 5.0 },
            new[] { 0.0, -1.0, 3.0 },
            new[] { -4.0, 8.0, 2.0, 6.0 },
            new[] { 0.0, 100.0, 1.0 },
        };

        foreach (double[] points in sets) {
            double lower = points[0];
            double upper = points[points.Length - 1];
            for (int step = 0; step <= 40; ++step) {
                double value = lower + ((upper - lower) * step / 40.0);
                Result<SegmentSelection, ErrorCode, Error<ErrorCode>> result =
                    Select(value, points);

                if (result is Failed<SegmentSelection, ErrorCode, Error<ErrorCode>> failed) {
                    failed.Error.Message.Should().NotContain(
                        "no valid segment",
                        "that branch is unreachable once the domain checks pass");
                }
            }
        }
    }

    /// <summary>
    /// **两段在断点处不相等时，报告仍然确定** / When the segments disagree at a breakpoint, the report is still deterministic.
    /// </summary>
    /// <remarks>
    /// **断点处的两个"正确答案"是一个真实情形：`[0,5]` 上 `slope 1`、`[5,10]` 上 `slope 0, intercept 100`，
    /// 那么在 `5` 处第一段给 `5`、第二段给 `100`。**
    ///
    /// **判据不判断哪个"对"（那是调用方的事），只断言它每次返回同一个下标。**
    ///
    /// **The two right answers at a breakpoint are a real case: `slope 1` on `[0,5]` then `slope 0, intercept 100` on
    /// `[5,10]` gives `5` from the first segment and `100` from the second at `5`.**
    ///
    /// **The criterion does not judge which is right (that is the caller's business), only that the same index comes back
    /// every time.**
    /// </remarks>
    [Fact]
    public void TheChoiceAtABreakpoint_ShouldBeDeterministic() {
        int first = Select(5.0, 0.0, 5.0, 10.0).Value.Index;
        for (int attempt = 0; attempt < 5; ++attempt) {
            Select(5.0, 0.0, 5.0, 10.0).Value.Index.Should().Be(
                first, "the same input must always select the same segment");
        }

        first.Should().Be(0);
    }

    private static Result<SegmentSelection, ErrorCode, Error<ErrorCode>> Select(
        double input, params double[] breakpoints) {
        var points = new Flt64[breakpoints.Length];
        for (int index = 0; index < breakpoints.Length; ++index) {
            points[index] = new Flt64(breakpoints[index]);
        }

        return NativePiecewiseReport.SelectSegment(new Flt64(input), points);
    }
}
