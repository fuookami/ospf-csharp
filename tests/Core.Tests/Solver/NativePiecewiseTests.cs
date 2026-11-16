#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 原生 PWL primitive：严格递增、连续性与 y 点推导 / Strict increase, continuity, y-point derivation.
/// </summary>
/// <remarks>
/// 对齐的 `NativePiecewiseData` 与 `prepareNativePiecewise`。
///
/// Adds </remarks>
public class NativePiecewiseTests
{
    /// <summary>
    /// **`y` 点由每段两个端点算出，且首尾相接** / `y` points come from each segment's ends and meet.
    /// </summary>
    /// <remarks>
    /// **`[0, 5]` 上 `slope 1, intercept 0`、`[5, 10]` 上 `slope 0, intercept 5`：**
    /// **`y` 应当是 `[0, 5, 5]`——第一段给 `0` 与 `5`，第二段给 `5` 与 `5`。**
    ///
    /// **注意 `y` 的长度比 `x` 多不了：`n` 段给出 `n + 1` 个点，正好与断点数相同。**
    ///
    /// **Segments `slope 1, intercept 0` on `[0, 5]` then `slope 0, intercept 5` on `[5, 10]` give
    /// `y = [0, 5, 5]`.** **Note `y` has `n + 1` points, matching the breakpoint count.**
    /// </remarks>
    [Fact]
    public void TheYPoints_ShouldComeFromTheSegmentEnds() {
        Result<NativePiecewiseData, ErrorCode, Error<ErrorCode>> result =
            NativePiecewise.Prepare(Structure());

        result.Should().BeOfType<Ok<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();
        NativePiecewiseData data = result.Value;
        data.XPoints.Should().Equal(0.0, 5.0, 10.0);
        data.YPoints.Should().Equal(0.0, 5.0, 5.0);
        data.InputKey.Should().Be(X.Key);
        data.ResultKey.Should().Be(Result.Key);
        data.Name.Should().Be("pwl");
    }

    /// <summary>
    /// **第一段左端非零时 `y[0]` 必须被真的写进去** / With a nonzero left end, `y[0]` must actually be written.
    /// </summary>
    /// <remarks>
    /// **这是我在变异验证里发现的一处判据盲区。**
    ///
    /// **上面那条判据的算例是 `[0, 5, 10]`，第一段左端的值是 `0`。**
    /// **而 `double[]` 的默认值也是 `0`——所以"写 `yPoints[0]`"与"不写"给出<b>同一个结果</b>，
    /// 删掉那一行，判据照样全绿。**
    ///
    /// **这正是"两个机制给出同一个答案"这一类盲区，与地板被类型修复掩盖是同一个形状。**
    ///
    /// **判据改用 `[2, 5, 10]`：第一段 `slope 1, intercept 0` 在 `2` 处给 `2`，非零。**
    /// **第二段 `slope 0, intercept 5` 在 `5` 处给 `5`（与第一段右端相等，连续）在 `10` 处给 `5`。**
    /// **所以 `y` 应当是 `[2, 5, 5]`——首项非零，删掉赋值就会变成 `[0, 5, 5]`。**
    ///
    /// **This is a criterion blind spot found during mutation testing.**
    ///
    /// **The case above uses `[0, 5, 10]`, whose first segment starts at `0`.** **A `double[]` also defaults to `0`, so
    /// "write `yPoints[0]`" and "do not write it" produce <b>the same result</b> and deleting the line leaves the
    /// criterion green.**
    ///
    /// **This is the same shape as the earlier floor being masked by the type repair: two mechanisms giving the same
    /// answer.**
    ///
    /// **The criterion uses `[2, 5, 10]`: `slope 1, intercept 0` gives `2` at `2` — nonzero.** **`slope 0, intercept 5`
    /// gives `5` at `5` (matching the first segment, so continuous) and `5` at `10`.** **So `y` must be `[2, 5, 5]`;
    /// without the assignment it would be `[0, 5, 5]`.**
    /// </remarks>
    [Fact]
    public void ANonzeroLeftEnd_ShouldBeWrittenIntoY() {
        Result<NativePiecewiseData, ErrorCode, Error<ErrorCode>> result =
            NativePiecewise.Prepare(Structure(
                points: new[] { 2.0, 5.0, 10.0 },
                slopes: new[] { 1.0, 0.0 },
                intercepts: new[] { 0.0, 5.0 }));

        result.Should().BeOfType<Ok<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();
        // **首项非零——这是关键。`double[]` 的默认值是 0，所以只有非零首项才能区分"写了"与"没写"。**
        // **A nonzero first element is the point: `double[]` defaults to 0, so only a nonzero one distinguishes "written"
        // from "not written".**
        result.Value.YPoints.Should().Equal(2.0, 5.0, 5.0);
        result.Value.YPoints[0].Should().NotBe(
            0.0, "the first y is the first segment's left end, not the array default");
    }

    /// <summary>
    /// **断点必须严格递增：相等也拒** / Breakpoints must be strictly increasing; equality is rejected too.
    /// </summary>
    /// <remarks>
    /// **`>=` 而不是 `>`：两个断点重合会造出<b>零宽分段</b>，
    /// 而零宽分段在 `selectSegment` 的查找里会让同一段既含左端又含右端——
    /// 一个"看起来有 n 段、实际只有 n-1 段"的结构。**
    ///
    /// **判据同时测"逆序"与"相等"两种：只测逆序的话，`>=` 可以写成 `>` 而不被发现。**
    ///
    /// **`>=` rather than `>`: coincident breakpoints create a <b>zero-width segment</b>, which in `selectSegment`'s
    /// search contains both its ends — a structure that looks like `n` segments but has `n-1`.**
    ///
    /// **The criterion tests both reversed and equal: with reversed alone, `>=` could be written as `>` undetected.**
    /// </remarks>
    [Fact]
    public void TheBreakpoints_ShouldBeStrictlyIncreasing() {
        Result<NativePiecewiseData, ErrorCode, Error<ErrorCode>> reversed =
            NativePiecewise.Prepare(Structure(points: new[] { 0.0, 10.0, 5.0 }));
        ((Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>)reversed).Error.Message.Should()
            .Contain("strictly increasing");

        Result<NativePiecewiseData, ErrorCode, Error<ErrorCode>> equal =
            NativePiecewise.Prepare(Structure(points: new[] { 0.0, 5.0, 5.0 }));
        ((Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>)equal).Error.Message.Should()
            .Contain("strictly increasing");
    }

    /// <summary>
    /// **不连续的分段线性被拒** / A discontinuous piecewise function is rejected.
    /// </summary>
    /// <remarks>
    /// **这是这里最有内容的一条。**
    ///
    /// **native primitive 只吃一组 `(x_i, y_i)` 点，它没有"每段一个截距"的概念。**
    /// **所以不连续的分段线性在这种表示下<b>无法表达</b>。**
    ///
    /// **它必须在转换时被拒，而不是被静默地压成一条折线——
    /// 后者会让"两段在断点处差 3"这个事实消失，而调用方永远看不到。**
    ///
    /// **判据构造两段在 `5` 处不相等的结构：第一段给 `5`，第二段给 `8`。**
    ///
    /// **This is the batch's most substantive criterion.**
    ///
    /// **The native primitive takes only a set of `(x_i, y_i)` points; it has no per-segment intercept.** **A
    /// discontinuous piecewise function is therefore <b>inexpressible</b> in that representation.**
    ///
    /// **It must be rejected at conversion rather than silently flattened into a polyline — flattening would make "the two
    /// segments differ by 3 at the breakpoint" vanish, and the caller would never see it.**
    ///
    /// **The criterion builds two segments that disagree at `5`: the first gives `5`, the second gives `8`.**
    /// </remarks>
    [Fact]
    public void ADiscontinuousRelation_ShouldBeRejected() {
        // **第二段 `slope 0, intercept 8` 在 `5` 处给 8，而第一段给 5。**
        // **The second segment, `slope 0, intercept 8`, gives 8 at `5` while the first gives 5.**
        Result<NativePiecewiseData, ErrorCode, Error<ErrorCode>> result =
            NativePiecewise.Prepare(Structure(intercepts: new[] { 0.0, 8.0 }));

        result.Should().BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();
        ((Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should()
            .Contain("discontinuous");
    }

    /// <summary>
    /// **输入必须是 `1·x + 0`** / The input must be `1*x + 0`.
    /// </summary>
    [Fact]
    public void TheInput_ShouldBeExactlyUnitVariable() {
        NativePiecewise.Prepare(Structure(coefficient: 2.0)).Should()
            .BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();
        NativePiecewise.Prepare(Structure(constant: 3.0)).Should()
            .BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();

        var twoMonomials = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(Flt64.One, X),
                new LinearMonomial<Flt64>(Flt64.One, Y),
            }, Flt64.Zero);
        NativePiecewise.Prepare(Structure(input: twoMonomials)).Should()
            .BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **数量不匹配被拒** / Count mismatches are rejected.
    /// </summary>
    /// <remarks>
    /// **三种：断点少于两个、斜率数与段数不符、截距数与斜率数不符。**
    /// **三种各一例，否则可以整段缺失。**
    ///
    /// **Three: fewer than two breakpoints, slope count off, intercept count off.**
    /// </remarks>
    [Fact]
    public void TheCounts_ShouldMatch() {
        NativePiecewise.Prepare(Structure(points: new[] { 0.0 })).Should()
            .BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();

        NativePiecewise.Prepare(Structure(slopes: new[] { 1.0 })).Should()
            .BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();

        NativePiecewise.Prepare(Structure(intercepts: new[] { 0.0 })).Should()
            .BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **缺转换器被拒，且消息与 `NativeAbs` 区分得开** /
    /// A missing converter is rejected with a message distinct from `NativeAbs`'s.
    /// </summary>
    [Fact]
    public void TheMissingConverter_ShouldBeRejected() {
        var noConverter = new UnivariateLinearPiecewiseStructure(
            Input: Poly(1.0, 0.0),
            Breakpoints: new[] { new Flt64(0.0), new Flt64(5.0), new Flt64(10.0) },
            Slopes: new[] { Flt64.One, Flt64.Zero },
            Intercepts: new[] { Flt64.Zero, new Flt64(5.0) },
            ResultVariable: Result, SelectorVariables: new[] { Bit, Bit2 });

        Result<NativePiecewiseData, ErrorCode, Error<ErrorCode>> result =
            NativePiecewise.Prepare(noConverter);

        result.Should().BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();
        ((Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should()
            .StartWith("invalid native PWL data:");
    }

    /// <summary>
    /// **`maximumMagnitude` 是严格上界，且 `NaN` 被单独拒** /
    /// `maximumMagnitude` is a strict bound and `NaN` is rejected explicitly.
    /// </summary>
    [Fact]
    public void TheMaximumMagnitude_ShouldBeStrictAndRejectNaN() {
        NativePiecewise.Prepare(Structure(), maximumMagnitude: 0.0).Should()
            .BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();
        NativePiecewise.Prepare(Structure(), maximumMagnitude: double.NaN).Should()
            .BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>(
                "otherwise every value would pass the magnitude check");

        // **断点 `10` 恰好等于上限时被拒（严格上界）。**
        // **Breakpoint `10` exactly at the limit is rejected (strict bound).**
        NativePiecewise.Prepare(Structure(), maximumMagnitude: 10.0).Should()
            .BeOfType<Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();
        NativePiecewise.Prepare(Structure(), maximumMagnitude: 10.1).Should()
            .BeOfType<Ok<NativePiecewiseData, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **与对照：同一份乱序断点在两层得到两种质量的消息** /
    /// The contrast: the same shuffled breakpoints get messages of different quality at the two layers.
    /// </summary>
    /// <remarks>
    /// **primitive 层（这里）给出 `breakpoints must be strictly increasing`——准确的，指向结构。**
    ///
    /// **报告层（ `selectSegment`）没有这项检查，而是把<b>最后一个</b>断点当定义域上界。**
    /// **对 `[0, 10, 5]` 来说，上界被当成 `5`，于是 `7` 被判为 `outside its domain`——
    /// 尽管第一段 `[0, 10]` 实际覆盖 `7`。**
    ///
    /// **这是比 `no valid segment` 更值得记的一条：它<b>不是</b>一个死分支，
    /// 而是一条会真的发出来、并且把人引向错误方向的消息。**
    /// **一个断点顺序错的结构，得到的是"你的输入越界了"。**
    ///
    /// **最初判据断言的是 `no valid segment`，实测拿到的是 `outside its domain`——
    /// 因为 `upper` 取最后一个断点。改判据之前先想清楚是判据错还是实现错：
    /// 这里是判据错，实现。**
    ///
    /// **The primitive layer says `breakpoints must be strictly increasing` — accurate, pointing at the
    /// structure.**
    ///
    /// **The reporting layer (the earlier `selectSegment`) has no such check and takes the <b>last</b> breakpoint as the
    /// domain upper bound.** **For `[0, 10, 5]` the upper bound becomes `5`, so `7` is reported as `outside its domain` —
    /// although the first segment `[0, 10]` actually covers it.**
    ///
    /// **This is worth recording more than `no valid segment`: it is <b>not</b> a dead branch but a message that really
    /// fires and points the wrong way.** **A structure with mis-ordered breakpoints is told "your input is out of
    /// range".**
    ///
    /// **My first version asserted `no valid segment` and actually got `outside its domain`, because `upper` is the last
    /// breakpoint.** **Before changing a criterion, decide whether the criterion or the implementation is wrong: here the
    /// criterion was, and the implementation matches the code.**
    /// </remarks>
    [Fact]
    public void TheTwoLayers_ShouldReportShuffledBreakpointsDifferently() {
        Flt64[] shuffled = { new Flt64(0.0), new Flt64(10.0), new Flt64(5.0) };

        Result<NativePiecewiseData, ErrorCode, Error<ErrorCode>> primitive =
            NativePiecewise.Prepare(Structure(points: new[] { 0.0, 10.0, 5.0 }));
        ((Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>>)primitive).Error.Message.Should()
            .Contain("strictly increasing", "the primitive layer checks ordering at the entry");

        // **报告层：`upper` 是最后一个断点 `5`，所以 `7` 被判越界。**
        // **Reporting layer: `upper` is the last breakpoint, `5`, so `7` is called out of range.**
        Result<SegmentSelection, ErrorCode, Error<ErrorCode>> report =
            NativePiecewiseReport.SelectSegment(new Flt64(7.0), shuffled);
        report.Should().BeOfType<Failed<SegmentSelection, ErrorCode, Error<ErrorCode>>>();
        ((Failed<SegmentSelection, ErrorCode, Error<ErrorCode>>)report).Error.Message.Should()
            .Contain("outside its domain",
                "the last breakpoint is taken as the upper bound, so a covered value looks out of range");

        // **而 `2` 落在第一段 `[0, 10]` 里，所以是 `Ok`——越界的判断只对"超过最后一个断点"的值触发。**
        // **And `2` sits inside the first segment `[0, 10]`, so it is `Ok` — the out-of-range verdict only fires past the
        // last breakpoint.**
        NativePiecewiseReport.SelectSegment(new Flt64(2.0), shuffled).Should()
            .BeOfType<Ok<SegmentSelection, ErrorCode, Error<ErrorCode>>>();
    }

    private static UnivariateLinearPiecewiseStructure Structure(
        LinearPolynomial<Flt64>? input = null,
        double[]? points = null,
        double[]? slopes = null,
        double[]? intercepts = null,
        double coefficient = 1.0,
        double constant = 0.0) {
        points ??= new[] { 0.0, 5.0, 10.0 };
        slopes ??= new[] { 1.0, 0.0 };
        intercepts ??= new[] { 0.0, 5.0 };

        var breakpoints = new Flt64[points.Length];
        for (int index = 0; index < points.Length; ++index) {
            breakpoints[index] = new Flt64(points[index]);
        }

        var slopeValues = new Flt64[slopes.Length];
        for (int index = 0; index < slopes.Length; ++index) {
            slopeValues[index] = new Flt64(slopes[index]);
        }

        var interceptValues = new Flt64[intercepts.Length];
        for (int index = 0; index < intercepts.Length; ++index) {
            interceptValues[index] = new Flt64(intercepts[index]);
        }

        int segments = points.Length - 1;
        var selectors = new IVariableItem[global::System.Math.Max(segments, 0)];
        for (int index = 0; index < selectors.Length; ++index) {
            selectors[index] = index == 0 ? Bit : Bit2;
        }

        return new UnivariateLinearPiecewiseStructure(
            Input: input ?? Poly(coefficient, constant),
            Breakpoints: breakpoints, Slopes: slopeValues, Intercepts: interceptValues,
            ResultVariable: Result, SelectorVariables: selectors) {
            Converter = IIntoValue<Flt64>.Identity,
            Name = "pwl",
        };
    }

    private static LinearPolynomial<Flt64> Poly(double coefficient, double constant) =>
        new(new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), X) },
            new Flt64(constant));

    private static readonly RealVar X = new("np-x");

    private static readonly RealVar Y = new("np-y");

    private static readonly RealVar Result = new("np-result");

    private static readonly BinVar Bit = new("np-bit");

    private static readonly BinVar Bit2 = new("np-bit2");
}
